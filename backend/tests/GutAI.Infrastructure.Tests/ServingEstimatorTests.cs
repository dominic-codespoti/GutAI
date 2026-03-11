using FluentAssertions;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class ServingEstimatorTests
{
    // ════════════════════════════════════════════════════════
    //  WeightUnitToGrams
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("g", 1)]
    [InlineData("gram", 1)]
    [InlineData("grams", 1)]
    [InlineData("kg", 1000)]
    [InlineData("kilogram", 1000)]
    [InlineData("oz", 28.35)]
    [InlineData("ounce", 28.35)]
    [InlineData("ounces", 28.35)]
    [InlineData("lb", 453.6)]
    [InlineData("lbs", 453.6)]
    [InlineData("pound", 453.6)]
    [InlineData("mg", 0.001)]
    public void WeightUnitToGrams_ReturnsExpected(string unit, decimal expected)
    {
        ServingEstimator.WeightUnitToGrams(unit).Should().Be(expected);
    }

    [Theory]
    [InlineData("g", true)]
    [InlineData("kg", true)]
    [InlineData("oz", true)]
    [InlineData("lb", true)]
    [InlineData("mg", true)]
    [InlineData("cup", false)]
    [InlineData("glass", false)]
    [InlineData("slice", false)]
    [InlineData("banana", false)]
    public void IsWeightUnit_ClassifiesCorrectly(string unit, bool expected)
    {
        ServingEstimator.IsWeightUnit(unit).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════
    //  VolumeUnitToGrams
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("glass", "juice", 240)]
    [InlineData("glasses", "water", 240)]
    [InlineData("bowl", "soup", 300)]
    [InlineData("bowls", "cereal", 300)]
    [InlineData("tbsp", "honey", 15)]
    [InlineData("tablespoon", "oil", 15)]
    [InlineData("tsp", "sugar", 5)]
    [InlineData("teaspoon", "salt", 5)]
    [InlineData("ml", "water", 1)]
    [InlineData("l", "milk", 1000)]
    [InlineData("litre", "juice", 1000)]
    [InlineData("pint", "beer", 473)]
    [InlineData("fl oz", "water", 30)]
    [InlineData("gallon", "milk", 3785)]
    public void VolumeUnitToGrams_ReturnsExpected(string unit, string food, decimal expected)
    {
        ServingEstimator.VolumeUnitToGrams(unit, food).Should().Be(expected);
    }

    [Theory]
    [InlineData("cup", true)]
    [InlineData("glass", true)]
    [InlineData("glasses", true)]
    [InlineData("bowl", true)]
    [InlineData("ml", true)]
    [InlineData("l", true)]
    [InlineData("tbsp", true)]
    [InlineData("pint", true)]
    [InlineData("slice", false)]
    [InlineData("piece", false)]
    [InlineData("g", false)]
    public void IsVolumeUnit_ClassifiesCorrectly(string unit, bool expected)
    {
        ServingEstimator.IsVolumeUnit(unit).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════
    //  IsCountUnit
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("slice", true)]
    [InlineData("slices", true)]
    [InlineData("piece", true)]
    [InlineData("serving", true)]
    [InlineData("can", true)]
    [InlineData("bottle", true)]
    [InlineData("fillet", true)]
    [InlineData("rasher", true)]
    [InlineData("wing", true)]
    [InlineData("breast", true)]
    [InlineData("clove", true)]
    [InlineData("wedge", true)]
    [InlineData("cup", false)]
    [InlineData("g", false)]
    [InlineData("glass", false)]
    public void IsCountUnit_ClassifiesCorrectly(string unit, bool expected)
    {
        ServingEstimator.IsCountUnit(unit).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════
    //  EstimateCupWeightG (food-aware)
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("rice", 185)]
    [InlineData("pasta", 185)]
    [InlineData("milk", 240)]
    [InlineData("juice", 240)]
    [InlineData("yogurt", 245)]
    [InlineData("flour", 125)]
    [InlineData("berries", 150)]
    [InlineData("spinach", 60)]
    [InlineData("beans", 180)]
    [InlineData("almonds", 140)]
    [InlineData("granola", 120)]
    [InlineData("honey", 340)]
    [InlineData("olive oil", 220)]
    [InlineData("ice cream", 140)]
    [InlineData("soup", 240)]
    [InlineData("unknown food", 150)] // default
    public void EstimateCupWeightG_ReturnsExpected(string food, decimal expected)
    {
        ServingEstimator.EstimateCupWeightG(food).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════
    //  EstimateDefaultServingG
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("orange juice", 330)]
    [InlineData("egg", 50)]
    [InlineData("banana", 120)]
    [InlineData("chicken breast", 140)]
    [InlineData("bread", 30)]
    [InlineData("pizza", 110)]
    [InlineData("coffee", 240)]
    [InlineData("beer", 355)]
    [InlineData("wine", 150)]
    [InlineData("smoothie", 350)]
    [InlineData("unknown", 100)] // default
    public void EstimateDefaultServingG_ReturnsExpected(string food, decimal expected)
    {
        ServingEstimator.EstimateDefaultServingG(food).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════
    //  EstimateUnitWeightG (dispatcher)
    // ════════════════════════════════════════════════════════

    [Fact]
    public void EstimateUnitWeightG_WeightUnit_ReturnsGrams()
    {
        ServingEstimator.EstimateUnitWeightG(null, "oz", "chicken").Should().Be(28.35m);
    }

    [Fact]
    public void EstimateUnitWeightG_VolumeUnit_ReturnsGrams()
    {
        ServingEstimator.EstimateUnitWeightG(null, "glass", "juice").Should().Be(240m);
    }

    [Fact]
    public void EstimateUnitWeightG_CountUnit_UsesProductServingQty()
    {
        ServingEstimator.EstimateUnitWeightG(45m, "slice", "bread").Should().Be(45m);
    }

    [Fact]
    public void EstimateUnitWeightG_CountUnit_FallsBackToDefaultServing()
    {
        ServingEstimator.EstimateUnitWeightG(null, "piece", "pizza").Should().Be(110m);
    }

    [Fact]
    public void EstimateUnitWeightG_NoUnit_UsesProductServingQty()
    {
        ServingEstimator.EstimateUnitWeightG(85m, "", "toast").Should().Be(85m);
    }

    [Fact]
    public void EstimateUnitWeightG_NoUnit_NoProduct_FallsBackToDefault()
    {
        ServingEstimator.EstimateUnitWeightG(null, "", "banana").Should().Be(120m);
    }

    // ════════════════════════════════════════════════════════
    //  ExtractSizeMultiplier
    // ════════════════════════════════════════════════════════

    [Fact]
    public void ExtractSizeMultiplier_Large_Returns1_3()
    {
        var food = "large coffee";
        ServingEstimator.ExtractSizeMultiplier(ref food).Should().Be(1.3m);
        food.Should().Be("coffee");
    }

    [Fact]
    public void ExtractSizeMultiplier_Small_Returns0_7()
    {
        var food = "small fries";
        ServingEstimator.ExtractSizeMultiplier(ref food).Should().Be(0.7m);
        food.Should().Be("fries");
    }

    [Fact]
    public void ExtractSizeMultiplier_ExtraLarge_Returns1_5()
    {
        var food = "extra large pizza";
        ServingEstimator.ExtractSizeMultiplier(ref food).Should().Be(1.5m);
        food.Should().Be("pizza");
    }

    [Fact]
    public void ExtractSizeMultiplier_NoModifier_Returns1()
    {
        var food = "chicken";
        ServingEstimator.ExtractSizeMultiplier(ref food).Should().Be(1m);
        food.Should().Be("chicken");
    }

    // ════════════════════════════════════════════════════════
    //  EstimateUnitGrams (no product context)
    // ════════════════════════════════════════════════════════

    [Fact]
    public void EstimateUnitGrams_Glass_Returns240()
    {
        ServingEstimator.EstimateUnitGrams("glass", "juice").Should().Be(240m);
    }

    [Fact]
    public void EstimateUnitGrams_Bowl_Returns300()
    {
        ServingEstimator.EstimateUnitGrams("bowl", "soup").Should().Be(300m);
    }

    [Fact]
    public void EstimateUnitGrams_Cup_FoodAware()
    {
        ServingEstimator.EstimateUnitGrams("cup", "rice").Should().Be(185m);
        ServingEstimator.EstimateUnitGrams("cup", "milk").Should().Be(240m);
    }

    [Fact]
    public void EstimateUnitGrams_Gram_Returns1()
    {
        ServingEstimator.EstimateUnitGrams("g", "anything").Should().Be(1m);
    }

    [Fact]
    public void EstimateUnitGrams_Slice_ReturnsDefaultServing()
    {
        // Count unit → uses EstimateDefaultServingG for the food
        ServingEstimator.EstimateUnitGrams("slice", "bread").Should().Be(30m);
    }

    [Fact]
    public void EstimateUnitGrams_Empty_ReturnsNull()
    {
        ServingEstimator.EstimateUnitGrams("", "food").Should().BeNull();
    }

    [Fact]
    public void EstimateUnitGrams_Unknown_ReturnsNull()
    {
        ServingEstimator.EstimateUnitGrams("bloop", "food").Should().BeNull();
    }

    // ════════════════════════════════════════════════════════
    //  FormatUnitLabel
    // ════════════════════════════════════════════════════════

    [Fact]
    public void FormatUnitLabel_Glass_ShowsGrams()
    {
        ServingEstimator.FormatUnitLabel(1, "glass", "juice").Should().Be("1 glass (240g)");
    }

    [Fact]
    public void FormatUnitLabel_TwoCups_ShowsGrams()
    {
        ServingEstimator.FormatUnitLabel(2, "cup", "rice").Should().Be("2 cup (185g)");
    }

    [Fact]
    public void FormatUnitLabel_NoUnit_ReturnsNull()
    {
        ServingEstimator.FormatUnitLabel(1, "", "food").Should().BeNull();
    }
}
