using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodQualityScorerTests
{
    private static FoodProductDto MakeFood(
        string name, string source = "OpenFoodFacts", FoodKind kind = FoodKind.Unknown,
        string? brand = null, string? imageUrl = null, string? ingredients = null) =>
        new() { Id = Guid.NewGuid(), Name = name, DataSource = source, FoodKind = kind, Brand = brand, ImageUrl = imageUrl, Ingredients = ingredients };

    [Fact]
    public void Score_UsdaWholeFood_OutscoresBrandedWithImage()
    {
        var usda = MakeFood("Banana, raw", source: "USDA", kind: FoodKind.WholeFood);
        var branded = MakeFood("Chiquita Banana Chips", source: "OpenFoodFacts", kind: FoodKind.Branded,
            brand: "Chiquita", imageUrl: "https://example.com/img.jpg");

        FoodQualityScorer.Score(usda).Should().BeGreaterThan(FoodQualityScorer.Score(branded));
    }

    [Fact]
    public void Score_HardPenaltyTerm_SignificantlyReducesQuality()
    {
        var clean = MakeFood("Banana, raw", kind: FoodKind.WholeFood);
        var frozen = MakeFood("Banana, frozen", kind: FoodKind.WholeFood);

        FoodQualityScorer.Score(clean).Should().BeGreaterThan(FoodQualityScorer.Score(frozen) + 1f,
            "hard penalty terms should significantly reduce quality");
    }

    [Fact]
    public void Score_ShorterName_ScoresHigherThanLongerName()
    {
        var shortName = MakeFood("Banana, raw");
        var longName = MakeFood("Banana, raw, extremely long descriptive name with many qualifiers");

        FoodQualityScorer.Score(shortName).Should().BeGreaterThan(FoodQualityScorer.Score(longName));
    }

    [Fact]
    public void Score_MoreCommasAndParens_ScoresLower()
    {
        var plain = MakeFood("Chicken breast");
        var punctuated = MakeFood("Chicken breast (skinless, boneless, raw)");

        FoodQualityScorer.Score(plain).Should().BeGreaterThan(FoodQualityScorer.Score(punctuated));
    }

    [Fact]
    public void Score_NutritionCompleteness_IncreasesQuality()
    {
        var bare = MakeFood("Mystery Food");
        var complete = bare with { Calories100g = 100, Protein100g = 5, Carbs100g = 10, Fat100g = 2, Fiber100g = 1, Sugar100g = 1 };

        FoodQualityScorer.Score(complete).Should().BeGreaterThan(FoodQualityScorer.Score(bare));
    }

    [Fact]
    public void Score_UnbrandedNoCommaIngredients_LooksWholeAndGetsBonus()
    {
        var looksWhole = MakeFood("Apple", kind: FoodKind.Unknown, brand: null, ingredients: "apple");
        var lookslikeRecipe = MakeFood("Apple pie", kind: FoodKind.Unknown, brand: "SomeBrand", ingredients: "apple, sugar, flour");

        FoodQualityScorer.Score(looksWhole).Should().BeGreaterThan(FoodQualityScorer.Score(lookslikeRecipe));
    }
}
