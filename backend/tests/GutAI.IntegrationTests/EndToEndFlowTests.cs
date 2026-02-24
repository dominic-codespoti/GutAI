using FluentAssertions;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class MealDiaryFlowTests(AzuriteFixture fx)
{
    [Fact]
    public async Task FullMealLifecycle_CreateUpdateSoftDelete()
    {
        var userId = Guid.NewGuid();
        await fx.Store.UpsertUserAsync(new User { Id = userId, Email = "meal-flow@test.com" });

        var mealId = Guid.NewGuid();
        var loggedAt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        await fx.Store.UpsertMealLogAsync(new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Lunch,
            LoggedAt = loggedAt,
            TotalCalories = 500
        });

        await fx.Store.UpsertMealItemsAsync(userId, mealId, [
            new() { Id = Guid.NewGuid(), FoodName = "Grilled Chicken", Calories = 300, ProteinG = 35 },
            new() { Id = Guid.NewGuid(), FoodName = "Brown Rice", Calories = 200, CarbsG = 45 }
        ]);

        var meal = await fx.Store.GetMealLogAsync(userId, mealId);
        meal.Should().NotBeNull();
        meal!.Items.Should().HaveCount(2);
        meal.TotalCalories.Should().Be(500);

        // Update items
        await fx.Store.UpsertMealItemsAsync(userId, mealId, [
            new() { Id = Guid.NewGuid(), FoodName = "Grilled Chicken", Calories = 300, ProteinG = 35 },
            new() { Id = Guid.NewGuid(), FoodName = "Quinoa", Calories = 220, CarbsG = 39 },
            new() { Id = Guid.NewGuid(), FoodName = "Side Salad", Calories = 50 }
        ]);
        await fx.Store.UpsertMealLogAsync(new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Lunch,
            LoggedAt = loggedAt,
            TotalCalories = 570
        });

        meal = await fx.Store.GetMealLogAsync(userId, mealId);
        meal!.Items.Should().HaveCount(3);
        meal.TotalCalories.Should().Be(570);

        // Soft delete
        await fx.Store.UpsertMealLogAsync(new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = MealType.Lunch,
            LoggedAt = loggedAt,
            IsDeleted = true
        });

        var dayMeals = await fx.Store.GetMealLogsByDateAsync(userId, new DateOnly(2025, 6, 15));
        dayMeals.Should().BeEmpty();

        // Direct get still returns it
        var directGet = await fx.Store.GetMealLogAsync(userId, mealId);
        directGet.Should().NotBeNull();
    }

    [Fact]
    public async Task MultipleMealsInDay_AllTracked()
    {
        var userId = Guid.NewGuid();
        var date = new DateTime(2025, 7, 4, 0, 0, 0, DateTimeKind.Utc);

        var breakfast = new MealLog { Id = Guid.NewGuid(), UserId = userId, MealType = MealType.Breakfast, LoggedAt = date.AddHours(8) };
        var lunch = new MealLog { Id = Guid.NewGuid(), UserId = userId, MealType = MealType.Lunch, LoggedAt = date.AddHours(12) };
        var dinner = new MealLog { Id = Guid.NewGuid(), UserId = userId, MealType = MealType.Dinner, LoggedAt = date.AddHours(19) };
        var snack = new MealLog { Id = Guid.NewGuid(), UserId = userId, MealType = MealType.Snack, LoggedAt = date.AddHours(15) };

        await fx.Store.UpsertMealLogAsync(breakfast);
        await fx.Store.UpsertMealLogAsync(lunch);
        await fx.Store.UpsertMealLogAsync(dinner);
        await fx.Store.UpsertMealLogAsync(snack);

        await fx.Store.UpsertMealItemsAsync(userId, breakfast.Id, [new() { Id = Guid.NewGuid(), FoodName = "Oatmeal", Calories = 300 }]);
        await fx.Store.UpsertMealItemsAsync(userId, lunch.Id, [new() { Id = Guid.NewGuid(), FoodName = "Sandwich", Calories = 500 }]);
        await fx.Store.UpsertMealItemsAsync(userId, dinner.Id, [new() { Id = Guid.NewGuid(), FoodName = "Pasta", Calories = 700 }]);
        await fx.Store.UpsertMealItemsAsync(userId, snack.Id, [new() { Id = Guid.NewGuid(), FoodName = "Apple", Calories = 80 }]);

        var dayMeals = await fx.Store.GetMealLogsByDateAsync(userId, new DateOnly(2025, 7, 4));
        dayMeals.Should().HaveCount(4);
        dayMeals.Should().BeInAscendingOrder(m => m.LoggedAt);
    }
}

