using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Helpers;

/// <summary>
/// Canonical identity for a food search candidate, used to deduplicate results across
/// multiple providers (USDA, OpenFoodFacts, embedded databases, local store) without
/// collapsing genuinely distinct products that merely share a display name.
/// Precedence: barcode (globally unique) > source + external id (provider-unique) >
/// brand + name (best-effort fallback for providers with no stable id).
/// </summary>
public static class FoodCandidateIdentity
{
    public static string Of(FoodProductDto dto) =>
        !string.IsNullOrWhiteSpace(dto.Barcode)
            ? $"barcode:{dto.Barcode}"
            : !string.IsNullOrWhiteSpace(dto.ExternalId)
                ? $"{dto.DataSource}:{dto.ExternalId}"
                : $"name:{dto.Brand}:{dto.Name}";
}
