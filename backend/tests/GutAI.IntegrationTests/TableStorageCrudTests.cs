using FluentAssertions;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class UserStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTripsAllFields()
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            Email = "alice@example.com",
            DisplayName = "Alice",
            DailyCalorieGoal = 1800,
            DailyProteinGoalG = 60,
            DailyCarbGoalG = 200,
            DailyFatGoalG = 55,
            DailyFiberGoalG = 30,
            Allergies = ["peanuts", "shellfish"],
            DietaryPreferences = ["vegan"],
            OnboardingCompleted = true,
            TimezoneId = "America/New_York"
        };

        await fx.Store.UpsertUserAsync(user);
        var loaded = await fx.Store.GetUserAsync(id);

        loaded.Should().NotBeNull();
        loaded!.Email.Should().Be("alice@example.com");
        loaded.DisplayName.Should().Be("Alice");
        loaded.DailyCalorieGoal.Should().Be(1800);
        loaded.DailyProteinGoalG.Should().Be(60);
        loaded.DailyCarbGoalG.Should().Be(200);
        loaded.DailyFatGoalG.Should().Be(55);
        loaded.DailyFiberGoalG.Should().Be(30);
        loaded.Allergies.Should().BeEquivalentTo(["peanuts", "shellfish"]);
        loaded.DietaryPreferences.Should().BeEquivalentTo(["vegan"]);
        loaded.OnboardingCompleted.Should().BeTrue();
        loaded.TimezoneId.Should().Be("America/New_York");
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var result = await fx.Store.GetUserAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_OverwritesPreviousValues()
    {
        var id = Guid.NewGuid();
        await fx.Store.UpsertUserAsync(new User { Id = id, Email = "v1@test.com", DailyCalorieGoal = 2000 });
        await fx.Store.UpsertUserAsync(new User { Id = id, Email = "v2@test.com", DailyCalorieGoal = 2500 });

        var loaded = await fx.Store.GetUserAsync(id);
        loaded!.Email.Should().Be("v2@test.com");
        loaded.DailyCalorieGoal.Should().Be(2500);
    }

    [Fact]
    public async Task Delete_RemovesUser()
    {
        var id = Guid.NewGuid();
        await fx.Store.UpsertUserAsync(new User { Id = id, Email = "del@test.com" });
        await fx.Store.DeleteUserAsync(id);

        var loaded = await fx.Store.GetUserAsync(id);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Delete_NonExistent_DoesNotThrow()
    {
        var act = () => fx.Store.DeleteUserAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EmptyArrays_RoundTripCorrectly()
    {
        var id = Guid.NewGuid();
        await fx.Store.UpsertUserAsync(new User { Id = id, Email = "empty@test.com", Allergies = [], DietaryPreferences = [] });

        var loaded = await fx.Store.GetUserAsync(id);
        loaded!.Allergies.Should().BeEmpty();
        loaded.DietaryPreferences.Should().BeEmpty();
    }
}

[Collection("Azurite")]
public class IdentityStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGetById_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var identity = new IdentityRecord
        {
            UserId = userId,
            Email = $"id-{userId}@test.com",
            NormalizedEmail = $"ID-{userId}@TEST.COM",
            PasswordHash = "hashed-pw-123",
            SecurityStamp = "stamp-abc"
        };

        await fx.Store.UpsertIdentityAsync(identity);
        var loaded = await fx.Store.GetIdentityByIdAsync(userId);

        loaded.Should().NotBeNull();
        loaded!.Email.Should().Be(identity.Email);
        loaded.PasswordHash.Should().Be("hashed-pw-123");
        loaded.SecurityStamp.Should().Be("stamp-abc");
    }

    [Fact]
    public async Task GetByEmail_FindsViaLookupEntity()
    {
        var userId = Guid.NewGuid();
        var email = $"lookup-{userId}@test.com";
        await fx.Store.UpsertIdentityAsync(new IdentityRecord
        {
            UserId = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "pw"
        });

        var loaded = await fx.Store.GetIdentityByEmailAsync(email);
        loaded.Should().NotBeNull();
        loaded!.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task GetByEmail_CaseInsensitive()
    {
        var userId = Guid.NewGuid();
        var email = $"CaSe-{userId}@Example.COM";
        await fx.Store.UpsertIdentityAsync(new IdentityRecord
        {
            UserId = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "pw"
        });

        var loaded = await fx.Store.GetIdentityByEmailAsync(email.ToLowerInvariant());
        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByEmail_NonExistent_ReturnsNull()
    {
        var result = await fx.Store.GetIdentityByEmailAsync("nope@nope.com");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesIdentityAndLookup()
    {
        var userId = Guid.NewGuid();
        var email = $"del-id-{userId}@test.com";
        await fx.Store.UpsertIdentityAsync(new IdentityRecord
        {
            UserId = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "pw"
        });

        await fx.Store.DeleteIdentityAsync(userId);

        (await fx.Store.GetIdentityByIdAsync(userId)).Should().BeNull();
        (await fx.Store.GetIdentityByEmailAsync(email)).Should().BeNull();
    }
}

[Collection("Azurite")]
public class MealLogStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGetMealLog_RoundTrips()
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
            Notes = "Tasty lunch",
            TotalCalories = 550.5m,
            TotalProteinG = 30.2m,
            TotalCarbsG = 60.0m,
            TotalFatG = 20.1m,
            OriginalText = "chicken and rice"
        };

        await fx.Store.UpsertMealLogAsync(meal);
        var loaded = await fx.Store.GetMealLogAsync(userId, mealId);

        loaded.Should().NotBeNull();
        loaded!.MealType.Should().Be(MealType.Lunch);
        loaded.LoggedAt.Should().Be(loggedAt);
        loaded.Notes.Should().Be("Tasty lunch");
        loaded.TotalCalories.Should().Be(550.5m);
        loaded.TotalProteinG.Should().Be(30.2m);
        loaded.OriginalText.Should().Be("chicken and rice");
        loaded.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetByDateRange_FiltersCorrectly()
    {
        var userId = Guid.NewGuid();
        var day1 = new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2025, 3, 5, 12, 0, 0, DateTimeKind.Utc);
        var day3 = new DateTime(2025, 3, 10, 18, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = day1, MealType = MealType.Breakfast });
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = day2, MealType = MealType.Lunch });
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = day3, MealType = MealType.Dinner });

        var range = await fx.Store.GetMealLogsByDateRangeAsync(userId, new DateOnly(2025, 3, 2), new DateOnly(2025, 3, 8));

        range.Should().HaveCount(1);
        range[0].MealType.Should().Be(MealType.Lunch);
    }

    [Fact]
    public async Task GetByDate_ReturnsSingleDayMeals()
    {
        var userId = Guid.NewGuid();
        var target = new DateTime(2025, 7, 4, 8, 0, 0, DateTimeKind.Utc);
        var other = new DateTime(2025, 7, 5, 8, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = target, MealType = MealType.Breakfast });
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = target.AddHours(6), MealType = MealType.Lunch });
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = other, MealType = MealType.Dinner });

        var meals = await fx.Store.GetMealLogsByDateAsync(userId, new DateOnly(2025, 7, 4));
        meals.Should().HaveCount(2);
    }

    [Fact]
    public async Task SoftDeleted_ExcludedFromDateRange()
    {
        var userId = Guid.NewGuid();
        var dt = new DateTime(2025, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = dt, IsDeleted = true });
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = dt, IsDeleted = false });

        var meals = await fx.Store.GetMealLogsByDateAsync(userId, new DateOnly(2025, 8, 1));
        meals.Should().HaveCount(1);
    }

    [Fact]
    public async Task UserIsolation_OnlyReturnsOwnMeals()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var dt = new DateTime(2025, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = user1, LoggedAt = dt });
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = Guid.NewGuid(), UserId = user2, LoggedAt = dt });

        var meals = await fx.Store.GetMealLogsByDateAsync(user1, new DateOnly(2025, 9, 1));
        meals.Should().HaveCount(1);
        meals[0].UserId.Should().Be(user1);
    }

    [Fact]
    public async Task GetMealLog_IncludesMealItems()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = DateTime.UtcNow });

        var items = new List<MealItem>
        {
            new() { Id = Guid.NewGuid(), MealLogId = mealId, FoodName = "Chicken", Calories = 250, ProteinG = 30 },
            new() { Id = Guid.NewGuid(), MealLogId = mealId, FoodName = "Rice", Calories = 200, CarbsG = 45 }
        };
        await fx.Store.UpsertMealItemsAsync(userId, mealId, items);

        var loaded = await fx.Store.GetMealLogAsync(userId, mealId);
        loaded!.Items.Should().HaveCount(2);
        loaded.Items.Select(i => i.FoodName).Should().BeEquivalentTo(["Chicken", "Rice"]);
    }
}

