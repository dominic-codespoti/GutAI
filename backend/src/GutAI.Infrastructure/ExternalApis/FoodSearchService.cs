using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

/// <summary>
/// The general-purpose "search across every external food source" entry point for
/// consumers with no local-store/persistence concerns (chat tools, MCP tools, NLP meal
/// parsing). Fetches via <see cref="IExternalFoodAggregator"/>, canonicalizes, ranks once.
/// </summary>
public sealed class FoodSearchService : IFoodSearchService
{
    private readonly IExternalFoodAggregator _aggregator;
    private readonly IFoodRanker _ranker;
    private readonly ILogger<FoodSearchService> _logger;

    private const int DefaultMaxResults = 20;

    public FoodSearchService(IExternalFoodAggregator aggregator, IFoodRanker ranker, ILogger<FoodSearchService> logger)
    {
        _aggregator = aggregator;
        _ranker = ranker;
        _logger = logger;
    }

    public Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
        => SearchPersonalizedAsync(query, [], ct);

    public async Task<IReadOnlyList<FoodProductDto>> SearchPersonalizedAsync(
        string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default)
    {
        var outcome = await _aggregator.SearchAsync(query, ct);

        var failed = outcome.ProviderOutcomes.Where(o => o.Status == ProviderSearchStatus.Failed).ToList();
        if (failed.Count > 0 && failed.Count == outcome.ProviderOutcomes.Count)
            _logger.LogWarning("All {Count} providers failed for query '{Query}'", failed.Count, query);

        var canonical = FoodCandidateCanonicalizer.Canonicalize(outcome.Candidates);
        return _ranker.Rank(canonical, query, boostIds, DefaultMaxResults);
    }

    public async Task<FoodResolutionDto> ResolveAsync(
        string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default)
    {
        var outcome = await _aggregator.SearchAsync(query, ct);

        var failed = outcome.ProviderOutcomes.Where(o => o.Status == ProviderSearchStatus.Failed).ToList();
        if (failed.Count > 0 && failed.Count == outcome.ProviderOutcomes.Count)
            _logger.LogWarning("All {Count} providers failed for query '{Query}'", failed.Count, query);

        var canonical = FoodCandidateCanonicalizer.Canonicalize(outcome.Candidates);
        return _ranker.Resolve(canonical, query, boostIds, DefaultMaxResults);
    }

    public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
        => _aggregator.LookupBarcodeAsync(barcode, ct);
}
