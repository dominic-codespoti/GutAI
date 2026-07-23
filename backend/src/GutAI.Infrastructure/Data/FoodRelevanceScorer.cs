using GutAI.Domain.Enums;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// Query-dependent relevance: token coverage, exact/prefix match bonuses, brand handling,
/// universal and food-specific modifier rules, and nutrition plausibility. This is the
/// sole text-relevance signal now that there's no Lucene BM25 score to blend with — it
/// carries the full weight of "does this candidate match the query" that used to be split
/// (and duplicated) across a Lucene BooleanQuery and this re-ranking pass.
/// </summary>
internal static class FoodRelevanceScorer
{
    /// <summary>Weight applied to the precomputed, query-independent quality score when
    /// combining with relevance. Matches the old Lucene custom-score blend factor — quality
    /// acts as a moderate booster, not a dominant signal.</summary>
    public const float QualityWeight = 8f;

    /// <summary>Nutrition-implausibility is scaled up specifically here — this scorer has
    /// no Lucene BM25 term to blend with, so an exact lexical match on a mislabeled catalog
    /// entry can accumulate 80-90+ points of coverage/exact-match bonus that the archetypes'
    /// raw severity values alone can't overcome. <see cref="NaturalLanguageFallbackService"/>'s
    /// separate <c>ScoreMatch</c> also calls into <c>FoodMacroArchetypes</c> but is calibrated
    /// against its own (unscaled) bonus magnitudes, so the multiplier lives at this call site,
    /// not inside the shared archetype scorer.</summary>
    private const float ImplausibilityWeight = 10f;

    /// <summary>Bonus for candidates the user has previously logged, comparable in magnitude
    /// to an exact-name-match bonus so personalization meaningfully re-orders ties without
    /// overriding a clearly-better textual match.</summary>
    public const float PersonalizationBoost = 50f;

    /// <summary>Minimum meaningful lexical/alias/brand overlap a candidate must have with the
    /// query to be returned at all. Without this gate, a query with zero real overlap against
    /// the whole catalog would still rank and return the highest-quality candidates — a
    /// confident wrong guess instead of "no match". See the <c>isEligible</c> output below.</summary>
    public static float Score(FoodCandidate candidate, in FoodQueryContext ctx) =>
        Score(candidate, ctx, out _, out _, out _);

    /// <summary>Scores a candidate and reports the signals a resolution decision needs:
    /// whether it has any meaningful overlap with the query at all (<paramref name="isEligible"/>),
    /// whether its name is a literal match (<paramref name="isExactMatch"/>), and its best
    /// token-coverage fraction (<paramref name="coverage"/>), used to derive display confidence.</summary>
    public static float Score(FoodCandidate candidate, in FoodQueryContext ctx,
        out bool isEligible, out bool isExactMatch, out float coverage)
    {
        var dto = candidate.Dto;
        var nameLower = candidate.NameLower;
        var queryLower = ctx.QueryLower;
        var queryTokens = ctx.RawTokens;

        float score = ComputeCoverageSignals(candidate, ctx, out float nameCoverage, out float primaryCoverage);
        var brandSignal = ComputeBrandMatchSignal(dto, queryLower, queryTokens);
        score += ComputeSourceKindSignal(dto, queryTokens, ctx.QueryHasBrand);
        score += brandSignal;
        score += FoodQualityTerms.ScoreConditionalPenalties(nameLower, queryLower, queryTokens.Length);
        score += FoodQualityTerms.ScoreModifierRules(nameLower, queryLower);
        score += FoodMacroArchetypes.Score(dto, queryLower) * ImplausibilityWeight;

        if (queryTokens.Length >= 2)
        {
            if (primaryCoverage < 0.5f) score -= 20f;
            else if (primaryCoverage == 0.5f) score -= 10f;
        }

        var nameStem = FoodTextNormalizer.Depluralize(nameLower);
        isExactMatch = nameLower == queryLower || nameStem == ctx.QueryStem;
        coverage = Math.Max(primaryCoverage, nameCoverage);
        isEligible = isExactMatch || coverage > 0f || brandSignal > 0f;

        return score;
    }

    /// <summary>Maps the eligibility signals from <see cref="Score"/> to a 0–1 display
    /// confidence. This is the single confidence calculation for auto-selected food matches —
    /// callers must not compute their own separate confidence heuristic on top of this.</summary>
    public static decimal ComputeConfidence(bool isExactMatch, float coverage)
    {
        if (isExactMatch) return 1.0m;
        if (coverage >= 1f) return 0.85m;
        if (coverage <= 0f) return 0m;
        return Math.Round(0.4m + 0.4m * (decimal)coverage, 2);
    }

