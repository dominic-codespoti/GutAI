using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public enum ProviderSearchStatus
{
    Success,
    Failed,
    Skipped,
}

/// <summary>Structured, per-provider search outcome for diagnostics — so degraded search
/// (one provider down, all providers down, provider skipped for missing config) is
/// observable without parsing logs.</summary>
public sealed record ProviderSearchResult(
    string Source,
    ProviderSearchStatus Status,
    int ResultCount,
    TimeSpan Duration);

/// <summary>Raw, unranked, non-deduplicated candidates from every capable provider,
/// plus what happened on each provider call.</summary>
public sealed record ExternalSearchOutcome(
    IReadOnlyList<FoodProductDto> Candidates,
    IReadOnlyList<ProviderSearchResult> ProviderOutcomes);

/// <summary>
/// Fans a query out to every registered <see cref="IFoodProvider"/> capable of answering
/// it, isolates individual provider failures, and propagates caller cancellation instead
/// of swallowing it as a provider failure. Does not rank, cache, or deduplicate across
/// calls — see <see cref="IFoodSearchService"/> and the shared canonicalizer/ranker for that.
/// </summary>
public interface IExternalFoodAggregator
{
    Task<ExternalSearchOutcome> SearchAsync(string query, CancellationToken ct = default);
    Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default);
}
