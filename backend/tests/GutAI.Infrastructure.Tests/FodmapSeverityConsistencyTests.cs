using System.Text.RegularExpressions;
using FluentAssertions;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FodmapSeverityConsistencyTests
{
    [Fact]
    public void GutRiskData_WholeFoodRiskPatterns_MatchSharedFodmapSeverities()
    {
        var sharedSeverities = SharedFodmapSeverities.Severities;

        // Verify WholeFoodRiskPatterns that reference SharedFodmapSeverities (excluding entries with independent risk scoring)
        foreach (var entry in GutRiskData.WholeFoodRiskPatterns)
        {
            if (entry.Pattern is "blackberry" or "peach" or "fig" or "leek" or "cheese")
                continue;

            if (sharedSeverities.TryGetValue(entry.Pattern, out var sharedSeverity))
            {
                var expectedRiskLevel = SharedFodmapSeverities.ToRiskLevel(sharedSeverity);
                entry.Info.RiskLevel.Should().Be(
                    expectedRiskLevel,
                    because: $"GutRiskData whole food pattern '{entry.Pattern}' should match canonical severity in SharedFodmapSeverities ({sharedSeverity} -> {expectedRiskLevel})");
            }
        }
    }

    [Fact]
    public void FodmapData_IngredientTriggers_MatchSharedFodmapSeverities()
    {
        var sharedSeverities = SharedFodmapSeverities.Severities;

        // Verify FodmapData.IngredientTriggers (excluding WholeFoodTriggers per requirements)
        foreach (var entry in FodmapData.IngredientTriggers)
        {
            if (sharedSeverities.TryGetValue(entry.Pattern, out var sharedSeverity))
            {
                entry.Trigger.Severity.Should().Be(
                    sharedSeverity,
                    because: $"FodmapData ingredient pattern '{entry.Pattern}' should match canonical severity in SharedFodmapSeverities ({sharedSeverity})");
            }
        }
    }

    // ─── Governance coverage metric (hardening #1) ──────────────────────

    [Theory]
    [InlineData("bread"), InlineData("cake"), InlineData("pie"), InlineData("bar"),
     InlineData("shake"), InlineData("wrap"), InlineData("sandwich"), InlineData("soup"),
     InlineData("salad"), InlineData("curry"), InlineData("burger"), InlineData("chip"),
     InlineData("cookie"), InlineData("biscuit"), InlineData("pudding"), InlineData("muffin"),
     InlineData("donut"), InlineData("doughnut"), InlineData("pancake"), InlineData("waffle"),
     InlineData("nugget"), InlineData("jam"), InlineData("jelly"), InlineData("dish"),
     InlineData("meal"), InlineData("snack"), InlineData("casserole"), InlineData("stew")]
    public void KeyableRule_ExcludesDishWords(string dishWord)
    {
        IsKeyable(dishWord).Should().BeFalse();
        IsKeyable($"apple {dishWord}").Should().BeFalse();
        IsKeyable("banana").Should().BeTrue();
        IsKeyable("chickpea flour").Should().BeTrue();
    }

    private static bool IsKeyable(string pattern) =>
        !Regex.IsMatch(pattern,
            @"\b(bread|cake|pie|bars?|shakes?|wraps?|sandwich(es)?|soup|salad|curry|burger|chips?|cookie|biscuit|pudding|muffin|donut|doughnut|pancake|waffle|nugget|jams?|jelly|dish|meal|snack|casserole|stew)\b",
            RegexOptions.IgnoreCase);

    [Fact]
    public void IngredientTriggers_GovernanceCoverage_AtLeast85Percent()
    {
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(
            System.AppContext.BaseDirectory,
            "../../../../../src/GutAI.Infrastructure/Services/FodmapData.cs"));

        var patterns = Regex.Matches(src, "new\\(\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        var governed = patterns.Count(p => SharedFodmapSeverities.Severities.ContainsKey(p));
        var coverage = (double)governed / Math.Max(1, patterns.Count);

        // Denominator includes whole-food + additive-name patterns too — a
        // conservative floor for the 85% governance target from the plan.
        coverage.Should().BeGreaterThanOrEqualTo(0.85,
            $"governance coverage is {governed}/{patterns.Count} = {coverage:P0}");
    }
}
