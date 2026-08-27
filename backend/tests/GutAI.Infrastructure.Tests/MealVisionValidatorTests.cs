using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class MealVisionValidatorTests
{
    private static ScannedComponent ValidComponent(
        string name = "grilled chicken",
        decimal low = 80m, decimal mid = 120m, decimal high = 160m, decimal conf = 0.9m)
        => new()
        {
            Name = name,
            EstimatedGramsLow = low,
            EstimatedGramsMidpoint = mid,
            EstimatedGramsHigh = high,
            Confidence = conf,
            PreparationNote = "",
        };

    [Fact]
    public void Validate_ValidSingleComponent_PassesThrough()
    {
        var result = MealVisionValidator.Validate(new MealVisionResult
        {
            Components = [ValidComponent()],
            ReferenceObjectVisible = true,
            OverallConfidence = 0.85m,
        }, maxComponents: 12);

        result.Components.Should().HaveCount(1);
        result.Components[0].Name.Should().Be("grilled chicken");
        result.DroppedNotes.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ServingHintWithinBounds_IsPreserved()
    {
        var component = ValidComponent();
        component.ServingHintUnit = "large egg";
        component.ServingHintUnitPlural = "large eggs";
        component.ServingHintUnitGrams = 50m;

        var result = MealVisionValidator.Validate(
            new MealVisionResult { Components = [component] }, maxComponents: 12);

        result.Components[0].ServingHintUnit.Should().Be("large egg");
        result.Components[0].ServingHintUnitPlural.Should().Be("large eggs");
        result.Components[0].ServingHintUnitGrams.Should().Be(50m);
    }

    [Fact]
    public void Validate_InvalidServingHint_IsCleared()
    {
        var component = ValidComponent();
        component.ServingHintUnit = "large egg";
        component.ServingHintUnitPlural = "large eggs";
        component.ServingHintUnitGrams = 5000m;

        var result = MealVisionValidator.Validate(
            new MealVisionResult { Components = [component] }, maxComponents: 12);

        result.Components[0].ServingHintUnit.Should().BeEmpty();
        result.Components[0].ServingHintUnitPlural.Should().BeEmpty();
        result.Components[0].ServingHintUnitGrams.Should().Be(0m);
    }

    [Fact]
    public void Validate_EmptyComponents_Throws()
    {
        var act = () => MealVisionValidator.Validate(new MealVisionResult(), maxComponents: 12);
        act.Should().Throw<MealScanValidationException>();
    }

    [Fact]
    public void validate_MidpointOutsideRange_Dropped()
    {
        var result = MealVisionValidator.Validate(new MealVisionResult
        {
            Components =
            [
                ValidComponent(name: "bad range", low: 150m, mid: 100m, high: 160m),
                ValidComponent(),
            ],
        }, maxComponents: 12);

        result.Components.Should().ContainSingle("only the well-ordered component survives");
        result.DroppedNotes.Should().Contain(n => n.Contains("implausible portion range"));
    }

    [Fact]
    public void Validate_NegativeGrams_AllInvalid_Throws()
    {
        var act = () => MealVisionValidator.Validate(new MealVisionResult
        {
            Components = [ValidComponent(low: -10m)],
        }, maxComponents: 12);

        act.Should().Throw<MealScanValidationException>();
    }

    [Fact]
    public void Validate_UnnamedComponent_Dropped()
    {
        var result = MealVisionValidator.Validate(new MealVisionResult
        {
            Components = [ValidComponent(name: "   "), ValidComponent()],
        }, maxComponents: 12);

        result.Components.Should().ContainSingle();
        result.DroppedNotes.Should().Contain(n => n.Contains("Unnamed component"));
    }

    [Fact]
    public void Validate_OverFiveKilograms_Dropped()
    {
        var result = MealVisionValidator.Validate(new MealVisionResult
        {
            Components = [ValidComponent(high: 6000m, mid: 5500m, low: 5000m), ValidComponent()],
        }, maxComponents: 12);

        result.Components.Should().ContainSingle();
        result.DroppedNotes.Should().Contain(n => n.Contains("5 kg"));
    }

    [Fact]
    public void Validate_ConfidenceOutOfRange_ClampedNotDropped()
    {
        var result = MealVisionValidator.Validate(new MealVisionResult
        {
            Components = [ValidComponent(conf: 1.7m)],
        }, maxComponents: 12);

        result.Components.Should().ContainSingle();
        result.Components[0].Confidence.Should().Be(1m);
    }

    [Fact]
    public void Validate_MoreThanMaxComponents_KeepsFirstNAndWarns()
    {
        var components = Enumerable.Range(0, 20)
            .Select(i => ValidComponent(name: $"food {i}"))
            .ToList();

        var result = MealVisionValidator.Validate(new MealVisionResult
        {
            Components = components,
        }, maxComponents: 12);

        result.Components.Should().HaveCount(12);
        result.DroppedNotes.Should().Contain(n => n.Contains("Component limit"));
    }

    [Fact]
    public void Validate_AllComponentsInvalid_Throws()
    {
        var act = () => MealVisionValidator.Validate(new MealVisionResult
        {
            Components = [ValidComponent(name: ""), ValidComponent(low: 300m, mid: 100m, high: 200m)],
        }, maxComponents: 12);

        act.Should().Throw<MealScanValidationException>()
            .WithMessage("*none had usable*");
    }
}
