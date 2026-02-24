using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class PersonalizedScoringServiceTests
{
    private readonly PersonalizedScoringService _sut;
    private readonly GutRiskService _gutRisk = new();
    private readonly FodmapService _fodmap = new();

    public PersonalizedScoringServiceTests()
    {
        _sut = new PersonalizedScoringService(_gutRisk, _fodmap);
    }

    private static FoodProductDto MakeProduct(
        string name = "Test Product",
        string? ingredients = null,
        int? novaGroup = null,
        decimal? fiber = null,
        decimal? sugar = null,
        string[]? allergensTags = null,
        List<string>? additiveTags = null,
        List<FoodAdditiveDto>? additives = null)
    {
        return new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Ingredients = ingredients,
            NovaGroup = novaGroup,
            Fiber100g = fiber,
            Sugar100g = sugar,
            AllergensTags = allergensTags ?? [],
            AdditivesTags = additiveTags ?? [],
            Additives = additives ?? [],
        };
    }

    // ─── Component scoring (no DB needed) ──────────────────────────────

    [Fact]
    public async Task CleanProduct_ScoresExcellent()
    {
        var product = MakeProduct("Organic Rice", "white rice, water", novaGroup: 1, fiber: 7m);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.CompositeScore.Should().BeGreaterOrEqualTo(80);
        result.Rating.Should().Be("Excellent");
        result.Explanations.Should().HaveCount(6);
        result.PersonalWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task UltraProcessed_ScoresLower()
    {
        var product = MakeProduct("Instant Noodles", "wheat flour, palm oil, msg, garlic powder, onion powder",
            novaGroup: 4, fiber: 0.5m);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.CompositeScore.Should().BeLessThan(70);
        result.NovaComponent.Should().Be(30);
    }

    [Fact]
    public async Task HighFiber_BoostsFiberComponent()
    {
        var product = MakeProduct("Bran Flakes", "wheat bran, sugar", novaGroup: 2, fiber: 8m);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.FiberComponent.Should().Be(100);
        var fiberExplanation = result.Explanations.First(e => e.Component == "Fiber Content");
        fiberExplanation.RawScore.Should().Be(100);
    }

    [Fact]
    public async Task NoFiber_LowFiberComponent()
    {
        var product = MakeProduct("White Sugar", "sugar", fiber: 0m);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.FiberComponent.Should().Be(25);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 75)]
    [InlineData(3, 50)]
    [InlineData(4, 30)]
    public async Task NovaGroup_MapsToCorrectScore(int nova, int expectedScore)
    {
        var product = MakeProduct(novaGroup: nova);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.NovaComponent.Should().Be(expectedScore);
    }

    [Fact]
    public async Task SugarAlcohols_DetectedInIngredients()
    {
        var product = MakeProduct("Sugar Free Candy", "maltitol, xylitol, sorbitol, flavor");
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.SugarAlcoholComponent.Should().Be(10);
        var sugarExplanation = result.Explanations.First(e => e.Component == "Sugar Alcohols");
        sugarExplanation.RawScore.Should().Be(10);
    }

    [Fact]
    public async Task NoSugarAlcohols_FullScore()
    {
        var product = MakeProduct("Plain Rice", "rice, water");
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.SugarAlcoholComponent.Should().Be(100);
    }

    [Fact]
    public async Task OneSugarAlcohol_ModeratePenalty()
    {
        var product = MakeProduct("Protein Bar", "whey, erythritol, cocoa");
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.SugarAlcoholComponent.Should().Be(60);
    }

    // ─── Allergen matching ─────────────────────────────────────────────

    [Fact]
    public async Task AllergenMatch_DropsToZero()
    {
        var userId = Guid.NewGuid();
        var product = MakeProduct("Peanut Butter", "peanuts, salt", allergensTags: ["en:peanuts"]);
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "test@test.com", Allergies = ["peanuts"] }]).Object;
        var result = await _sut.ScoreAsync(product, userId, store);

        result.AllergenComponent.Should().Be(0);
        result.PersonalWarnings.Should().Contain(w => w.Contains("peanuts"));
    }

    [Fact]
    public async Task NoAllergenMatch_FullScore()
    {
        var userId = Guid.NewGuid();
        var product = MakeProduct("Rice Cakes", "rice", allergensTags: ["en:gluten"]);
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "test@test.com", Allergies = ["peanuts"] }]).Object;
        var result = await _sut.ScoreAsync(product, userId, store);

        result.AllergenComponent.Should().Be(100);
    }

    [Fact]
    public async Task NoUserAllergies_FullScore()
    {
        var userId = Guid.NewGuid();
        var product = MakeProduct("Peanut Butter", "peanuts", allergensTags: ["en:peanuts"]);
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "test@test.com" }]).Object;
        var result = await _sut.ScoreAsync(product, userId, store);

        result.AllergenComponent.Should().Be(100);
    }

    // ─── Personal trigger penalty ──────────────────────────────────────

    [Fact]
    public async Task PersonalTrigger_PenalizesScore()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var mealId = Guid.NewGuid();

        var meals = new List<GutAI.Domain.Entities.MealLog>
        {
            new()
            {
                Id = mealId,
                UserId = userId,
                LoggedAt = now.AddDays(-5),
                Items = [new() { Id = Guid.NewGuid(), MealLogId = mealId, FoodName = "Pizza" }]
            }
        };

        var symptoms = new List<GutAI.Domain.Entities.SymptomLog>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Severity = 7,
                OccurredAt = now.AddDays(-5).AddHours(4),
                SymptomTypeId = 1,
                SymptomType = new() { Id = 1, Name = "Bloating", Category = "GI" }
            }
        };

        var product = MakeProduct("Pizza Margherita", "wheat flour, mozzarella, tomato, garlic", novaGroup: 3);
        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.ScoreAsync(product, userId, store);

        result.PersonalTriggerPenalty.Should().BeGreaterThan(0);
        result.PersonalWarnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NoSymptomHistory_NoPenalty()
    {
        var userId = Guid.NewGuid();
        var product = MakeProduct("Pizza", "wheat, cheese");
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, userId, store);

        result.PersonalTriggerPenalty.Should().Be(0);
        result.PersonalWarnings.Should().BeEmpty();
    }

    // ─── Rating bands ──────────────────────────────────────────────────

    [Fact]
    public async Task RatingBand_Excellent()
    {
        var product = MakeProduct("Pure Rice", "rice", novaGroup: 1, fiber: 8m);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);
        result.Rating.Should().Be("Excellent");
    }

    [Fact]
    public async Task Summary_ContainsProductName()
    {
        var product = MakeProduct("Chocolate Bar", "cocoa, sugar, milk", novaGroup: 3, fiber: 2m);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.Summary.Should().Contain("Chocolate Bar");
        result.Summary.Should().Contain("/100");
    }

    // ─── FODMAP component integration ──────────────────────────────────

    [Fact]
    public async Task HighFodmapProduct_LowFodmapComponent()
    {
        var product = MakeProduct("Garlic Bread", "wheat flour, garlic, butter, onion", novaGroup: 3);
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.FodmapComponent.Should().BeLessThan(80);
    }

    [Fact]
    public async Task Explanations_HaveSixComponents()
    {
        var product = MakeProduct();
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.Explanations.Should().HaveCount(6);
        result.Explanations.Select(e => e.Component).Should().Contain("FODMAP Risk");
        result.Explanations.Select(e => e.Component).Should().Contain("Additive Risk");
        result.Explanations.Select(e => e.Component).Should().Contain("NOVA Processing");
        result.Explanations.Select(e => e.Component).Should().Contain("Fiber Content");
        result.Explanations.Select(e => e.Component).Should().Contain("Allergen Match");
        result.Explanations.Select(e => e.Component).Should().Contain("Sugar Alcohols");
    }

    [Fact]
    public async Task CompositeScore_IsClamped0To100()
    {
        var product = MakeProduct("Terrible Food",
            "wheat, garlic, onion, inulin, sorbitol, maltitol, xylitol, isomalt, mannitol",
            novaGroup: 4, fiber: 0m,
            allergensTags: ["en:gluten", "en:milk"],
            additiveTags: ["en:e420", "en:e433", "en:e466"]);

        var userId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "t@t.com", Allergies = ["gluten", "milk"] }]).Object;
        var result = await _sut.ScoreAsync(product, userId, store);

        result.CompositeScore.Should().BeGreaterOrEqualTo(0);
        result.CompositeScore.Should().BeLessOrEqualTo(100);
    }

    // ─── Weight calculations ───────────────────────────────────────────

    [Fact]
    public async Task WeightsAddUp()
    {
        var product = MakeProduct();
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        var totalWeight = result.Explanations.Sum(e => e.Weight);
        totalWeight.Should().Be(100);
    }

    // ─── Fiber null handling ───────────────────────────────────────────

    [Fact]
    public async Task NullFiber_ScoresLow_NotModerate()
    {
        var product = MakeProduct("Beef Steak", "beef");
        var store = MockTableStoreFactory.Create().Object;
        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.FiberComponent.Should().Be(25);
        var fiberExplanation = result.Explanations.First(e => e.Component == "Fiber Content");
        fiberExplanation.Explanation.Should().Contain("no fiber bonus applied");
        fiberExplanation.Explanation.Should().NotContain("assuming moderate");
    }
}
