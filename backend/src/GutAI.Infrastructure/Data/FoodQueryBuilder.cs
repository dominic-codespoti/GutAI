using Lucene.Net.Index;
using Lucene.Net.Search;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// Builds the Lucene BooleanQuery for food search, including multi-word synonym expansion.
/// </summary>
internal static class FoodQueryBuilder
{
    // Colloquial multi-word synonyms (query-time expansion)
    private static readonly Dictionary<string, string[]> MultiWordSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orange juice"] = ["orange", "juice", "raw"],
        ["chicken breast"] = ["chicken", "broilers", "breast", "meat"],
        ["white rice"] = ["rice", "white", "long", "grain"],
        ["brown rice"] = ["rice", "brown", "long", "grain"],
        ["sweet potato"] = ["sweet", "potato", "raw"],
        ["olive oil"] = ["oil", "olive", "salad", "cooking"],
        ["white bread"] = ["bread", "white"],
        ["ground beef"] = ["beef", "ground"],
        ["whole milk"] = ["milk", "whole"],
        ["corn tortilla"] = ["tortilla", "corn"],
        ["rice cake"] = ["rice", "cake", "puffed"],
        ["rice cakes"] = ["rice", "cake", "puffed"],
    };

    public static string[] ExpandMultiWordSynonyms(string queryLower, string[] analyzedTokens)
    {
        foreach (var (key, expansion) in MultiWordSynonyms)
        {
            if (queryLower.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                var stemmedExpansion = expansion.Select(t => StemSingle(t)).Where(t => t.Length > 0);
                return analyzedTokens.Concat(stemmedExpansion).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        return analyzedTokens;
    }

    /// <summary>Stem a single token through Porter for consistent index matching.</summary>
    public static string StemSingle(string token)
    {
        var stemmer = new Lucene.Net.Tartarus.Snowball.Ext.PorterStemmer();
        stemmer.SetCurrent(token.ToLowerInvariant());
        stemmer.Stem();
        return stemmer.Current;
    }

    public static BooleanQuery Build(string queryLower, string[] rawTokens, string[] analyzedTokens, string[] expandedTokens, IEnumerable<Guid> boostIds)
    {
        var boolQuery = new BooleanQuery();

        // 0) PERSONALIZATION BOOST (JIT)
        foreach (var id in boostIds)
        {
            var termQuery = new TermQuery(new Term("Id", id.ToString()));
            termQuery.Boost = 50.0f;
            boolQuery.Add(termQuery, Occur.SHOULD);
        }

        // 1) Exact phrase on name using analyzed (stemmed) tokens
        if (analyzedTokens.Length > 0)
        {
            var phraseQuery = new PhraseQuery { Slop = 2 };
            foreach (var token in analyzedTokens)
                phraseQuery.Add(new Term("name", token));
            phraseQuery.Boost = 12f;
            boolQuery.Add(phraseQuery, Occur.SHOULD);
        }

        // 2) Phrase on primary noun field
        if (analyzedTokens.Length > 0)
        {
            var primaryPhrase = new PhraseQuery { Slop = 1 };
            foreach (var token in analyzedTokens)
                primaryPhrase.Add(new Term("primary", token));
            primaryPhrase.Boost = 16f;
            boolQuery.Add(primaryPhrase, Occur.SHOULD);
        }

        // 3) Per-token on primary noun (heavily boosted)
        foreach (var token in expandedTokens)
        {
            bool isSynonym = !analyzedTokens.Contains(token);
            float scale = isSynonym ? 0.6f : 1f;

            boolQuery.Add(new TermQuery(new Term("primary", token)) { Boost = 10f * scale }, Occur.SHOULD);
            if (token.Length >= 2)
                boolQuery.Add(new PrefixQuery(new Term("primary", token)) { Boost = 6f * scale }, Occur.SHOULD);
        }

        // 4) Per analyzed token on name field: exact + prefix + fuzzy
        foreach (var token in expandedTokens)
        {
            bool isSynonym = !analyzedTokens.Contains(token);
            float scale = isSynonym ? 0.6f : 1f;

            boolQuery.Add(new TermQuery(new Term("name", token)) { Boost = 5f * scale }, Occur.SHOULD);
            if (token.Length >= 2)
                boolQuery.Add(new PrefixQuery(new Term("name", token)) { Boost = 3f * scale }, Occur.SHOULD);
            if (token.Length >= 3)
                boolQuery.Add(new FuzzyQuery(new Term("name", token), 1) { Boost = 1f * scale }, Occur.SHOULD);
        }

        // 5) Also search with raw (un-stemmed) tokens for exact substring matches
        foreach (var token in rawTokens)
        {
            boolQuery.Add(new TermQuery(new Term("name", token)) { Boost = 4f }, Occur.SHOULD);
            boolQuery.Add(new TermQuery(new Term("brand", token)) { Boost = 25f }, Occur.SHOULD);
            if (token.Length >= 2)
                boolQuery.Add(new PrefixQuery(new Term("name", token)) { Boost = 2f }, Occur.SHOULD);
        }

        // 6) Exact full match on lowered name
        boolQuery.Add(new TermQuery(new Term("name_exact", queryLower)) { Boost = 50f }, Occur.SHOULD);

        // 7) Multi-word: require at least one token to appear
        if (rawTokens.Length > 1)
        {
            var mustMatchAny = new BooleanQuery();
            foreach (var token in analyzedTokens)
            {
                mustMatchAny.Add(new TermQuery(new Term("name", token)), Occur.SHOULD);
                mustMatchAny.Add(new TermQuery(new Term("primary", token)), Occur.SHOULD);
            }
            foreach (var token in rawTokens)
                mustMatchAny.Add(new TermQuery(new Term("name", token)), Occur.SHOULD);
            mustMatchAny.MinimumNumberShouldMatch = 1;
            boolQuery.Add(mustMatchAny, Occur.MUST);
        }

        return boolQuery;
    }
}
