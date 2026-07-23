using System.Diagnostics;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

/// <summary>
/// Fans a query/barcode out to every registered <see cref="IFoodProvider"/> capable of
/// answering it. Isolates individual provider failures, propagates caller cancellation,
/// and reports structured per-provider outcomes. Deliberately does NOT rank, cache, or
/// canonicalize — that's <see cref="FoodCandidateCanonicalizer"/> and <see cref="IFoodRanker"/>,
/// owned by callers (<see cref="FoodSearchService"/> for general search; <c>FoodEndpoints</c>
/// for the local-store-aware search that also needs region-aware ranking).
/// </summary>
public sealed class ExternalFoodProviderAggregator : IExternalFoodAggregator
{
    private readonly IEnumerable<IFoodProvider> _providers;
    private readonly ILogger<ExternalFoodProviderAggregator> _logger;

    public ExternalFoodProviderAggregator(
        IEnumerable<IFoodProvider> providers,
        ILogger<ExternalFoodProviderAggregator> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        foreach (var provider in _providers.Where(p => p.Capabilities.HasFlag(FoodProviderCapabilities.Barcode)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await provider.LookupBarcodeAsync(barcode, ct);
                if (result is not null) return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Barcode lookup failed for provider {Provider} and barcode {Barcode}", provider.SourceName, barcode);
            }
        }
        return null;
    }

    public async Task<ExternalSearchOutcome> SearchAsync(string query, CancellationToken ct = default)
    {
        var searchCapable = _providers.Where(p => p.Capabilities.HasFlag(FoodProviderCapabilities.Search)).ToList();
        var tasks = searchCapable.Select(p => SafeSearch(p, query, ct)).ToList();

        var outcomes = await Task.WhenAll(tasks);

        var candidates = new List<FoodProductDto>();
        var providerResults = new List<ProviderSearchResult>(outcomes.Length);
        foreach (var (results, status, duration, source) in outcomes)
        {
            candidates.AddRange(results);
            providerResults.Add(new ProviderSearchResult(source, status, results.Count, duration));
        }

        return new ExternalSearchOutcome(candidates, providerResults);
    }

    private async Task<(IReadOnlyList<FoodProductDto> Results, ProviderSearchStatus Status, TimeSpan Duration, string Source)>
        SafeSearch(IFoodProvider provider, string query, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var results = await provider.SearchAsync(query, ct);
            return (results, ProviderSearchStatus.Success, sw.Elapsed, provider.SourceName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search failed for provider {Provider} with query '{Query}'", provider.SourceName, query);
            return ([], ProviderSearchStatus.Failed, sw.Elapsed, provider.SourceName);
        }
    }
}
