using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// A single food data source (USDA, OpenFoodFacts, an embedded database, ...). Leaf
/// providers only know how to answer their own queries — they carry no personalization,
/// ranking, caching, or cross-provider merge logic. That orchestration lives in
/// <see cref="IExternalFoodAggregator"/> and <see cref="IFoodSearchService"/>.
/// </summary>
public interface IFoodProvider
{
    string SourceName { get; }
    FoodProviderCapabilities Capabilities { get; }
    Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default);
    Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default);
}