[Collection("Azurite")]
public class SymptomTrackingFlowTests(AzuriteFixture fx)
{
    [Fact]
    public async Task MealThenSymptom_LinkedByMealLogId()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var mealTime = new DateTime(2025, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var symptomTime = mealTime.AddHours(3);

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = mealTime, MealType = MealType.Lunch });
        await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = "Spicy Curry" }]);

        var symptomId = Guid.NewGuid();
        await fx.Store.UpsertSymptomLogAsync(new SymptomLog
        {
            Id = symptomId,
            UserId = userId,
            SymptomTypeId = 1,
            Severity = 7,
            OccurredAt = symptomTime,
            RelatedMealLogId = mealId,
            Notes = "Burning sensation"
        });

        var symptom = await fx.Store.GetSymptomLogAsync(userId, symptomId);
        symptom!.RelatedMealLogId.Should().Be(mealId);
        symptom.Severity.Should().Be(7);

        var relatedMeal = await fx.Store.GetMealLogAsync(userId, symptom.RelatedMealLogId!.Value);
        relatedMeal.Should().NotBeNull();
        relatedMeal!.Items.Should().Contain(i => i.FoodName == "Spicy Curry");
    }

    [Fact]
    public async Task WeekOfSymptomsAndMeals_DateRangeQueryWorks()
    {
        var userId = Guid.NewGuid();
        var baseDate = new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        for (var day = 0; day < 7; day++)
        {
            var mealId = Guid.NewGuid();
            var dt = baseDate.AddDays(day).AddHours(12);
            await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = dt, MealType = MealType.Lunch });
            await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = $"Food-Day{day}" }]);

            if (day % 2 == 0)
            {
                await fx.Store.UpsertSymptomLogAsync(new SymptomLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SymptomTypeId = 1,
                    Severity = 5,
                    OccurredAt = dt.AddHours(3)
                });
            }
        }

        var meals = await fx.Store.GetMealLogsByDateRangeAsync(userId, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 16));
        meals.Should().HaveCount(7);

        var symptoms = await fx.Store.GetSymptomLogsByDateRangeAsync(userId, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 16));
        symptoms.Should().HaveCount(4); // days 0,2,4,6
    }
}

[Collection("Azurite")]
public class FoodDiaryAnalysisE2ETests(AzuriteFixture fx)
{
    private readonly FoodDiaryAnalysisService _sut = new();

