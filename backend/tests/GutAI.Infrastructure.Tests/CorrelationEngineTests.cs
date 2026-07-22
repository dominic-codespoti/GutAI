using FluentAssertions;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class CorrelationEngineTests
{
    private static CorrelationEngine MakeSut(GutAI.Application.Common.Interfaces.ITableStore store)
        => new(store);

    // ─── No data ────────────────────────────────────────────────────────

    [Fact]
    public async Task NoData_ReturnsEmpty()
    {
        var store = MockTableStoreFactory.Create().Object;
        var sut = MakeSut(store);

        var result = await sut.ComputeCorrelationsAsync(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), DateOnly.FromDateTime(DateTime.UtcNow));

        result.Should().BeEmpty();
    }

    // ─── Onset window boundaries (1-6h, shared with FoodDiaryAnalysisService) ──

    [Fact]
    public async Task SymptomWithinWindow_CountsTowardCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().Contain(c => c.FoodOrAdditive == "Garlic Bread" && c.SymptomName == "Bloating");
    }

    [Fact]
    public async Task SymptomBeforeMinOnset_ExcludedFromCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            // 30 minutes — below the 1h minimum onset.
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddMinutes(30), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().NotContain(c => c.FoodOrAdditive == "Garlic Bread");
    }

    [Fact]
    public async Task SymptomAfterMaxOnset_ExcludedFromCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            // 7 hours — beyond the 6h maximum onset shared with FoodDiaryAnalysisService.
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(7), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().NotContain(c => c.FoodOrAdditive == "Garlic Bread");
    }

    [Fact]
    public async Task SymptomExactlyAtMaxOnsetBoundary_StillCounted()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(6), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().Contain(c => c.FoodOrAdditive == "Garlic Bread");
    }

    // ─── Occurrence threshold (>= 3 meals required to surface at all) ────

    [Fact]
    public async Task FewerThanThreeOccurrences_NotSurfaced()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 2; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Sushi"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Nausea", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().NotContain(c => c.FoodOrAdditive == "Sushi");
    }

    // ─── Confidence tiers ─────────────────────────────────────────────────

    [Theory]
    [InlineData(15, "High")]
    [InlineData(5, "Medium")]
    [InlineData(3, "Low")]
    public async Task ConfidenceTier_MatchesOccurrenceCount(int occurrenceCount, string expectedConfidence)
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < occurrenceCount; i++)
        {
            var mealTime = now.AddDays(-(occurrenceCount * 2) + i * 2);
            meals.AddRange(MakeMeal(userId, mealTime, "Dairy"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Cramps", 5));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-(occurrenceCount * 2 + 5))), DateOnly.FromDateTime(now));

        var correlation = result.First(c => c.FoodOrAdditive == "Dairy");
        correlation.Confidence.Should().Be(expectedConfidence);
    }

    // ─── Normalized-name grouping (case/plural fragmentation fix) ────────

    [Fact]
    public async Task DifferentCasingAndPlurals_GroupedAsSameFood()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        var variants = new[] { "Chicken Wing", "chicken wings", "CHICKEN WING" };
        for (int i = 0; i < variants.Length; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, variants[i]));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        // All three casing/plural variants must merge into a single correlation bucket
        // with 3 occurrences (crossing the >=3 threshold) rather than three separate
        // buckets of 1 occurrence each that would never surface.
        result.Should().ContainSingle(c => c.SymptomName == "Bloating");
        result.Single(c => c.SymptomName == "Bloating").Occurrences.Should().Be(3);
    }

    // ─── Additive correlation ──────────────────────────────────────────

    [Fact]
    public async Task AdditiveInProduct_CorrelatesWithSymptom()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var productId = Guid.NewGuid();

        var product = new FoodProduct { Id = productId, Name = "Diet Soda" };
        var additive = new FoodAdditive { Id = 1, Name = "Aspartame", Category = "Sweetener" };

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Diet Soda", productId));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(2), "Headache", 5));
        }

        var store = MockTableStoreFactory.Create(
            meals: meals,
            symptoms: symptoms,
            foodProducts: [product],
            additives: [additive],
            additiveIdsByProduct: new Dictionary<Guid, List<int>> { [productId] = [1] }
        ).Object;

        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().Contain(c => c.FoodOrAdditive == "[additive] Aspartame" && c.SymptomName == "Headache");
    }

    // ─── User isolation ────────────────────────────────────────────────

    [Fact]
    public async Task OnlyReturnsCorrelationsForSpecifiedUser()
    {
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(otherUser, mealTime, "Peanuts"));
            symptoms.AddRange(MakeSymptom(otherUser, mealTime.AddHours(3), "Hives", 8));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().BeEmpty();
    }

    // ─── Ranking / cap ──────────────────────────────────────────────────

    [Fact]
    public async Task ResultsOrderedByOccurrenceDescending_AndCappedAtTwenty()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();

        for (int food = 0; food < 25; food++)
        {
            var occurrences = 3 + food; // ensures each food crosses the surfacing threshold with a distinct count
            for (int i = 0; i < occurrences; i++)
            {
                var mealTime = now.AddDays(-200 + (food * occurrences + i));
                meals.AddRange(MakeMeal(userId, mealTime, $"Food{food}"));
                symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(2), "Bloating", 5));
            }
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-400)), DateOnly.FromDateTime(now));

        result.Should().HaveCountLessOrEqualTo(20);
        result.Should().BeInDescendingOrder(c => c.Occurrences);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static List<MealLog> MakeMeal(Guid userId, DateTime loggedAt, string foodName, Guid? foodProductId = null)
    {
        var mealId = Guid.NewGuid();
        return
        [
            new MealLog
            {
                Id = mealId,
                UserId = userId,
                LoggedAt = loggedAt,
                MealType = MealType.Lunch,
                Items =
                [
                    new MealItem
                    {
                        Id = Guid.NewGuid(),
                        MealLogId = mealId,
                        FoodName = foodName,
                        FoodProductId = foodProductId,
                    }
                ],
            }
        ];
    }

    private static List<SymptomLog> MakeSymptom(Guid userId, DateTime occurredAt, string symptomName, int severity)
    {
        return
        [
            new SymptomLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SymptomTypeId = 1,
                Severity = severity,
                OccurredAt = occurredAt,
                SymptomType = new SymptomType { Id = 1, Name = symptomName, Category = "GI" },
            }
        ];
    }
}
