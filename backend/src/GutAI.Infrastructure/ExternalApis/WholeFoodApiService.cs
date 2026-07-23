using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;

namespace GutAI.Infrastructure.ExternalApis;

public class WholeFoodApiService : IFoodProvider
{
    public string SourceName => DataSources.Usda;
    public FoodProviderCapabilities Capabilities => FoodProviderCapabilities.Search;

    public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        // Whole foods database (FDC) doesn't typically index by standard barcode in this generated file.
        return Task.FromResult<FoodProductDto?>(null);
    }

    public Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = WholeFoodsDatabase.Search(query, 10);
        foreach (var product in results)
        {
            // Update the data source to use the constant
            var updatedProduct = product with { DataSource = DataSources.Usda };

            // If the generator tool included the FDC ID, it might be in ExternalId or name.
            // Currently, WholeFoodsDatabase.cs (F) function doesn't set ExternalId.
        }
        return Task.FromResult<IReadOnlyList<FoodProductDto>>(results);
    }
}
