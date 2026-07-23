using FluentAssertions;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodTextNormalizerTests
{
    [Theory]
    [InlineData("eggs", "egg")]
    [InlineData("tomatoes", "tomato")]
    [InlineData("potatoes", "potato")]
    [InlineData("berries", "berry")]
    [InlineData("sauces", "sauce")]
    [InlineData("cheeses", "cheese")]
    [InlineData("matches", "match")]
    [InlineData("dishes", "dish")]
    [InlineData("bananas", "banana")]
    [InlineData("glass", "glass")] // -ss guard
    [InlineData("hummus", "hummus")] // -us guard
    [InlineData("swiss", "swiss")]
    public void Depluralize_HandlesCommonSuffixes(string input, string expected)
    {
        FoodTextNormalizer.Depluralize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("an")]
    [InlineData("as")]
    public void Depluralize_LeavesShortWordsUnchanged(string input)
    {
        FoodTextNormalizer.Depluralize(input).Should().Be(input);
    }

    [Theory]
    [InlineData("Egg, whole, raw, fresh", "Egg")]
    [InlineData("Banana, raw", "Banana")]
    [InlineData("Chicken breast", "Chicken breast")]
    public void ExtractPrimaryNoun_ReturnsTextBeforeFirstComma(string name, string expected)
    {
        FoodTextNormalizer.ExtractPrimaryNoun(name).Should().Be(expected);
    }

    [Fact]
    public void Tokenize_SplitsOnDelimitersAndLowercases()
    {
        FoodTextNormalizer.Tokenize("Grilled Chicken-Breast (Skinless)")
            .Should().BeEquivalentTo(["grilled", "chicken", "breast", "skinless"]);
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmpty()
    {
        FoodTextNormalizer.Tokenize("").Should().BeEmpty();
    }

    [Fact]
    public void NormalizeFoodName_StripsStopWordsAndDepluralizes()
    {
        FoodTextNormalizer.NormalizeFoodName("Bread, white, toasted").Should().Contain("toast");
        FoodTextNormalizer.NormalizeFoodName("Bread, white").Should().Contain("bread");
    }

    [Fact]
    public void NormalizeFoodName_StripsParentheticals()
    {
        FoodTextNormalizer.NormalizeFoodName("Chicken breast (skinless, boneless)")
            .Should().NotContain("skinless");
    }
}
