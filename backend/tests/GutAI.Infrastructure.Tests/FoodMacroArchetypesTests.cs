using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodMacroArchetypesTests
{
    private static FoodProductDto MakeFood(
        string name, decimal? cal = null, decimal? protein = null, decimal? carbs = null,
        decimal? fat = null, decimal? sugar = null) =>
        new()
        {
            Id = Guid.NewGuid(), Name = name,
            Calories100g = cal, Protein100g = protein, Carbs100g = carbs, Fat100g = fat, Sugar100g = sugar,
        };

    [Fact]
    public void Score_NoCalorieData_ReturnsZero()
    {
        var food = MakeFood("Mystery item");
        FoodMacroArchetypes.Score(food, "egg").Should().Be(0f);
    }

    [Fact]
    public void Score_EggQuery_HighCarbCandyEgg_IsPenalized()
    {
        var candyEgg = MakeFood("Chocolate Egg", cal: 500, protein: 4, carbs: 60, fat: 20, sugar: 55);
        FoodMacroArchetypes.Score(candyEgg, "eggs").Should().BeLessThan(0f);
    }

    [Fact]
    public void Score_EggQuery_PlausibleEgg_IsNotPenalized()
    {
        var realEgg = MakeFood("Egg, whole, raw, fresh", cal: 143, protein: 13, carbs: 1, fat: 10, sugar: 0.5m);
        FoodMacroArchetypes.Score(realEgg, "eggs").Should().Be(0f);
    }

    [Fact]
    public void Score_LeanProteinQuery_HighCarbMislabeledEntry_IsPenalized()
    {
        // The real bug this whole system is built around: an unbranded OpenFoodFacts
        // "grilled chicken breast" entry with implausible carbs for plain meat.
        var mislabeled = MakeFood("Grilled chicken breast", cal: 86, protein: 9.29m, carbs: 5.71m, fat: 2.86m);
        FoodMacroArchetypes.Score(mislabeled, "grilled chicken breast").Should().BeLessThan(0f);
    }

    [Fact]
    public void Score_LeanProteinQuery_LegitimateCompositeDish_IsNotPenalized()
    {
        // "chicken salad" legitimately carries carbs from the salad — must not be penalized
        // just because the query mentions a lean-protein keyword.
        var chickenSalad = MakeFood("Chicken salad with mayo", cal: 200, protein: 15, carbs: 12, fat: 10);
        FoodMacroArchetypes.Score(chickenSalad, "chicken salad").Should().Be(0f);
    }

    [Fact]
    public void Score_OilQuery_LowFatCandidate_IsPenalized()
    {
        var fakeOil = MakeFood("Diet cooking spray", cal: 150, fat: 2);
        FoodMacroArchetypes.Score(fakeOil, "olive oil").Should().BeLessThan(0f);
    }

    [Fact]
    public void Score_FruitQuery_HighCalorieCandidate_IsPenalized()
    {
        var candyBanana = MakeFood("Banana chocolate bar", cal: 400, fat: 20);
        FoodMacroArchetypes.Score(candyBanana, "banana").Should().BeLessThan(0f);
    }

    [Fact]
    public void Score_FruitQuery_PlausibleRawFruit_IsNotPenalized()
    {
        var realBanana = MakeFood("Bananas, raw", cal: 89, fat: 0.3m);
        FoodMacroArchetypes.Score(realBanana, "banana").Should().Be(0f);
    }

    [Fact]
    public void Score_UnrelatedQuery_NoArchetypeTriggers_ReturnsZero()
    {
        var food = MakeFood("Some random item", cal: 9999, protein: 9999, carbs: 9999, fat: 9999, sugar: 9999);
        FoodMacroArchetypes.Score(food, "widget").Should().Be(0f);
    }

    [Fact]
    public void Score_CaloriesFarExceedMacros_IsPenalized()
    {
        // Real-world case: a branded "Oatmeal" product surfaced 1580 kcal/100g against
        // carbs=66.7g/fat=6.67g (macro-derived estimate ~327 kcal) — consistent with a kJ
        // figure entered into the kcal field upstream.
        var corrupted = MakeFood("Oatmeal", cal: 1580, protein: null, carbs: 66.7m, fat: 6.67m, sugar: 0);
        FoodMacroArchetypes.Score(corrupted, "oatmeal").Should().BeLessThan(0f);
    }

    [Fact]
    public void Score_PlausibleCalorieDensity_IsNotPenalizedByEnergyCheck()
    {
        var real = MakeFood("Oatmeal, dry", cal: 380, protein: 13, carbs: 66, fat: 7, sugar: 1);
        FoodMacroArchetypes.Score(real, "oatmeal").Should().Be(0f);
    }

    [Fact]
    public void Score_AlcoholicBeverage_NotFlaggedByEnergyCheck()
    {
        // Alcohol (~7 kcal/g) isn't tracked in FoodMacros, so wine legitimately has a large
        // calorie-to-macro ratio but a small absolute gap — must not be penalized.
        var wine = MakeFood("Red Wine", cal: 85, protein: 0.1m, carbs: 2.6m, fat: 0, sugar: 0.6m);
        FoodMacroArchetypes.Score(wine, "wine").Should().Be(0f);
    }

    [Theory]
    [InlineData("chicken")]
    [InlineData("beef")]
    [InlineData("salmon")]
    public void IsLeanProteinQuery_MeatKeywordAlone_ReturnsTrue(string query)
    {
        FoodMacroArchetypes.IsLeanProteinQuery(query).Should().BeTrue();
    }

    [Theory]
    [InlineData("chicken salad")]
    [InlineData("beef stir fry")]
    [InlineData("breaded chicken")]
    public void IsLeanProteinQuery_WithLegitimateCarbSource_ReturnsFalse(string query)
    {
        FoodMacroArchetypes.IsLeanProteinQuery(query).Should().BeFalse();
    }
}
