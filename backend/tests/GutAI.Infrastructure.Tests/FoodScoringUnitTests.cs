using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// Unit tests for FoodScoring helpers: Depluralize, ExtractPrimaryNoun,
/// ComputeStaticQuality, and ProcessedTerms penalty logic.
/// Covers audit gaps B5 (ProcessedTerms), B7 (Depluralize), B8 (Quality), B10 (CustomScore).
/// </summary>
public sealed class FoodScoringUnitTests
{
    // ════════════════════════════════════════════════════════════════
    //  DEPLURALIZE (B7)
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tomatoes", "tomato")]
    [InlineData("potatoes", "potato")]
    [InlineData("mangoes", "mango")]
    [InlineData("heroes", "hero")]
    [InlineData("sauces", "sauce")]
    [InlineData("cheeses", "cheese")]
    [InlineData("juices", "juice")]
    [InlineData("berries", "berry")]
    [InlineData("cherries", "cherry")]
    [InlineData("strawberries", "strawberry")]
    [InlineData("crackers", "cracker")]
    [InlineData("bananas", "banana")]
    [InlineData("eggs", "egg")]
    [InlineData("almonds", "almond")]
    [InlineData("walnuts", "walnut")]
    [InlineData("grapes", "grape")]
    [InlineData("lentils", "lentil")]
    [InlineData("oats", "oat")]
    [InlineData("mushrooms", "mushroom")]
    public void Depluralize_ProducesExpectedSingular(string input, string expected)
    {
        FoodScoring.Depluralize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("bus")]         // 3-letter word → unchanged
    [InlineData("us")]          // short → unchanged
    [InlineData("octopus")]     // ends in -us → unchanged
    [InlineData("analysis")]    // ends in -is → unchanged
    [InlineData("moss")]        // ends in -ss → unchanged
    public void Depluralize_DoesNotMangle_NonPluralEndings(string input)
    {
        FoodScoring.Depluralize(input).Should().Be(input);
    }