[Collection("Azurite")]
public class MealItemStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTripsAllNutritionFields()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var items = new List<MealItem>
        {
            new()
            {
                Id = itemId,
                MealLogId = mealId,
                FoodProductId = Guid.NewGuid(),
                FoodName = "Salmon Fillet",
                Barcode = "1234567890",
                Servings = 1.5m,
                ServingUnit = "piece",
                ServingWeightG = 150m,
                Calories = 350,
                ProteinG = 40,
                CarbsG = 0,
                FatG = 20,
                FiberG = 0,
                SugarG = 0,
                SodiumMg = 50,
                CholesterolMg = 85,
                SaturatedFatG = 4,
                PotassiumMg = 500
            }
        };

        await fx.Store.UpsertMealItemsAsync(userId, mealId, items);
        var loaded = await fx.Store.GetMealItemsAsync(userId, mealId);

        loaded.Should().HaveCount(1);
        var item = loaded[0];
        item.FoodName.Should().Be("Salmon Fillet");
        item.Barcode.Should().Be("1234567890");
        item.Servings.Should().Be(1.5m);
        item.ServingUnit.Should().Be("piece");
        item.ServingWeightG.Should().Be(150m);
        item.Calories.Should().Be(350);
        item.ProteinG.Should().Be(40);
        item.FatG.Should().Be(20);
        item.SodiumMg.Should().Be(50);
        item.CholesterolMg.Should().Be(85);
        item.SaturatedFatG.Should().Be(4);
        item.PotassiumMg.Should().Be(500);
    }

    [Fact]
    public async Task Upsert_ReplacesExistingItems()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();

        await fx.Store.UpsertMealItemsAsync(userId, mealId, [
            new() { Id = Guid.NewGuid(), FoodName = "Old Item 1" },
            new() { Id = Guid.NewGuid(), FoodName = "Old Item 2" }
        ]);

        await fx.Store.UpsertMealItemsAsync(userId, mealId, [
            new() { Id = Guid.NewGuid(), FoodName = "New Item" }
        ]);

        var loaded = await fx.Store.GetMealItemsAsync(userId, mealId);
        loaded.Should().HaveCount(1);
        loaded[0].FoodName.Should().Be("New Item");
    }

    [Fact]
    public async Task Delete_RemovesAllItems()
    {
        var userId = Guid.NewGuid();
        var mealId = Guid.NewGuid();

        await fx.Store.UpsertMealItemsAsync(userId, mealId, [
            new() { Id = Guid.NewGuid(), FoodName = "A" },
            new() { Id = Guid.NewGuid(), FoodName = "B" }
        ]);

        await fx.Store.DeleteMealItemsAsync(userId, mealId);
        var loaded = await fx.Store.GetMealItemsAsync(userId, mealId);
        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task ItemsBelongToCorrectMeal()
    {
        var userId = Guid.NewGuid();
        var meal1 = Guid.NewGuid();
        var meal2 = Guid.NewGuid();

        await fx.Store.UpsertMealItemsAsync(userId, meal1, [new() { Id = Guid.NewGuid(), FoodName = "From Meal 1" }]);
        await fx.Store.UpsertMealItemsAsync(userId, meal2, [new() { Id = Guid.NewGuid(), FoodName = "From Meal 2" }]);

        var items1 = await fx.Store.GetMealItemsAsync(userId, meal1);
        var items2 = await fx.Store.GetMealItemsAsync(userId, meal2);

        items1.Should().HaveCount(1);
        items1[0].FoodName.Should().Be("From Meal 1");
        items2.Should().HaveCount(1);
        items2[0].FoodName.Should().Be("From Meal 2");
    }
}

