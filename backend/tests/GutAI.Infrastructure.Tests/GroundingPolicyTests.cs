using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class GroundingPolicyTests
{
    private static FoodProductDto Product(string name, decimal confidence = 0.9m, decimal? calories = 150m) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DataSource = "USDA",
        MatchConfidence = confidence,
        Calories100g = calories,
        Protein100g = 25m,
        Carbs100g = 0m,
        Fat100g = 5m,
    };

    private static FoodResolutionDto Resolution(FoodResolutionStatus status, FoodProductDto? selected, decimal confidence,
        params FoodProductDto[] alternatives) => new()
        {
            Status = status,
            Selected = selected,
            MatchConfidence = confidence,
            Alternatives = alternatives,
        };

    [Fact]
    public void ExactAtConfidenceFloor_AutoSelects()
    {
        var candidate = Product("Chicken breast", 0.85m);

        var decision = GroundingPolicy.Decide(Resolution(FoodResolutionStatus.Exact, candidate, 0.85m));

        decision.AutoSelected.Should().BeTrue();
        decision.Selected.Should().Be(candidate);
        decision.Reason.Should().Be(GroundingPolicy.Reasons.AutoSelected);
    }

    [Fact]
    public void ProbableBelowConfidenceFloor_AbstainsWithCandidates()
    {
        var candidate = Product("Chicken breast", 0.84m);

        var decision = GroundingPolicy.Decide(Resolution(FoodResolutionStatus.Probable, candidate, 0.84m));

        decision.AutoSelected.Should().BeFalse();
        decision.Selected.Should().BeNull();
        decision.Reason.Should().Be(GroundingPolicy.Reasons.BelowConfidenceFloor);
        decision.Candidates.Should().Contain(candidate);
    }

    [Fact]
    public void Ambiguous_RanksAtMostThreeCandidatesByConfidence()
    {
        var candidates = Enumerable.Range(1, 4).Select(i => Product($"Food {i}", i / 10m)).ToArray();

        var decision = GroundingPolicy.Decide(Resolution(FoodResolutionStatus.Ambiguous, candidates[0], 0.1m, candidates[1..]));

        decision.Reason.Should().Be(GroundingPolicy.Reasons.StatusNotConfident);
        decision.Candidates.Should().ContainInOrder(candidates[3], candidates[2], candidates[1]);
        decision.Candidates.Should().HaveCount(3);
    }

    [Fact]
    public void MissingCalories_Abstains()
    {
        var candidate = Product("Chicken", calories: null);

        var decision = GroundingPolicy.Decide(Resolution(FoodResolutionStatus.Exact, candidate, 0.95m));

        decision.Reason.Should().Be(GroundingPolicy.Reasons.MissingCalories);
        decision.AutoSelected.Should().BeFalse();
    }

    [Fact]
    public void KiloJoulesAsCalories_IsNotAutoSelectedAndCarriesQualityFlag()
    {
        var candidate = Product("Food", 0.95m, 1550m) with
        {
            Protein100g = 10m,
            Carbs100g = 60m,
            Fat100g = 12m,
        };

        var decision = GroundingPolicy.Decide(Resolution(FoodResolutionStatus.Exact, candidate, 0.95m));

        decision.Reason.Should().Be(GroundingPolicy.Reasons.ImplausibleNutrition);
        decision.AutoSelected.Should().BeFalse();
        GroundingPolicy.DataQualityFlags(candidate).Should().Contain(NutritionSanity.Codes.EnergyLooksLikeKj);
    }

    [Fact]
    public void CompatibilityMismatch_VetoesAutoSelection()
    {
        var snack = Product("Beef Sausage Snack Pieces", 0.95m) with
        {
            FoodKind = FoodKind.Branded,
            Brand = "Example Brand",
        };
        var observation = new ScannedComponent
        {
            Name = "sausage",
            SearchQueries = ["cooked sausage"],
            PreparationNote = "appears cooked",
        };

        var decision = GroundingPolicy.Decide(Resolution(FoodResolutionStatus.Probable, snack, 0.95m), observation);

        decision.Reason.Should().Be(GroundingPolicy.Reasons.CompatibilityVeto);
        decision.AutoSelected.Should().BeFalse();
    }

    [Fact]
    public void FoodFormMismatch_VetoesAutoSelection()
    {
        var juice = Product("Blueberry Juice", 0.95m);
        var observation = new ScannedComponent { Name = "raw blueberries" };

        var decision = GroundingPolicy.Decide(Resolution(FoodResolutionStatus.Exact, juice, 0.95m), observation);

        decision.Reason.Should().Be(GroundingPolicy.Reasons.FoodFormVeto);
        decision.AutoSelected.Should().BeFalse();
    }
    [Fact]
    public void RankCandidates_UsesConfidenceWithoutObservationAndCompatibilityWithObservation()
    {
        var low = Product("Blueberries, wild, raw", 0.7m);
        var high = Product("Blueberry Snack Bar", 0.75m);

        GroundingPolicy.RankCandidates([low, high], null).Should().ContainInOrder(high, low);
        GroundingPolicy.RankCandidates([high, low], new ScannedComponent { Name = "raw blueberries" })
            .Should().ContainInOrder(low, high);
    }
}