    private static float ComputeCoverageSignals(
        FoodCandidate candidate, in FoodQueryContext ctx, out float nameCoverage, out float primaryCoverage)
    {
        var queryLower = ctx.QueryLower;
        var queryTokens = ctx.RawTokens;
        var allQueryTokens = ctx.ExpandedTokens;
        var nameLower = candidate.NameLower;
        var primaryTokens = candidate.PrimaryTokens;
        var nameTokens = candidate.NameTokens;

        float score = 0f;

        int primaryMatched = allQueryTokens.Count(qt => primaryTokens.Any(pt => pt == qt || pt.StartsWith(qt) || qt.StartsWith(pt)));
        primaryCoverage = allQueryTokens.Length > 0 ? (float)primaryMatched / allQueryTokens.Length : 0f;
        score += primaryCoverage * 20f;
        if (primaryCoverage >= 1f) score += 15f;

        int nameMatched = allQueryTokens.Count(qt => nameTokens.Any(nt => nt == qt || nt.StartsWith(qt) || qt.StartsWith(nt)));
        nameCoverage = allQueryTokens.Length > 0 ? (float)nameMatched / allQueryTokens.Length : 0f;
        score += nameCoverage * 15f;
        if (nameCoverage >= 1f) score += 10f;

        if (queryTokens.Length > 0 && primaryTokens.Length > 0)
        {
            var pt0 = primaryTokens[0];
            var qt0 = queryTokens[0];
            float firstTokenBonus = 0f;
            if (pt0 == qt0) firstTokenBonus = 20f;
            else if (pt0.StartsWith(qt0) && pt0.Length <= qt0.Length + 3) firstTokenBonus = 12f;
            else if (qt0.StartsWith(pt0)) firstTokenBonus = 10f;

            if (queryTokens.Length >= 2)
                firstTokenBonus *= nameCoverage;

            score += firstTokenBonus;
        }

        if (queryTokens.Length >= 2 && nameCoverage >= 1f)
            score += 15f;

        var nameStem = FoodTextNormalizer.Depluralize(nameLower);
        var queryStem = ctx.QueryStem;
        if (nameLower == queryLower) score += 50f;
        else if (nameStem == queryStem) score += 45f;
        if (nameLower.StartsWith(queryLower)) score += 20f;
        else if (nameStem.StartsWith(queryStem) && Math.Abs(nameStem.Length - queryStem.Length) <= nameStem.Length) score += 18f;

        // Single-word query matching a USDA descriptor (e.g. "cheddar" in "Cheese, cheddar")
        if (queryTokens.Length == 1 && primaryTokens.Length > 0)
        {
            var commaIdx = nameLower.IndexOf(',');
            var descriptorPart = commaIdx >= 0 ? nameLower[(commaIdx + 1)..].Trim() : "";
            var descTokens = descriptorPart.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (descTokens.Any(dt => dt == queryLower || FoodTextNormalizer.Depluralize(dt) == queryStem))
                score += 20f;
        }

        if (queryTokens.Length <= 2 && !queryTokens.Any(FoodQualityTerms.PreparationMethodTerms.Contains))
        {
            foreach (var term in FoodQualityTerms.RawFreshTerms)
                if (nameLower.Contains(term)) score += 12f;
            foreach (var term in FoodQualityTerms.PlainTerms)
                if (nameLower.Contains(term)) score += 5f;
        }

        if (queryTokens.Length <= 2)
        {

            var queryTokenSet = new HashSet<string>(queryTokens, StringComparer.OrdinalIgnoreCase);
            var queryDepluralized = queryTokens.Select(FoodTextNormalizer.Depluralize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var term in FoodQualityTerms.ProcessedTerms)
            {
                if (nameLower.Contains(term) && !queryLower.Contains(term)
                    && !queryTokenSet.Contains(term) && !queryDepluralized.Contains(term))
                {
                    score -= queryTokens.Length == 1 ? 12f : 6f;
                    break;
                }
            }
        }

        return score;
    }

    private static float ComputeSourceKindSignal(GutAI.Application.Common.DTOs.FoodProductDto dto, string[] queryTokens, bool queryHasBrand)
    {
        float score = 0f;
        if (queryTokens.Length > 3) return score;

        if (dto.FoodKind == FoodKind.WholeFood)
            score += 10f;
        else if (dto.FoodKind == FoodKind.Branded && !queryHasBrand)
        {
            score -= 25f;

            // A single-word branded product name (e.g. "Eggs", "Garlic", "Coffee") that
            // matches a common whole-food query is almost always misleading — it's a
            // candy, sausage, or drink mix, not the actual whole food.
            var nameTokens = dto.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (nameTokens.Length <= 2 && !string.IsNullOrEmpty(dto.Brand))
                score -= 20f;
        }

        if (!queryHasBrand && !string.IsNullOrEmpty(dto.Brand) && dto.Brand.Length > 1)
            score -= 5f;

        return score;
    }

    private static float ComputeBrandMatchSignal(GutAI.Application.Common.DTOs.FoodProductDto dto, string queryLower, string[] queryTokens)
    {
        if (string.IsNullOrEmpty(dto.Brand)) return 0f;

        var brandLower = dto.Brand.ToLowerInvariant();
        if (queryLower.Contains(brandLower)) return 40f;
        if (queryTokens.Any(brandLower.Contains)) return 20f;
        return 0f;
    }
}
