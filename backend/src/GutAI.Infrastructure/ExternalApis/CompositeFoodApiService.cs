using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Data;
using GutAI.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class CompositeFoodApiService : IFoodApiService
{
    private readonly IEnumerable<IFoodApiService> _clients;
    private readonly ILogger<CompositeFoodApiService> _logger;

    private static readonly FoodSearchIndex _externalIndex = new();

    public string SourceName => "Composite";

    public CompositeFoodApiService(
        IEnumerable<IFoodApiService> clients,
        ILogger<CompositeFoodApiService> logger)
    {
        _clients = clients;
        _logger = logger;
    }

    public async Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        // Try clients in order (usually configured specific ones first)
        foreach (var client in _clients.Where(c => c is not CompositeFoodApiService))
        {
            try
            {
                var result = await client.LookupBarcodeAsync(barcode, ct);
                if (result is not null) return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Barcode lookup failed for client {Client} and barcode {Barcode}", client.SourceName, barcode);
            }
        }
        return null;
    }

    public async Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        var tasks = _clients
            .Where(c => c is not CompositeFoodApiService)
            .Select(c => SafeSearch(() => c.SearchAsync(query, ct), c.SourceName, query))
            .ToList();

        // Previously-seen external results via Lucene
        var cachedExternal = _externalIndex.Search(query, 10);

        var results = await Task.WhenAll(tasks);

        var merged = new List<FoodProductDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First add results from all clients
        foreach (var batch in results)
        {
            foreach (var item in batch)
            {
                if (seen.Add(item.Name))
                {
                    merged.Add(item);
                    // Index new external results for future queries if they aren't already from WholeFoodApiService
                    if (item.DataSource != DataSources.Usda)
                        _externalIndex.Add(item);
                }
            }
        }

        // Add cached results
        foreach (var ce in cachedExternal)
            if (seen.Add(ce.Name))
                merged.Add(ce);

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
            _logger.LogWarning(ex, "Search failed for source {Source} with query '{Query}'", source, query);
            return [];
        }
    }
}
