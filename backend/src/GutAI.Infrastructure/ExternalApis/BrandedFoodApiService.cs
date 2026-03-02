using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Infrastructure.Data;

namespace GutAI.Infrastructure.ExternalApis;

public class BrandedFoodApiService : IFoodApiService
{
    public string SourceName => DataSources.Usda;

    public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
        => Task.FromResult<FoodProductDto?>(null);

    public Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = BrandedFoodsDatabase.Search(query, 10);
        return Task.FromResult(results);
    }
}
