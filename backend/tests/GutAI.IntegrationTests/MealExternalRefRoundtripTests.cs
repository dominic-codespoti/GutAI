using FluentAssertions;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class MealExternalRefRoundtripTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGetMealLog_PersistsExternalSourceAndExternalId_WhenSet()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var loggedAt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var meal = new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Lunch,
            LoggedAt = loggedAt,
            TotalCalories = 500m,
            ExternalSource = "health-connect",
            ExternalId = "hc-rec-12345"
        };

        await fx.Store.UpsertMealLogAsync(meal);
        var loaded = await fx.Store.GetMealLogAsync(userId, mealId);

        loaded.Should().NotBeNull();
        loaded!.ExternalSource.Should().Be("health-connect");
        loaded.ExternalId.Should().Be("hc-rec-12345");
    }

    [Fact]
    public async Task UpsertAndGetMealLog_PersistsNullExternalSourceAndExternalId()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var loggedAt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var meal = new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Dinner,
            LoggedAt = loggedAt,
            TotalCalories = 600m,
            ExternalSource = null,
            ExternalId = null
        };

        await fx.Store.UpsertMealLogAsync(meal);
        var loaded = await fx.Store.GetMealLogAsync(userId, mealId);

        loaded.Should().NotBeNull();
        loaded!.ExternalSource.Should().BeNull();
        loaded.ExternalId.Should().BeNull();
    }

    [Fact]
    public async Task GetMealLogByExternalRefAsync_FindsMealBySourceAndExternalId()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var loggedAt = new DateTime(2025, 6, 15, 8, 30, 0, DateTimeKind.Utc);

        var meal = new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Breakfast,
            LoggedAt = loggedAt,
            TotalCalories = 350m,
            ExternalSource = "myfitnesspal",
            ExternalId = "mfp-meal-999"
        };

        await fx.Store.UpsertMealLogAsync(meal);

        var found = await fx.Store.GetMealLogByExternalRefAsync(userId, "myfitnesspal", "mfp-meal-999");
        found.Should().NotBeNull();
        found!.Id.Should().Be(mealId);
        found.UserId.Should().Be(userId);
        found.ExternalSource.Should().Be("myfitnesspal");
        found.ExternalId.Should().Be("mfp-meal-999");
        found.TotalCalories.Should().Be(350m);
    }

    [Fact]
    public async Task GetMealLogByExternalRefAsync_ReturnsNull_ForUnknownPairOrDifferentUser()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var mealId = Guid.NewGuid();

        var meal = new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Snack,
            LoggedAt = DateTime.UtcNow,
            ExternalSource = "cronometer",
            ExternalId = "crono-555"
        };

        await fx.Store.UpsertMealLogAsync(meal);

        // Different source
        var wrongSource = await fx.Store.GetMealLogByExternalRefAsync(userId, "healthkit", "crono-555");
        wrongSource.Should().BeNull();

        // Different externalId
        var wrongExternalId = await fx.Store.GetMealLogByExternalRefAsync(userId, "cronometer", "crono-999");
        wrongExternalId.Should().BeNull();

        // Different user
        var wrongUser = await fx.Store.GetMealLogByExternalRefAsync(otherUserId, "cronometer", "crono-555");
        wrongUser.Should().BeNull();
    }

    [Fact]
    public async Task GetMealLogByExternalRefAsync_IgnoresSoftDeletedRows()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();

        var meal = new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Lunch,
            LoggedAt = DateTime.UtcNow,
            ExternalSource = "healthkit",
            ExternalId = "hk-uuid-delete-test",
            IsDeleted = true
        };

        await fx.Store.UpsertMealLogAsync(meal);

        var found = await fx.Store.GetMealLogByExternalRefAsync(userId, "healthkit", "hk-uuid-delete-test");
        found.Should().BeNull();
    }
}
