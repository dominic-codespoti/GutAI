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

namespace GutAI.Infrastructure.Data;

public sealed class FoodSearchIndex : IDisposable
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    private readonly RAMDirectory _directory;
    private readonly Analyzer _analyzer;
    private readonly SearcherManager _searcherManager;
    private readonly object _writeLock = new();

    // Maps Lucene doc ID → FoodProductDto (avoids deserializing stored fields)
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
                // Edge-ngram style isn't needed — we'll use prefix + fuzzy queries
                ["name"] = new StandardAnalyzer(Version),
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

        var boolQuery = new BooleanQuery();

        // 1) Exact phrase match on the full query (highest boost)
        var phraseQuery = new PhraseQuery { Slop = 2 };
        foreach (var token in tokens)
            phraseQuery.Add(new Term("name", token));
        phraseQuery.Boost = 10f;
        boolQuery.Add(phraseQuery, Occur.SHOULD);

        // 2) Each token: exact + prefix + fuzzy (all SHOULD, so more matches = higher score)
        foreach (var token in tokens)
        {
            // Exact token match (high boost)
            var exactTerm = new TermQuery(new Term("name", token)) { Boost = 5f };
            boolQuery.Add(exactTerm, Occur.SHOULD);

            // Prefix match (good for partial typing like "cere" → "cereal")
            if (token.Length >= 2)
            {
                var prefixQ = new PrefixQuery(new Term("name", token)) { Boost = 3f };
                boolQuery.Add(prefixQ, Occur.SHOULD);
            }

            // Fuzzy match (typo tolerance: "coles" → "corn", "ceraal" → "cereal")
            if (token.Length >= 3)
            {
                var fuzzyQ = new FuzzyQuery(new Term("name", token), 1) { Boost = 1f };
                boolQuery.Add(fuzzyQ, Occur.SHOULD);
            }
        }

        // 3) If multi-word, require at least one token to appear (prevents total garbage)
        if (tokens.Length > 1)
        {
            var mustMatchAny = new BooleanQuery();
            foreach (var token in tokens)
                mustMatchAny.Add(new TermQuery(new Term("name", token)), Occur.SHOULD);
            mustMatchAny.MinimumNumberShouldMatch = 1;
            boolQuery.Add(mustMatchAny, Occur.MUST);
        }

        _searcherManager.MaybeRefreshBlocking();
        var searcher = _searcherManager.Acquire();
        try
        {
            // Fetch more than needed so we can re-rank
            var topDocs = searcher.Search(boolQuery, maxResults * 2);
            var results = new List<(FoodProductDto food, float luceneScore)>();

            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var idx = int.Parse(doc.Get("idx"));
                if (idx < _foods.Count)
                    results.Add((_foods[idx], scoreDoc.Score));
            }

            // Re-rank: combine Lucene relevance with data quality
            return results
                .OrderByDescending(r => CombinedScore(r.food, r.luceneScore, queryLower, tokens))
                .Take(maxResults)
                .Select(r => r.food)
                .ToList();
        }
        finally
        {
            _searcherManager.Release(searcher);
        }
    }

    private static float CombinedScore(FoodProductDto dto, float luceneScore, string queryLower, string[] queryTokens)
    {
        float score = luceneScore * 10f; // Lucene relevance is the primary signal

        // Data quality bonuses
        if (dto.DataSource == "USDA") score += 5f;
        if (dto.Calories100g.HasValue) score += 2f;
        if (dto.Protein100g.HasValue) score += 1f;
        if (dto.Carbs100g.HasValue) score += 1f;
        if (dto.Fat100g.HasValue) score += 1f;

        // Prefer shorter, more specific names
        score += Math.Max(0, 10f - dto.Name.Length / 8f);

        // Exact name starts with query — big bonus
        if (dto.Name.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase))
            score += 20f;

        return score;
    }

    public int Count => _foods.Count;

    public void Dispose()
    {
        _searcherManager?.Dispose();
        _directory?.Dispose();
        _analyzer?.Dispose();
    }
}
