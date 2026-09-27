using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// Remaining nutrition budget for today (goals minus committed diary totals), shared by the
/// Coach <c>get_nutrition_summary</c> tool and meal suggestions.
/// </summary>
public interface INutritionBudgetService
{
    /// <param name="mealType">When set, <see cref="NutritionBudgetDto.MealTarget"/> holds that meal's configured share of the remaining budget.</param>
    /// <param name="timezoneId">IANA/Windows zone overriding the profile's <c>TimezoneId</c> for "today".</param>
    Task<NutritionBudgetDto> GetBudgetAsync(
        Guid userId,
        string? mealType = null,
        string? timezoneId = null,
        CancellationToken ct = default);
}
