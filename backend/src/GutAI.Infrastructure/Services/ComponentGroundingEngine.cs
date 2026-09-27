using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Data;


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
public sealed partial class ComponentGroundingEngine(
    IFoodSearchService foodSearch,
    bool multiQueryAutoSelect = false)
{
    public async Task<GroundedItem> GroundAsync(
        ScannedComponent component,
        GroundingContext? context = null,
        CancellationToken ct = default)
    {
        var queries = BuildResolverQueries(component);
        var boostIds = context?.BoostIds ?? [];
        var resolutions = await Task.WhenAll(
            queries.Select(query => foodSearch.ResolveAsync(query, boostIds, ct)));
        var primary = resolutions[0];
        var mergedCandidates = resolutions
            .SelectMany(resolution => new[] { resolution.Selected }
                .Concat(resolution.Alternatives)
                .Where(product => product is not null)
                .Select(product => product!));

        var decision = GroundingPolicy.Decide(primary, component);
        var chosenResolution = primary;
        if (multiQueryAutoSelect && !decision.AutoSelected)
        {
            var passing = resolutions
                .Skip(1)
                .Select((resolution, index) => (Resolution: resolution, Decision: GroundingPolicy.Decide(resolution, component), Index: index))
                .Where(candidate => candidate.Decision.AutoSelected)
                .OrderByDescending(candidate => candidate.Resolution.MatchConfidence)
                .ThenBy(candidate => candidate.Index)
                .FirstOrDefault();
            if (passing.Decision is not null)
            {
                chosenResolution = passing.Resolution;
                decision = passing.Decision;
            }
        }

        var status = chosenResolution.Status;
        if (chosenResolution.Selected is not null
            && status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable
            && decision.Reason is GroundingPolicy.Reasons.CompatibilityVeto or GroundingPolicy.Reasons.FoodFormVeto)
            status = FoodResolutionStatus.Ambiguous;
        if (chosenResolution.Selected is null && mergedCandidates.Any() && status == FoodResolutionStatus.Unresolved)
            status = FoodResolutionStatus.Ambiguous;

        var candidateProducts = decision.AutoSelected
            ? new[] { decision.Selected! }
                .Concat(GroundingPolicy.RankCandidates(mergedCandidates, component)
                    .Where(product => !StringComparer.OrdinalIgnoreCase.Equals(
                        FoodCandidateIdentity.Of(product),
                        FoodCandidateIdentity.Of(decision.Selected!))))
                .Take(GroundingPolicy.MaxCandidates)
                .ToList()
            : GroundingPolicy.RankCandidates(mergedCandidates, component)
                .Take(GroundingPolicy.MaxCandidates)
                .ToList();
        var candidates = candidateProducts.Select(product =>
        {
            var flags = GroundingPolicy.DataQualityFlags(product);
            return new GroundingCandidateDto(
                product.Name,
                product.Id == Guid.Empty ? null : product.Id,
                MapSource(product.DataSource),
                product.MatchConfidence,
                product.Brand,
                product.ExternalId,
                product.SourceUrl,
                product.Calories100g,
                product.Protein100g,
                product.Carbs100g,
                product.Fat100g,
                product.Fiber100g,
                product.Sugar100g,
                product.SodiumMg100g,
                FoodCandidateIdentity.Of(product),
                flags.Count > 0 ? flags : null);
        }).ToList();

        var attempt = new GroundingAttemptDto
        {
            Query = queries[0],
            Queries = queries,
            ResolutionStatus = status.ToString().ToLowerInvariant(),
            AutoSelected = decision.AutoSelected,
            Candidates = candidates,
            MatchConfidence = chosenResolution.MatchConfidence,
            Method = "resolve_async",
            SelectedFoodProductId = decision.AutoSelected ? decision.Selected!.Id : null,
            CanonicalName = decision.AutoSelected ? decision.Selected!.Name : null,
        };

        return new GroundedItem(component, decision.Selected, attempt, candidateProducts);
    }

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

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\s+(?:with\s+)?(?:rice\s+)?(?:bowl|plate|platter|dish|set|meal)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ServingSuffixPattern();

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

public sealed record GroundingContext(IReadOnlyCollection<Guid> BoostIds);

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
    public MealDraftItemDto ToItem()
    {
        var p = ResolvedProduct;
        var basis = p is null ? null : NutritionCalculator.BasisFrom(p);
        var amounts = basis is null ? null : NutritionCalculator.Compute(basis, Original.EstimatedGramsMidpoint);

        return new MealDraftItemDto
        {
            ItemId = Guid.NewGuid(),
            Name = Original.Name,
            CanonicalName = p?.Name,
            FoodProductId = p?.Id,
            Source = p is null ? "ai" : SourceKey(p.DataSource),
            Grams = Original.EstimatedGramsMidpoint,
            PortionLowGrams = Original.EstimatedGramsLow,
            PortionHighGrams = Original.EstimatedGramsHigh,
            PortionMethod = "vision_estimate",
            ServingHintUnit = Original.ServingHintUnit,
            ServingHintUnitPlural = Original.ServingHintUnitPlural,
            ServingHintUnitGrams = Original.ServingHintUnitGrams,
            PortionConfidence = Original.PortionConfidence,
            IsGarnish = Original.IsGarnish,
            Calories = amounts?.Calories,
            ProteinG = amounts?.ProteinG,
            CarbsG = amounts?.CarbsG,
            FatG = amounts?.FatG,
            FiberG = amounts?.FiberG,
            SugarG = amounts?.SugarG,
            SodiumMg = amounts?.SodiumMg,
            MatchConfidence = p?.MatchConfidence ?? 0m,
            VisionConfidence = Original.Confidence,
            CandidateNames = Attempt.Candidates.Select(c => c.Name).ToList(),
            Grounding = Attempt,
            Per100g = basis,
            NutritionProvenance = NutritionProvenanceRules.ForDraftSource(p?.DataSource, basis is not null).ToString(),
            NeedsChoice = p is null && CandidateProducts.Count > 0,
        };
    }

    private static string SourceKey(string? dataSource) => dataSource?.ToLowerInvariant() switch
    {
        "usda" or "usda fdc" or "fdc" => "usda",
        "open food facts" or "off" => "off",
        "au" or "australian" or "afcd" => "au",
        "" or null => "db",
        var other => other.ToLowerInvariant(),
    };
}
