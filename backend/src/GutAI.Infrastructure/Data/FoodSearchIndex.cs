using GutAI.Application.Common.DTOs;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System.Text.RegularExpressions;

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
        _analyzer = CreateAnalyzer();

        using var writer = NewWriter();
        writer.Commit();

        _searcherManager = new SearcherManager(_directory, null);
    }

    public FoodSearchIndex(IEnumerable<FoodProductDto> foods) : this()
    {
        AddRange(foods);
    }

    private static Analyzer CreateAnalyzer()
    {
        return new PerFieldAnalyzerWrapper(
            new StandardAnalyzer(Version),
            new Dictionary<string, Analyzer>
            {
                ["name"] = new StandardAnalyzer(Version),
                ["name_norm"] = new StandardAnalyzer(Version),
                ["name_exact"] = new KeywordAnalyzer(),
            });
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

    internal static string NormalizeFoodName(string name)
    {
        var s = name.ToLowerInvariant();
        s = Regex.Replace(s, @"\([^)]*\)", " ");
        s = Regex.Replace(s, @"[,;:/\-–—]", " ");
        s = Regex.Replace(s, @"[^a-z0-9 ]", "");

        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var noiseWords = new HashSet<string> {
            "with", "and", "in", "of", "style", "flavored", "flavoured",
            "ready-to-eat", "ns", "as", "to", "the", "for", "a", "an",
            "nfs", "not", "further", "specified", "type", "all", "purpose",
            "usda", "commodity", "purchased"
        };

        var result = new List<string>();
        foreach (var t in tokens)
        {
            if (noiseWords.Contains(t)) continue;
            var normalized = t;
            if (normalized.EndsWith("ies") && normalized.Length > 4)
                normalized = normalized[..^3] + "y";
            else if (normalized.EndsWith("es") && normalized.Length > 3 &&
                     !normalized.EndsWith("ches") && !normalized.EndsWith("shes"))
                normalized = normalized[..^2];
            else if (normalized.EndsWith("s") && normalized.Length > 2 && !normalized.EndsWith("ss"))
                normalized = normalized[..^1];
            result.Add(normalized);
        }

        return string.Join(" ", result);
    }

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

                var doc = new Document
                {
                    new StringField("idx", idx.ToString(), Field.Store.YES),
                    new TextField("name", food.Name, Field.Store.NO),
                    new TextField("name_norm", NormalizeFoodName(food.Name), Field.Store.NO),
                    new StringField("name_exact", food.Name.ToLowerInvariant(), Field.Store.NO),
                    new TextField("brand", food.Brand ?? "", Field.Store.NO),
                    new StringField("source", food.DataSource ?? "", Field.Store.NO),
                };

                writer.AddDocument(doc);
            }
            writer.Commit();
        }
        _searcherManager.MaybeRefresh();
    }

    public List<FoodProductDto> Search(string query, int maxResults = 15)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var queryLower = query.Trim().ToLowerInvariant();
        var tokens = queryLower.Split(new[] { ' ', ',', '(', ')', '/', '-' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return [];

        var queryNorm = NormalizeFoodName(query);
        var normTokens = queryNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var boolQuery = new BooleanQuery();

        // 1) Exact phrase on normalized field (highest boost)
        if (normTokens.Length > 0)
        {
            var phraseQuery = new PhraseQuery { Slop = 2 };
            foreach (var token in normTokens)
                phraseQuery.Add(new Term("name_norm", token));
            phraseQuery.Boost = 12f;
            boolQuery.Add(phraseQuery, Occur.SHOULD);
        }

        // 2) Exact phrase on original name field
        {
            var phraseQuery = new PhraseQuery { Slop = 2 };
            foreach (var token in tokens)
                phraseQuery.Add(new Term("name", token));
            phraseQuery.Boost = 10f;
            boolQuery.Add(phraseQuery, Occur.SHOULD);
        }

        // 3) Per-token on normalized field: exact + prefix + fuzzy
        foreach (var token in normTokens)
        {
            var exactTerm = new TermQuery(new Term("name_norm", token)) { Boost = 5f };
            boolQuery.Add(exactTerm, Occur.SHOULD);

            if (token.Length >= 2)
            {
                var prefixQ = new PrefixQuery(new Term("name_norm", token)) { Boost = 3f };
                boolQuery.Add(prefixQ, Occur.SHOULD);
            }

            if (token.Length >= 3)
            {
                var fuzzyQ = new FuzzyQuery(new Term("name_norm", token), 1) { Boost = 1f };
                boolQuery.Add(fuzzyQ, Occur.SHOULD);
            }
        }

        // 4) Per-token on original name field
        foreach (var token in tokens)
        {
            var exactTerm = new TermQuery(new Term("name", token)) { Boost = 4f };
            boolQuery.Add(exactTerm, Occur.SHOULD);

            if (token.Length >= 2)
            {
                var prefixQ = new PrefixQuery(new Term("name", token)) { Boost = 2f };
                boolQuery.Add(prefixQ, Occur.SHOULD);
            }

            if (token.Length >= 3)
            {
                var fuzzyQ = new FuzzyQuery(new Term("name", token), 1) { Boost = 0.5f };
                boolQuery.Add(fuzzyQ, Occur.SHOULD);
            }
        }

        // 5) For multi-word, require at least one token to appear
        if (tokens.Length > 1)
        {
            var mustMatchAny = new BooleanQuery();
            foreach (var token in tokens)
                mustMatchAny.Add(new TermQuery(new Term("name", token)), Occur.SHOULD);
            foreach (var token in normTokens)
                mustMatchAny.Add(new TermQuery(new Term("name_norm", token)), Occur.SHOULD);
            mustMatchAny.MinimumNumberShouldMatch = 1;
            boolQuery.Add(mustMatchAny, Occur.MUST);
        }

        _searcherManager.MaybeRefreshBlocking();
        var searcher = _searcherManager.Acquire();
        try
        {
            var topDocs = searcher.Search(boolQuery, maxResults * 5);
            var results = new List<(FoodProductDto food, float luceneScore)>();

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var idx = int.Parse(doc.Get("idx"));
                if (idx < _foods.Count)
                    results.Add((_foods[idx], scoreDoc.Score));
            }

            return results
                .OrderByDescending(r => CombinedScore(r.food, r.luceneScore, queryLower, tokens, queryNorm, normTokens))
                .Take(maxResults)
                .Select(r => r.food)
                .ToList();
        }
        finally
        {
            _searcherManager.Release(searcher);
        }
    }

    private static float CombinedScore(FoodProductDto dto, float luceneScore,
        string queryLower, string[] queryTokens,
        string queryNorm, string[] normTokens)
    {
        float score = luceneScore * 10f;
        var nameLower = dto.Name.ToLowerInvariant();
        var nameNorm = NormalizeFoodName(dto.Name);
        var nameNormTokens = nameNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // --- Token coverage: how many query tokens matched? ---
        int matched = 0;
        foreach (var qt in normTokens)
        {
            foreach (var nt in nameNormTokens)
            {
                if (nt == qt || nt.StartsWith(qt) || qt.StartsWith(nt))
                { matched++; break; }
            }
        }
        float coverage = normTokens.Length > 0 ? (float)matched / normTokens.Length : 0f;
        score += coverage * 30f;
        if (coverage >= 1f) score += 15f;

        // --- Token order bonus ---
        if (normTokens.Length > 1 && matched > 1)
        {
            int lastIdx = -1;
            bool inOrder = true;
            foreach (var qt in normTokens)
            {
                int foundAt = -1;
                for (int i = 0; i < nameNormTokens.Length; i++)
                {
                    if (nameNormTokens[i] == qt || nameNormTokens[i].StartsWith(qt))
                    { foundAt = i; break; }
                }
                if (foundAt >= 0)
                {
                    if (foundAt <= lastIdx) { inOrder = false; break; }
                    lastIdx = foundAt;
                }
            }
            if (inOrder) score += 10f;
        }

        // --- Data quality ---
        if (dto.DataSource == "USDA") score += 12f;
        if (dto.Calories100g.HasValue) score += 2f;
        if (dto.Protein100g.HasValue) score += 1f;
        if (dto.Carbs100g.HasValue) score += 1f;
        if (dto.Fat100g.HasValue) score += 1f;

        // --- Whole food boost: unbranded, single-ingredient foods ---
        bool isLikelyWholeFood = string.IsNullOrEmpty(dto.Brand) &&
            (string.IsNullOrEmpty(dto.Ingredients) || !dto.Ingredients.Contains(','));
        if (isLikelyWholeFood)
            score += 15f;

        // --- Exact name bonuses ---
        if (dto.Name.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
            score += 50f;
        if (dto.Name.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase))
            score += 20f;
        if (nameNorm == queryNorm)
            score += 40f;
        if (nameNorm.StartsWith(queryNorm))
            score += 15f;

        // --- Prefer shorter names (non-linear penalty for long names) ---
        if (dto.Name.Length <= 30)
            score += Math.Max(0, 10f - dto.Name.Length / 8f);
        else
            score -= (dto.Name.Length - 30) * (dto.Name.Length - 30) / 50f;

        // --- Comma penalty ---
        var commas = dto.Name.Count(c => c == ',');
        score -= commas * 3f;

        // --- Parenthetical penalty ---
        var parenCount = dto.Name.Count(c => c == '(');
        score -= parenCount * 5f;

        // --- Weirdness / lab-report penalties ---
        string[] hardPenalty = [
            "frozen", "canned", "dehydrated", "powder", "mix",
            "mixture", "substitute", "imitation", "baby food", "infant", "formula",
            "alaska native", "industrial", "fast food",
            "ns as to", "usda commodity", "as purchased", "not further specified",
            "nfs", "ready-to-eat", "glucose reduced", "stabilized"
        ];
        foreach (var term in hardPenalty)
            if (nameLower.Contains(term))
                score -= 20f;

        string[] softPenalty = [
            "navajo", "hopi", "southwest", "shoshone", "apache",
            "pasteurized", "restaurant", "commercial", "institutional"
        ];
        foreach (var term in softPenalty)
            if (nameLower.Contains(term))
                score -= 12f;

        // --- Prefer plain/raw/whole/fresh for simple queries ---
        if (queryTokens.Length <= 2)
        {
            string[] preferredTerms = ["raw", "fresh", "whole", "plain"];
            foreach (var term in preferredTerms)
                if (nameLower.Contains(term))
                    score += 5f;
        }

        // --- Nutrition plausibility checks ---
        score += NutritionPlausibilityScore(dto, queryLower);

        // --- Brand handling: penalize branded results for generic queries ---
        if (!string.IsNullOrEmpty(dto.Brand) && dto.Brand.Length > 1)
        {
            bool queryLooksLikeBrand = queryTokens.Any(t => t.Length > 0 && char.IsUpper(t[0]));
            if (!queryLooksLikeBrand && !nameLower.Contains(queryLower))
                score -= 8f;
        }

        return score;
    }

    private static float NutritionPlausibilityScore(FoodProductDto dto, string queryLower)
    {
        float penalty = 0f;
        if (!dto.Calories100g.HasValue) return 0f;

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