[Collection("Azurite")]
public class SymptomStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTripsAllFields()
    {
        var userId = Guid.NewGuid();
        var symptomId = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var occurredAt = new DateTime(2025, 6, 20, 14, 30, 0, DateTimeKind.Utc);

        var symptom = new SymptomLog
        {
            Id = symptomId,
            UserId = userId,
            SymptomTypeId = 3,
            Severity = 7,
            OccurredAt = occurredAt,
            RelatedMealLogId = mealId,
            Notes = "After eating pizza",
            Duration = TimeSpan.FromMinutes(90)
        };

        await fx.Store.UpsertSymptomLogAsync(symptom);
        var loaded = await fx.Store.GetSymptomLogAsync(userId, symptomId);

        loaded.Should().NotBeNull();
        loaded!.SymptomTypeId.Should().Be(3);
        loaded.Severity.Should().Be(7);
        loaded.OccurredAt.Should().Be(occurredAt);
        loaded.RelatedMealLogId.Should().Be(mealId);
        loaded.Notes.Should().Be("After eating pizza");
        loaded.Duration.Should().Be(TimeSpan.FromMinutes(90));
        loaded.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetByDateRange_FiltersCorrectly()
    {
        var userId = Guid.NewGuid();
        var inside = new DateTime(2025, 4, 15, 10, 0, 0, DateTimeKind.Utc);
        var outside = new DateTime(2025, 4, 25, 10, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = Guid.NewGuid(), UserId = userId, SymptomTypeId = 1, Severity = 5, OccurredAt = inside });
        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = Guid.NewGuid(), UserId = userId, SymptomTypeId = 1, Severity = 3, OccurredAt = outside });

        var results = await fx.Store.GetSymptomLogsByDateRangeAsync(userId, new DateOnly(2025, 4, 10), new DateOnly(2025, 4, 20));
        results.Should().HaveCount(1);
        results[0].Severity.Should().Be(5);
    }

    [Fact]
    public async Task SoftDeleted_ExcludedFromRange()
    {
        var userId = Guid.NewGuid();
        var dt = new DateTime(2025, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = Guid.NewGuid(), UserId = userId, SymptomTypeId = 1, Severity = 5, OccurredAt = dt, IsDeleted = true });
        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = Guid.NewGuid(), UserId = userId, SymptomTypeId = 1, Severity = 3, OccurredAt = dt });

        var results = await fx.Store.GetSymptomLogsByDateAsync(userId, new DateOnly(2025, 5, 1));
        results.Should().HaveCount(1);
        results[0].Severity.Should().Be(3);
    }

    [Fact]
    public async Task NullDuration_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();

        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = id, UserId = userId, SymptomTypeId = 1, Severity = 2, OccurredAt = DateTime.UtcNow, Duration = null });

        var loaded = await fx.Store.GetSymptomLogAsync(userId, id);
        loaded!.Duration.Should().BeNull();
    }

    [Fact]
    public async Task NullRelatedMealLogId_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();

        await fx.Store.UpsertSymptomLogAsync(new SymptomLog { Id = id, UserId = userId, SymptomTypeId = 1, Severity = 4, OccurredAt = DateTime.UtcNow, RelatedMealLogId = null });

        var loaded = await fx.Store.GetSymptomLogAsync(userId, id);
        loaded!.RelatedMealLogId.Should().BeNull();
    }
}

