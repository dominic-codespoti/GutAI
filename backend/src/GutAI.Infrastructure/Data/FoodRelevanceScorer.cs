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

    /// <summary>Per-token name evidence strongly prioritizes complete food-name matches.</summary>
    private const float NameTokenMatchWeight = 500f;

    /// <summary>Severe macro implausibility remains stronger than lexical matching, even
    /// when the candidate matches every query token.</summary>
    private const float ImplausibilityWeight = 100f;

    /// <summary>Bonus for preparation-first queries whose catalog match leads with the
    /// food noun that appears last in the query.</summary>
    private const float PreparationFirstQueryHeadBonus = 100f;

    /// <summary>Bonus for candidates the user has previously logged, comparable in magnitude
    /// to an exact-name-match bonus so personalization meaningfully re-orders ties without
    /// overriding a clearly-better textual match.</summary>
    public const float PersonalizationBoost = 50f;

    /// <summary>Minimum meaningful lexical/alias/brand overlap a candidate must have with the
    /// query to be returned at all. Without this gate, a query with zero real overlap against
    /// the whole catalog would still rank and return the highest-quality candidates — a
    /// confident wrong guess instead of "no match". See the <c>isEligible</c> output below.</summary>
    public static float Score(FoodCandidate candidate, in FoodQueryContext ctx) =>
        Score(candidate, ctx, out _, out _, out _, out _);

    /// <summary>Scores a candidate and reports the signals a resolution decision needs:
    /// whether it has any meaningful overlap with the query at all (<paramref name="isEligible"/>),
    /// whether its name is a literal match (<paramref name="isExactMatch"/>), and its best
    /// token-coverage fraction (<paramref name="coverage"/>), used to derive display confidence.</summary>
    public static float Score(FoodCandidate candidate, in FoodQueryContext ctx,
        out bool isEligible, out bool isExactMatch, out float coverage, out float tokenMatchScore)
    {
        var dto = candidate.Dto;
        var nameLower = candidate.NameLower;
        var queryLower = ctx.QueryLower;
        var queryTokens = ctx.RawTokens;
        var queryNamesBrand = IsBrandNamed(candidate, ctx);

        float score = ComputeCoverageSignals(candidate, ctx, out float nameCoverage, out float primaryCoverage, out tokenMatchScore);
        var brandSignal = ComputeBrandMatchSignal(candidate, queryLower, queryNamesBrand);
        score += ComputeSourceKindSignal(candidate, queryTokens.Length, ctx.QueryHasBrand);
        score += brandSignal;
        score += FoodQualityTerms.ScoreConditionalPenalties(nameLower, queryLower, queryTokens.Length);
        score += FoodQualityTerms.ScoreModifierRules(nameLower, queryLower);
        score += FoodMacroArchetypes.Score(candidate.Macros, ctx.MacroArchetypeMask, candidate.NameTokenStems) * ImplausibilityWeight;

        if (queryTokens.Length >= 2)
        {
            if (primaryCoverage < 0.5f) score -= 20f;
            else if (primaryCoverage == 0.5f && nameCoverage < 1f) score -= 10f;
        }

        isExactMatch = candidate.NormalizedName == ctx.NormalizedName;
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

    public static bool IsBrandNamed(FoodCandidate candidate, in FoodQueryContext ctx)
    {
        var brandTokenStems = candidate.BrandTokenStems;
        for (var i = 0; i < brandTokenStems.Length; i++)
        {
            if (brandTokenStems[i].Length >= 4 && ctx.RawTokenStemSet.Contains(brandTokenStems[i]))
                return true;
        }

        return false;
    }

    public static bool MatchesEveryQueryToken(FoodCandidate candidate, in FoodQueryContext ctx)
    {
        var nameTokenStems = candidate.NameTokenStems;
        foreach (var queryTokenStem in ctx.RawTokenStems)
        {
            var found = false;
            for (var i = 0; i < nameTokenStems.Length; i++)
            {
                if (nameTokenStems[i] == queryTokenStem)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return ctx.RawTokenStems.Length > 0;
    }

    private static float ComputeCoverageSignals(
        FoodCandidate candidate, in FoodQueryContext ctx, out float nameCoverage, out float primaryCoverage, out float tokenMatchScore)
    {
        var queryLower = ctx.QueryLower;
        var queryTokens = ctx.RawTokens;
        var allQueryTokens = ctx.ExpandedTokenStems;
        var nameLower = candidate.NameLower;
        var primaryTokens = candidate.PrimaryTokenStems;
        var nameTokens = candidate.NameTokenStems;

        float score = 0f;
        float primaryMatch = 0f;
        foreach (var qt in allQueryTokens)
            primaryMatch += BestTokenMatch(primaryTokens, qt);
        primaryCoverage = allQueryTokens.Length > 0 ? primaryMatch / allQueryTokens.Length : 0f;
        score += primaryCoverage * 20f;
        if (primaryCoverage >= 1f) score += 15f;

        float nameMatch = 0f;
        foreach (var qt in allQueryTokens)
            nameMatch += BestTokenMatch(nameTokens, qt);
        nameCoverage = allQueryTokens.Length > 0 ? nameMatch / allQueryTokens.Length : 0f;
        tokenMatchScore = nameMatch * NameTokenMatchWeight;
        score += nameMatch * NameTokenMatchWeight + nameCoverage * 15f;
        if (nameCoverage >= 1f) score += 100f;

        if (queryTokens.Length > 0 && primaryTokens.Length > 0)
        {
            var pt0 = primaryTokens[0];
            var qt0 = queryTokens[0];
            float firstTokenBonus = 0f;
            if (pt0 == qt0) firstTokenBonus = 20f;
            else if (pt0.StartsWith(qt0) && pt0.Length <= qt0.Length + 3) firstTokenBonus = 12f;
            else if (qt0.StartsWith(pt0)) firstTokenBonus = 10f;
            else if (HasQueryToken(primaryTokens, ctx)) firstTokenBonus = 15f * nameCoverage;

            if (queryTokens.Length >= 2 && pt0 == qt0)
                firstTokenBonus *= nameCoverage;
            score += firstTokenBonus;
        }

        if (ctx.QueryHasPreparationMethod && queryTokens.Length >= 3
            && nameTokens.Length > 0 && nameTokens[0] == ctx.RawTokenStems[^1])
            score += PreparationFirstQueryHeadBonus;

        if (queryTokens.Length >= 2 && nameCoverage >= 1f)
        {
            score += 15f;
            if (HasQueryToken(primaryTokens, ctx))
                score += 20f;
        }

        var nameStem = candidate.NameStem;
        var queryStem = ctx.QueryStem;
        if (nameLower == queryLower) score += 50f;
        else if (nameStem == queryStem) score += 45f;
        if (nameLower.StartsWith(queryLower)) score += 20f;
        else if (nameStem.StartsWith(queryStem) && Math.Abs(nameStem.Length - queryStem.Length) <= nameStem.Length) score += 18f;

        if (queryTokens.Length == 1 && primaryTokens.Length > 0)
        {
            var descriptorTokens = candidate.DescriptorTokens;
            var descriptorTokenStems = candidate.DescriptorTokenStems;
            for (var i = 0; i < descriptorTokens.Length; i++)
            {
                if (descriptorTokens[i] == queryLower || descriptorTokenStems[i] == queryStem)
                {
                    score += 20f;
                    break;
                }
            }
        }

        if (queryTokens.Length <= 2 && !ctx.QueryHasPreparationMethod)
        {
            foreach (var term in FoodQualityTerms.RawFreshTerms)
                if (nameLower.Contains(term)) score += 12f;
            foreach (var term in FoodQualityTerms.PlainTerms)
                if (nameLower.Contains(term)) score += 5f;
        }

        if (queryTokens.Length <= 2)
        {
            foreach (var term in FoodQualityTerms.ProcessedTerms)
            {
                if (nameLower.Contains(term) && !queryLower.Contains(term)
                    && !ctx.RawTokenSet.Contains(term) && !ctx.RawTokenStemSet.Contains(term))
                {
                    score -= queryTokens.Length == 1 ? 12f : 6f;
                    break;
                }
            }
        }

        return score;
    }

    private static bool HasQueryToken(string[] candidateTokens, in FoodQueryContext ctx)
    {
        foreach (var token in candidateTokens)
            if (ctx.RawTokenSet.Contains(token) || ctx.RawTokenStemSet.Contains(token))
                return true;
        return false;
    }

    private static float BestTokenMatch(string[] candidateTokens, string queryToken)
    {
        var hasPrefixMatch = false;
        foreach (var token in candidateTokens)
        {
            if (token == queryToken)
                return 1f;

            if (token.Length >= 3 && queryToken.StartsWith(token, StringComparison.Ordinal)
                || queryToken.Length >= 3 && token.StartsWith(queryToken, StringComparison.Ordinal))
                hasPrefixMatch = true;
        }

        return hasPrefixMatch ? 0.25f : 0f;
    }

    private static float ComputeSourceKindSignal(
        FoodCandidate candidate, int queryTokenCount, bool queryHasBrand)
    {
        var dto = candidate.Dto;
        if (queryTokenCount > 3) return 0f;

        float score = 0f;
        if (dto.FoodKind == FoodKind.WholeFood)
            score += 10f;
        else if (dto.FoodKind == FoodKind.Branded && !queryHasBrand)
        {
            score -= 25f;
            if (candidate.NameTokenCount <= 2 && !string.IsNullOrEmpty(dto.Brand))
                score -= 20f;
        }

        if (!queryHasBrand && !string.IsNullOrEmpty(dto.Brand) && dto.Brand.Length > 1)
            score -= 5f;

        return score;
    }

    private static float ComputeBrandMatchSignal(FoodCandidate candidate, string queryLower, bool queryNamesBrand)
    {
        var brand = candidate.Dto.Brand;
        if (string.IsNullOrEmpty(brand) || !queryNamesBrand) return 0f;
        return queryLower.Contains(brand, StringComparison.OrdinalIgnoreCase) ? 40f : 20f;
    }
}
