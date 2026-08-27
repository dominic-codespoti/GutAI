using GutAI.Infrastructure.Data;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Stage B: grounds Stage-A components to real catalogue entries.
///
/// Frozen policy (docs/meal-scan-detailed-design.md §4.3 + P3 review):
/// - Every component goes through the EXISTING IFoodSearchService.ResolveAsync —
///   never a parallel lookup path.
/// - Auto-select only when the resolver says Exact/Probable AND confidence ≥ 0.85
///   (frozen). Anything else exposes top-3 candidates for human choice.
/// - "Unresolved" is a first-class outcome: the item stays ai-source. Grounding
///   NEVER fabricates a match to complete the meal.
/// - Grams always stay attached to the original detected component; the resolved
///   catalogue entry contributes only per-100g values and a canonical name.
/// - Success metric is correct-auto-grounding rate alongside incorrect-grounding
///   and abstention rate — NOT raw resolution percentage (which incentivizes
///   false positives).
/// </summary>
public sealed partial class ComponentGroundingEngine(IFoodSearchService foodSearch)
{
    /// <summary>Frozen auto-select floor. Deliberately a constant, not config.</summary>
    public const decimal MinAutoSelectConfidence = 0.85m;

    public const int MaxCandidates = 3;

    /// <summary>
    /// Minimum tolerated compatibility-vs-lexical-confidence gap before a
    /// lexically strong match is vetoed from auto-selection. A single strong
    /// mismatch (raw observed vs cooked candidate, -18) or several moderate ones
    /// stacked (packaged-snack form + excess unrequested tokens + brand miss)
    /// cross this; an isolated moderate penalty (brand-only, -8) does not.
    /// </summary>
    private const float MinCompatibilityMargin = -15f;

    public async Task<GroundedItem> GroundAsync(
        ScannedComponent component, CancellationToken ct = default)
    {
        var queries = BuildResolverQueries(component);
        var resolutions = await Task.WhenAll(
            queries.Select(query => foodSearch.ResolveAsync(query, boostIds: [], ct)));
        var primary = resolutions[0];

        var mergedCandidates = resolutions
            .SelectMany(resolution => new[] { resolution.Selected }
                .Concat(resolution.Alternatives)
                .Where(p => p is not null)
                .Select(p => p!))
            .GroupBy(CandidateKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(p => p.MatchConfidence).First())
            .ToList();

        var rankedCandidates = mergedCandidates
            .OrderByDescending(p => FoodCandidateCompatibilityScorer.Score(component, p))
            .ToList();
        var status = primary.Status;

        // Compatibility and deterministic food-form / specificity policy veto:
        // a lexically strong match (Exact/Probable, confidence >= floor) must still be
        // demoted to Ambiguous when the generic compatibility scorer or deterministic
        // food-form/specificity safety policy vetoes the candidate for auto-selection.
        if (primary.Selected is not null
            && status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable
            && (IsCompatibilityVetoed(component, primary.Selected) || FoodFormPolicy.Evaluate(component, primary.Selected) is not null))
            status = FoodResolutionStatus.Ambiguous;
        var candidateProducts = primary.Selected is not null
                                && (status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable)
            ? new[] { primary.Selected }
                .Concat(rankedCandidates.Where(p => CandidateKey(p) != CandidateKey(primary.Selected)))
                .Take(MaxCandidates)
                .ToList()
            : rankedCandidates.Take(MaxCandidates).ToList();

        if (primary.Selected is null
            && candidateProducts.Count > 0
            && status == FoodResolutionStatus.Unresolved)
            status = FoodResolutionStatus.Ambiguous;

        var candidates = candidateProducts
            .Select(p => new GroundingCandidateDto(
                p.Name,
                p.Id == Guid.Empty ? null : p.Id,
                MapSource(p.DataSource),
                p.MatchConfidence,
                p.Brand,
                p.ExternalId,
                p.SourceUrl,
                p.Calories100g,
                p.Protein100g,
                p.Carbs100g,
                p.Fat100g,
                p.Fiber100g,
                p.Sugar100g,
                p.SodiumMg100g))
            .ToList();

        var attempt = new GroundingAttemptDto
        {
            Query = queries[0],
            Queries = queries,
            ResolutionStatus = status.ToString().ToLowerInvariant(),
            AutoSelected = false,
            Candidates = candidates,
            MatchConfidence = primary.MatchConfidence,
            Method = "resolve_async",
        };

        var autoSelected = primary.Selected is not null
                           && primary.Selected.Calories100g.HasValue
                           && (status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable)
                           && primary.MatchConfidence >= MinAutoSelectConfidence;

        if (!autoSelected || primary.Selected is null)
            return new GroundedItem(component, null, attempt, candidateProducts);

        var selected = primary.Selected;
        var groundedAttempt = attempt with
        {
            AutoSelected = true,
            SelectedFoodProductId = selected.Id,
            CanonicalName = selected.Name,
        };

        return new GroundedItem(component, selected, groundedAttempt, candidateProducts);
    }

    /// <summary>
    /// True when <see cref="FoodCandidateCompatibilityScorer"/> finds the candidate's
    /// form/preparation/state disagrees with the observation by more than
    /// <see cref="MinCompatibilityMargin"/>, relative to what its own lexical
    /// confidence alone would justify.
    /// </summary>
    private static bool IsCompatibilityVetoed(ScannedComponent component, FoodProductDto candidate)
    {
        var compatibility = FoodCandidateCompatibilityScorer.Score(component, candidate);
        var lexicalBaseline = (float)(candidate.MatchConfidence * 100m);
        return compatibility - lexicalBaseline <= MinCompatibilityMargin;
    }

