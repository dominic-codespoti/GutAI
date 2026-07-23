using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;

namespace GutAI.Application.Common.Helpers;

public static class FoodDtoHelper
{
    public static async Task<FoodProductDto> BuildFoodProductDto(FoodProduct product, ITableStore store, CancellationToken ct)
    {
        var additiveIds = product.FoodProductAdditiveIds ?? [];
        var allAdditives = await store.GetAllFoodAdditivesAsync(ct);
        var additiveDtos = additiveIds.Select(aid =>
        {
            var a = allAdditives.FirstOrDefault(x => x.Id == aid);
            return new FoodAdditiveDto
            {
                Id = a?.Id ?? aid,
                Name = a?.Name ?? "Unknown",
                CspiRating = a?.CspiRating.ToString() ?? "Unknown",
                UsRegulatoryStatus = a?.UsRegulatoryStatus.ToString() ?? "Unknown",
                EuRegulatoryStatus = a?.EuRegulatoryStatus.ToString() ?? "Unknown",
                SafetyRating = a?.SafetyRating.ToString() ?? "Unknown",
                Category = a?.Category ?? "Unknown",
                ENumber = a?.ENumber,
                HealthConcerns = a?.HealthConcerns ?? ""
            };
        }).ToList();

        return new FoodProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Brand = product.Brand,
            Barcode = product.Barcode,
            Ingredients = product.Ingredients,
            ImageUrl = product.ImageUrl,
            NovaGroup = product.NovaGroup,
            NutriScore = product.NutriScore,
            AllergensTags = product.AllergensTags ?? [],
            Calories100g = product.Calories100g,
            Protein100g = product.Protein100g,
            Carbs100g = product.Carbs100g,
            Fat100g = product.Fat100g,
            Fiber100g = product.Fiber100g,
            Sugar100g = product.Sugar100g,
            SodiumMg100g = product.SodiumMg100g,
            FoodKind = product.FoodKind,
            DataSource = product.DataSource,
            SourceUrl = product.SourceUrl,
            ExternalId = product.ExternalId,
            SourceVersion = product.SourceVersion,
            LicenseType = product.LicenseType,
            Attribution = product.Attribution,
            RetrievedAt = product.RetrievedAt,
            ServingSize = product.ServingSize,
            ServingQuantity = product.ServingQuantity,
            NutritionInfo = product.NutritionInfo,
            SafetyScore = product.SafetyScore,
            SafetyRating = product.SafetyRating?.ToString(),
            IsDeleted = product.IsDeleted,
            Additives = additiveDtos,
            AdditivesTags = additiveDtos.Where(a => a.ENumber != null)
                .Select(a => $"en:{a.ENumber!.ToLowerInvariant()}").ToList()
        };
    }
}
