using FluentAssertions;
using GutAI.Application.Common.Helpers;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class FoodClassClassifierTests
{
    [Theory]
    [InlineData("orange juice", FoodClassClassifier.Classes.Beverage)]
    [InlineData("peanut butter", FoodClassClassifier.Classes.FatSauce)]
    [InlineData("chocolate cookies", FoodClassClassifier.Classes.Dessert)]
    [InlineData("chicken curry", FoodClassClassifier.Classes.MixedDish)]
    [InlineData("brown rice", FoodClassClassifier.Classes.Starch)]
    [InlineData("grilled chicken", FoodClassClassifier.Classes.Protein)]
    [InlineData("cheddar cheese", FoodClassClassifier.Classes.Dairy)]
    [InlineData("blueberries", FoodClassClassifier.Classes.Fruit)]
    [InlineData("roasted carrots", FoodClassClassifier.Classes.Vegetable)]
    [InlineData("a delicious entrée", FoodClassClassifier.Classes.Other)]
    public void Classify_matches_whole_word_rules_for_each_class(string name, string expected)
    {
        FoodClassClassifier.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("tea chicken", FoodClassClassifier.Classes.Beverage)]
    [InlineData("butter cake", FoodClassClassifier.Classes.FatSauce)]
    [InlineData("chocolate sushi", FoodClassClassifier.Classes.Dessert)]
    [InlineData("chicken curry", FoodClassClassifier.Classes.MixedDish)]
    [InlineData("potatoes", FoodClassClassifier.Classes.Starch)]
    [InlineData("eggs", FoodClassClassifier.Classes.Protein)]
    [InlineData("cream cheese", FoodClassClassifier.Classes.Dairy)]
    [InlineData("berries", FoodClassClassifier.Classes.Fruit)]
    [InlineData("tomatoes", FoodClassClassifier.Classes.Vegetable)]
    [InlineData("stir-fry vegetables", FoodClassClassifier.Classes.MixedDish)]
    public void Classify_resolves_overlaps_in_priority_order(string name, string expected)
    {
        FoodClassClassifier.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.4999d, "low")]
    [InlineData(0.5d, "medium")]
    [InlineData(0.7499d, "medium")]
    [InlineData(0.75d, "high")]
    public void ConfidenceTier_uses_inclusive_tier_boundaries(double confidence, string expected)
    {
        FoodClassClassifier.ConfidenceTier((decimal)confidence).Should().Be(expected);
    }


    [Fact]
    public void Classify_requires_a_whole_word()
    {
        FoodClassClassifier.Classify("teapot and buttery").Should().Be(FoodClassClassifier.Classes.Other);
    }
}