    [Fact]
    public async Task AnalyzeAsync_WithRealStore_FindsCorrelations()
    {
        var userId = Guid.NewGuid();
        var now = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = 7001, Name = "Bloating", Category = "GI" });

        for (var i = 0; i < 4; i++)
        {
            var mealTime = now.AddDays(-(i * 3));
            var mealId = Guid.NewGuid();
            await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = mealTime, MealType = MealType.Lunch });
            await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = "Garlic Bread", Calories = 200 }]);
            await fx.Store.UpsertSymptomLogAsync(new SymptomLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SymptomTypeId = 7001,
                Severity = 6,
                OccurredAt = mealTime.AddHours(3)
            });
        }

        var from = DateOnly.FromDateTime(now.AddDays(-30));
        var to = DateOnly.FromDateTime(now.AddDays(1));
        var analysis = await _sut.AnalyzeAsync(userId, from, to, fx.Store);

        analysis.TotalMealsAnalyzed.Should().Be(4);
        analysis.TotalSymptomsAnalyzed.Should().Be(4);
        analysis.PatternsFound.Should().BeGreaterThan(0);
        analysis.Patterns.Should().Contain(p => p.FoodName == "Garlic Bread" && p.SymptomName == "Bloating");

        var pattern = analysis.Patterns.First(p => p.FoodName == "Garlic Bread");
        pattern.Occurrences.Should().Be(4);
        pattern.AverageOnsetHours.Should().Be(3.0m);
        pattern.Confidence.Should().Be("Medium");
    }

    [Fact]
    public async Task AnalyzeAsync_NoData_ReturnsEmptyAnalysis()
    {
        var userId = Guid.NewGuid();
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 1, 31);

        var analysis = await _sut.AnalyzeAsync(userId, from, to, fx.Store);

        analysis.TotalMealsAnalyzed.Should().Be(0);
        analysis.TotalSymptomsAnalyzed.Should().Be(0);
        analysis.PatternsFound.Should().Be(0);
        analysis.Summary.Should().Contain("No meals or symptoms");
    }

    [Fact]
    public async Task AnalyzeAsync_MealsButNoSymptoms_NoPatternsButMealsCounted()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var dt = new DateTime(2025, 4, 10, 12, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = dt, MealType = MealType.Lunch });
        await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = "Rice" }]);

        var analysis = await _sut.AnalyzeAsync(userId, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), fx.Store);

        analysis.TotalMealsAnalyzed.Should().Be(1);
        analysis.TotalSymptomsAnalyzed.Should().Be(0);
        analysis.PatternsFound.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_SymptomOutsideWindow_NotCorrelated()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var mealTime = new DateTime(2025, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = 7002, Name = "Cramps", Category = "GI" });
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = mealTime, MealType = MealType.Lunch });
        await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = "Pizza" }]);
        await fx.Store.UpsertSymptomLogAsync(new SymptomLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SymptomTypeId = 7002,
            Severity = 5,
            OccurredAt = mealTime.AddHours(10) // beyond the 8h window
        });

        var analysis = await _sut.AnalyzeAsync(userId, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 2), fx.Store);
        analysis.Patterns.Should().NotContain(p => p.FoodName == "Pizza");
    }

    [Fact]
    public async Task GetEliminationStatus_WithRealStore()
    {
        var userId = Guid.NewGuid();
        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = 7003, Name = "Bloating", Category = "GI" });

        var analysis = await _sut.GetEliminationStatusAsync(userId, fx.Store);
        analysis.Phase.Should().Be("Not Started");
        analysis.Summary.Should().Contain("No symptoms have been logged");
    }
}

[Collection("Azurite")]
public class NutritionTrackingFlowTests(AzuriteFixture fx)
{
    [Fact]
    public async Task DayOfEating_SummarizedCorrectly()
    {
        var userId = Guid.NewGuid();
        var date = new DateOnly(2025, 8, 20);
        var baseTime = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Log 3 meals
        var meals = new[]
        {
            (MealType.Breakfast, 350m, 15m, 50m, 10m),
            (MealType.Lunch, 600m, 35m, 60m, 25m),
            (MealType.Dinner, 750m, 40m, 80m, 30m)
        };

        foreach (var (type, cal, prot, carb, fat) in meals)
        {
            var mealId = Guid.NewGuid();
            await fx.Store.UpsertMealLogAsync(new MealLog
            {
                Id = mealId,
                UserId = userId,
                MealType = type,
                LoggedAt = baseTime.AddHours(type == MealType.Breakfast ? 8 : type == MealType.Lunch ? 12 : 19),
                TotalCalories = cal,
                TotalProteinG = prot,
                TotalCarbsG = carb,
                TotalFatG = fat
            });
        }

        // Create and store summary
        var summary = new DailyNutritionSummary
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = date,
            TotalCalories = 1700,
            TotalProteinG = 90,
            TotalCarbsG = 190,
            TotalFatG = 65,
            MealCount = 3,
            CalorieGoal = 2000
        };
        await fx.Store.UpsertDailyNutritionSummaryAsync(summary);

