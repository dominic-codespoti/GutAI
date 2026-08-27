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
    public async Task AllergenMatch_CapsCompositeAtAvoid()
    {
        var userId = Guid.NewGuid();
        var product = MakeProduct("Peanut Snack", "peanuts", novaGroup: 1, fiber: 8m, allergensTags: ["en:peanuts"]);
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "test@test.com", Allergies = ["peanuts"] }]).Object;

        var result = await _sut.ScoreAsync(product, userId, store);

        result.CompositeScore.Should().BeLessThan(20);
        result.Rating.Should().Be("Avoid");
        result.Summary.Should().Contain("matches an allergen");
    }

    [Fact]
    public async Task MissingAllergenData_IsDisclosed()
    {
        var userId = Guid.NewGuid();
        var product = MakeProduct("Unlabelled Food", "rice");
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "test@test.com", Allergies = ["peanuts"] }]).Object;

        var result = await _sut.ScoreAsync(product, userId, store);

        result.PersonalWarnings.Should().Contain(warning => warning.Contains("unavailable"));
        result.Explanations.Single(explanation => explanation.Component == "Allergen Match")
            .Explanation.Should().Contain("cannot establish safety");
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

    [Fact]
    public async Task GenericIngredientWordTrigger_DoesNotFalsePositiveOnUnrelatedProduct()
    {
        // Regression test: the old implementation matched triggers against the full raw
        // ingredients string, so a trigger food logged as "Milk" would flag almost any
        // packaged product whose ingredients merely mention milk. Matching should now be
        // FoodProductId-exact or normalized-NAME-only, so an unrelated product that just
        // happens to contain the trigger word in its ingredients must NOT be penalized.
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
                Items = [new() { Id = Guid.NewGuid(), MealLogId = mealId, FoodName = "Milk" }]
            }
        };
        var symptoms = new List<GutAI.Domain.Entities.SymptomLog>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Severity = 7,
                OccurredAt = now.AddDays(-5).AddHours(3),
                SymptomTypeId = 1,
                SymptomType = new() { Id = 1, Name = "Bloating", Category = "GI" }
            }
        };

        var unrelatedProduct = MakeProduct("Dark Chocolate Bar", "cocoa, sugar, milk fat, soy lecithin", novaGroup: 3);
        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.ScoreAsync(unrelatedProduct, userId, store);

        result.PersonalTriggerPenalty.Should().Be(0);
        result.PersonalWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task SameFoodProductIdLoggedBefore_MatchesRegardlessOfNameDrift()
    {
        // A product re-scanned/renamed between logs (e.g. OFF data changed the display name)
        // should still match via FoodProductId even though the raw names differ entirely.
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var mealId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var meals = new List<GutAI.Domain.Entities.MealLog>
        {
            new()
            {
                Id = mealId,
                UserId = userId,
                LoggedAt = now.AddDays(-5),
                Items = [new() { Id = Guid.NewGuid(), MealLogId = mealId, FoodName = "Old Product Name", FoodProductId = productId }]
            }
        };
        var symptoms = new List<GutAI.Domain.Entities.SymptomLog>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Severity = 7,
                OccurredAt = now.AddDays(-5).AddHours(3),
                SymptomTypeId = 1,
                SymptomType = new() { Id = 1, Name = "Bloating", Category = "GI" }
            }
        };

        var sameProductRenamed = MakeProduct("Brand New Display Name", "wheat, sugar", novaGroup: 3);
        sameProductRenamed = sameProductRenamed with { Id = productId };
        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await _sut.ScoreAsync(sameProductRenamed, userId, store);

        result.PersonalTriggerPenalty.Should().BeGreaterThan(0);
        result.PersonalWarnings.Should().NotBeEmpty();
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
        result.CompositeScore.Should().BeLessOrEqualTo(19); // allergen match caps the Avoid band
        result.Rating.Should().Be("Avoid");
    }

    [Fact]
    public async Task FodmapIngredients_DoNotAlsoReduceAdditiveComponent()
    {
        var product = MakeProduct("Garlic Rice", "rice, garlic", novaGroup: 1, fiber: 3m);
        var store = MockTableStoreFactory.Create().Object;

        var result = await _sut.ScoreAsync(product, Guid.NewGuid(), store);

        result.FodmapComponent.Should().BeLessThan(100);
        result.AdditiveRiskComponent.Should().Be(100);
        result.Explanations.Single(explanation => explanation.Component == "Sugar Alcohols")
            .Weight.Should().Be(0);
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

    // ─── Profile conditions & dynamic weighting ─────────────────────────

    [Fact]
    public async Task DefaultProfile_ReproducesBaselineComposite()
    {
        // Fixture product: "Garlic Bread" with wheat/garlic/butter/onion, novaGroup 3, fiber 2g, no allergens.
        // FodmapScore: Garlic, onion, wheat detected -> score is 1 (multiple high FODMAP triggers)
        // AdditiveScore: 100 (no additive flags)
        // NovaScore: 50 (NovaGroup 3)
        // FiberScore: 50 (Fiber 2g >= 1m)
        // AllergenScore: 100 (no user allergies)
        // Default weights: FODMAP 35%, Additive 20%, NOVA 15%, Fiber 15%, Allergen 15%
        // Composite = (int)(1 * 0.35 + 100 * 0.20 + 50 * 0.15 + 50 * 0.15 + 100 * 0.15)
        //           = (int)(0.35 + 20 + 7.5 + 7.5 + 15) = (int)(50.35) = 50.
        var product = MakeProduct("Garlic Bread", "wheat flour, garlic, butter, onion", novaGroup: 3, fiber: 2m);
        var defaultUserId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create(users: [new() { Id = defaultUserId, Email = "default@test.com", GutConditions = [], DietaryPreferences = [], Allergies = [] }]).Object;

        var result = await _sut.ScoreAsync(product, defaultUserId, store);

        result.FodmapComponent.Should().Be(1);
        result.AdditiveRiskComponent.Should().Be(100);
        result.NovaComponent.Should().Be(50);
        result.FiberComponent.Should().Be(50);
        result.AllergenComponent.Should().Be(100);
        result.CompositeScore.Should().Be(50);

        var fodmapExpl = result.Explanations.Single(e => e.Component == "FODMAP Risk");
        fodmapExpl.Weight.Should().Be(35);
        fodmapExpl.WeightedContribution.Should().Be(0); // (int)(1 * 0.35) = 0
        fodmapExpl.Explanation.Should().NotContain("Weight increased");

        result.Explanations.Single(e => e.Component == "NOVA Processing").Weight.Should().Be(15);
        result.Explanations.Single(e => e.Component == "Fiber Content").Weight.Should().Be(15);
        result.Explanations.Single(e => e.Component == "Additive Risk").Weight.Should().Be(20);
        result.Explanations.Single(e => e.Component == "Allergen Match").Weight.Should().Be(15);
    }

    [Theory]
    [InlineData("IBS")]
    [InlineData("irritable bowel syndrome")]
    [InlineData("SIBO")]
    [InlineData("fructose malabsorption")]
    [InlineData("bloating and gas")]
    public async Task IbsCondition_ShiftsWeightsAndRecomputesComposite(string condition)
    {
        // Same fixture product: "Garlic Bread" with scores 1, 100, 50, 50, 100.
        // With fodmapSensitive condition: FODMAP weight shifts 35->45, NOVA 15->10, Fiber 15->10.
        // Composite = (int)(1 * 0.45 + 100 * 0.20 + 50 * 0.10 + 50 * 0.10 + 100 * 0.15)
        //           = (int)(0.45 + 20 + 5 + 5 + 15) = (int)(45.45) = 45.
        // Baseline was 50, shifted is 45.
        var product = MakeProduct("Garlic Bread", "wheat flour, garlic, butter, onion", novaGroup: 3, fiber: 2m);
        var userId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "ibs@test.com", GutConditions = [condition] }]).Object;

        var result = await _sut.ScoreAsync(product, userId, store);

        result.CompositeScore.Should().Be(45);
        var fodmapExpl = result.Explanations.Single(e => e.Component == "FODMAP Risk");
        fodmapExpl.Weight.Should().Be(45);
        fodmapExpl.WeightedContribution.Should().Be(0); // (int)(1 * 0.45) = 0
        fodmapExpl.Explanation.Should().Contain("Weight increased because your profile indicates FODMAP sensitivity.");

        var novaExpl = result.Explanations.Single(e => e.Component == "NOVA Processing");
        novaExpl.Weight.Should().Be(10);
        novaExpl.WeightedContribution.Should().Be(5); // (int)(50 * 0.10) = 5

        var fiberExpl = result.Explanations.Single(e => e.Component == "Fiber Content");
        fiberExpl.Weight.Should().Be(10);
        fiberExpl.WeightedContribution.Should().Be(5); // (int)(50 * 0.10) = 5

        var totalWeight = result.Explanations.Sum(e => e.Weight);
        totalWeight.Should().Be(100);
    }

    [Fact]
    public async Task LowFodmapDietaryPreference_ShiftsWeights()
    {
        var product = MakeProduct("Garlic Bread", "wheat flour, garlic, butter, onion", novaGroup: 3, fiber: 2m);
        var userId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "lowfod@test.com", DietaryPreferences = ["low-fodmap"] }]).Object;

        var result = await _sut.ScoreAsync(product, userId, store);

        result.CompositeScore.Should().Be(45);
        result.Explanations.Single(e => e.Component == "FODMAP Risk").Weight.Should().Be(45);
        result.Explanations.Single(e => e.Component == "NOVA Processing").Weight.Should().Be(10);
        result.Explanations.Single(e => e.Component == "Fiber Content").Weight.Should().Be(10);
    }

    [Fact]
    public async Task CeliacProfile_WithWheatIngredientAndNoGlutenTag_YieldsAllergenZeroAndWarning()
    {
        // Product has allergen tags (e.g. "en:milk") but no "en:gluten" or "en:wheat" tag.
        // Ingredients contain "wheat flour". User profile has "celiac".
        var product = MakeProduct("Baked Biscuit", "wheat flour, sugar, butter", novaGroup: 3, fiber: 1m,
            allergensTags: ["en:milk"]);
        var userId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "celiac@test.com", GutConditions = ["celiac disease"] }]).Object;

        var result = await _sut.ScoreAsync(product, userId, store);

        result.AllergenComponent.Should().Be(0);
        result.PersonalWarnings.Should().Contain("Gluten source detected in ingredients (profile indicates gluten sensitivity).");
        result.CompositeScore.Should().BeLessOrEqualTo(19);
        result.Rating.Should().Be("Avoid");

        var allergenExpl = result.Explanations.Single(e => e.Component == "Allergen Match");
        allergenExpl.RawScore.Should().Be(0);
        allergenExpl.WeightedContribution.Should().Be(0);
        allergenExpl.Explanation.Should().Contain("ingredient text scan");
    }

    [Fact]
    public async Task CeliacProfile_WithCleanIngredients_LeavesAllergenNeutral()
    {
        // Product has allergen tags ("en:milk") and clean ingredients without wheat/barley/rye/spelt/triticale/malt.
        var product = MakeProduct("Rice Milk", "water, rice, sunflower oil, salt", novaGroup: 1, fiber: 1m,
            allergensTags: ["en:soybeans"]);
        var userId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "celiac@test.com", GutConditions = ["celiac disease"] }]).Object;

        var result = await _sut.ScoreAsync(product, userId, store);

        result.AllergenComponent.Should().Be(100);
        result.PersonalWarnings.Should().NotContain(w => w.Contains("Gluten source detected"));

        var allergenExpl = result.Explanations.Single(e => e.Component == "Allergen Match");
        allergenExpl.RawScore.Should().Be(100);
        allergenExpl.Explanation.Should().Be("No profile allergen match was detected in the available allergen data.");
    }

    [Theory]
    [InlineData("barley malt extract")]
    [InlineData("organic rye flour")]
    [InlineData("spelt flakes")]
    [InlineData("triticale meal")]
    [InlineData("malt syrup")]
    public async Task CeliacProfile_DetectsAllGlutenGrains(string ingredientSnippet)
    {
        var product = MakeProduct("Grain Cereal", $"corn, {ingredientSnippet}, sugar", novaGroup: 2,
            allergensTags: ["en:nuts"]);
        var userId = Guid.NewGuid();
        var store = MockTableStoreFactory.Create(users: [new() { Id = userId, Email = "gluten@test.com", GutConditions = ["gluten intolerance"] }]).Object;

        var result = await _sut.ScoreAsync(product, userId, store);

        result.AllergenComponent.Should().Be(0);
        result.PersonalWarnings.Should().Contain("Gluten source detected in ingredients (profile indicates gluten sensitivity).");
    }
}
