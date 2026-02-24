using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class CompositeFoodApiService : IFoodApiService
{
    private readonly EdamamFoodClient _edamamClient;
    private readonly OpenFoodFactsClient _offClient;
    private readonly ILogger<CompositeFoodApiService> _logger;

    private static readonly FoodSearchIndex _externalIndex = new();

    public CompositeFoodApiService(
        EdamamFoodClient edamamClient,
        OpenFoodFactsClient offClient,
        ILogger<CompositeFoodApiService> logger)
    {
        _edamamClient = edamamClient;
        _offClient = offClient;
        _logger = logger;
    }

    public async Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        if (_edamamClient.IsConfigured)
        {
            try
            {
                var result = await _edamamClient.LookupBarcodeAsync(barcode, ct);
                if (result is not null) return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Edamam barcode lookup failed for '{Barcode}'", barcode);
            }
        }

        var offResult = await _offClient.LookupBarcodeAsync(barcode, ct);
        return offResult;
    }

    public async Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        var tasks = new List<Task<List<FoodProductDto>>>();

        if (_edamamClient.IsConfigured)
            tasks.Add(SafeSearch(() => _edamamClient.SearchAsync(query, ct), "Edamam", query));

        tasks.Add(SafeSearch(() => _offClient.SearchAsync(query, ct), "OpenFoodFacts", query));

        // Lucene-powered USDA search (instant, sub-millisecond)
        var wholeFoods = WholeFoodsDatabase.Search(query, 15);

        // Also search previously-seen external results via Lucene
        var cachedExternal = _externalIndex.Search(query, 10);

        var results = await Task.WhenAll(tasks);

        var merged = new List<FoodProductDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // USDA whole foods first
        foreach (var wf in wholeFoods)
            if (seen.Add(wf.Name))
                merged.Add(wf);

        // Previously-indexed external results
        foreach (var ce in cachedExternal)
            if (seen.Add(ce.Name))
                merged.Add(ce);

        // Fresh external results
        var newExternal = new List<FoodProductDto>();
        foreach (var batch in results)
            foreach (var item in batch)
                if (seen.Add(item.Name))
                {
                    merged.Add(item);
                    newExternal.Add(item);
                }

        // Index new external results for future queries
        if (newExternal.Count > 0)
            _externalIndex.AddRange(newExternal);

        // Let Lucene re-rank the merged results
        if (merged.Count > 1)
        {
            using var rankIndex = new FoodSearchIndex(merged);
            return rankIndex.Search(query, 20);
        }

        return merged;
    }

    private async Task<List<FoodProductDto>> SafeSearch(Func<Task<List<FoodProductDto>>> search, string source, string query)
    {
        try
        {
            return await search();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Source} search failed for '{Query}'", source, query);
            return [];
        }
    }
}
