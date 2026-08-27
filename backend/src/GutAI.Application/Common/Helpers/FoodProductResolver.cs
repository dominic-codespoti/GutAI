using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GutAI.Application.Common.Helpers;

public static class FoodProductResolver
{
    public static async Task<FoodProduct?> GetEnrichedCatalogProductAsync(
        Guid id,
        ITableStore store,
        IOfflineFoodDatabase? offlineDb,
        IExternalFoodAggregator? foodApi,
        CancellationToken ct = default,
        ILogger? logger = null)
    {
        var product = await store.GetFoodProductAsync(id, ct);
        if (product is null) return null;

        if (product.DataSource == DataSources.OpenFoodFacts &&
            string.IsNullOrEmpty(product.Ingredients) &&
            !string.IsNullOrEmpty(product.Barcode))
        {
            await EnrichFromOffBarcodeAsync(product, offlineDb, foodApi, store, ct, logger);
        }

        return product;
    }

    private static async Task EnrichFromOffBarcodeAsync(
        FoodProduct product,
        IOfflineFoodDatabase? offlineDb,
        IExternalFoodAggregator? foodApi,
        ITableStore store,
        CancellationToken ct = default,
        ILogger? logger = null)
    {
        try
        {
            var enriched = await LookupOffProductAsync(product.Barcode!, offlineDb, foodApi, ct);
            if (enriched is null) return;

            product.Ingredients = enriched.Ingredients ?? product.Ingredients;
            product.NovaGroup = enriched.NovaGroup ?? product.NovaGroup;
            product.NutriScore = enriched.NutriScore ?? product.NutriScore;
            product.ServingSize = enriched.ServingSize ?? product.ServingSize;
            product.ServingQuantity = enriched.ServingQuantity ?? product.ServingQuantity;
            product.ImageUrl = enriched.ImageUrl ?? product.ImageUrl;
            product.AllergensTags = enriched.AllergensTags.Length > 0 ? enriched.AllergensTags : product.AllergensTags;
            product.Calories100g = enriched.Calories100g ?? product.Calories100g;
            product.Protein100g = enriched.Protein100g ?? product.Protein100g;
            product.Carbs100g = enriched.Carbs100g ?? product.Carbs100g;
            product.Fat100g = enriched.Fat100g ?? product.Fat100g;
            product.Fiber100g = enriched.Fiber100g ?? product.Fiber100g;
            product.Sugar100g = enriched.Sugar100g ?? product.Sugar100g;
            product.SodiumMg100g = enriched.SodiumMg100g ?? product.SodiumMg100g;

            // Re-persist enriched data so subsequent views don't need another lookup
            await store.UpsertFoodProductAsync(product, ct);
        }
        catch (Exception ex)
        {
            // Silently degrade — the product will show "Ingredients unavailable"
            // and we'll try again on next view
            logger?.LogWarning(ex, "Failed to enrich food product {ProductId} ({Barcode}) from OFF barcode", product.Id, product.Barcode);
        }
    }

    private static async Task<FoodProductDto?> LookupOffProductAsync(
        string barcode,
        IOfflineFoodDatabase? offlineDb,
        IExternalFoodAggregator? foodApi,
        CancellationToken ct = default)
    {
        // 1. Try offline database (Azure Table "offproducts", unlimited lookups)
        if (offlineDb is not null)
        {
            var result = await offlineDb.LookupByBarcodeAsync(barcode, ct);
            if (result is not null)
                return result;
        }

        // 2. Fall back to barcode API (rate-limited to 12 req/min/IP)
        if (foodApi is not null)
            return await foodApi.LookupBarcodeAsync(barcode, ct);

        return null;
    }
}