[Collection("Azurite")]
public class SymptomTypeStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTrips()
    {
        var id = Random.Shared.Next(10000, 99999);
        var type = new SymptomType { Id = id, Name = "Bloating", Category = "GI", Icon = "🫧" };

        await fx.Store.UpsertSymptomTypeAsync(type);
        var loaded = await fx.Store.GetSymptomTypeAsync(id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Bloating");
        loaded.Category.Should().Be("GI");
        loaded.Icon.Should().Be("🫧");
    }

    [Fact]
    public async Task GetAll_ReturnsMultiple()
    {
        var id1 = Random.Shared.Next(100000, 199999);
        var id2 = Random.Shared.Next(200000, 299999);

        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = id1, Name = "Nausea", Category = "GI" });
        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = id2, Name = "Headache", Category = "Neuro" });

        var all = await fx.Store.GetAllSymptomTypesAsync();
        all.Should().Contain(t => t.Id == id1 && t.Name == "Nausea");
        all.Should().Contain(t => t.Id == id2 && t.Name == "Headache");
    }

    [Fact]
    public async Task Exists_TrueWhenPresent()
    {
        var id = Random.Shared.Next(300000, 399999);
        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = id, Name = "Test", Category = "Test" });

        (await fx.Store.SymptomTypeExistsAsync(id)).Should().BeTrue();
    }

    [Fact]
    public async Task Exists_FalseWhenAbsent()
    {
        (await fx.Store.SymptomTypeExistsAsync(999999)).Should().BeFalse();
    }
}

