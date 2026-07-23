using FluentAssertions;
using GutAI.Application.Common.Helpers;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class MealValidationTests
{
    [Fact]
    public void ClampServings_ExcessiveValue_ClampsToMax()
    {
        MealValidation.ClampServings(999999999m).Should().Be(MealValidation.MaxServings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ClampServings_NonPositiveValue_DefaultsToOne(decimal servings)
    {
        MealValidation.ClampServings(servings).Should().Be(1m);
    }

    [Fact]
    public void ClampServings_NormalValue_PassesThroughUnchanged()
    {
        MealValidation.ClampServings(3.5m).Should().Be(3.5m);
    }

    [Fact]
    public void ClampNutrient_ExcessiveCalories_ClampsToMax()
    {
        MealValidation.ClampNutrient(9999999999m, MealValidation.MaxCalories).Should().Be(MealValidation.MaxCalories);
    }

    [Fact]
    public void ClampNutrient_NegativeValue_ClampsToZero()
    {
        MealValidation.ClampNutrient(-100m, MealValidation.MaxMacroG).Should().Be(0m);
    }

    [Fact]
    public void ClampNutrient_NormalValue_PassesThroughUnchanged()
    {
        MealValidation.ClampNutrient(250m, MealValidation.MaxCalories).Should().Be(250m);
    }
}
