using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Infrastructure.Data;

namespace GutAI.Infrastructure.ExternalApis;

public class WholeFoodApiService : IFoodApiService
{
    public string SourceName => DataSources.UsdaWhole;

    public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        // Whole foods database (FDC) doesn't typically index by standard barcode in this generated file.
        return Task.FromResult<FoodProductDto?>(null);
    }

    public Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = WholeFoodsDatabase.Search(query, 10);
        foreach (var product in results)
        {
            // Update the data source to use the constant
            var updatedProduct = product with { DataSource = DataSources.UsdaWhole };

            // If the generator tool included the FDC ID, it might be in ExternalId or name.
            // Currently, WholeFoodsDatabase.cs (F) function doesn't set ExternalId.
        }
        return Task.FromResult(results);
    }
}