[Collection("Azurite")]
public class FoodProductStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTripsAllFields()
    {
        var id = Guid.NewGuid();
        var product = new FoodProduct
        {
            Id = id,
            Barcode = $"BC-{id.ToString()[..8]}",
            Name = "Organic Oat Milk",
            Brand = "Oatly",
            Ingredients = "water, oats, rapeseed oil",
            ImageUrl = "https://img.example.com/oatmilk.jpg",
            NovaGroup = 2,
            NutriScore = "A",
            AllergensTags = ["en:gluten"],
            Calories100g = 48m,
            Protein100g = 1.0m,
            Carbs100g = 6.5m,
            Fat100g = 1.5m,
            Fiber100g = 0.8m,
            Sugar100g = 4.0m,
            Sodium100g = 0.04m,
            ServingSize = "250ml",
            ServingQuantity = 250m,
            DataSource = "OpenFoodFacts",
            ExternalId = "ext-123",
            CacheTtlHours = 48,
            SafetyScore = 85,
            SafetyRating = SafetyRating.Safe
        };

        await fx.Store.UpsertFoodProductAsync(product);
        var loaded = await fx.Store.GetFoodProductAsync(id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Organic Oat Milk");
        loaded.Brand.Should().Be("Oatly");
        loaded.Ingredients.Should().Be("water, oats, rapeseed oil");
        loaded.NovaGroup.Should().Be(2);
        loaded.NutriScore.Should().Be("A");
        loaded.AllergensTags.Should().BeEquivalentTo(["en:gluten"]);
        loaded.Calories100g.Should().Be(48m);
        loaded.Protein100g.Should().Be(1.0m);
        loaded.Fiber100g.Should().Be(0.8m);
        loaded.Sugar100g.Should().Be(4.0m);
        loaded.ServingSize.Should().Be("250ml");
        loaded.ServingQuantity.Should().Be(250m);
        loaded.DataSource.Should().Be("OpenFoodFacts");
        loaded.SafetyScore.Should().Be(85);
        loaded.SafetyRating.Should().Be(SafetyRating.Safe);
    }

    [Fact]
    public async Task GetByBarcode_FindsViaLookup()
    {
        var id = Guid.NewGuid();
        var barcode = $"4006381-{id.ToString()[..6]}";
        await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = id, Name = "Haribo", Barcode = barcode });

        var loaded = await fx.Store.GetFoodProductByBarcodeAsync(barcode);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(id);
        loaded.Name.Should().Be("Haribo");
    }

    [Fact]
    public async Task GetByBarcode_NonExistent_ReturnsNull()
    {
        var result = await fx.Store.GetFoodProductByBarcodeAsync("0000000000000");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Search_MatchesByName()
    {
        var id = Guid.NewGuid();
        var unique = $"UniqueName-{id.ToString()[..8]}";
        await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = id, Name = unique });

        var results = await fx.Store.SearchFoodProductsAsync(unique, 10);
        results.Should().Contain(p => p.Id == id);
    }

    [Fact]
    public async Task Search_MatchesByBrand()
    {
        var id = Guid.NewGuid();
        var brand = $"BrandSearch-{id.ToString()[..8]}";
        await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = id, Name = "Generic Food", Brand = brand });

        var results = await fx.Store.SearchFoodProductsAsync(brand, 10);
        results.Should().Contain(p => p.Id == id);
    }

    [Fact]
    public async Task Search_RespectsMaxResults()
    {
        var tag = Guid.NewGuid().ToString()[..8];
        for (var i = 0; i < 5; i++)
            await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = Guid.NewGuid(), Name = $"Limit-{tag}-{i}" });

        var results = await fx.Store.SearchFoodProductsAsync($"Limit-{tag}", 3);
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task NullableFields_RoundTripAsNull()
    {
        var id = Guid.NewGuid();
        await fx.Store.UpsertFoodProductAsync(new FoodProduct
        {
            Id = id,
            Name = "Minimal",
            NovaGroup = null,
            NutriScore = null,
            SafetyScore = null,
            SafetyRating = null,
            Fiber100g = null
        });

        var loaded = await fx.Store.GetFoodProductAsync(id);
        loaded!.NovaGroup.Should().BeNull();
        loaded.NutriScore.Should().BeNull();
        loaded.SafetyScore.Should().BeNull();
        loaded.SafetyRating.Should().BeNull();
        loaded.Fiber100g.Should().BeNull();
    }
}

