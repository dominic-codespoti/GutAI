using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class NutritionCalculatorTests
{
    public static TheoryData<NutritionPer100gDto, decimal, NutritionAmountsDto> Fixtures => new()
    {
        {
            new NutritionPer100gDto
            {
                CaloriesKcal = 155.5m, ProteinG = 20.25m, CarbsG = 30.15m, FatG = 4.05m,
                FiberG = 2.25m, SugarG = 5.05m, SodiumMg = 123.5m,
            },
            100m,
            new NutritionAmountsDto
            {
                Calories = 156m, ProteinG = 20.3m, CarbsG = 30.2m, FatG = 4.1m,
                FiberG = 2.3m, SugarG = 5.1m, SodiumMg = 124m,
            }
        },
        {
            new NutritionPer100gDto
            {
                CaloriesKcal = 0.5m, ProteinG = 2.25m, CarbsG = 1.05m, FatG = 0.05m,
                FiberG = 0.15m, SugarG = 0.05m, SodiumMg = 0.5m,
            },
            100m,
            new NutritionAmountsDto
            {
                Calories = 1m, ProteinG = 2.3m, CarbsG = 1.1m, FatG = 0.1m,
                FiberG = 0.2m, SugarG = 0.1m, SodiumMg = 1m,
            }
        },
        {
            new NutritionPer100gDto
            {
                CaloriesKcal = 203.4m, ProteinG = 7.36m, CarbsG = 18.24m, FatG = 9.98m,
                FiberG = null, SugarG = 3.18m, SodiumMg = null,
            },
            37.5m,
            new NutritionAmountsDto
            {
                Calories = 76m, ProteinG = 2.8m, CarbsG = 6.8m, FatG = 3.7m,
                FiberG = null, SugarG = 1.2m, SodiumMg = null,
            }
        },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Compute_matches_shared_cross_platform_fixtures(
        NutritionPer100gDto basis, decimal grams, NutritionAmountsDto expected)
    {
        NutritionCalculator.Compute(basis, grams).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Compute_is_linear_in_grams_within_rounding()
    {
        var basis = new NutritionPer100gDto
        {
            CaloriesKcal = 240m,
            ProteinG = 17.3m,
            CarbsG = 28.7m,
            FatG = 8.4m,
        };

        var half = NutritionCalculator.Compute(basis, 50m);
        var whole = NutritionCalculator.Compute(basis, 100m);

        half.Calories.Should().Be(whole.Calories / 2m);
        half.ProteinG.Should().BeApproximately(whole.ProteinG / 2m, 0.1m);
        half.CarbsG.Should().BeApproximately(whole.CarbsG / 2m, 0.1m);
        half.FatG.Should().BeApproximately(whole.FatG / 2m, 0.1m);
    }

    [Fact]
    public void Compute_preserves_null_optional_nutrients()
    {
        var result = NutritionCalculator.Compute(new NutritionPer100gDto { CaloriesKcal = 80m }, 50m);

        result.FiberG.Should().BeNull();
        result.SugarG.Should().BeNull();
        result.SodiumMg.Should().BeNull();
    }

    [Fact]
    public void Compute_rejects_negative_grams()
    {
        var act = () => NutritionCalculator.Compute(new NutritionPer100gDto { CaloriesKcal = 80m }, -1m);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("grams");
    }

    [Fact]
    public void Sum_keeps_optional_nutrients_null_until_a_part_supplies_a_value()
    {
        var allNull = NutritionCalculator.Sum([new NutritionAmountsDto(), new NutritionAmountsDto()]);
        var onePresent = NutritionCalculator.Sum(
        [
            new NutritionAmountsDto { Calories = 2m, FiberG = 1m, SugarG = null, SodiumMg = 0m },
            new NutritionAmountsDto { Calories = 3m, FiberG = null, SugarG = null, SodiumMg = null },
        ]);

        allNull.FiberG.Should().BeNull();
        allNull.SugarG.Should().BeNull();
        allNull.SodiumMg.Should().BeNull();
        onePresent.Calories.Should().Be(5m);
        onePresent.FiberG.Should().Be(1m);
        onePresent.SugarG.Should().BeNull();
        onePresent.SodiumMg.Should().Be(0m);
    }

    [Fact]
    public void BasisFrom_returns_null_when_calories_are_missing()
    {
        NutritionCalculator.BasisFrom(new FoodProductDto { Name = "Unknown food" }).Should().BeNull();
    }
}
