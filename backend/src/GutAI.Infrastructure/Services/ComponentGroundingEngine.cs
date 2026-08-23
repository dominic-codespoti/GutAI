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
public sealed class ComponentGroundingEngine(IFoodSearchService foodSearch)
{
    /// <summary>Frozen auto-select floor. Deliberately a constant, not config.</summary>
    public const decimal MinAutoSelectConfidence = 0.85m;

    public const int MaxCandidates = 3;

    public async Task<GroundedItem> GroundAsync(
        ScannedComponent component, CancellationToken ct = default)
    {
        var query = QuerySanitizer.Sanitize(component.Name);
        var resolution = await foodSearch.ResolveAsync(query, boostIds: [], ct);

        var candidates = new[] { resolution.Selected }
            .Concat(resolution.Alternatives)
            .Where(p => p is not null)
            .Take(MaxCandidates)
            .Select(p => new GroundingCandidateDto(
                p!.Name, p.Id, MapSource(p.DataSource), p.MatchConfidence))
            .ToList();

        var attempt = new GroundingAttemptDto
        {
            Query = query,
            ResolutionStatus = resolution.Status.ToString().ToLowerInvariant(),
            AutoSelected = false,
            Candidates = candidates,
            MatchConfidence = resolution.MatchConfidence,
            Method = "resolve_async",
        };

        var autoSelected = resolution.Status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable
                           && resolution.Selected is not null
                           && resolution.MatchConfidence >= MinAutoSelectConfidence;

        if (!autoSelected || resolution.Selected is null)
        {
            // Ambiguous / Unresolved / low-confidence: keep the component as-is,
            // expose candidates, do NOT guess.
            return new GroundedItem(component, null, attempt with { AutoSelected = false });
        }

        var selected = resolution.Selected;
        var groundedAttempt = attempt with
        {
            AutoSelected = true,
            SelectedFoodProductId = selected.Id,
            CanonicalName = selected.Name,
        };

        return new GroundedItem(component, selected, groundedAttempt);
    }

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
    GroundingAttemptDto Attempt)
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
            Calories = grounded ? Round0(p!.Calories100g * factor) : null,
            ProteinG = grounded ? Round1(p!.Protein100g * factor) : null,
            CarbsG = grounded ? Round1(p!.Carbs100g * factor) : null,
            FatG = grounded ? Round1(p!.Fat100g * factor) : null,
            FiberG = grounded ? Round1(p!.Fiber100g * factor) : null,
            SugarG = grounded ? Round1(p!.Sugar100g * factor) : null,
            SodiumMg = grounded ? Round0(p!.SodiumMg100g * factor) : null,
            MatchConfidence = grounded ? p!.MatchConfidence : 1m,
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
        "" or null => "ai",
        var other => other.ToLowerInvariant(),
    };

    private static decimal? Round0(decimal? v) => v is null ? null : decimal.Round(v.Value, 0);
    private static decimal? Round1(decimal? v) => v is null ? null : decimal.Round(v.Value, 1);
}