    private static string CandidateKey(FoodProductDto product) =>
        product.Id != Guid.Empty
            ? $"id:{product.Id}"
            : $"{product.DataSource}|{product.ExternalId}|{product.Brand}|{product.Name}";

    internal static IReadOnlyList<string> BuildResolverQueries(ScannedComponent component)
    {
        var queries = new List<string>();
        AddQuery(NormalizeRetrievalQuery(component.Name));
        AddQuery(component.Name);
        foreach (var query in component.SearchQueries)
            AddQuery(query);
        return queries.Take(3).ToArray();

        void AddQuery(string? raw)
        {
            var query = QuerySanitizer.Sanitize(raw ?? "");
            if (query.Length >= 2 && !queries.Contains(query, StringComparer.OrdinalIgnoreCase))
                queries.Add(query);
        }
    }

    /// <summary>
    /// Produces a retrieval-first variant for verbose Stage-A dish names. Resolver
    /// ranking is lexical and sensitive to serving words: "katsu curry rice bowl"
    /// can retrieve bread while "katsu curry" retrieves the dish. The original
    /// name remains as a secondary query for compatibility scoring.
    /// </summary>
    internal static string NormalizeRetrievalQuery(string raw)
    {
        var normalized = QuerySanitizer.Sanitize(raw);
        if (normalized.Length == 0) return normalized;

        normalized = ServingSuffixPattern().Replace(normalized, "").Trim(' ', ',', '-', '—');
        normalized = Whitespace().Replace(normalized, " ").Trim();

        var withMatch = LeadingCompositePattern().Match(normalized);
        if (withMatch.Success)
        {
            var core = withMatch.Groups["core"].Value.Trim(' ', ',', '-', '—');
            if (core.Length >= 3)
                return core;
        }

        return normalized;
    }

    // "katsu curry rice bowl", "taco salad plate", "curry set"
    [System.Text.RegularExpressions.GeneratedRegex(
        @"\s+(?:with\s+)?(?:rice\s+)?(?:bowl|plate|platter|dish|set|meal)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ServingSuffixPattern();

    // Long composite: "loaded nachos with grilled meat, lettuce, and salsa".
    [System.Text.RegularExpressions.GeneratedRegex(
        @"^(?<core>[^,;]+?)\s+with\s+.+$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex LeadingCompositePattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s{2,}")]
    private static partial System.Text.RegularExpressions.Regex Whitespace();

    private static string MapSource(string dataSource) => dataSource?.ToLowerInvariant() switch
    {
        "usda" or "usda fdc" or "fdc" => "usda",
        "open food facts" or "off" => "off",
        "au" or "australian" or "afcd" => "au",
        "" or null => "ai",
        var other => other.ToLowerInvariant(),
    };
}

/// <summary>
/// The boundary object from the P3 review: original Stage-A measurement preserved,
/// canonical catalogue data attached alongside it. Macros are computed here
/// (Stage C) deterministically from DB per-100g × ORIGINAL grams.
/// </summary>
public sealed record GroundedItem(
    ScannedComponent Original,
    FoodProductDto? ResolvedProduct,
    GroundingAttemptDto Attempt,
    IReadOnlyList<FoodProductDto> CandidateProducts)
{
    public MealScanItemDto ToItem()
    {
        var p = ResolvedProduct;
        var grounded = p is not null;
        var grams = Original.EstimatedGramsMidpoint;
        var factor = grams / 100m;

        return new MealScanItemDto
        {
            ItemId = Guid.NewGuid(),
            Name = Original.Name,
            CanonicalName = grounded ? p!.Name : null,
            FoodProductId = grounded ? p!.Id : null,
            Source = grounded ? SourceKey(p!.DataSource) : "ai",
            Grams = grams,
            PortionLowGrams = Original.EstimatedGramsLow,
            PortionHighGrams = Original.EstimatedGramsHigh,
            PortionMethod = "vision_estimate",
            ServingHintUnit = Original.ServingHintUnit,
            ServingHintUnitPlural = Original.ServingHintUnitPlural,
            ServingHintUnitGrams = Original.ServingHintUnitGrams,
            PortionConfidence = Original.PortionConfidence,
            IsGarnish = Original.IsGarnish,
            Calories = grounded ? Round0(p!.Calories100g * factor) : null,
            ProteinG = grounded ? Round1(p!.Protein100g * factor) : null,
            CarbsG = grounded ? Round1(p!.Carbs100g * factor) : null,
            FatG = grounded ? Round1(p!.Fat100g * factor) : null,
            FiberG = grounded ? Round1(p!.Fiber100g * factor) : null,
            SugarG = grounded ? Round1(p!.Sugar100g * factor) : null,
            SodiumMg = grounded ? Round0(p!.SodiumMg100g * factor) : null,
            MatchConfidence = grounded ? p!.MatchConfidence : 0m,
            VisionConfidence = Original.Confidence,
            CandidateNames = Attempt.Candidates.Select(c => c.Name).ToList(),
            Grounding = Attempt,
        };
    }

    private static string SourceKey(string? dataSource) => dataSource?.ToLowerInvariant() switch
    {
        "usda" or "usda fdc" or "fdc" => "usda",
        "open food facts" or "off" => "off",
        "au" or "australian" or "afcd" => "au",
        // Grounded products with an unknown/blank DataSource must NOT map to "ai": the
        // meal-scan web cascade treats Source == "ai" as ungrounded and replaces the item,
        // which would strip a real catalog match while retaining its health signals.
        "" or null => "db",
        var other => other.ToLowerInvariant(),
    };

    private static decimal? Round0(decimal? v) => v is null ? null : decimal.Round(v.Value, 0);
    private static decimal? Round1(decimal? v) => v is null ? null : decimal.Round(v.Value, 1);
}
