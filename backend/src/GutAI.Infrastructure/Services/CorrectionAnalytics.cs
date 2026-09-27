using System.Text.Json;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public sealed record CorrectionAnalyticsOptions
{
    public int MinSamples { get; init; } = 20;
    public string ProposedVersion { get; init; } = "correction-analytics-v1";
}

public sealed record CorrectionAnalyticsReport
{
    public required int DraftCount { get; init; }
    public required int MalformedRows { get; init; }
    public required IReadOnlyList<PortionCorrectionCell> Portion { get; init; }
    public required IReadOnlyList<SwapCorrectionRate> SwapsByMethod { get; init; }
    public required RemovalCorrectionRates Removal { get; init; }
    public required IReadOnlyList<OriginAcceptanceRate> AcceptanceByOrigin { get; init; }
    public required CalibrationProposal Calibration { get; init; }
}

public sealed record PortionCorrectionCell(string FoodClass, string Tier, int N, decimal MedianRatio, decimal P25, decimal P75);
public sealed record SwapCorrectionRate(string Method, int N, int Swaps, decimal Rate);
public sealed record RemovalRate(int N, int Removed, decimal Rate);
public sealed record RemovalCorrectionRates(RemovalRate Overall, IReadOnlyList<NamedRemovalRate> ByOrigin, IReadOnlyList<NamedRemovalRate> ByMethod);
public sealed record NamedRemovalRate(string Name, RemovalRate Metrics);
public sealed record OriginAcceptanceRate(string Origin, int Committed, int Discarded, int Expired, int Suggested, decimal AcceptanceRate);
public sealed record CalibrationFactor(string FoodClass, string Tier, decimal Factor, int Samples);
public sealed record CalibrationProposal(string Version, int MinSamples, IReadOnlyList<CalibrationFactor> Factors, IReadOnlyList<PortionCorrectionCell> BelowMinimum, string ConfigJson);

