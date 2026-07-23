using GutAI.Application.Common.DTOs;
using GutAI.Domain.Constants;
using GutAI.Domain.Enums;

namespace GutAI.Application.Common.Helpers;

/// <summary>
/// Named source/region trust policy for food candidates, used both as the
/// canonicalization tie-break (which duplicate wins) and as barcode-provider call order.
/// Replaces the ad hoc, endpoint-local "SourcePriority" that only the search endpoint saw.
/// </summary>
public static class FoodSourcePolicy
{
    public static FoodRegion ParseRegion(string? region) =>
        region?.Trim().ToUpperInvariant() switch
        {
            "AU" => FoodRegion.Au,
            "US" => FoodRegion.Us,
            _ => FoodRegion.Default,
        };

    public static int Priority(FoodProductDto dto, FoodRegion region)
    {
        var isWholeFood = dto.FoodKind == FoodKind.WholeFood;
        if (isWholeFood)
        {
            if (region == FoodRegion.Au)
                return dto.DataSource == DataSources.Ausnut ? 300 : dto.DataSource == DataSources.Usda ? 200 : 100;
            return dto.DataSource == DataSources.Usda ? 300 : dto.DataSource == DataSources.Ausnut ? 200 : 100;
        }

        return dto.DataSource == DataSources.OpenFoodFacts ? 300
            : dto.DataSource == DataSources.Ausnut ? 250
            : dto.DataSource == DataSources.Usda ? 200
            : 100;
    }
}
