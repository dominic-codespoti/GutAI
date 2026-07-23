using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;

namespace GutAI.Application.Common.Helpers;

/// <summary>
/// The single path for turning a search-result DTO into a persisted, canonical
/// <see cref="FoodProduct"/>. Resolves existing identity (barcode → source+externalId →
/// name+brand, matching <see cref="FoodCandidateIdentity"/>'s precedence) before ever
/// minting a new GUID, so repeated searches/parses for the same external product reuse
/// one row instead of growing a duplicate per query variation. Used both by the search
/// endpoint (on result persistence) and the NLP meal parser (on auto-selected match).
///
/// Refreshes nutrition/metadata from the freshest external fetch on every call, but
/// preserves fields no external DTO ever carries — <see cref="FoodProduct.SafetyScore"/>,
/// <see cref="FoodProduct.SafetyRating"/>, <see cref="FoodProduct.FoodProductAdditiveIds"/>,
/// <see cref="FoodProduct.IsDeleted"/> — so a re-resolve never silently wipes curated data
/// computed by other pipelines (background scoring jobs, admin edits).
/// </summary>
public static class FoodProductPersistence
{
    public static async Task<Guid> ResolveOrPersistAsync(FoodProductDto dto, ITableStore store, CancellationToken ct = default)
    {
        var existing = await FindExistingAsync(dto, store, ct);
        var id = existing?.Id ?? Guid.NewGuid();
        var product = MapToProduct(dto, existing, id);
        await store.UpsertFoodProductAsync(product, ct);
        return id;
    }

    private static async Task<FoodProduct?> FindExistingAsync(FoodProductDto dto, ITableStore store, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dto.Barcode))
        {
            var byBarcode = await store.GetFoodProductByBarcodeAsync(dto.Barcode, ct);
            if (byBarcode is not null) return byBarcode;
        }

        if (!string.IsNullOrWhiteSpace(dto.ExternalId) && !string.IsNullOrWhiteSpace(dto.DataSource))
        {
            var bySource = await store.GetFoodProductBySourceAsync(dto.DataSource, dto.ExternalId, ct);
            if (bySource is not null) return bySource;
        }

        // Best-effort name+brand identity fallback for providers with no stable id
        // (the embedded whole-food/branded/Australian databases never set Barcode/ExternalId).
        if (string.IsNullOrWhiteSpace(dto.Barcode) && string.IsNullOrWhiteSpace(dto.ExternalId))
        {
            var candidates = await store.SearchFoodProductsAsync(dto.Name, 10, ct);
            return candidates.FirstOrDefault(c =>
                string.Equals(c.Name, dto.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Brand, dto.Brand, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static FoodProduct MapToProduct(FoodProductDto dto, FoodProduct? existing, Guid id) => new()
    {
        Id = id,
        Name = dto.Name,
        Barcode = dto.Barcode,
        Brand = dto.Brand,
        Ingredients = dto.Ingredients,
        NovaGroup = dto.NovaGroup,
        ServingSize = dto.ServingSize,
        NutritionInfo = dto.NutritionInfo,
        Calories100g = dto.Calories100g,
        Protein100g = dto.Protein100g,
        Carbs100g = dto.Carbs100g,
        Fat100g = dto.Fat100g,
        Fiber100g = dto.Fiber100g,
        Sugar100g = dto.Sugar100g,
        SodiumMg100g = dto.SodiumMg100g,
        DataSource = dto.DataSource,
        SourceUrl = dto.SourceUrl,
        ExternalId = dto.ExternalId,
        SourceVersion = dto.SourceVersion ?? dto.DataSource,
        LicenseType = dto.LicenseType ?? existing?.LicenseType ?? dto.DataSource switch
        {
            "USDA" => "USDA FoodData Central terms",
            "OpenFoodFacts" => "Open Food Facts ODbL",
            _ => null
        },
        Attribution = dto.Attribution ?? dto.DataSource,
        RetrievedAt = dto.RetrievedAt ?? DateTime.UtcNow,
        CachedAt = DateTime.UtcNow,
        CacheTtlHours = existing?.CacheTtlHours ?? 168,
        ImageUrl = dto.ImageUrl ?? existing?.ImageUrl,
        NutriScore = dto.NutriScore,
        ServingQuantity = dto.ServingQuantity,
        AllergensTags = dto.AllergensTags.Length > 0 ? dto.AllergensTags : (existing?.AllergensTags ?? []),
        FoodKind = dto.FoodKind,

        // Not sourced from an external DTO — always carried over from any existing row.
        SafetyScore = existing?.SafetyScore,
        SafetyRating = existing?.SafetyRating,
        FoodProductAdditiveIds = existing?.FoodProductAdditiveIds ?? [],
        IsDeleted = existing?.IsDeleted ?? false,
    };
}
