namespace GutAI.Infrastructure.Data;

/// <summary>Everything about the query itself that's independent of which candidate is
/// being scored — built once per search call instead of recomputed per candidate.</summary>
internal readonly record struct FoodQueryContext(
    string QueryLower, string[] RawTokens, string[] ExpandedTokens, string QueryStem, bool QueryHasBrand)
{
    public static FoodQueryContext Build(string query, IReadOnlySet<string> knownBrandTokens)
    {
        var queryLower = query.Trim().ToLowerInvariant();
        var rawTokens = FoodTextNormalizer.Tokenize(queryLower);
        var expandedTokens = FoodSynonyms.Expand(queryLower, rawTokens);
        var queryStem = FoodTextNormalizer.Depluralize(queryLower);
        var queryHasBrand = rawTokens.Any(knownBrandTokens.Contains);
        return new(queryLower, rawTokens, expandedTokens, queryStem, queryHasBrand);
    }
}
