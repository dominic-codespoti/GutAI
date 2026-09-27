using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class NutritionSanityTests
{
    private static NutritionPer100gDto Basis(
        decimal kcal = 100m,
        decimal protein = 10m,
        decimal carbs = 10m,
        decimal fat = 4m,
        decimal? fiber = null,
        decimal? sugar = null,
        decimal? sodium = null) => new()
        {
            CaloriesKcal = kcal,
            ProteinG = protein,
            CarbsG = carbs,
            FatG = fat,
            FiberG = fiber,
            SugarG = sugar,
            SodiumMg = sodium,
        };

    [Fact]
    public void Catalog_detects_kilojoules_mislabeled_as_kilocalories()
    {
        var result = NutritionSanity.Check(Basis(kcal: 1550m, protein: 10m, carbs: 60m, fat: 12m));

        result.Codes.Should().Contain(NutritionSanity.Codes.EnergyLooksLikeKj);
        result.HasHardViolation.Should().BeTrue();
        result.Codes.Should().NotContain(NutritionSanity.Codes.AtwaterMismatch);
    }

    [Fact]
    public void Web_profile_rejects_mixed_rows_and_salt_while_catalog_accepts_salt()
    {
        var mixedRows = NutritionSanity.Check(
            Basis(kcal: 52m, protein: 20m, carbs: 40m, fat: 30m), NutritionSanityProfile.Web);
        var salt = Basis(kcal: 0m, protein: 0m, carbs: 0m, fat: 0m, sodium: 38758m);

        mixedRows.IsPlausible.Should().BeFalse();
        mixedRows.HasHardViolation.Should().BeTrue();
        NutritionSanity.Check(salt).IsPlausible.Should().BeTrue();
        NutritionSanity.Check(salt, NutritionSanityProfile.Web).IsPlausible.Should().BeFalse();
    }

    [Fact]
    public void Catalog_exempts_wine_vinegar_and_zero_calorie_diet_soda_from_atwater_mismatch()
    {
        NutritionSanity.Check(Basis(kcal: 83m, protein: 0m, carbs: 2.6m, fat: 0m), foodName: "Red wine")
            .IsPlausible.Should().BeTrue();
        NutritionSanity.Check(Basis(kcal: 18m, protein: 0m, carbs: 0m, fat: 0m), foodName: "Vinegar")
            .IsPlausible.Should().BeTrue();
        NutritionSanity.Check(Basis(kcal: 0m, protein: 0m, carbs: 0m, fat: 0m), foodName: "Diet soda")
            .IsPlausible.Should().BeTrue();
    }

    [Theory]
    [InlineData(100, 10, 10, 4, "", true)]
    [InlineData(100, 10, 10, 4, "protein powder", true)]
    [InlineData(100, 0, 30, 0, "", true)]
    public void Catalog_accepts_plausible_macro_profiles(decimal kcal, decimal protein, decimal carbs, decimal fat, string name, bool expected)
    {
        NutritionSanity.Check(Basis(kcal, protein, carbs, fat), foodName: name).IsPlausible.Should().Be(expected);
    }

    [Fact]
    public void Catalog_reports_soft_sugar_issue_and_hard_macro_mass_issue()
    {
        var excessSugar = NutritionSanity.Check(Basis(kcal: 100m, protein: 0m, carbs: 10m, fat: 0m, sugar: 16m));
        excessSugar.Codes.Should().Contain(NutritionSanity.Codes.SugarExceedsCarbs);
        excessSugar.HasHardViolation.Should().BeFalse();

        var excessMass = NutritionSanity.Check(Basis(kcal: 500m, protein: 40m, carbs: 40m, fat: 23m));
        excessMass.Codes.Should().Contain(NutritionSanity.Codes.MacroMassExceedsTotal);
        excessMass.HasHardViolation.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.9, false)]
    [InlineData(1, true)]
    [InlineData(900, true)]
    [InlineData(901, false)]
    public void Web_energy_limits_match_cascade(decimal kcal, bool expected)
    {
        NutritionSanity.Check(Basis(kcal, protein: 0m, carbs: 0m, fat: 0m), NutritionSanityProfile.Web)
            .IsPlausible.Should().Be(expected);
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(59, false)]
    [InlineData(140, true)]
    [InlineData(141, false)]
    public void Web_atwater_ratio_boundaries_match_cascade(decimal macroKcal, bool expected)
    {
        // Protein contributes exactly 4 kcal per gram; keep it in the web range.
        NutritionSanity.Check(Basis(kcal: 100m, protein: macroKcal / 4m, carbs: 0m, fat: 0m), NutritionSanityProfile.Web)
            .IsPlausible.Should().Be(expected);
    }

    [Fact]
    public void CheckPortion_scales_by_grams_and_applies_catalog_rules()
    {
        var per100gEquivalent = NutritionSanity.Check(Basis(kcal: 1550m, protein: 10m, carbs: 60m, fat: 12m));
        var portion = new NutritionAmountsDto
        {
            Calories = 310m,
            ProteinG = 2m,
            CarbsG = 12m,
            FatG = 2.4m,
        };

        NutritionSanity.CheckPortion(portion, 20m).Codes.Should().Contain(NutritionSanity.Codes.EnergyLooksLikeKj);
        per100gEquivalent.Codes.Should().Contain(NutritionSanity.Codes.EnergyLooksLikeKj);
    }

    [Theory]
    [InlineData(15, false)]
    [InlineData(15.01, true)]
    public void Fiber_above_carbohydrates_uses_profile_tolerance(decimal fiber, bool exceedsTolerance)
    {
        foreach (var profile in new[] { NutritionSanityProfile.Catalog, NutritionSanityProfile.Web })
        {
            var result = NutritionSanity.Check(
                Basis(kcal: 100m, protein: 10m, carbs: 10m, fat: 4m, fiber: fiber), profile);

            result.Codes.Contains(NutritionSanity.Codes.FiberExceedsCarbs).Should().Be(exceedsTolerance);
        }
    }

    [Fact]
    public void Catalog_rejects_negative_nutrition_values()
    {
        var negative = NutritionSanity.Check(Basis(kcal: -1m));

        negative.Codes.Should().Contain(NutritionSanity.Codes.NegativeValue);
        negative.HasHardViolation.Should().BeTrue();
    }

    [Fact]
    public void Atwater_tolerance_boundaries_are_profile_data_and_customizable()
    {
        NutritionSanity.Check(Basis(kcal: 100m, protein: 15m, carbs: 0m, fat: 0m), NutritionSanityProfile.Catalog)
            .Codes.Should().NotContain(NutritionSanity.Codes.AtwaterMismatch);
        NutritionSanity.Check(Basis(kcal: 100m, protein: 35m, carbs: 0m, fat: 0m), NutritionSanityProfile.Catalog)
            .Codes.Should().NotContain(NutritionSanity.Codes.AtwaterMismatch);
        NutritionSanity.Check(Basis(kcal: 100m, protein: 15m, carbs: 0m, fat: 0m), NutritionSanityProfile.Web)
            .Codes.Should().NotContain(NutritionSanity.Codes.AtwaterMismatch);
        NutritionSanity.Check(Basis(kcal: 100m, protein: 35m, carbs: 0m, fat: 0m), NutritionSanityProfile.Web)
            .Codes.Should().NotContain(NutritionSanity.Codes.AtwaterMismatch);

        var custom = NutritionSanityProfile.Web with { AtwaterTolerance = 0.2m };
        NutritionSanity.Check(Basis(kcal: 100m, protein: 20m, carbs: 0m, fat: 0m), custom)
            .Codes.Should().NotContain(NutritionSanity.Codes.AtwaterMismatch);
        NutritionSanity.Check(Basis(kcal: 100m, protein: 31.25m, carbs: 0m, fat: 0m), custom)
            .Codes.Should().Contain(NutritionSanity.Codes.AtwaterMismatch);
    }

    [Theory]
    [InlineData("beer")]
    [InlineData("wine")]
    [InlineData("xylitol sweetened candy")]
    public void Web_profile_applies_atwater_exemptions(string exemptName)
    {
        var mismatched = Basis(kcal: 100m, protein: 50m, carbs: 0m, fat: 0m);

        NutritionSanity.Check(mismatched, NutritionSanityProfile.Web, exemptName).Codes
            .Should().NotContain(NutritionSanity.Codes.AtwaterMismatch);
        NutritionSanity.Check(mismatched, NutritionSanityProfile.Web, "plain drink").Codes
            .Should().Contain(NutritionSanity.Codes.AtwaterMismatch);
    }

    [Fact]
    public void CheckPortion_without_grams_runs_negative_and_ratio_checks_on_portion_values()
    {
        var mismatch = NutritionSanity.CheckPortion(new NutritionAmountsDto
        {
            Calories = 400m,
            ProteinG = 2m,
            CarbsG = 5m,
            FatG = 1m,
        }, grams: null);
        var negative = NutritionSanity.CheckPortion(new NutritionAmountsDto { Calories = -1m }, grams: 0m);

        mismatch.Codes.Should().Contain(NutritionSanity.Codes.AtwaterMismatch);
        negative.Codes.Should().ContainSingle().Which.Should().Be(NutritionSanity.Codes.NegativeValue);
        negative.HasHardViolation.Should().BeTrue();
    }
}
