using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodCandidateCompatibilityScorerTests
{
    private static ScannedComponent CookedObservation() => new()
    {
        Name = "sausage pieces",
        SearchQueries = ["cooked sausage"],
        PreparationNote = "appears cooked",
        EstimatedGramsLow = 40,
        EstimatedGramsMidpoint = 55,
        EstimatedGramsHigh = 80,
        Confidence = 0.8m,
    };

    private static FoodProductDto Product(string name, FoodKind kind = FoodKind.WholeFood, string? brand = null) => new()
    {
        Name = name,
        Brand = brand,
        FoodKind = kind,
        DataSource = "USDA",
        MatchConfidence = 0.85m,
        Calories100g = 250,
    };

    [Fact]
    public void Score_PenalizesRawCandidateForCookedObservation()
    {
        var raw = FoodCandidateCompatibilityScorer.Score(CookedObservation(), Product("Sausage, turkey, fresh, raw"));
        var cooked = FoodCandidateCompatibilityScorer.Score(CookedObservation(), Product("Sausage, cooked"));

        cooked.Should().BeGreaterThan(raw);
    }

    [Fact]
    public void Score_PenalizesUnrequestedBrand()
    {
        var generic = FoodCandidateCompatibilityScorer.Score(CookedObservation(), Product("Sausage, cooked"));
        var branded = FoodCandidateCompatibilityScorer.Score(CookedObservation(), Product("Breakfast Sausage", FoodKind.Branded, "Example Brand"));

        generic.Should().BeGreaterThan(branded);
    }

    [Fact]
    public void Score_PenalizesComplexCompositeCandidateForSimpleObservation()
    {
        var observation = new ScannedComponent
        {
            Name = "melted cheese",
            EstimatedGramsLow = 20,
            EstimatedGramsMidpoint = 30,
            EstimatedGramsHigh = 45,
            Confidence = 0.8m,
        };
        var simple = Product("Mozzarella cheese");
        var composite = Product(
            "Detroit style herbed mozzarella cheese blend pizza sauce pepperoni",
            FoodKind.Unknown);

        FoodCandidateCompatibilityScorer.Score(observation, simple)
            .Should().BeGreaterThan(FoodCandidateCompatibilityScorer.Score(observation, composite));
    }
}
