using System.Text.Json;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class GoldenMetricsTests
{
    private static ScannedComponent Scanned(string name, decimal mid) => new()
    {
        Name = name,
        EstimatedGramsLow = mid * 0.8m,
        EstimatedGramsMidpoint = mid,
        EstimatedGramsHigh = mid * 1.2m,
        Confidence = 0.9m,
        PreparationNote = "",
    };

    [Theory]
    [InlineData("grilled chicken breast", "chicken breast", true)]
    [InlineData("white rice", "rice", true)]
    [InlineData("mixed green salad", "salad", true)]
    [InlineData("greek yogurt", "yogurt", true)]          // substring fallback
    [InlineData("pizza", "pasta", false)]
    [InlineData("orange juice", "apple juice", false)]
    [InlineData("spaghetti", "spaghetti with tomato sauce", false)]  // scanned dropped detail — must NOT auto-credit
    // Plural morphology tests
    [InlineData("mixed berries", "mixed berry", true)]
    [InlineData("roasted vegetables", "roasted vegetable", true)]
    [InlineData("strawberries", "strawberry", true)]
    [InlineData("potatoes", "potato", true)]
    [InlineData("tomatoes", "tomato", true)]
    [InlineData("steamed mushrooms", "steamed mushroom", true)]
    // Alias families: salad greens
    [InlineData("mixed greens", "salad greens", true)]
    [InlineData("leafy greens", "salad greens", true)]
    [InlineData("leafy salad", "salad greens", true)]
    [InlineData("salad greens", "leafy greens", true)]
    [InlineData("green salad", "mixed greens", true)]
    // Alias families: queso
    [InlineData("cheese sauce", "queso", true)]
    [InlineData("queso dip", "queso", true)]
    [InlineData("cheese sauce", "queso dip", true)]
    // Alias families: smoothie
    [InlineData("fruit smoothie", "smoothie", true)]
    [InlineData("orange smoothie", "smoothie", true)]
    [InlineData("fruit smoothie", "orange smoothie", true)]
    // Alias families: mixed vegetables
    [InlineData("corn vegetable hash", "mixed vegetables", true)]
    [InlineData("mixed cooked vegetables", "mixed vegetables", true)]
    [InlineData("corn vegetable hash", "mixed cooked vegetables", true)]
    [InlineData("mixed vegetables", "corn vegetable hash", true)]
    public void MatchComponents_NameMatching(string scannedName, string expectedName, bool shouldMatch)
    {
        var expected = new List<GoldenExpected> { new() { Name = expectedName, Grams = 100m } };
        var scanned = new List<ScannedComponent> { Scanned(scannedName, 100m) };

        var matches = GoldenMetrics.MatchComponents(expected, scanned);

        matches.Should().HaveCount(shouldMatch ? 1 : 0);
    }

    [Fact]
    public void MatchComponents_EachScannedUsedAtMostOnce()
    {
        var expected = new List<GoldenExpected>
        {
            new() { Name = "rice", Grams = 200m },
            new() { Name = "rice", Grams = 100m },
        };
        var scanned = new List<ScannedComponent> { Scanned("steamed rice", 150m) };

        var matches = GoldenMetrics.MatchComponents(expected, scanned);

        matches.Should().HaveCount(1); // one scan can't satisfy two expectations
    }

    [Fact]
    public void GramErrorPercent_ComputesRelativeError()
    {
        var s = Scanned("rice", 250m);
        GoldenMetrics.GramErrorPercent(s, 200m).Should().BeApproximately(25.0, 0.01);
        GoldenMetrics.GramErrorPercent(s, 250m).Should().Be(0);
        GoldenMetrics.GramErrorPercent(s, 500m).Should().BeApproximately(50.0, 0.01);
    }

    [Fact]
    public void ScoreCase_ReportsMissesAndMatches()
    {
        var c = new GoldenCase
        {
            Image = "test.jpg",
            Expected =
            [
                new() { Name = "rice", Grams = 200m },
                new() { Name = "chicken", Grams = 150m },
                new() { Name = "broccoli", Grams = 80m },   // will be missed
            ],
        };
        var scanned = new List<ScannedComponent>
        {
            Scanned("steamed rice", 220m),      // +10% error
            Scanned("grilled chicken", 150m),   // 0% error
        };

        var score = GoldenMetrics.ScoreCase(c, scanned);

        score.MatchedCount.Should().Be(2);
        score.Recall.Should().BeApproximately(2.0 / 3.0, 0.001);
        score.MeanGramErrorPercent.Should().BeApproximately(5.0, 0.01);
        score.PerComponent.Should().Contain(p => p.Expected == "broccoli" && p.Matched == null);
    }

    [Fact]
    public void ScoreCase_EmptyExpected_FullRecall()
    {
        var score = GoldenMetrics.ScoreCase(
            new GoldenCase { Image = "x.jpg" },
            [Scanned("something", 100m)]);
        score.Recall.Should().Be(1.0);
    }
    [Fact]
    public void EvaluateCase_ComputesPrecisionF1AndKcalErrors()
    {
        var c = new GoldenCase
        {
            Expected = [new GoldenExpected { Name = "rice", Grams = 100m, Kcal = 200m,
                ProteinG = 5m, CarbsG = 40m, FatG = 2m }],
        };
        var predicted = new[]
        {
            new GoldenMetrics.GoldenPredictedItem("rice", 110m, 90m, 110m, null, null, null, false, 250m, 5m, 40m, 2m),
            new GoldenMetrics.GoldenPredictedItem("bread", 50m, null, null, null, null, null, false, 10m, null, null, null),
        };

        var result = GoldenMetrics.EvaluateCase(c, predicted);

        result.Precision.Should().Be(0.5);
        result.Recall.Should().Be(1);
        result.F1.Should().BeApproximately(2d / 3d, 1e-12);
        result.KcalAbsoluteErrorPercent.Should().Be(30);
        result.KcalSignedErrorPercent.Should().Be(30);
        result.PredictedKcal.Should().Be(260m);
    }
    [Fact]
    public void Aggregate_ComputesAbsoluteErrorForEachMacroWithAvailableGroundTruth()
    {
        var c = new GoldenCase
        {
            Expected = [new GoldenExpected
            {
                Name = "rice", Grams = 100, ProteinG = 10, CarbsG = 20, FatG = 5,
            }],
        };
        var evaluation = GoldenMetrics.EvaluateCase(c,
        [
            new("rice", 100, null, null, null, null, null, true, null, 15, 20, 10),
        ]);

        var aggregate = GoldenMetrics.Aggregate([evaluation]);

        aggregate.MeanAbsoluteMacroErrorPercent["protein_g"].Should().Be(50);
        aggregate.MeanAbsoluteMacroErrorPercent["carbs_g"].Should().Be(0);
        aggregate.MeanAbsoluteMacroErrorPercent["fat_g"].Should().Be(100);
    }

    [Fact]
    public void EvaluateCase_OmitsExpectedNutritionWhenAnyComponentIsMissing()
    {
        var c = new GoldenCase
        {
            Expected =
            [
                new GoldenExpected { Name = "rice", Grams = 100m, Kcal = 200m },
                new GoldenExpected { Name = "beans", Grams = 100m },
            ],
        };

        var result = GoldenMetrics.EvaluateCase(c, []);

        result.ExpectedKcal.Should().BeNull();
        double.IsNaN(result.KcalAbsoluteErrorPercent).Should().BeTrue();
        double.IsNaN(result.KcalSignedErrorPercent).Should().BeTrue();
    }

    [Fact]
    public void Aggregate_ComputesIdentityAbstentionAndMethodAccuracy()
    {
        var c = new GoldenCase
        {
            Expected = [new GoldenExpected { Name = "rice", Grams = 100m, AcceptableIdentities = ["USDA:1"] }],
        };
        var result = GoldenMetrics.EvaluateCase(c,
        [
            new("rice", 100m, 90m, 110m, "usda:1", "vision", 0.8m, true, 100m, null, null, null),
            new("bread", 50m, null, null, null, "vision", null, false, null, null, null, null),
        ]);

        var aggregate = GoldenMetrics.Aggregate([result]);

        aggregate.IdentityPrecision.Should().Be(1);
        aggregate.AbstentionRate.Should().Be(0.5);
        aggregate.AccuracyByMethod["vision"].Should().Be(1);
        aggregate.IntervalCoverageRate.Should().Be(1);
    }

    [Fact]
    public void Aggregate_ComputesTenBinExpectedCalibrationError()
    {
        var c = new GoldenCase
        {
            Expected =
            [
                new GoldenExpected { Name = "rice", Grams = 100m, AcceptableIdentities = ["USDA:1"] },
                new GoldenExpected { Name = "beans", Grams = 100m, AcceptableIdentities = ["USDA:2"] },
            ],
        };
        var result = GoldenMetrics.EvaluateCase(c,
        [
            new("rice", 100m, null, null, "USDA:1", null, 0.8m, true, null, null, null, null),
            new("beans", 100m, null, null, "USDA:wrong", null, 0.6m, true, null, null, null, null),
        ]);

        GoldenMetrics.Aggregate([result]).IdentityEce.Should().BeApproximately(0.4, 1e-12);
    }
    [Fact]
    public void Aggregate_EcePlacesExactBinEdgesInTheUpperBinAndIncludesOne()
    {
        var c = new GoldenCase
        {
            Expected =
            [
                new GoldenExpected { Name = "rice", Grams = 100, AcceptableIdentities = ["food:rice"] },
                new GoldenExpected { Name = "beans", Grams = 100, AcceptableIdentities = ["food:beans"] },
                new GoldenExpected { Name = "corn", Grams = 100, AcceptableIdentities = ["food:corn"] },
            ],
        };
        var evaluation = GoldenMetrics.EvaluateCase(c,
        [
            new("rice", 100, null, null, "food:rice", null, 0.1m, true, null, null, null, null),
            new("beans", 100, null, null, "wrong", null, 0.2m, true, null, null, null, null),
            new("corn", 100, null, null, "food:corn", null, 1m, true, null, null, null, null),
        ]);

        GoldenMetrics.Aggregate([evaluation]).IdentityEce.Should().BeApproximately(11d / 30d, 1e-12);
    }

    [Fact]
    public void Aggregate_ComputesPopulationKcalCoefficientOfVariation()
    {
        var result = GoldenMetrics.Aggregate([], new Dictionary<string, IReadOnlyList<double>>
        {
            ["case01.jpg"] = [90, 110],
        });

        result.MeanKcalCvPercent.Should().Be(10);
    }

    [Fact]
    public void AggregateStages_ComputesPerStagePercentilesAcrossLatencyAndUsage()
    {
        var result = GoldenMetrics.AggregateStages([
            new("vision", 1, 100, 20, 0.01),
            new("vision", 3, 300, 60, 0.03),
            new("selection", 2, 50, 10, 0.005),
        ]);

        result["vision"].P50LatencySeconds.Should().Be(2);
        result["vision"].P95LatencySeconds.Should().BeApproximately(2.9, 1e-12);
        result["vision"].P95InputTokens.Should().BeApproximately(290, 1e-12);
        result["vision"].P50CostUsd.Should().BeApproximately(0.02, 1e-12);
        result["selection"].P95OutputTokens.Should().Be(10);
    }

    [Fact]
    public void EvaluateCase_IntervalCoverageIncludesBoundsAndExcludesOutsideValues()
    {
        var c = new GoldenCase
        {
            Expected =
            [
                new() { Name = "rice", Grams = 90 },
                new() { Name = "beans", Grams = 120 },
                new() { Name = "corn", Grams = 40 },
            ],
        };
        var evaluated = GoldenMetrics.EvaluateCase(c,
        [
            new("rice", 100, 90, 110, null, null, null, false, null, null, null, null),
            new("beans", 100, 110, 120, null, null, null, false, null, null, null, null),
            new("corn", 50, 45, 55, null, null, null, false, null, null, null, null),
        ]);

        evaluated.IntervalCovered.Should().Equal(true, true, false);
        GoldenMetrics.Aggregate([evaluated]).IntervalCoverageRate.Should().BeApproximately(2d / 3d, 1e-12);
    }

    [Fact]
    public void Aggregate_CoefficientOfVariationUsesPopulationDeviationForEachCase()
    {
        var aggregate = GoldenMetrics.Aggregate([], new Dictionary<string, IReadOnlyList<double>>
        {
            ["stable"] = [100, 100, 100],
            ["varying"] = [80, 120],
        });

        aggregate.MeanKcalCvPercent.Should().Be(10);
    }
    [Fact]
    public void Aggregate_RecallCoefficientOfVariationExcludesSingleRunCases()
    {
        var aggregate = GoldenMetrics.Aggregate([], repeatedRecallByCase:
            new Dictionary<string, IReadOnlyList<double>>
            {
                ["varying"] = [0.5, 1.0],
                ["single-run"] = [0.25],
            });

        aggregate.MeanRecallCvPercent.Should().BeApproximately(100d / 3d, 1e-12);
    }

    [Fact]
    public void Percentile_InterpolatesAndReturnsNaNForEmptyValues()
    {
        GoldenMetrics.Percentile([0, 10], 25).Should().Be(2.5);
        double.IsNaN(GoldenMetrics.Percentile([], 50)).Should().BeTrue();
    }

    [Fact]
    public void EvaluateGate_ReportsPassFailAndNotEvaluated()
    {
        var aggregate = new GoldenAggregate(
            0.9, 0.8, 0.85, 20, 20, -5, 0.9, 0.1,
            new Dictionary<string, double>(), double.NaN, 0.95, 4, 25, 0.8, 0.1);
        var thresholds = new GateThresholds
        {
            MinPrecision = 0.8,
            MaxIdentityEce = 0.1,
            MaxP95LatencySeconds = 5,
            MaxP95CostUsd = 1,
        };

        var passing = GoldenMetrics.EvaluateGate(thresholds, aggregate, 4, null);
        passing.Passed.Should().BeTrue();
        passing.Failed.Should().BeEmpty();
        passing.NotEvaluated.Select(c => c.Name).Should().BeEquivalentTo(new[] { "max_identity_ece", "max_p95_cost_usd" });

        var failing = GoldenMetrics.EvaluateGate(new GateThresholds { MinPrecision = 0.95 }, aggregate, null, null);
        var overBudget = GoldenMetrics.EvaluateGate(thresholds, aggregate, 4, 1.01);
        overBudget.Passed.Should().BeFalse();
        overBudget.Failed.Should().ContainSingle().Which.Name.Should().Be("max_p95_cost_usd");

        failing.Passed.Should().BeFalse();
        failing.Failed.Should().ContainSingle().Which.Actual.Should().Be(0.9);
    }
    [Fact]
    public void GoldenManifest_ParsesLegacyFieldsAndDefaultsNewFields()
    {
        var manifest = JsonSerializer.Deserialize<GoldenManifest>(
            """{"cases":[{"image":"case01.jpg","expected":[{"name":"rice","grams":100}]}]}""")!;

        manifest.SchemaVersion.Should().Be(1);
        manifest.Gate.Should().NotBeNull();
        manifest.Cases[0].Tags.Should().BeEmpty();
        manifest.Cases[0].ReferenceObject.Should().BeNull();
        manifest.Cases[0].Expected[0].Weighed.Should().BeFalse();
        manifest.Cases[0].Expected[0].Kcal.Should().BeNull();
        manifest.Cases[0].Expected[0].AcceptableIdentities.Should().BeEmpty();
    }
}
