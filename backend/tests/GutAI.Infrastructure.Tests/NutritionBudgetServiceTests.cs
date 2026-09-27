using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class NutritionBudgetServiceTests
{
    [Fact]
    public async Task GetBudgetAsync_UsesLocalDayClampsRemainingAndAppliesMealShare()
    {
        var userId = Guid.NewGuid();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var localStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var insideId = Guid.NewGuid();
        var outsideId = Guid.NewGuid();
        var inside = new MealLog { Id = insideId, UserId = userId, LoggedAt = TimeZoneInfo.ConvertTimeToUtc(localStart.AddHours(1), zone), TotalCalories = 2200, TotalProteinG = 60, TotalCarbsG = 300, TotalFatG = 70 };
        var outside = new MealLog { Id = outsideId, UserId = userId, LoggedAt = inside.LoggedAt.AddDays(-1) };
        var items = new Dictionary<Guid, List<MealItem>>
        {
            [insideId] = [new MealItem { FoodName = "meal", ServingWeightG = 100m, Calories = 2200, ProteinG = 60, CarbsG = 300, FatG = 70, FiberG = 30, NutritionProvenance = "Sourced" }, new MealItem { FoodName = "unknown", NutritionProvenance = "Unknown" }],
            [outsideId] = [],
        };
        var store = new Mock<ITableStore>();
        store.Setup(s => s.GetUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = userId, TimezoneId = "Pacific/Kiritimati", DailyCalorieGoal = 2000, DailyProteinGoalG = 50, DailyCarbGoalG = 250, DailyFatGoalG = 65, DailyFiberGoalG = 25 });
        store.Setup(s => s.GetMealLogsByDateRangeAsync(userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([inside, outside]);
        store.Setup(s => s.GetMealItemsAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid _, Guid mealId, CancellationToken _) => items[mealId]);
        var service = new NutritionBudgetService(store.Object, new ConfigurationBuilder().AddInMemoryCollection().Build());

        var budget = await service.GetBudgetAsync(userId, "breakfast");

        Assert.Equal(today, budget.Date);
        Assert.Equal(1, budget.MealCount);
        Assert.Equal(1, budget.ItemsWithoutNutrition);
        Assert.Equal(2200m, budget.Consumed.Calories);
        Assert.Equal(0m, budget.Remaining.Calories);
        Assert.Equal(0m, budget.Remaining.ProteinG);
        Assert.Equal("Breakfast", budget.MealType);
        Assert.Equal(0m, budget.MealTarget!.Calories);
        Assert.Equal(0m, budget.MealTarget.FiberG);
        store.Verify(s => s.GetMealLogsByDateRangeAsync(userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBudgetAsync_UsesConfiguredShareAndProfileGoalDefaults()
    {
        var id = Guid.NewGuid();
        var store = new Mock<ITableStore>();
        store.Setup(s => s.GetUserAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        store.Setup(s => s.GetMealLogsByDateRangeAsync(id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MealSuggestions:MealShares:Lunch"] = "0.5" }).Build();
        var budget = await new NutritionBudgetService(store.Object, config).GetBudgetAsync(id, "LUNCH");
        Assert.Equal(2000m, budget.Goals.Calories);
        Assert.Equal(50m, budget.Goals.ProteinG);
        Assert.Equal(1000m, budget.MealTarget!.Calories);
        Assert.Equal(125m, budget.MealTarget.CarbsG);
    }
}
