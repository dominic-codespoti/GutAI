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
}
