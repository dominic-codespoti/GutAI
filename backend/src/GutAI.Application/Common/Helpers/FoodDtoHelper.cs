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
            NovaGroup = product.NovaGroup,
            NutriScore = product.NutriScore,
            AllergensTags = product.AllergensTags ?? [],
            Calories100g = product.Calories100g,
            Protein100g = product.Protein100g,
            Carbs100g = product.Carbs100g,
            Fat100g = product.Fat100g,
            Fiber100g = product.Fiber100g,
            Sugar100g = product.Sugar100g,
            Sodium100g = product.Sodium100g,
            ServingSize = product.ServingSize,
            Additives = additiveDtos,
            AdditivesTags = additiveDtos.Where(a => a.ENumber != null)
                .Select(a => $"en:{a.ENumber!.ToLowerInvariant()}").ToList()
        };
    }
}