        // Verify
        var dayMeals = await fx.Store.GetMealLogsByDateAsync(userId, date);
        dayMeals.Should().HaveCount(3);

        var loadedSummary = await fx.Store.GetDailyNutritionSummaryAsync(userId, date);
        loadedSummary!.TotalCalories.Should().Be(1700);
        loadedSummary.MealCount.Should().Be(3);
        loadedSummary.CalorieGoal.Should().Be(2000);
    }
}

[Collection("Azurite")]
public class FoodProductAndAdditiveFlowTests(AzuriteFixture fx)
{
    [Fact]
    public async Task CreateProduct_LinkAdditives_QueryByBarcode()
    {
        var productId = Guid.NewGuid();
        var barcode = $"FLOW-{productId.ToString()[..8]}";

        // Create additives
        var additiveId1 = Random.Shared.Next(700000, 799999);
        var additiveId2 = Random.Shared.Next(800000, 899999);
        await fx.Store.UpsertFoodAdditiveAsync(new FoodAdditive { Id = additiveId1, Name = "Carrageenan", Category = "Thickener", CspiRating = CspiRating.Avoid });
        await fx.Store.UpsertFoodAdditiveAsync(new FoodAdditive { Id = additiveId2, Name = "Citric Acid", Category = "Preservative", CspiRating = CspiRating.Safe });

        // Create food product
        await fx.Store.UpsertFoodProductAsync(new FoodProduct
        {
            Id = productId,
            Name = "Almond Milk",
            Barcode = barcode,
            Ingredients = "water, almonds, carrageenan, citric acid",
            NovaGroup = 3,
            Calories100g = 30
        });
        await fx.Store.SetAdditiveIdsForProductAsync(productId, [additiveId1, additiveId2]);

        // Lookup by barcode
        var product = await fx.Store.GetFoodProductByBarcodeAsync(barcode);
        product.Should().NotBeNull();
        product!.Name.Should().Be("Almond Milk");
        product.Ingredients.Should().Contain("carrageenan");

        // Get linked additives
        var additiveIds = await fx.Store.GetAdditiveIdsForProductAsync(productId);
        additiveIds.Should().HaveCount(2);
        additiveIds.Should().Contain(additiveId1);
        additiveIds.Should().Contain(additiveId2);

        // Get full additive details
        var carrageenan = await fx.Store.GetFoodAdditiveAsync(additiveId1);
        carrageenan!.CspiRating.Should().Be(CspiRating.Avoid);
    }

    [Fact]
    public async Task MealItem_LinkedToFoodProduct()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var barcode = $"LINK-{productId.ToString()[..8]}";

        await fx.Store.UpsertFoodProductAsync(new FoodProduct
        {
            Id = productId,
            Name = "Greek Yogurt",
            Barcode = barcode,
            Calories100g = 97,
            Protein100g = 10,
            Fat100g = 5
        });

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = DateTime.UtcNow, MealType = MealType.Snack });
        await fx.Store.UpsertMealItemsAsync(userId, mealId, [
            new()
            {
                Id = Guid.NewGuid(), FoodName = "Greek Yogurt", Barcode = barcode,
                FoodProductId = productId, Servings = 1, Calories = 145, ProteinG = 15, FatG = 7.5m
            }
        ]);

        var meal = await fx.Store.GetMealLogAsync(userId, mealId);
        var item = meal!.Items.First();
        item.FoodProductId.Should().Be(productId);

        var product = await fx.Store.GetFoodProductAsync(item.FoodProductId!.Value);
        product.Should().NotBeNull();
        product!.Name.Should().Be("Greek Yogurt");
    }
}

