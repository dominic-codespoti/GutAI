using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class PortionCalibratorTests
{
    [Fact]
    public void Disabled_calibration_returns_same_item_and_has_no_version()
    {
        var item = CreateItem();
        var calibrator = CreateCalibrator(enabled: false, Factor("starch", "any", 1.5m));

        calibrator.Version.Should().BeNull();
        calibrator.Apply(item).Should().BeSameAs(item);
    }

    [Fact]
    public void Enabled_calibration_without_version_does_not_apply_factors()
    {
        var item = CreateItem(grams: 100m);
        var calibrator = CreateCalibratorWithVersion(enabled: true, version: null, Factor("starch", "any", 1.5m));

        calibrator.Version.Should().BeNull();
        calibrator.Apply(item).Should().BeSameAs(item);
        calibrator.Apply(item).Grams.Should().Be(100m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Enabled_calibration_with_blank_version_does_not_apply_factors(string? version)
    {
        var item = CreateItem(grams: 100m);
        var calibrator = CreateCalibratorWithVersion(enabled: true, version, Factor("starch", "any", 1.5m));

        calibrator.Version.Should().BeNull();
        calibrator.Apply(item).Grams.Should().Be(100m);
    }

    [Fact]
    public void Disabled_calibration_with_version_and_factors_does_not_apply()
    {
        var item = CreateItem(grams: 100m);
        var calibrator = CreateCalibratorWithVersion(enabled: false, version: "portion-v1", Factor("starch", "any", 1.5m));

        calibrator.Version.Should().BeNull();
        calibrator.Apply(item).Should().BeSameAs(item);
    }

    [Fact]
    public void Apply_clamps_calibrated_grams_to_the_high_bound_and_recomputes_nutrition()
    {
        var item = CreateItem(grams: 100m, low: 80m, high: 120m);
        var calibrator = CreateCalibrator(enabled: true, Factor("starch", "high", 2m));

        var result = calibrator.Apply(item);

        result.Should().NotBeSameAs(item);
        result.Grams.Should().Be(120m);
        result.PortionMethod.Should().Be("vision_estimate_calibrated");
        result.PortionLowGrams.Should().Be(80m);
        result.PortionHighGrams.Should().Be(120m);
        var expected = NutritionCalculator.Compute(item.Per100g!, 120m);
        result.Calories.Should().Be(expected.Calories);
        result.ProteinG.Should().Be(expected.ProteinG);
        result.CarbsG.Should().Be(expected.CarbsG);
        result.FatG.Should().Be(expected.FatG);
        result.FiberG.Should().Be(expected.FiberG);
        result.SugarG.Should().Be(expected.SugarG);
        result.SodiumMg.Should().Be(expected.SodiumMg);
    }

    [Fact]
    public void Apply_clamps_calibrated_grams_to_the_low_bound()
    {
        var item = CreateItem(grams: 100m, low: 80m, high: 140m);
        var calibrator = CreateCalibrator(enabled: true, Factor("starch", "high", 0.5m));

        calibrator.Apply(item).Grams.Should().Be(80m);
    }

    [Fact]
    public void Apply_prefers_tier_specific_factor_over_any_factor()
    {
        var item = CreateItem(name: "orange juice", grams: 100m, confidence: 0.2m);
        var calibrator = CreateCalibrator(enabled: true,
            Factor("beverage", "any", 0.5m),
            Factor("beverage", "low", 1.5m));

        calibrator.Apply(item).Grams.Should().Be(150m);
    }

    [Fact]
    public void Out_of_range_factor_is_ignored_and_does_not_set_version()
    {
        var item = CreateItem();
        var calibrator = CreateCalibrator(enabled: true, Factor("starch", "any", 2.01m));

        calibrator.Version.Should().BeNull();
        calibrator.Apply(item).Should().BeSameAs(item);
    }

    [Fact]
    public void Valid_factor_exposes_configured_version_and_applies_factor()
    {
        var item = CreateItem(grams: 100m);
        var calibrator = CreateCalibratorWithVersion(enabled: true, version: "portion-v1", Factor("starch", "any", 1.1m));

        calibrator.Version.Should().Be("portion-v1");
        calibrator.Apply(item).Grams.Should().Be(110m);
    }

    private static MealDraftItemDto CreateItem(
        string name = "rice",
        decimal grams = 100m,
        decimal? low = null,
        decimal? high = null,
        decimal confidence = 0.8m) => new()
        {
            ItemId = Guid.NewGuid(),
            Name = name,
            Source = "usda",
            Grams = grams,
            PortionLowGrams = low,
            PortionHighGrams = high,
            PortionConfidence = confidence,
            MatchConfidence = 0.95m,
            Per100g = new NutritionPer100gDto
            {
                CaloriesKcal = 130m,
                ProteinG = 2.7m,
                CarbsG = 28.2m,
                FatG = 0.3m,
                FiberG = 0.4m,
                SugarG = 0.1m,
                SodiumMg = 1m
            }
        };

    private static Dictionary<string, string?> Factor(string foodClass, string tier, decimal factor) => new()
    {
        [$"FoodClass"] = foodClass,
        [$"Tier"] = tier,
        [$"Factor"] = factor.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static PortionCalibrator CreateCalibrator(bool enabled, params Dictionary<string, string?>[] factors) =>
        CreateCalibratorWithVersion(enabled, "portion-v1", factors);

    private static PortionCalibrator CreateCalibratorWithVersion(
        bool enabled,
        string? version,
        params Dictionary<string, string?>[] factors)
    {
        var values = new Dictionary<string, string?>
        {
            ["Features:PortionCalibration"] = enabled.ToString(),
            ["MealScan:PortionCalibration:Version"] = version
        };
        for (var index = 0; index < factors.Length; index++)
        {
            foreach (var (key, value) in factors[index])
                values[$"MealScan:PortionCalibration:Factors:{index}:{key}"] = value;
        }

        return new PortionCalibrator(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}
