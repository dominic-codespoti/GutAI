using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Services;

namespace GutAI.Infrastructure.Services;

/// <summary>Shared timezone-aware diary window used by Coach and nutrition budgets.</summary>
public static class TodaysMealsLoader
{
    public static async Task<List<MealLog>> LoadAsync(
        ITableStore store, Guid userId, User? user, string? timezoneId, CancellationToken ct = default)
    {
        var (rangeStart, rangeEnd) = TimeZoneHelper.GetUserTodayUtcRange(user, timezoneId);
        var meals = await store.GetMealLogsByDateRangeAsync(userId,
            DateOnly.FromDateTime(rangeStart), DateOnly.FromDateTime(rangeEnd), ct);
        meals = meals.Where(meal => meal.LoggedAt >= rangeStart && meal.LoggedAt <= rangeEnd).ToList();
        foreach (var meal in meals)
            meal.Items = await store.GetMealItemsAsync(userId, meal.Id, ct);
        return meals;
    }
}
