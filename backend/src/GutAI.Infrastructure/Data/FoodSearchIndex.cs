using GutAI.Application.Common.DTOs;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
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
    //  BACKWARD-COMPAT FORWARDING METHODS
    // ════════════════════════════════════════════════════════════════

    internal static string ExtractPrimaryNoun(string name) => FoodScoring.ExtractPrimaryNoun(name);
    internal static string NormalizeFoodName(string name) => FoodScoring.NormalizeFoodName(name);

    // ════════════════════════════════════════════════════════════════
    //  INDEXING
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

                var primaryNoun = FoodScoring.ExtractPrimaryNoun(food.Name);
                var quality = FoodScoring.ComputeStaticQuality(food);

                var doc = new Document
                {
                    new StringField("idx", idx.ToString(), Field.Store.YES),
                    new TextField("name", food.Name, Field.Store.NO),
                    new TextField("primary", primaryNoun, Field.Store.NO),
                    new StringField("name_exact", food.Name.ToLowerInvariant(), Field.Store.NO),
                    new TextField("brand", food.Brand ?? "", Field.Store.NO),
                    new StringField("source", food.DataSource ?? "", Field.Store.NO),
                    new SingleDocValuesField("quality", quality),
                    new Int32Field("has_image", food.ImageUrl != null ? 1 : 0, Field.Store.NO),
                    new Int32Field("has_ingredients", !string.IsNullOrEmpty(food.Ingredients) ? 1 : 0, Field.Store.NO),
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
        => SearchPersonalized(query, [], maxResults);

    public List<FoodProductDto> SearchPersonalized(string query, IEnumerable<Guid> boostIds, int maxResults = 15)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var queryLower = query.Trim().ToLowerInvariant();
        var rawTokens = queryLower.Split([' ', ',', '(', ')', '/', '-'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (rawTokens.Length == 0)
            return [];

        var analyzedTokens = AnalyzeQuery(queryLower);
        if (analyzedTokens.Length == 0) analyzedTokens = rawTokens;

        var expandedTokens = FoodQueryBuilder.ExpandMultiWordSynonyms(queryLower, analyzedTokens);

        var brandTokens = BuildBrandTokens(_foods);
        bool queryHasBrand = rawTokens.Any(t => brandTokens.Contains(t));

        var boolQuery = FoodQueryBuilder.Build(queryLower, rawTokens, analyzedTokens, expandedTokens, boostIds);

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

            return results
                .OrderByDescending(r => FoodScoring.FinalScore(r.food, r.score, queryLower, rawTokens, analyzedTokens, queryHasBrand))
                .Take(maxResults)
                .Select(r => r.food)
                .ToList();
        }
        finally
        {
            _searcherManager.Release(searcher);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  BRAND DETECTION
    // ════════════════════════════════════════════════════════════════

    public int Count => _foods.Count;

    private static HashSet<string> _knownBrands = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastBrandUpdate = DateTime.MinValue;

    private static HashSet<string> BuildBrandTokens(IEnumerable<FoodProductDto> foods)
    {
        if (DateTime.UtcNow - _lastBrandUpdate < TimeSpan.FromMinutes(5))
            return _knownBrands;

        var brands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in foods)
        {
            if (!string.IsNullOrEmpty(f.Brand))
            {
                var tokens = f.Brand.Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var t in tokens) if (t.Length > 2) brands.Add(t);
            }
        }
        _knownBrands = brands;
        _lastBrandUpdate = DateTime.UtcNow;
        return brands;
    }

    // ════════════════════════════════════════════════════════════════
    //  DISPOSE
    // ════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        _searcherManager?.Dispose();
        _directory?.Dispose();
        _analyzer?.Dispose();
    }
}
