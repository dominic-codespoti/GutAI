namespace GutAI.Infrastructure.Data;

/// <summary>Everything about the query itself that's independent of which candidate is
/// being scored — built once per search call instead of recomputed per candidate.</summary>
internal readonly record struct FoodQueryContext(
    string QueryLower,
    string NormalizedName,
    string[] RawTokens,
    string[] RawTokenStems,
    string[] ExpandedTokenStems,
    HashSet<string> RawTokenSet,
    HashSet<string> RawTokenStemSet,
    string QueryStem,
    bool QueryHasBrand,
    bool QueryHasPreparationMethod,
    ulong MacroArchetypeMask)
{
    public static FoodQueryContext Build(string query, IReadOnlySet<string> knownBrandTokens)
    {
        var queryLower = query.Trim().ToLowerInvariant();
        var rawTokens = FoodTextNormalizer.Tokenize(queryLower);
        var tokenStems = new string[rawTokens.Length];
        var rawTokenSet = new HashSet<string>(rawTokens, StringComparer.OrdinalIgnoreCase);
        var rawTokenStemSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queryHasBrand = false;
        var queryHasPreparationMethod = false;
        for (var i = 0; i < rawTokens.Length; i++)
        {
            var stem = FoodTextNormalizer.Depluralize(rawTokens[i]);
            tokenStems[i] = stem;
            rawTokenStemSet.Add(stem);
            if (knownBrandTokens.Contains(rawTokens[i]))
                queryHasBrand = true;
            if (FoodQualityTerms.PreparationMethodTerms.Contains(rawTokens[i]))
                queryHasPreparationMethod = true;
        }

        var expandedTokenStems = FoodTextNormalizer.DepluralizeAll(FoodSynonyms.Expand(queryLower, rawTokens));

        return new(
            queryLower,
            FoodTextNormalizer.NormalizeFoodName(queryLower),
            rawTokens,
            tokenStems,
            expandedTokenStems,
            rawTokenSet,
            rawTokenStemSet,
            FoodTextNormalizer.Depluralize(queryLower),
            queryHasBrand,
            queryHasPreparationMethod,
            FoodMacroArchetypes.GetQueryMaskFromStems(tokenStems));
    }
}