[Collection("Azurite")]
public class UserAlertFlowTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UserSetsAlerts_ScansProduct_ChecksAdditives()
    {
        var userId = Guid.NewGuid();
        await fx.Store.UpsertUserAsync(new User { Id = userId, Email = "alert-test@test.com" });

        // Create additives
        var dangerousId = Random.Shared.Next(900000, 999999);
        var safeId = dangerousId + 1;
        await fx.Store.UpsertFoodAdditiveAsync(new FoodAdditive { Id = dangerousId, Name = "Potassium Bromate", Category = "Flour Treatment", CspiRating = CspiRating.Avoid });
        await fx.Store.UpsertFoodAdditiveAsync(new FoodAdditive { Id = safeId, Name = "Ascorbic Acid", Category = "Antioxidant", CspiRating = CspiRating.Safe });

        // User alerts on the dangerous one
        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = userId, FoodAdditiveId = dangerousId, AlertEnabled = true });

        // Create a product that has both
        var productId = Guid.NewGuid();
        await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = productId, Name = "White Bread" });
        await fx.Store.SetAdditiveIdsForProductAsync(productId, [dangerousId, safeId]);

        // Check product's additives against user alerts
        var alerts = await fx.Store.GetUserFoodAlertsAsync(userId);
        var alertAdditiveIds = alerts.Where(a => a.AlertEnabled).Select(a => a.FoodAdditiveId).ToHashSet();
        var productAdditiveIds = await fx.Store.GetAdditiveIdsForProductAsync(productId);
        var matchingAlerts = productAdditiveIds.Where(id => alertAdditiveIds.Contains(id)).ToList();

        matchingAlerts.Should().HaveCount(1);
        matchingAlerts.Should().Contain(dangerousId);
    }
}

[Collection("Azurite")]
public class AuthFlowTests(AzuriteFixture fx)
{
    [Fact]
    public async Task RegisterLoginRefreshLogout_FullCycle()
    {
        var userId = Guid.NewGuid();
        var email = $"auth-{userId}@test.com";

        // Register
        await fx.Store.UpsertUserAsync(new User { Id = userId, Email = email });
        await fx.Store.UpsertIdentityAsync(new IdentityRecord
        {
            UserId = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "hashed-password",
            SecurityStamp = Guid.NewGuid().ToString()
        });

        // Login produces token
        var tokenValue = $"login-token-{Guid.NewGuid()}";
        await fx.Store.UpsertRefreshTokenAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = tokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        // Verify identity lookup works
        var identity = await fx.Store.GetIdentityByEmailAsync(email);
        identity.Should().NotBeNull();
        identity!.PasswordHash.Should().Be("hashed-password");

        // Verify token works
        var token = await fx.Store.GetRefreshTokenByValueAsync(tokenValue);
        token.Should().NotBeNull();
        token!.IsActive.Should().BeTrue();

        // Refresh: revoke old, issue new
        token.RevokedAt = DateTime.UtcNow;
        var newTokenValue = $"refreshed-{Guid.NewGuid()}";
        token.ReplacedByToken = newTokenValue;
        await fx.Store.UpsertRefreshTokenAsync(token);

        await fx.Store.UpsertRefreshTokenAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = newTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        // Old token is now revoked
        var oldToken = await fx.Store.GetRefreshTokenByValueAsync(tokenValue);
        oldToken!.IsRevoked.Should().BeTrue();
        oldToken.ReplacedByToken.Should().Be(newTokenValue);

        // New token is active
        var activeTokens = await fx.Store.GetActiveRefreshTokensAsync(userId);
        activeTokens.Should().HaveCount(1);
        activeTokens[0].Token.Should().Be(newTokenValue);

        // Logout — delete all tokens
        await fx.Store.DeleteRefreshTokensForUserAsync(userId);
        (await fx.Store.GetActiveRefreshTokensAsync(userId)).Should().BeEmpty();
    }
}

[Collection("Azurite")]
public class DataIsolationTests(AzuriteFixture fx)
{
    [Fact]
    public async Task DifferentUsers_CompleteIsolation()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await fx.Store.UpsertUserAsync(new User { Id = alice, Email = "alice@isolation.com", Allergies = ["peanuts"] });
        await fx.Store.UpsertUserAsync(new User { Id = bob, Email = "bob@isolation.com", Allergies = ["shellfish"] });