[Collection("Azurite")]
public class FoodAdditiveStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTrips()
    {
        var id = Random.Shared.Next(400000, 499999);
        var additive = new FoodAdditive
        {
            Id = id,
            ENumber = "E420",
            Name = "Sorbitol",
            AlternateNames = ["Glucitol"],
            Category = "Sweetener",
            CspiRating = CspiRating.CutBack,
            UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
            EuRegulatoryStatus = EuRegulatoryStatus.Approved,
            EfsaAdiMgPerKgBw = 150m,
            HealthConcerns = "May cause digestive issues",
            Description = "A sugar alcohol",
            FdaAdverseEventCount = 12,
            FdaRecallCount = 0,
            BannedInCountries = ["JP"]
        };

        await fx.Store.UpsertFoodAdditiveAsync(additive);
        var loaded = await fx.Store.GetFoodAdditiveAsync(id);

        loaded.Should().NotBeNull();
        loaded!.ENumber.Should().Be("E420");
        loaded.Name.Should().Be("Sorbitol");
        loaded.AlternateNames.Should().BeEquivalentTo(["Glucitol"]);
        loaded.Category.Should().Be("Sweetener");
        loaded.CspiRating.Should().Be(CspiRating.CutBack);
        loaded.UsRegulatoryStatus.Should().Be(UsRegulatoryStatus.GRAS);
        loaded.EuRegulatoryStatus.Should().Be(EuRegulatoryStatus.Approved);
        loaded.EfsaAdiMgPerKgBw.Should().Be(150m);
        loaded.HealthConcerns.Should().Be("May cause digestive issues");
        loaded.FdaAdverseEventCount.Should().Be(12);
        loaded.FdaRecallCount.Should().Be(0);
        loaded.BannedInCountries.Should().BeEquivalentTo(["JP"]);
    }

    [Fact]
    public async Task GetAll_ReturnsUpsertedAdditives()
    {
        var id1 = Random.Shared.Next(500000, 599999);
        var id2 = Random.Shared.Next(600000, 699999);

        await fx.Store.UpsertFoodAdditiveAsync(new FoodAdditive { Id = id1, Name = "Add-A", Category = "Color" });
        await fx.Store.UpsertFoodAdditiveAsync(new FoodAdditive { Id = id2, Name = "Add-B", Category = "Preservative" });

        var all = await fx.Store.GetAllFoodAdditivesAsync();
        all.Should().Contain(a => a.Id == id1);
        all.Should().Contain(a => a.Id == id2);
    }
}

[Collection("Azurite")]
public class FoodProductAdditiveStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task SetAndGet_RoundTrips()
    {
        var productId = Guid.NewGuid();
        await fx.Store.SetAdditiveIdsForProductAsync(productId, [10, 20, 30]);

        var ids = await fx.Store.GetAdditiveIdsForProductAsync(productId);
        ids.Should().BeEquivalentTo([10, 20, 30]);
    }

    [Fact]
    public async Task Set_ReplacesExisting()
    {
        var productId = Guid.NewGuid();
        await fx.Store.SetAdditiveIdsForProductAsync(productId, [1, 2, 3]);
        await fx.Store.SetAdditiveIdsForProductAsync(productId, [4, 5]);

        var ids = await fx.Store.GetAdditiveIdsForProductAsync(productId);
        ids.Should().BeEquivalentTo([4, 5]);
    }

    [Fact]
    public async Task Get_EmptyWhenNoneSet()
    {
        var ids = await fx.Store.GetAdditiveIdsForProductAsync(Guid.NewGuid());
        ids.Should().BeEmpty();
    }
}

