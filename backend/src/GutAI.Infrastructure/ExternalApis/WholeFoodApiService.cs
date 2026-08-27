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
        // Provenance (DataSource/SourceVersion/license/attribution) is stamped per-record
        // by the WholeFoodsDatabase factory at generation time — nothing to rewrite here.
        return Task.FromResult<IReadOnlyList<FoodProductDto>>(WholeFoodsDatabase.Search(query, 10));
    }
}
