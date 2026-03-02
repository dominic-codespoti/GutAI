using GutAI.Application.Common.DTOs;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Synonym;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.Queries.Function;
using Lucene.Net.Queries.Function.ValueSources;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace GutAI.Infrastructure.Data;

public sealed class FoodSearchIndex : IDisposable
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    private readonly RAMDirectory _directory;
    private readonly Analyzer _analyzer;
    private readonly SearcherManager _searcherManager;
    private readonly object _writeLock = new();

    private readonly List<FoodProductDto> _foods = [];
    private readonly Dictionary<string, int> _nameToIndex = new(StringComparer.OrdinalIgnoreCase);

    public FoodSearchIndex()
    {
        _directory = new RAMDirectory();
        _analyzer = new FoodAnalyzer();

        using var writer = NewWriter();
        writer.Commit();

        _searcherManager = new SearcherManager(_directory, null);
    }

    public FoodSearchIndex(IEnumerable<FoodProductDto> foods) : this()
    {
        AddRange(foods);
    }

    private IndexWriter NewWriter()
    {
        var config = new IndexWriterConfig(Version, _analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND,
            RAMBufferSizeMB = 16,
        };
        return new IndexWriter(_directory, config);
    }

    // ════════════════════════════════════════════════════════════════
    //  INDEX-TIME: pre-computed quality signals
    // ════════════════════════════════════════════════════════════════

    internal static string ExtractPrimaryNoun(string name)
    {
        // USDA convention: "PrimaryNoun, descriptor, descriptor"
        var commaIdx = name.IndexOf(',');
        if (commaIdx > 0)
            return name[..commaIdx].Trim();
        return name.Trim();
    }

    // Kept for backward compat (used by NaturalLanguageFallbackService scoring)
    internal static string NormalizeFoodName(string name)
    {
        var s = name.ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\([^)]*\)", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[,;:/\-–—]", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9 ]", "");
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var noise = new HashSet<string>
        {
            "with", "and", "in", "of", "style", "flavored", "flavoured",
            "ns", "as", "to", "the", "for", "a", "an",
            "nfs", "not", "further", "specified", "type", "all", "purpose",
            "usda", "commodity", "purchased", "commercially", "prepared"
        };
        return string.Join(" ", tokens.Where(t => !noise.Contains(t)).Select(Depluralize));
    }

    private static string Depluralize(string word)
    {
        if (word.Length <= 3) return word;
        if (word.EndsWith("ies") && word.Length > 4)
            return word[..^3] + "y";     // fryers → fryer handled below; berries → berry
        if (word.EndsWith("ers") && word.Length > 4)
            return word[..^1];            // broilers → broiler, fryers → fryer
        if (word.EndsWith("es") && word.Length > 4 &&
            !word.EndsWith("oes") && !word.EndsWith("ses") && !word.EndsWith("ches") && !word.EndsWith("shes"))
            return word[..^1];            // olives → olive (but not potatoes → potatoe)
        if (word.EndsWith('s') && !word.EndsWith("ss") && !word.EndsWith("us") && !word.EndsWith("is"))
            return word[..^1];            // bananas → banana, eggs → egg
        return word;
    }

    private static readonly string[] HardPenaltyTerms =
    [
        "frozen", "canned", "dehydrated", "powder", "mix",
        "mixture", "substitute", "imitation", "baby food", "infant", "formula",
        "alaska native", "industrial", "fast food",
        "ns as to", "usda commodity", "as purchased", "not further specified",
        "nfs", "ready-to-eat", "ready-to-heat", "glucose reduced", "stabilized",
        "nuggets", "nugget", "breaded", "patties", "patty", "stick", "sticks"
    ];

    private static readonly string[] SoftPenaltyTerms =
    [
        "navajo", "hopi", "southwest", "shoshone", "apache",
        "pasteurized", "restaurant", "commercial", "institutional"
    ];

    private static float ComputeStaticQuality(FoodProductDto dto)
    {
        var nameLower = dto.Name.ToLowerInvariant();
        float q = 0f;

        // Source trust
        if (dto.DataSource is "USDA" or "AUSNUT") q += 0.4f;

        // Nutrition completeness
        if (dto.Calories100g.HasValue) q += 0.06f;
        if (dto.Protein100g.HasValue) q += 0.04f;
        if (dto.Carbs100g.HasValue) q += 0.03f;
        if (dto.Fat100g.HasValue) q += 0.03f;
        if (dto.Fiber100g.HasValue) q += 0.02f;
        if (dto.Sugar100g.HasValue) q += 0.02f;

        // Whole-food boost
        if (dto.FoodKind == GutAI.Domain.Enums.FoodKind.WholeFood) q += 0.5f;
        else if (dto.FoodKind == GutAI.Domain.Enums.FoodKind.Unknown)
        {
            // Heuristic fallback for unclassified foods
            bool looksWhole = string.IsNullOrEmpty(dto.Brand) &&
                (string.IsNullOrEmpty(dto.Ingredients) || !dto.Ingredients.Contains(','));
            if (looksWhole) q += 0.5f;
        }

        // Name length — shorter = better
        if (dto.Name.Length <= 40)
            q += Math.Max(0f, 1f - dto.Name.Length / 60f) * 0.3f;
        else
            q -= (dto.Name.Length - 40) * (dto.Name.Length - 40) / 10000f;

        // Comma penalty (light — USDA uses structural commas)
        q -= dto.Name.Count(c => c == ',') * 0.05f;

        // Parenthetical penalty
        q -= dto.Name.Count(c => c == '(') * 0.15f;

        // Hard penalties
        foreach (var term in HardPenaltyTerms)
            if (nameLower.Contains(term)) q -= 1.2f;

        // Soft penalties
        foreach (var term in SoftPenaltyTerms)
            if (nameLower.Contains(term)) q -= 0.7f;

        return q;
    }

    // ════════════════════════════════════════════════════════════════
    //  ADD / INDEX
    // ════════════════════════════════════════════════════════════════

    public void AddRange(IEnumerable<FoodProductDto> foods)
    {
        lock (_writeLock)
        {
            using var writer = NewWriter();
            foreach (var food in foods)
            {
                if (_nameToIndex.ContainsKey(food.Name))
                    continue;

                var idx = _foods.Count;
                _foods.Add(food);
                _nameToIndex[food.Name] = idx;

                var primaryNoun = ExtractPrimaryNoun(food.Name);
                var quality = ComputeStaticQuality(food);

                var doc = new Document
                {
                    new StringField("idx", idx.ToString(), Field.Store.YES),
                    new TextField("name", food.Name, Field.Store.NO),
                    new TextField("primary", primaryNoun, Field.Store.NO),
                    new StringField("name_exact", food.Name.ToLowerInvariant(), Field.Store.NO),
                    new TextField("brand", food.Brand ?? "", Field.Store.NO),
                    new StringField("source", food.DataSource ?? "", Field.Store.NO),
                    new SingleDocValuesField("quality", quality),
                };

                writer.AddDocument(doc);
            }
            writer.Commit();
        }
        _searcherManager.MaybeRefresh();
    }

    public void Add(FoodProductDto food)
    {
        AddRange([food]);
    }

    // ════════════════════════════════════════════════════════════════
    //  SEARCH
    // ════════════════════════════════════════════════════════════════

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

    private string[] AnalyzeQuery(string text)
    {
        var tokens = new List<string>();
        using var reader = new StringReader(text);
        using var stream = _analyzer.GetTokenStream("name", reader);
        var termAttr = stream.GetAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
        stream.Reset();
        while (stream.IncrementToken())
            tokens.Add(termAttr.ToString());
        stream.End();
        return tokens.ToArray();
    }

    public List<FoodProductDto> Search(string query, int maxResults = 15)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var queryLower = query.Trim().ToLowerInvariant();
        var rawTokens = queryLower.Split([' ', ',', '(', ')', '/', '-'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (rawTokens.Length == 0)
            return [];

        // Run query through the same analyzer pipeline (stop → synonym → stem)
        var analyzedTokens = AnalyzeQuery(queryLower);
        if (analyzedTokens.Length == 0)
            analyzedTokens = rawTokens; // fallback

        // Multi-word synonym expansion at query time
        var expandedTokens = ExpandMultiWordSynonyms(queryLower, analyzedTokens);

        var boolQuery = BuildLuceneQuery(queryLower, rawTokens, analyzedTokens, expandedTokens);

        // Wrap in CustomScoreQuery to blend Lucene relevance with static quality
        var qualitySource = new SingleFieldSource("quality");
        var customQuery = new FoodCustomScoreQuery(boolQuery, new FunctionQuery(qualitySource));

        _searcherManager.MaybeRefreshBlocking();
        var searcher = _searcherManager.Acquire();
        try
        {
            var topDocs = searcher.Search(customQuery, maxResults * 4);
            var results = new List<(FoodProductDto food, float score)>();

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var idx = int.Parse(doc.Get("idx"));
                if (idx < _foods.Count)
                    results.Add((_foods[idx], scoreDoc.Score));
            }

            // Post-Lucene: apply query-dependent signals that need DTO access
            return results
                .OrderByDescending(r => FinalScore(r.food, r.score, queryLower, rawTokens, expandedTokens))
                .Take(maxResults)
                .Select(r => r.food)
                .ToList();
        }
        finally
        {
            _searcherManager.Release(searcher);
        }
    }

    private static string[] ExpandMultiWordSynonyms(string queryLower, string[] analyzedTokens)
    {
        foreach (var (key, expansion) in MultiWordSynonyms)
        {
            if (queryLower.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                // Stem the expansion values to match the stemmed index
                var stemmedExpansion = expansion.Select(t => StemSingle(t)).Where(t => t.Length > 0);
                return analyzedTokens.Concat(stemmedExpansion).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        return analyzedTokens;
    }

    // Stem a single token through Porter for consistent index matching
    private static string StemSingle(string token)
    {
        var stemmer = new Lucene.Net.Tartarus.Snowball.Ext.PorterStemmer();
        stemmer.SetCurrent(token.ToLowerInvariant());
        stemmer.Stem();
        return stemmer.Current;
    }

    private static BooleanQuery BuildLuceneQuery(string queryLower, string[] rawTokens, string[] analyzedTokens, string[] expandedTokens)
    {
        var boolQuery = new BooleanQuery();

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

    // ════════════════════════════════════════════════════════════════
    //  POST-LUCENE SCORING (query-dependent signals only)
    // ════════════════════════════════════════════════════════════════

    private static float FinalScore(FoodProductDto dto, float luceneScore, string queryLower, string[] queryTokens, string[] analyzedTokens)
    {
        float score = luceneScore;
        var primaryNoun = ExtractPrimaryNoun(dto.Name).ToLowerInvariant();
        var nameLower = dto.Name.ToLowerInvariant();

        // Use the union of raw + analyzed tokens for coverage (captures synonym expansions)
        var allQueryTokens = queryTokens.Concat(analyzedTokens).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Token coverage against primary noun
        var primaryTokens = primaryNoun.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int primaryMatched = allQueryTokens.Count(qt => primaryTokens.Any(pt =>
            pt == qt || pt.StartsWith(qt) || qt.StartsWith(pt)));
        float primaryCoverage = allQueryTokens.Length > 0 ? (float)primaryMatched / allQueryTokens.Length : 0f;
        score += primaryCoverage * 20f;
        if (primaryCoverage >= 1f) score += 15f;

        // Token coverage against FULL name (catches descriptors like "brown", "grilled")
        var nameTokens = nameLower.Split([' ', ',', '(', ')', '/', '-'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int nameMatched = allQueryTokens.Count(qt => nameTokens.Any(nt =>
            nt == qt || nt.StartsWith(qt) || qt.StartsWith(nt)));
        float nameCoverage = allQueryTokens.Length > 0 ? (float)nameMatched / allQueryTokens.Length : 0f;
        score += nameCoverage * 15f;
        if (nameCoverage >= 1f) score += 10f;

        // Primary noun first-token match bonus (exact preferred over prefix)
        if (queryTokens.Length > 0 && primaryTokens.Length > 0)
        {
            var pt0 = primaryTokens[0];
            var qt0 = queryTokens[0];
            if (pt0 == qt0)
                score += 20f; // exact match
            else if (pt0.StartsWith(qt0) && pt0.Length <= qt0.Length + 3)
                score += 12f; // close prefix (e.g., "salmon" matching "salmons")
            else if (qt0.StartsWith(pt0))
                score += 10f; // query longer than primary token
            // Don't reward when primary is much longer (e.g., "salmonberries" for "salmon")
        }

        // Exact name match bonuses
        if (nameLower == queryLower) score += 50f;
        if (nameLower.StartsWith(queryLower)) score += 20f;

        // Prefer plain/raw variants for simple queries
        if (queryTokens.Length <= 2)
        {
            foreach (var term in (ReadOnlySpan<string>)["raw", "fresh", "whole", "plain", "white", "regular"])
                if (nameLower.Contains(term)) score += 5f;
        }

        // Nutrition plausibility
        score += NutritionPlausibilityScore(dto, queryLower);

        // Brand penalty for generic queries
        if (!string.IsNullOrEmpty(dto.Brand) && dto.Brand.Length > 1)
        {
            bool queryLooksLikeBrand = queryTokens.Any(t => t.Length > 0 && char.IsUpper(t[0]));
            if (!queryLooksLikeBrand) score -= 15f;
        }

        return score;
    }

    private static float NutritionPlausibilityScore(FoodProductDto dto, string queryLower)
    {
        if (!dto.Calories100g.HasValue) return 0f;

        float penalty = 0f;
        var cal = dto.Calories100g.Value;
        var protein = dto.Protein100g ?? 0m;
        var carbs = dto.Carbs100g ?? 0m;
        var fat = dto.Fat100g ?? 0m;

        if (queryLower.Contains("chicken") || queryLower.Contains("beef") ||
            queryLower.Contains("fish") || queryLower.Contains("turkey") ||
            queryLower.Contains("pork") || queryLower.Contains("lamb") ||
            queryLower.Contains("steak") || queryLower.Contains("salmon"))
        {
            if (carbs > 40m) penalty -= 15f;
            if (protein < 5m && cal > 50m) penalty -= 10f;
        }

        if (queryLower.Contains("oil") || queryLower.Contains("butter") || queryLower.Contains("lard"))
        {
            if (fat < 20m && cal > 100m) penalty -= 15f;
        }

        if (queryLower.Contains("lettuce") || queryLower.Contains("spinach") ||
            queryLower.Contains("kale") || queryLower.Contains("celery") ||
            queryLower.Contains("cucumber"))
        {
            if (cal > 100m) penalty -= 15f;
        }

        if (queryLower.Contains("juice") || queryLower.Contains("water") ||
            queryLower.Contains("tea") || queryLower.Contains("coffee"))
        {
            if (fat > 20m) penalty -= 10f;
        }

        return penalty;
    }

    public int Count => _foods.Count;

    public void Dispose()
    {
        _searcherManager?.Dispose();
        _directory?.Dispose();
        _analyzer?.Dispose();
    }
}

// ════════════════════════════════════════════════════════════════
//  FoodAnalyzer: StandardTokenizer → LowerCase → Stop → Synonym → PorterStem
// ════════════════════════════════════════════════════════════════

internal sealed class FoodAnalyzer : Analyzer
{
    private static readonly CharArraySet StopWords;
    private static readonly SynonymMap Synonyms;

    static FoodAnalyzer()
    {
        StopWords = BuildStopWords();
        Synonyms = BuildSynonymMap();
    }

    private static CharArraySet BuildStopWords()
    {
        var words = new CharArraySet(LuceneVersion.LUCENE_48, 40, ignoreCase: true);
        foreach (var w in new[]
        {
            "with", "and", "in", "of", "style", "flavored", "flavoured",
            "ns", "as", "to", "the", "for", "a", "an",
            "nfs", "not", "further", "specified", "type", "all", "purpose",
            "usda", "commodity", "purchased", "commercially", "prepared",
            "ready", "eat"
        })
        {
            words.Add(w);
        }
        return words.AsReadOnly();
    }

    private static SynonymMap BuildSynonymMap()
    {
        var builder = new SynonymMap.Builder(dedup: true);

        AddSynonym(builder, "toast", "bread", "toasted");
        AddSynonym(builder, "steak", "beef", "loin");
        AddSynonym(builder, "oatmeal", "oats", "cereal");
        AddSynonym(builder, "fries", "potatoes", "french", "fried");
        AddSynonym(builder, "chips", "potato", "chips");
        AddSynonym(builder, "soda", "carbonated", "beverage");
        AddSynonym(builder, "pop", "carbonated", "beverage");

        return builder.Build();
    }

    private static void AddSynonym(SynonymMap.Builder builder, string input, params string[] outputs)
    {
        var inputCs = new CharsRef(input);
        foreach (var output in outputs)
            builder.Add(inputCs, new CharsRef(output), true);
    }

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new StandardTokenizer(LuceneVersion.LUCENE_48, reader);
        TokenStream stream = new LowerCaseFilter(LuceneVersion.LUCENE_48, tokenizer);
        stream = new StopFilter(LuceneVersion.LUCENE_48, stream, StopWords);
        stream = new SynonymFilter(stream, Synonyms, ignoreCase: true);
        stream = new PorterStemFilter(stream);
        return new TokenStreamComponents(tokenizer, stream);
    }
}

// ════════════════════════════════════════════════════════════════
//  CustomScoreQuery: blends Lucene relevance with pre-computed quality
// ════════════════════════════════════════════════════════════════

internal sealed class FoodCustomScoreQuery : CustomScoreQuery
{
    public FoodCustomScoreQuery(Query subQuery, FunctionQuery qualityQuery)
        : base(subQuery, qualityQuery) { }

    protected override CustomScoreProvider GetCustomScoreProvider(AtomicReaderContext context)
        => new FoodScoreProvider(context);
}

internal sealed class FoodScoreProvider : CustomScoreProvider
{
    public FoodScoreProvider(AtomicReaderContext context) : base(context) { }

    public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
    {
        // subQueryScore = Lucene BooleanQuery relevance
        // valSrcScore = pre-computed static quality (from SingleDocValuesField)
        // Quality acts as tiebreaker/booster on top of relevance
        return subQueryScore + valSrcScore * 15f;
    }
}