/// <summary>Pure aggregation of committed user corrections; no persistence or model access.</summary>
public static class CorrectionAnalytics
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static CorrectionAnalyticsReport Compute(IEnumerable<MealDraftRecord> drafts, CorrectionAnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MinSamples < 1) throw new ArgumentOutOfRangeException(nameof(options), "MinSamples must be positive.");
        if (string.IsNullOrWhiteSpace(options.ProposedVersion)) throw new ArgumentException("A proposed version is required.", nameof(options));

        var rows = drafts.ToList();
        var portions = new List<PortionSample>();
        var swaps = new Dictionary<string, (int N, int Swaps)>(StringComparer.Ordinal);
        var removals = new Dictionary<string, (int N, int Removed)>(StringComparer.Ordinal);
        var originRemovals = new Dictionary<string, (int N, int Removed)>(StringComparer.Ordinal);
        var acceptances = new Dictionary<string, AcceptanceCounts>(StringComparer.Ordinal);
        var malformed = 0;

        foreach (var draft in rows)
        {
            if (!acceptances.TryGetValue(draft.Origin, out var counts)) counts = new AcceptanceCounts();
            counts = draft.Status switch
            {
                MealDraftStatuses.Committed => counts with { Committed = counts.Committed + 1 },
                MealDraftStatuses.Discarded => counts with { Discarded = counts.Discarded + 1 },
                MealDraftStatuses.Expired => counts with { Expired = counts.Expired + 1 },
                _ => counts
            };
            acceptances[draft.Origin] = counts;

            if (draft.Status != MealDraftStatuses.Committed || string.IsNullOrWhiteSpace(draft.CorrectionDeltaJson)) continue;
            List<Delta>? deltas;
            List<DraftItem>? items;
            try
            {
                if (string.IsNullOrWhiteSpace(draft.ItemsJson)) throw new JsonException("Missing draft items.");
                deltas = JsonSerializer.Deserialize<List<Delta>>(draft.CorrectionDeltaJson, JsonOptions);
                items = JsonSerializer.Deserialize<List<DraftItem>>(draft.ItemsJson, JsonOptions);
                if (deltas is null || items is null || deltas.Any(d => d is null) || items.Any(i => i is null))
                    throw new JsonException("Expected arrays of objects.");
            }
            catch (JsonException)
            {
                malformed++;
                continue;
            }

            var methods = items.Where(i => i.ItemId != Guid.Empty)
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First().Grounding?.Method ?? "unknown");
            foreach (var delta in deltas)
            {
                if (delta.FoodClass is not null && delta.PortionConfidenceTier is not null && delta.GramRatio is { } ratio && !delta.Removed)
                    portions.Add(new PortionSample(delta.FoodClass, delta.PortionConfidenceTier, ratio));

                var method = methods.GetValueOrDefault(delta.ItemId, "unknown");
                Add(removals, method, delta.Removed);
                Add(originRemovals, draft.Origin, delta.Removed);
                var swapped = !string.IsNullOrWhiteSpace(delta.SwappedFromKey) ||
                              !string.IsNullOrWhiteSpace(delta.SwappedToKey) || delta.ReplacementFoodProductId is not null;
                if (!swaps.TryGetValue(method, out var swapCount)) swapCount = default;
                swaps[method] = (swapCount.N + 1, swapCount.Swaps + (swapped ? 1 : 0));
            }
        }

        var portionCells = portions.GroupBy(x => (x.FoodClass, x.Tier))
            .Select(g =>
            {
                var sorted = g.Select(x => x.Ratio).Order().ToArray();
                return new PortionCorrectionCell(g.Key.FoodClass, g.Key.Tier, sorted.Length,
                    Percentile(sorted, 0.5m), Percentile(sorted, 0.25m), Percentile(sorted, 0.75m));
            })
            .OrderBy(x => x.FoodClass, StringComparer.Ordinal).ThenBy(x => x.Tier, StringComparer.Ordinal).ToList();

        var factorCells = portionCells.Where(c => c.N >= options.MinSamples)
            .Select(c => new CalibrationFactor(c.FoodClass, c.Tier, c.MedianRatio, c.N)).ToList();
        var below = portionCells.Where(c => c.N < options.MinSamples).ToList();
        var configShape = new
        {
            MealScan = new
            {
                PortionCalibration = new
                {
                    Version = options.ProposedVersion,
                    Factors = factorCells.Select(f => new { f.FoodClass, f.Tier, f.Factor }).ToList()
                }
            }
        };
        var configJson = JsonSerializer.Serialize(configShape, new JsonSerializerOptions { WriteIndented = true });

        var allRemoval = Aggregate(removals.Values);
        var byOrigin = originRemovals.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new NamedRemovalRate(x.Key, Rate(x.Value.N, x.Value.Removed))).ToList();
        var byMethod = removals.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new NamedRemovalRate(x.Key, Rate(x.Value.N, x.Value.Removed))).ToList();
        var acceptanceReport = acceptances.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x =>
        {
            var total = x.Value.Committed + x.Value.Discarded + x.Value.Expired;
            return new OriginAcceptanceRate(x.Key, x.Value.Committed, x.Value.Discarded, x.Value.Expired, total,
                total == 0 ? 0m : (decimal)x.Value.Committed / total);
        }).ToList();

        return new CorrectionAnalyticsReport
        {
            DraftCount = rows.Count,
            MalformedRows = malformed,
            Portion = portionCells,
            SwapsByMethod = swaps.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new SwapCorrectionRate(x.Key, x.Value.N, x.Value.Swaps, (decimal)x.Value.Swaps / x.Value.N)).ToList(),
            Removal = new RemovalCorrectionRates(allRemoval, byOrigin, byMethod),
            AcceptanceByOrigin = acceptanceReport,
            Calibration = new CalibrationProposal(options.ProposedVersion, options.MinSamples, factorCells, below, configJson)
        };
    }

    private static decimal Percentile(decimal[] sorted, decimal p)
    {
        if (sorted.Length == 1) return sorted[0];
        var position = (sorted.Length - 1) * p;
        var lower = (int)decimal.Floor(position);
        var upper = (int)decimal.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static void Add(Dictionary<string, (int N, int Removed)> rates, string name, bool removed)
    {
        if (!rates.TryGetValue(name, out var count)) count = default;
        rates[name] = (count.N + 1, count.Removed + (removed ? 1 : 0));
    }

    private static RemovalRate Aggregate(IEnumerable<(int N, int Removed)> values)
    {
        var total = values.Aggregate((N: 0, Removed: 0), (sum, value) => (sum.N + value.N, sum.Removed + value.Removed));
        return Rate(total.N, total.Removed);
    }

    private static RemovalRate Rate(int n, int removed) => new(n, removed, n == 0 ? 0m : (decimal)removed / n);

    private sealed record PortionSample(string FoodClass, string Tier, decimal Ratio);
    private sealed record AcceptanceCounts(int Committed = 0, int Discarded = 0, int Expired = 0);

    private sealed record Delta
    {
        public Guid ItemId { get; init; }
        public string? FoodClass { get; init; }
        public string? PortionConfidenceTier { get; init; }
        public decimal? GramRatio { get; init; }
        public bool Removed { get; init; }
        public string? SwappedFromKey { get; init; }
        public string? SwappedToKey { get; init; }
        public Guid? ReplacementFoodProductId { get; init; }
    }

    private sealed record DraftItem
    {
        public Guid ItemId { get; init; }
        public GroundingData? Grounding { get; init; }
    }

    private sealed record GroundingData { public string? Method { get; init; } }
}
