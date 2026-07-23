using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// Query-independent per-candidate quality: source trust, metadata richness, nutrition
/// completeness, whole-food preference, and name cleanliness. Computed once per candidate
/// at <see cref="FoodMatchIndex.AddRange"/> time — none of it depends on the search query,
/// so it never needs to be recomputed per search call.
/// </summary>
internal static class FoodQualityScorer
{
    public static float Score(FoodProductDto dto)
    {
        var nameLower = dto.Name.ToLowerInvariant();
        float q = 0f;

        // Source trust
        if (dto.DataSource is "USDA" or "AUSNUT") q += 0.4f;

        // Richness boost: images/ingredients improve UX, but only for non-whole-foods —
        // USDA whole foods never have images, so don't let metadata bias crush them.
        bool isTrustedWholeFood = (dto.DataSource is "USDA" or "AUSNUT" && dto.FoodKind != FoodKind.Branded)
            || dto.FoodKind == FoodKind.WholeFood;
        if (!string.IsNullOrEmpty(dto.ImageUrl))
            q += isTrustedWholeFood ? 0.1f : 0.25f;
        if (!string.IsNullOrEmpty(dto.Ingredients))
            q += isTrustedWholeFood ? 0.05f : 0.15f;

        // Nutrition completeness
        if (dto.Calories100g.HasValue) q += 0.06f;
        if (dto.Protein100g.HasValue) q += 0.04f;
        if (dto.Carbs100g.HasValue) q += 0.03f;
        if (dto.Fat100g.HasValue) q += 0.03f;
        if (dto.Fiber100g.HasValue) q += 0.02f;
        if (dto.Sugar100g.HasValue) q += 0.02f;

        // Whole-food boost
        if (dto.FoodKind == FoodKind.WholeFood) q += 0.5f;
        else if (dto.FoodKind == FoodKind.Unknown)
        {
            bool looksWhole = string.IsNullOrEmpty(dto.Brand) &&
                (string.IsNullOrEmpty(dto.Ingredients) || !dto.Ingredients.Contains(','));
            if (looksWhole) q += 0.5f;
        }

        // Name length — shorter is better, but not so short it's cropped/truncated data
        if (dto.Name.Length <= 40)
            q += Math.Max(0f, 1f - dto.Name.Length / 60f) * 0.3f;
        else
            q -= (dto.Name.Length - 40) * (dto.Name.Length - 40) / 10000f;

        // Structural punctuation penalties (light — USDA uses structural commas)
        q -= dto.Name.Count(c => c == ',') * 0.05f;
        q -= dto.Name.Count(c => c == '(') * 0.15f;

        foreach (var term in FoodQualityTerms.HardPenaltyTerms)
            if (nameLower.Contains(term)) q -= 1.2f;
        foreach (var term in FoodQualityTerms.SoftPenaltyTerms)
            if (nameLower.Contains(term)) q -= 0.7f;

        return q;
    }
}
