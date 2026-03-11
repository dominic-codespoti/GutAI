using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class ScoreReconcilerTests
{
    private static FodmapAssessmentDto MakeFodmap(int score, string? rating = null) => new()
    {
        FodmapScore = score,
        FodmapRating = rating ?? ScoreReconciler.RateFodmap(score),
    };

    private static GutRiskAssessmentDto MakeGutRisk(int score, string? rating = null) => new()
    {
        GutScore = score,
        GutRating = rating ?? ScoreReconciler.RateGut(score),
    };

    [Fact]
    public void BadFodmap_CapsGutScore()
    {
        // FODMAP=40 ("High FODMAP"), GutRisk=100 ("Good")
        // Gut cap = 40 + 20 = 60 → gut capped at 60 → "Fair"
        var (fodmap, gut) = ScoreReconciler.Reconcile(
            MakeFodmap(40), MakeGutRisk(100));

        gut.GutScore.Should().Be(60);
        gut.GutRating.Should().Be("Fair");
        fodmap.FodmapScore.Should().Be(40, "FODMAP score should not change when gut is higher");
    }

    [Fact]
    public void CloseScores_NoCappingNeeded()
    {
        // FODMAP=70, GutRisk=75 — both within 20 of each other
        // Gut cap = 70 + 20 = 90 → 75 < 90, no capping
        // FODMAP cap = 75 + 20 = 95 → 70 < 95, no capping
        var (fodmap, gut) = ScoreReconciler.Reconcile(
            MakeFodmap(70), MakeGutRisk(75));

        fodmap.FodmapScore.Should().Be(70);
        gut.GutScore.Should().Be(75);
        fodmap.FodmapRating.Should().Be("Moderate FODMAP");
        gut.GutRating.Should().Be("Fair");
    }

    [Fact]
    public void EqualScores_NoCapping()
    {
        var (fodmap, gut) = ScoreReconciler.Reconcile(
            MakeFodmap(50), MakeGutRisk(50));

        fodmap.FodmapScore.Should().Be(50);
        gut.GutScore.Should().Be(50);
    }

    [Fact]
    public void BadGutRisk_CapsFodmapScore()
    {
        // GutRisk=30 ("Bad"), FODMAP=90 ("Low FODMAP")
        // Gut cap = 90 + 20 = 110 → gut 30 < 110, no gut capping
        // FODMAP cap = 30 + 20 = 50 → 90 > 50 → FODMAP capped at 50
        var (fodmap, gut) = ScoreReconciler.Reconcile(
            MakeFodmap(90), MakeGutRisk(30));

        fodmap.FodmapScore.Should().Be(50);
        fodmap.FodmapRating.Should().Be("High FODMAP");
        gut.GutScore.Should().Be(30);
    }
}
