using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface IFoodApiService
{
    string SourceName { get; }
    Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default);
    Task<List<FoodProductDto>> SearchPersonalizedAsync(string query, IEnumerable<Guid> boostIds, CancellationToken ct = default) => SearchAsync(query, ct);
}
