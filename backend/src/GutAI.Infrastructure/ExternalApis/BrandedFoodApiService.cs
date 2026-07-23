using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;

namespace GutAI.Infrastructure.ExternalApis;

public class BrandedFoodApiService : IFoodProvider
{
    public string SourceName => DataSources.Usda;
    public FoodProviderCapabilities Capabilities => FoodProviderCapabilities.Search;

    public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
        => Task.FromResult<FoodProductDto?>(null);

    public Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = BrandedFoodsDatabase.Search(query, 10);
        return Task.FromResult<IReadOnlyList<FoodProductDto>>(results);
    }
}
