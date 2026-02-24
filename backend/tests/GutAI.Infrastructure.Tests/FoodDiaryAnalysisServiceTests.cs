using FluentAssertions;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodDiaryAnalysisServiceTests
{
    private readonly FoodDiaryAnalysisService _sut = new();

    // ─── AnalyzeAsync — empty data ─────────────────────────────────────

    [Fact]
    public async Task Analyze_NoData_ReturnsEmptyAnalysis()
    {
        var userId = Guid.NewGuid();
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var store = MockTableStoreFactory.Create().Object;

        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.TotalMealsAnalyzed.Should().Be(0);
        result.TotalSymptomsAnalyzed.Should().Be(0);
        result.PatternsFound.Should().Be(0);
        result.Patterns.Should().BeEmpty();
        result.Summary.Should().Contain("No meals or symptoms");
    }

    [Fact]
    public async Task Analyze_MealsNoSymptoms_ReturnsNoPatterns()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);
        var meals = MakeMeals(userId, now.AddDays(-3), "Chicken", "Rice");
        var store = MockTableStoreFactory.Create(meals: meals).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.TotalMealsAnalyzed.Should().Be(1);
        result.TotalSymptomsAnalyzed.Should().Be(0);
        result.PatternsFound.Should().Be(0);
        result.Summary.Should().Contain("No symptoms were reported");
    }

    // ─── AnalyzeAsync — correlations ───────────────────────────────────

    [Fact]
    public async Task Analyze_MealThenSymptom_FindsCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-3), "Pizza");
        var symptoms = MakeSymptoms(userId, now.AddDays(-3).AddHours(3), "Bloating", 7);
        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.PatternsFound.Should().BeGreaterThan(0);
        result.Patterns.Should().Contain(p => p.FoodName == "Pizza" && p.SymptomName == "Bloating");
    }

    [Fact]
    public async Task Analyze_SymptomTooLate_NoCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-3), "Pizza");
        var symptoms = MakeSymptoms(userId, now.AddDays(-3).AddHours(10), "Bloating", 7);
        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.Patterns.Should().NotContain(p => p.FoodName == "Pizza");
    }

    [Fact]
    public async Task Analyze_SymptomTooEarly_NoCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-3), "Pizza");
        var symptoms = MakeSymptoms(userId, now.AddDays(-3).AddMinutes(30), "Bloating", 7);
        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.Patterns.Should().NotContain(p => p.FoodName == "Pizza");
    }

    // ─── Confidence levels ─────────────────────────────────────────────

    [Fact]
    public async Task Analyze_HighConfidence_When5PlusOccurrencesAndHighSeverity()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-30));
        var to = DateOnly.FromDateTime(now);

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 6; i++)
        {
            var mealTime = now.AddDays(-25 + i * 3);
            meals.AddRange(MakeMeals(userId, mealTime, "Garlic Bread"));
            symptoms.AddRange(MakeSymptoms(userId, mealTime.AddHours(3), "Bloating", 7));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        var pattern = result.Patterns.First(p => p.FoodName == "Garlic Bread");
        pattern.Confidence.Should().Be("High");
        pattern.Occurrences.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task Analyze_LowConfidence_WhenFewOccurrences()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-3), "Sushi");
        var symptoms = MakeSymptoms(userId, now.AddDays(-3).AddHours(4), "Nausea", 3);

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        var pattern = result.Patterns.First(p => p.FoodName == "Sushi");
        pattern.Confidence.Should().Be("Low");
    }

    // ─── Timing insights ───────────────────────────────────────────────

    [Fact]
    public async Task Analyze_ProducesTimingInsights()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-14));
        var to = DateOnly.FromDateTime(now);

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 2);
            meals.AddRange(MakeMeals(userId, mealTime, "Pasta"));
            symptoms.AddRange(MakeSymptoms(userId, mealTime.AddHours(3), "Cramps", 5));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.TimingInsights.Should().NotBeEmpty();
        result.TimingInsights.Should().Contain(t => t.Category == "Peak symptom onset");
    }

    [Fact]
    public async Task Analyze_LongestSymptomFreeStreak()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-30));
        var to = DateOnly.FromDateTime(now);

        var symptoms = new List<SymptomLog>();
        symptoms.AddRange(MakeSymptoms(userId, now.AddDays(-25), "Bloating", 5));
        symptoms.AddRange(MakeSymptoms(userId, now.AddDays(-20), "Bloating", 5));
        symptoms.AddRange(MakeSymptoms(userId, now.AddDays(-5), "Bloating", 5));

        var store = MockTableStoreFactory.Create(symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        var streakInsight = result.TimingInsights.FirstOrDefault(t => t.Category == "Symptom-free streak");
        streakInsight.Should().NotBeNull();
        streakInsight!.Insight.Should().Contain("day(s)");
    }

    // ─── Recommendations ───────────────────────────────────────────────

    [Fact]
    public async Task Analyze_NoPatterns_RecommendsContinueLogging()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-3), "Rice");
        var symptoms = MakeSymptoms(userId, now.AddDays(-1), "Bloating", 2);
        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.Recommendations.Should().Contain(r => r.Contains("No clear food-symptom patterns"));
    }

    // ─── Multiple foods correlated ─────────────────────────────────────

    [Fact]
    public async Task Analyze_MultipleFoodsInMeal_AllCorrelated()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-3), "Pizza", "Garlic Bread", "Cola");
        var symptoms = MakeSymptoms(userId, now.AddDays(-3).AddHours(4), "Bloating", 6);

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.Patterns.Should().Contain(p => p.FoodName == "Pizza");
        result.Patterns.Should().Contain(p => p.FoodName == "Garlic Bread");
        result.Patterns.Should().Contain(p => p.FoodName == "Cola");
    }

    // ─── Date filtering ────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_FiltersToDateRange()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var insideMeals = MakeMeals(userId, now.AddDays(-3), "Pizza");
        var outsideMeals = MakeMeals(userId, now.AddDays(-30), "Sushi");
        var allMeals = insideMeals.Concat(outsideMeals).ToList();

        var symptoms = MakeSymptoms(userId, now.AddDays(-3).AddHours(3), "Bloating", 6);
        var store = MockTableStoreFactory.Create(meals: allMeals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.TotalMealsAnalyzed.Should().Be(1);
    }

    // ─── GetEliminationStatusAsync ─────────────────────────────────────

    [Fact]
    public async Task Elimination_NoSymptoms_NotStarted()
    {
        var userId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.GetEliminationStatusAsync(userId, store);

        result.Phase.Should().Be("Not Started");
        result.Summary.Should().Contain("No symptoms have been logged");
    }

    [Fact]
    public async Task Elimination_WithTriggers_Assessment()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 6; i++)
        {
            var mealTime = now.AddDays(-60 + i * 5);
            meals.AddRange(MakeMeals(userId, mealTime, "Garlic Bread"));
            symptoms.AddRange(MakeSymptoms(userId, mealTime.AddHours(3), "Bloating", 7));
        }
        // Eaten recently but within the 7-14 day window (not in last 7 days)
        meals.AddRange(MakeMeals(userId, now.AddDays(-10), "Garlic Bread"));

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.GetEliminationStatusAsync(userId, store);

        result.Phase.Should().Be("Assessment");
        result.FoodsToEliminate.Should().Contain("Garlic Bread");
    }

    [Fact]
    public async Task Elimination_SafeFoods_FrequentlyEatenNoCorrelations()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();

        // Pizza causes symptoms
        for (int i = 0; i < 6; i++)
        {
            var mealTime = now.AddDays(-60 + i * 5);
            meals.AddRange(MakeMeals(userId, mealTime, "Pizza"));
            symptoms.AddRange(MakeSymptoms(userId, mealTime.AddHours(3), "Bloating", 7));
        }

        // Rice eaten frequently at different times (offset by 12h to avoid symptom correlation)
        for (int i = 0; i < 6; i++)
        {
            meals.AddRange(MakeMeals(userId, now.AddDays(-80 + i * 5).AddHours(12), "Rice"));
        }

        // Still eating pizza recently (within 7-14 day window, not last 7)
        meals.AddRange(MakeMeals(userId, now.AddDays(-10), "Pizza"));

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.GetEliminationStatusAsync(userId, store);

        result.SafeFoods.Should().Contain("Rice");
    }

    // ─── Average onset ─────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_AverageOnsetHours_Calculated()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-5), "Milk");
        var symptoms = MakeSymptoms(userId, now.AddDays(-5).AddHours(2), "Diarrhea", 5);

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        var pattern = result.Patterns.First(p => p.FoodName == "Milk");
        pattern.AverageOnsetHours.Should().Be(2.0m);
    }

    // ─── Pattern explanation ───────────────────────────────────────────

    [Fact]
    public async Task Analyze_PatternExplanation_ContainsFoodAndSymptom()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var meals = MakeMeals(userId, now.AddDays(-3), "Cheese");
        var symptoms = MakeSymptoms(userId, now.AddDays(-3).AddHours(4), "Gas", 4);

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        var pattern = result.Patterns.First(p => p.FoodName == "Cheese");
        pattern.Explanation.Should().Contain("Cheese");
        pattern.Explanation.Should().Contain("Gas");
    }

    // ─── User isolation ────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_OnlyReturnsDataForSpecifiedUser()
    {
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now);

        var myMeals = MakeMeals(userId, now.AddDays(-3), "Pizza");
        var otherMeals = MakeMeals(otherUser, now.AddDays(-3), "Sushi");
        var allMeals = myMeals.Concat(otherMeals).ToList();

        var mySymptoms = MakeSymptoms(userId, now.AddDays(-3).AddHours(3), "Bloating", 6);
        var otherSymptoms = MakeSymptoms(otherUser, now.AddDays(-3).AddHours(3), "Nausea", 8);
        var allSymptoms = mySymptoms.Concat(otherSymptoms).ToList();

        var store = MockTableStoreFactory.Create(meals: allMeals, symptoms: allSymptoms).Object;
        var result = await _sut.AnalyzeAsync(userId, from, to, store);

        result.TotalMealsAnalyzed.Should().Be(1);
        result.TotalSymptomsAnalyzed.Should().Be(1);
        result.Patterns.Should().NotContain(p => p.FoodName == "Sushi");
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static List<MealLog> MakeMeals(Guid userId, DateTime loggedAt, params string[] foodNames)
    {
        var mealId = Guid.NewGuid();
        var items = foodNames.Select(f => new MealItem
        {
            Id = Guid.NewGuid(),
            MealLogId = mealId,
            FoodName = f,
        }).ToList();

        return [new MealLog
        {
            Id = mealId,
            UserId = userId,
            LoggedAt = loggedAt,
            MealType = MealType.Lunch,
            Items = items,
        }];
    }

    private static List<SymptomLog> MakeSymptoms(Guid userId, DateTime occurredAt, string symptomName, int severity)
    {
        return [new SymptomLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SymptomTypeId = 1,
            Severity = severity,
            OccurredAt = occurredAt,
            SymptomType = new SymptomType { Id = 1, Name = symptomName, Category = "GI" },
        }];
    }
}
