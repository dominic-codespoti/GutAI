using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// Application-level "search across every external food source" entry point for
/// consumers that don't need the local-store/persistence orchestration
/// (chat tools, MCP tools, NLP meal parsing). Combines <see cref="IExternalFoodAggregator"/>
/// output through the shared canonicalizer and <see cref="IFoodRanker"/> — exactly one
/// ranking pass, no per-caller re-scoring.
/// </summary>
public interface IFoodSearchService
{
    Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default);

    Task<IReadOnlyList<FoodProductDto>> SearchPersonalizedAsync(
        string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default);

    /// <summary>The single resolution decision for auto-selecting a food match — used by
    /// consumers that pick exactly one candidate without user confirmation (NLP meal
    /// parsing). Reports <see cref="FoodResolutionStatus.Unresolved"/> rather than silently
    /// substituting an unrelated candidate when nothing has meaningful overlap.</summary>
    Task<FoodResolutionDto> ResolveAsync(
        string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default);

    Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default);
}
