using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

/// <summary>
/// Shared food search/resolution orchestrator. Combines the local Azure Table
/// catalog with external providers before canonicalization and ranking so meal
/// grounding and the web search path see the same candidate universe.
/// </summary>
public sealed class FoodSearchService : IFoodSearchService
{
    private readonly ITableStore _store;
    private readonly IExternalFoodAggregator _aggregator;
    private readonly IFoodRanker _ranker;
    private readonly ILogger<FoodSearchService> _logger;

    private const int DefaultMaxResults = 20;

    public FoodSearchService(
        ITableStore store,
        IExternalFoodAggregator aggregator,
        IFoodRanker ranker,
        ILogger<FoodSearchService> logger)
    {
        _store = store;
        _aggregator = aggregator;
        _ranker = ranker;
        _logger = logger;
    }

    public Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
        => SearchPersonalizedAsync(query, [], ct);

    public async Task<IReadOnlyList<FoodProductDto>> SearchPersonalizedAsync(
        string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default)
    {
        var candidates = await CollectCandidatesAsync(query, ct);
        return _ranker.Rank(candidates, query, boostIds, DefaultMaxResults);
    }

    public async Task<FoodResolutionDto> ResolveAsync(
        string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default)
    {
        var local = await SearchLocalAsync(query, ct);
        if (local.Count > 0)
        {
            var localCanonical = FoodCandidateCanonicalizer.Canonicalize(local);
            var localResolution = _ranker.Resolve(localCanonical, query, boostIds, DefaultMaxResults);
            if ((localResolution.Status == FoodResolutionStatus.Exact || localResolution.Status == FoodResolutionStatus.Probable)
                && localResolution.Selected is not null)
            {
                return localResolution;
            }
        }

        var external = await SearchExternalAsync(query, ct);
        var combined = FoodCandidateCanonicalizer.Canonicalize(
            local.Concat(external).ToList());
        return _ranker.Resolve(combined, query, boostIds, DefaultMaxResults);
    }

    public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
        => _aggregator.LookupBarcodeAsync(barcode, ct);

    private async Task<IReadOnlyList<FoodProductDto>> CollectCandidatesAsync(
        string query, CancellationToken ct)
    {
        var localTask = SearchLocalAsync(query, ct);
        var externalTask = SearchExternalAsync(query, ct);
        await Task.WhenAll(localTask, externalTask);

        var local = await localTask;
        var external = await externalTask;

        return FoodCandidateCanonicalizer.Canonicalize(
            local.Concat(external).ToList());
    }

    private async Task<IReadOnlyList<FoodProductDto>> SearchExternalAsync(
        string query, CancellationToken ct)
    {
        var external = await _aggregator.SearchAsync(query, ct);
        var failed = external.ProviderOutcomes
            .Where(o => o.Status == ProviderSearchStatus.Failed)
            .ToList();

        if (failed.Count > 0)
            _logger.LogWarning(
                "Food search providers failed for query '{Query}': {Providers}",
                query,
                string.Join(", ", failed.Select(p => p.Source)));

        return external.Candidates;
    }

    private async Task<IReadOnlyList<FoodProductDto>> SearchLocalAsync(
        string query, CancellationToken ct)
    {
        try
        {
            var products = await _store.SearchFoodProductsAsync(query, DefaultMaxResults, ct);
            return products.Select(p => new FoodProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Brand = p.Brand,
                Barcode = p.Barcode,
                Ingredients = p.Ingredients,
                ImageUrl = p.ImageUrl,
                NovaGroup = p.NovaGroup,
                NutriScore = p.NutriScore,
                AllergensTags = p.AllergensTags ?? [],
                Calories100g = p.Calories100g,
                Protein100g = p.Protein100g,
                Carbs100g = p.Carbs100g,
                Fat100g = p.Fat100g,
                Fiber100g = p.Fiber100g,
                Sugar100g = p.Sugar100g,
                SodiumMg100g = p.SodiumMg100g,
                FoodKind = p.FoodKind,
                DataSource = p.DataSource,
                SourceUrl = p.SourceUrl,
                ExternalId = p.ExternalId,
                SourceVersion = p.SourceVersion,
                LicenseType = p.LicenseType,
                Attribution = p.Attribution,
                RetrievedAt = p.RetrievedAt,
                ServingSize = p.ServingSize,
                ServingQuantity = p.ServingQuantity,
                MatchConfidence = 1m,
                Additives = [],
                AdditivesTags = [],
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local food search failed for query '{Query}'.", query);
            return [];
        }
    }
}