[Collection("Azurite")]
public class RefreshTokenStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGetByValue_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var tokenValue = $"rt-{Guid.NewGuid()}";
        var expiresAt = DateTime.UtcNow.AddDays(7);

        var token = new RefreshToken
        {
            Id = tokenId,
            UserId = userId,
            Token = tokenValue,
            ExpiresAt = expiresAt
        };

        await fx.Store.UpsertRefreshTokenAsync(token);
        var loaded = await fx.Store.GetRefreshTokenByValueAsync(tokenValue);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(tokenId);
        loaded.UserId.Should().Be(userId);
        loaded.Token.Should().Be(tokenValue);
        loaded.RevokedAt.Should().BeNull();
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveTokens_ExcludesRevokedAndExpired()
    {
        var userId = Guid.NewGuid();

        var active = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = $"active-{Guid.NewGuid()}", ExpiresAt = DateTime.UtcNow.AddDays(7) };
        var revoked = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = $"revoked-{Guid.NewGuid()}", ExpiresAt = DateTime.UtcNow.AddDays(7), RevokedAt = DateTime.UtcNow };
        var expired = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = $"expired-{Guid.NewGuid()}", ExpiresAt = DateTime.UtcNow.AddDays(-1) };

        await fx.Store.UpsertRefreshTokenAsync(active);
        await fx.Store.UpsertRefreshTokenAsync(revoked);
        await fx.Store.UpsertRefreshTokenAsync(expired);

        var activeTokens = await fx.Store.GetActiveRefreshTokensAsync(userId);
        activeTokens.Should().HaveCount(1);
        activeTokens[0].Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task DeleteForUser_RemovesAllTokensAndLookups()
    {
        var userId = Guid.NewGuid();
        var token1Value = $"del1-{Guid.NewGuid()}";
        var token2Value = $"del2-{Guid.NewGuid()}";

        await fx.Store.UpsertRefreshTokenAsync(new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = token1Value, ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await fx.Store.UpsertRefreshTokenAsync(new RefreshToken { Id = Guid.NewGuid(), UserId = userId, Token = token2Value, ExpiresAt = DateTime.UtcNow.AddDays(7) });

        await fx.Store.DeleteRefreshTokensForUserAsync(userId);

        (await fx.Store.GetActiveRefreshTokensAsync(userId)).Should().BeEmpty();
        (await fx.Store.GetRefreshTokenByValueAsync(token1Value)).Should().BeNull();
        (await fx.Store.GetRefreshTokenByValueAsync(token2Value)).Should().BeNull();
    }

    [Fact]
    public async Task GetByValue_NonExistent_ReturnsNull()
    {
        var result = await fx.Store.GetRefreshTokenByValueAsync("nonexistent-token-value");
        result.Should().BeNull();
    }
}

[Collection("Azurite")]
public class DailyNutritionSummaryStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var date = new DateOnly(2025, 6, 15);
        var summary = new DailyNutritionSummary
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = date,
            TotalCalories = 2100,
            TotalProteinG = 90,
            TotalCarbsG = 250,
            TotalFatG = 70,
            TotalFiberG = 28,
            TotalSugarG = 45,
            TotalSodiumMg = 1800,
            MealCount = 3,
            CalorieGoal = 2000
        };

        await fx.Store.UpsertDailyNutritionSummaryAsync(summary);
        var loaded = await fx.Store.GetDailyNutritionSummaryAsync(userId, date);

        loaded.Should().NotBeNull();
        loaded!.Date.Should().Be(date);
        loaded.TotalCalories.Should().Be(2100);
        loaded.TotalProteinG.Should().Be(90);
        loaded.TotalCarbsG.Should().Be(250);
        loaded.TotalFatG.Should().Be(70);
        loaded.TotalFiberG.Should().Be(28);
        loaded.TotalSugarG.Should().Be(45);
        loaded.TotalSodiumMg.Should().Be(1800);
        loaded.MealCount.Should().Be(3);
        loaded.CalorieGoal.Should().Be(2000);
    }

    [Fact]
    public async Task Get_WrongDate_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await fx.Store.UpsertDailyNutritionSummaryAsync(new DailyNutritionSummary
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = new DateOnly(2025, 1, 1)
        });

        var result = await fx.Store.GetDailyNutritionSummaryAsync(userId, new DateOnly(2025, 1, 2));
        result.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_SameDate_Overwrites()
    {
        var userId = Guid.NewGuid();
        var date = new DateOnly(2025, 12, 25);

        await fx.Store.UpsertDailyNutritionSummaryAsync(new DailyNutritionSummary { Id = Guid.NewGuid(), UserId = userId, Date = date, TotalCalories = 1000 });
        await fx.Store.UpsertDailyNutritionSummaryAsync(new DailyNutritionSummary { Id = Guid.NewGuid(), UserId = userId, Date = date, TotalCalories = 3000 });

        var loaded = await fx.Store.GetDailyNutritionSummaryAsync(userId, date);
        loaded!.TotalCalories.Should().Be(3000);
    }
}