    // ════════════════════════════════════════════════════════════════
    //  EXTRACT PRIMARY NOUN
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Egg, whole, raw, fresh", "Egg")]
    [InlineData("Bananas, raw", "Bananas")]
    [InlineData("Chicken, broilers or fryers, breast", "Chicken")]
    [InlineData("Honey", "Honey")]
    [InlineData("Kale, raw", "Kale")]
    public void ExtractPrimaryNoun_ReturnsTextBeforeFirstComma(string name, string expected)
    {
        FoodScoring.ExtractPrimaryNoun(name).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════════════
    //  STATIC QUALITY SCORING (B8)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Quality_UsdaWholeFoodWithNutrition_ScoresHigherThanBrandedWithImage()
    {
        // USDA whole food: no image, no ingredients, but trusted source + whole food kind
        var usda = MakeDto("Bananas, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
            calories: 89, protein: 1.1m, carbs: 22.8m, fat: 0.3m);

        // OFF branded: has image + ingredients but unknown source
        var branded = MakeDto("Banana Chocolate Bar", dataSource: "OpenFoodFacts",
            foodKind: FoodKind.Branded, imageUrl: "https://img.example.com/bar.jpg",
            ingredients: "banana, chocolate, sugar, milk", calories: 350, protein: 5m, carbs: 45m, fat: 18m);

        var usdaQuality = FoodScoring.ComputeStaticQuality(usda);
        var brandedQuality = FoodScoring.ComputeStaticQuality(branded);

        usdaQuality.Should().BeGreaterThan(brandedQuality,
            "USDA whole food should have higher static quality than branded product with image");
    }

    [Fact]
    public void Quality_ImageBoost_IsCappedForWholeFood()
    {
        var wholeFoodNoImage = MakeDto("Egg, whole, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood);
        var wholeFoodWithImage = MakeDto("Egg, whole, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
            imageUrl: "https://img.example.com/egg.jpg");

        var diff = FoodScoring.ComputeStaticQuality(wholeFoodWithImage) - FoodScoring.ComputeStaticQuality(wholeFoodNoImage);

        // Image boost for whole food should be small (0.1), not the full 0.25
        diff.Should().BeLessThan(0.15f, "image boost for whole foods should be capped at 0.1");
        diff.Should().BeGreaterThan(0.05f, "image boost should still contribute something");
    }

    [Fact]
    public void Quality_IngredientsBoost_IsCappedForWholeFood()
    {
        var wholeFoodNoIng = MakeDto("Banana, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood);
        var wholeFoodWithIng = MakeDto("Banana, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
            ingredients: "banana");

        var diff = FoodScoring.ComputeStaticQuality(wholeFoodWithIng) - FoodScoring.ComputeStaticQuality(wholeFoodNoIng);

        diff.Should().BeLessThan(0.1f, "ingredients boost for whole foods should be capped at 0.05");
    }

    [Fact]
    public void Quality_HardPenaltyTerms_ReduceScore()
    {
        var clean = MakeDto("Chicken, breast, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood);
        var penalized = MakeDto("Chicken, breast, frozen, baby food", dataSource: "USDA", foodKind: FoodKind.WholeFood);

        FoodScoring.ComputeStaticQuality(clean).Should().BeGreaterThan(
            FoodScoring.ComputeStaticQuality(penalized) + 1f,
            "hard penalty terms should significantly reduce quality");
    }

    [Fact]
    public void Quality_ShorterNameScoresHigher()
    {
        var shortName = MakeDto("Honey", dataSource: "USDA", foodKind: FoodKind.WholeFood);
        var longName = MakeDto("Honey, strained or extracted, includes USDA commodity foods", dataSource: "USDA", foodKind: FoodKind.WholeFood);

        FoodScoring.ComputeStaticQuality(shortName).Should().BeGreaterThan(
            FoodScoring.ComputeStaticQuality(longName),
            "shorter names should score higher");
    }

    // ════════════════════════════════════════════════════════════════
    //  QUALITY MULTIPLIER EFFECT (B8 — integration)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void QualityMultiplier_UsdaVsBrandedGap_IsModerate()
    {
        // Simulate the worst case: USDA whole food vs OFF branded with image+ingredients
        var usda = MakeDto("Egg, whole, raw, fresh", dataSource: "USDA", foodKind: FoodKind.WholeFood,
            calories: 143, protein: 12.6m, carbs: 0.7m, fat: 9.5m);
        var off = MakeDto("Eggs", dataSource: "OpenFoodFacts", foodKind: FoodKind.Branded,
            imageUrl: "https://img.example.com/eggs.jpg",
            ingredients: "eggs, salt", calories: 140, protein: 12m, carbs: 1m, fat: 9m,
            brand: "FarmBrand");

        var usdaQ = FoodScoring.ComputeStaticQuality(usda);
        var offQ = FoodScoring.ComputeStaticQuality(off);

        // With multiplier of 8, the max Lucene score impact should be manageable
        var luceneGap = Math.Abs(offQ - usdaQ) * 8f;
        luceneGap.Should().BeLessThan(10f,
            "quality gap × multiplier should not exceed a single exact-match boost (~10 Lucene points)");
    }

    // ════════════════════════════════════════════════════════════════
    //  CUSTOM SCORE QUERY (B10)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CustomScore_CombinesSubQueryAndQuality()
    {
        // FoodCustomScoreQuery.CustomScore returns subQueryScore + valSrcScore * 8f
        // We can't easily instantiate the Lucene provider in isolation, but we can
        // verify the formula through a search scenario.
        var foods = new[]
        {
            MakeDto("Food A", dataSource: "USDA", foodKind: FoodKind.WholeFood),
            MakeDto("Food B", dataSource: "OpenFoodFacts", foodKind: FoodKind.Branded,
                imageUrl: "https://img.example.com/b.jpg", ingredients: "sugar, flour")
        };

        using var index = new FoodSearchIndex(foods);
        // Both foods have "Food" in the name — text relevance is similar.
        // USDA whole food should win due to higher static quality.
        var results = index.Search("food", 2);
        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results[0].Name.Should().Be("Food A",
            "USDA whole food should rank higher when text relevance is equal");
    }

    // ════════════════════════════════════════════════════════════════
    //  PROCESSED TERMS PENALTY BYPASS (B5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Search_TomatoSauce_DoesNotPenalizeSauceResults()
    {
        var foods = new[]
        {
            MakeDto("Tomato products, canned, sauce", dataSource: "USDA", foodKind: FoodKind.WholeFood,
                calories: 29, protein: 1.3m, carbs: 5.4m, fat: 0.2m),
            MakeDto("Tomatoes, red, ripe, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
                calories: 18, protein: 0.9m, carbs: 3.9m, fat: 0.2m),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("tomato sauce", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Contain("sauce",
            "searching 'tomato sauce' should rank sauce #1, not penalize it");
    }

    [Fact]
    public void Search_JustTomato_PenalizesSauceResults()
    {
        var foods = new[]
        {
            MakeDto("Tomatoes, red, ripe, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
                calories: 18, protein: 0.9m, carbs: 3.9m, fat: 0.2m),
            MakeDto("Tomato products, canned, sauce", dataSource: "USDA", foodKind: FoodKind.WholeFood,
                calories: 29, protein: 1.3m, carbs: 5.4m, fat: 0.2m),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("tomato", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Contain("raw",
            "searching just 'tomato' should prefer raw tomatoes over sauce");
    }

    [Fact]
    public void Search_OrangeJuice_DoesNotPenalizeJuiceResults()
    {
        var foods = new[]
        {
            MakeDto("Orange juice, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
                calories: 45, protein: 0.7m, carbs: 10.4m, fat: 0.2m),
            MakeDto("Oranges, raw, all commercial varieties", dataSource: "USDA", foodKind: FoodKind.WholeFood,
                calories: 47, protein: 0.9m, carbs: 11.8m, fat: 0.1m),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("orange juice", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Contain("juice",
            "searching 'orange juice' should rank juice #1, not penalize it");
    }

    [Theory]
    [InlineData("soy sauce", "sauce")]
    [InlineData("apple juice", "juice")]
    [InlineData("dried mango", "dried")]
    [InlineData("frozen peas", "frozen")]
    [InlineData("canned beans", "canned")]
    public void Search_QueryContainsProcessedTerm_DoesNotPenalizeIt(string query, string processedTerm)
    {
        var foods = new[]
        {
            MakeDto($"Test food with {processedTerm}", dataSource: "USDA", foodKind: FoodKind.WholeFood),
            MakeDto("Test food plain", dataSource: "USDA", foodKind: FoodKind.WholeFood),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search(query, 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Contain(processedTerm,
            $"searching '{query}' should not penalize results containing '{processedTerm}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  REGRESSION: Mars Chocolate "Eggs" should not outrank real eggs
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void StaticQuality_BrandedUSDA_NotTreatedAsWholeFood()
    {
        var marsEggs = MakeDto("Eggs", dataSource: "USDA", foodKind: FoodKind.Branded,
            brand: "Mars Chocolate North America LLC",
            calories: 525m, protein: 7.5m, carbs: 60m, fat: 27.5m);

        var realEggs = MakeDto("Egg, whole, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
            calories: 143m, protein: 12.6m, carbs: 0.7m, fat: 9.5m);

        var marsQ = FoodScoring.ComputeStaticQuality(marsEggs);
        var realQ = FoodScoring.ComputeStaticQuality(realEggs);

        // Real whole-food eggs should have higher static quality than branded chocolate
        realQ.Should().BeGreaterThan(marsQ,
            "branded USDA chocolate 'Eggs' must not get whole-food quality boost");
    }

    [Fact]
    public void FinalScore_EggsQuery_PenalizesHighCarbCandy()
    {
        var marsEggs = MakeDto("Eggs", dataSource: "USDA", foodKind: FoodKind.Branded,
            brand: "Mars Chocolate North America LLC",
            calories: 525m, protein: 7.5m, carbs: 60m, fat: 27.5m);

        var realEggs = MakeDto("Egg, whole, raw", dataSource: "USDA", foodKind: FoodKind.WholeFood,
            calories: 143m, protein: 12.6m, carbs: 0.7m, fat: 9.5m);

        var query = "eggs";
        var tokens = new[] { "eggs" };
        var analyzed = new[] { "egg" };

        var marsScore = FoodScoring.FinalScore(marsEggs, 50f, query, tokens, analyzed);
        var realScore = FoodScoring.FinalScore(realEggs, 50f, query, tokens, analyzed);

        // Real eggs should outscore Mars chocolate when searching "eggs"
        realScore.Should().BeGreaterThan(marsScore,
            "Mars Chocolate 'Eggs' with 60g carbs must score lower than real eggs for query 'eggs'");
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════

    private static FoodProductDto MakeDto(
        string name,
        string dataSource = "USDA",
        FoodKind foodKind = FoodKind.WholeFood,
        string? imageUrl = null,
        string? ingredients = null,
        decimal? calories = null,
        decimal? protein = null,
        decimal? carbs = null,
        decimal? fat = null,
        string? brand = null)
    {
        return new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataSource = dataSource,
            FoodKind = foodKind,
            ImageUrl = imageUrl,
            Ingredients = ingredients,
            Calories100g = calories,
            Protein100g = protein,
            Carbs100g = carbs,
            Fat100g = fat,
            Brand = brand,
        };
    }
}
