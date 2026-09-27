using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace GutAI.Infrastructure.Services;

public sealed class NutritionBudgetService(ITableStore store, IConfiguration config) : INutritionBudgetService
{
    public async Task<NutritionBudgetDto> GetBudgetAsync(
        Guid userId, string? mealType = null, string? timezoneId = null, CancellationToken ct = default)
    {
        var user = await store.GetUserAsync(userId, ct);
        var meals = await TodaysMealsLoader.LoadAsync(store, userId, user, timezoneId, ct);
        var zone = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var consumed = new NutritionTargetsDto
        {
            Calories = meals.Sum(m => m.TotalCalories),
            ProteinG = meals.Sum(m => m.TotalProteinG),
            CarbsG = meals.Sum(m => m.TotalCarbsG),
            FatG = meals.Sum(m => m.TotalFatG),
            FiberG = meals.SelectMany(m => m.Items).Sum(i => i.FiberG),
        };
        var goals = new NutritionTargetsDto
        {
            Calories = user?.DailyCalorieGoal ?? 2000,
            ProteinG = user?.DailyProteinGoalG ?? 50,
            CarbsG = user?.DailyCarbGoalG ?? 250,
            FatG = user?.DailyFatGoalG ?? 65,
            FiberG = user?.DailyFiberGoalG ?? 25,
        };
        var remaining = new NutritionTargetsDto
        {
            Calories = Math.Max(0, goals.Calories - consumed.Calories),
            ProteinG = Math.Max(0, goals.ProteinG - consumed.ProteinG),
            CarbsG = Math.Max(0, goals.CarbsG - consumed.CarbsG),
            FatG = Math.Max(0, goals.FatG - consumed.FatG),
            FiberG = Math.Max(0, goals.FiberG - consumed.FiberG),
        };
        var target = TryGetShare(mealType, out var share)
            ? new NutritionTargetsDto
            {
                Calories = Math.Min(remaining.Calories, goals.Calories * share),
                ProteinG = Math.Min(remaining.ProteinG, goals.ProteinG * share),
                CarbsG = Math.Min(remaining.CarbsG, goals.CarbsG * share),
                FatG = Math.Min(remaining.FatG, goals.FatG * share),
                FiberG = Math.Min(remaining.FiberG, goals.FiberG * share),
            }
            : null;
        var noNutrition = meals.SelectMany(m => m.Items).Count(i =>
            string.IsNullOrWhiteSpace(i.NutritionProvenance)
            || i.NutritionProvenance.Equals(nameof(NutritionProvenance.Unknown), StringComparison.OrdinalIgnoreCase)
            || (i.Calories <= 0 && i.ProteinG <= 0 && i.CarbsG <= 0 && i.FatG <= 0 && i.FiberG <= 0));
        return new NutritionBudgetDto
        {
            Date = today,
            Goals = goals,
            Consumed = consumed,
            Remaining = remaining,
            MealCount = meals.Count,
            ItemsWithoutNutrition = noNutrition,
            MealType = target is null ? null : CanonicalMealType(mealType!),
            MealTarget = target,
        };
    }

    private bool TryGetShare(string? mealType, out decimal share)
    {
        share = 0;
        if (mealType is null) return false;
        var canonical = CanonicalMealType(mealType);
        var fallback = canonical switch { "Breakfast" => .25m, "Lunch" => .35m, "Dinner" => .30m, "Snack" => .10m, _ => 0m };
        share = config.GetValue($"MealSuggestions:MealShares:{canonical}", fallback);
        return fallback > 0;
    }

    private static string CanonicalMealType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "breakfast" => "Breakfast",
        "lunch" => "Lunch",
        "dinner" => "Dinner",
        "snack" => "Snack",
        _ => value,
    };
}