[Collection("Azurite")]
public class UserFoodAlertStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var alert = new UserFoodAlert
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FoodAdditiveId = 420,
            AlertEnabled = true
        };

        await fx.Store.UpsertUserFoodAlertAsync(alert);
        var loaded = await fx.Store.GetUserFoodAlertAsync(userId, 420);

        loaded.Should().NotBeNull();
        loaded!.FoodAdditiveId.Should().Be(420);
        loaded.AlertEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetAll_ReturnsMultiple()
    {
        var userId = Guid.NewGuid();
        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = userId, FoodAdditiveId = 100 });
        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = userId, FoodAdditiveId = 200 });

        var all = await fx.Store.GetUserFoodAlertsAsync(userId);
        all.Should().HaveCount(2);
        all.Select(a => a.FoodAdditiveId).Should().BeEquivalentTo([100, 200]);
    }

    [Fact]
    public async Task Delete_RemovesSpecificAlert()
    {
        var userId = Guid.NewGuid();
        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = userId, FoodAdditiveId = 301 });
        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = userId, FoodAdditiveId = 302 });

        await fx.Store.DeleteUserFoodAlertAsync(userId, 301);

        var remaining = await fx.Store.GetUserFoodAlertsAsync(userId);
        remaining.Should().HaveCount(1);
        remaining[0].FoodAdditiveId.Should().Be(302);
    }

    [Fact]
    public async Task UserIsolation_AlertsBelongToUser()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = user1, FoodAdditiveId = 50 });
        await fx.Store.UpsertUserFoodAlertAsync(new UserFoodAlert { Id = Guid.NewGuid(), UserId = user2, FoodAdditiveId = 51 });

        var alerts1 = await fx.Store.GetUserFoodAlertsAsync(user1);
        alerts1.Should().AllSatisfy(a => a.UserId.Should().Be(user1));
    }
}

[Collection("Azurite")]
public class InsightReportStoreTests(AzuriteFixture fx)
{
    [Fact]
    public async Task UpsertAndGet_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var generatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var report = new InsightReport
        {
            Id = reportId,
            UserId = userId,
            GeneratedAt = generatedAt,
            PeriodStart = new DateOnly(2025, 5, 1),
            PeriodEnd = new DateOnly(2025, 5, 31),
            ReportType = ReportType.Monthly,
            CorrelationsJson = "[{\"food\":\"pizza\",\"symptom\":\"bloating\"}]",
            SummaryText = "You ate a lot of pizza.",
            AdditiveExposureJson = "{\"E420\":3}",
            TopTriggersJson = "[\"pizza\"]"
        };

        await fx.Store.UpsertInsightReportAsync(report);
        var loaded = await fx.Store.GetInsightReportAsync(userId, reportId);

        loaded.Should().NotBeNull();
        loaded!.GeneratedAt.Should().Be(generatedAt);
        loaded.PeriodStart.Should().Be(new DateOnly(2025, 5, 1));
        loaded.PeriodEnd.Should().Be(new DateOnly(2025, 5, 31));
        loaded.ReportType.Should().Be(ReportType.Monthly);
        loaded.CorrelationsJson.Should().Contain("pizza");
        loaded.SummaryText.Should().Be("You ate a lot of pizza.");
        loaded.AdditiveExposureJson.Should().Contain("E420");
        loaded.TopTriggersJson.Should().Contain("pizza");
    }

    [Fact]
    public async Task GetAll_OrderedByGeneratedAtDescending()
    {
        var userId = Guid.NewGuid();
        var older = new InsightReport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GeneratedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 1, 7),
            SummaryText = "Older"
        };
        var newer = new InsightReport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GeneratedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodStart = new DateOnly(2025, 6, 1),
            PeriodEnd = new DateOnly(2025, 6, 7),
            SummaryText = "Newer"
        };

        await fx.Store.UpsertInsightReportAsync(older);
        await fx.Store.UpsertInsightReportAsync(newer);

        var reports = await fx.Store.GetInsightReportsAsync(userId);
        reports.Should().HaveCountGreaterOrEqualTo(2);
        reports[0].SummaryText.Should().Be("Newer");
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var result = await fx.Store.GetInsightReportAsync(Guid.NewGuid(), Guid.NewGuid());
        result.Should().BeNull();
    }
}
