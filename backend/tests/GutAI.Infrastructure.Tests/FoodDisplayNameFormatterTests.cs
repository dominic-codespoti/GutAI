using Xunit;
using FluentAssertions;
using GutAI.Application.Common.Helpers;

namespace GutAI.Infrastructure.Tests;

public class FoodDisplayNameFormatterTests
{
    [Theory]
    [InlineData("grass-fed beef", "Grass-Fed Beef")]
    [InlineData("scrambled eggs", "Scrambled Eggs")]
    [InlineData("USDA chicken breast", "USDA Chicken Breast")]
    [InlineData("", "")]
    public void ToTitleCase_FormatsProviderFoodNames(string input, string expected)
    {
        FoodDisplayNameFormatter.ToTitleCase(input).Should().Be(expected);
    }
}