        var dt = new DateTime(2025, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var aliceMealId = Guid.NewGuid();
        var bobMealId = Guid.NewGuid();

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = aliceMealId, UserId = alice, LoggedAt = dt, MealType = MealType.Lunch });
        await fx.Store.UpsertMealItemsAsync(alice, aliceMealId, [new() { Id = Guid.NewGuid(), FoodName = "Alice's Salad" }]);

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = bobMealId, UserId = bob, LoggedAt = dt, MealType = MealType.Lunch });
        await fx.Store.UpsertMealItemsAsync(bob, bobMealId, [new() { Id = Guid.NewGuid(), FoodName = "Bob's Burger" }]);

        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = Guid.NewGuid(), UserId = alice, SymptomTypeId = 1, Severity = 5, OccurredAt = dt.AddHours(2) });
        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = Guid.NewGuid(), UserId = bob, SymptomTypeId = 2, Severity = 8, OccurredAt = dt.AddHours(3) });

        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = alice, FoodAdditiveId = 1 });
        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = bob, FoodAdditiveId = 2 });

        // Alice's data
        var aliceMeals = await fx.Store.GetMealLogsByDateAsync(alice, new DateOnly(2025, 10, 1));
        aliceMeals.Should().HaveCount(1);

        var aliceMeal = await fx.Store.GetMealLogAsync(alice, aliceMealId);
        aliceMeal!.Items.Select(i => i.FoodName).Should().Contain("Alice's Salad");
        aliceMeal.Items.Select(i => i.FoodName).Should().NotContain("Bob's Burger");

        var aliceSymptoms = await fx.Store.GetSymptomLogsByDateAsync(alice, new DateOnly(2025, 10, 1));
        aliceSymptoms.Should().HaveCount(1);
        aliceSymptoms[0].Severity.Should().Be(5);

        var aliceAlerts = await fx.Store.GetUserFoodAlertsAsync(alice);
        aliceAlerts.Should().HaveCount(1);
        aliceAlerts[0].FoodAdditiveId.Should().Be(1);

        // Bob's data
        var bobMeals = await fx.Store.GetMealLogsByDateAsync(bob, new DateOnly(2025, 10, 1));
        bobMeals.Should().HaveCount(1);

        var bobMeal = await fx.Store.GetMealLogAsync(bob, bobMealId);
        bobMeal!.Items.Select(i => i.FoodName).Should().Contain("Bob's Burger");

        var bobSymptoms = await fx.Store.GetSymptomLogsByDateAsync(bob, new DateOnly(2025, 10, 1));
        bobSymptoms.Should().HaveCount(1);
        bobSymptoms[0].Severity.Should().Be(8);

        var bobAlerts = await fx.Store.GetUserFoodAlertsAsync(bob);
        bobAlerts.Should().HaveCount(1);
        bobAlerts[0].FoodAdditiveId.Should().Be(2);
    }
}

[Collection("Azurite")]
public class ConcurrencyTests(AzuriteFixture fx)
{
    [Fact]
    public async Task ParallelUpserts_AllSucceed()
    {
        var userId = Guid.NewGuid();
        var tasks = Enumerable.Range(0, 10).Select(i =>
        {
            var mealId = Guid.NewGuid();
            return Task.WhenAll(
                fx.Store.UpsertMealLogAsync(new MealLog
                {
                    Id = mealId,
                    UserId = userId,
                    LoggedAt = new DateTime(2025, 11, 1, 8 + i, 0, 0, DateTimeKind.Utc),
                    MealType = MealType.Snack
                }),
                fx.Store.UpsertMealItemsAsync(userId, mealId, [
                    new() { Id = Guid.NewGuid(), FoodName = $"Food-{i}" }
                ])
            );
        });

        await Task.WhenAll(tasks);

        var meals = await fx.Store.GetMealLogsByDateAsync(userId, new DateOnly(2025, 11, 1));
        meals.Should().HaveCount(10);
    }

    [Fact]
    public async Task ParallelUserCreation_AllSucceed()
    {
        var tasks = Enumerable.Range(0, 20).Select(i =>
            fx.Store.UpsertUserAsync(new User { Id = Guid.NewGuid(), Email = $"parallel-{Guid.NewGuid()}@test.com" })
        );

        var act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }
}
