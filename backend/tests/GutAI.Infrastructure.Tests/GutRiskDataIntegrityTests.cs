using FluentAssertions;
using GutAI.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace GutAI.Infrastructure.Tests;

public class GutRiskDataIntegrityTests
{
    private readonly ITestOutputHelper _output;

    public GutRiskDataIntegrityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void IngredientPatterns_HasNoInternalDuplicates()
    {
        var ingredientPatterns = GutRiskData.IngredientPatterns.Select(e => e.Pattern).ToList();
        var ingredientDuplicates = ingredientPatterns
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (x{g.Count()})")
            .ToList();

        _output.WriteLine($"IngredientPatterns total: {ingredientPatterns.Count}, unique: {ingredientPatterns.Distinct(StringComparer.OrdinalIgnoreCase).Count()}");

        ingredientDuplicates.Should().BeEmpty(
            because: $"IngredientPatterns should contain no duplicate patterns, but found: {string.Join(", ", ingredientDuplicates)}");
    }

    [Fact]
    public void WholeFoodRiskPatterns_HasNoInternalDuplicates()
    {
        var wholeFoodPatterns = GutRiskData.WholeFoodRiskPatterns.Select(e => e.Pattern).ToList();
        var wholeFoodDuplicates = wholeFoodPatterns
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (x{g.Count()})")
            .ToList();

        _output.WriteLine($"WholeFoodRiskPatterns total: {wholeFoodPatterns.Count}, unique: {wholeFoodPatterns.Distinct(StringComparer.OrdinalIgnoreCase).Count()}");

        wholeFoodDuplicates.Should().BeEmpty(
            because: $"WholeFoodRiskPatterns should contain no duplicate patterns, but found: {string.Join(", ", wholeFoodDuplicates)}");
    }

    [Fact]
    public void WholeFoodRiskPatterns_ContainsZeroEntriesWithENumber()
    {
        // WholeFoodRiskPatterns is strictly for whole-food product names (fruits, vegetables, grains, legumes, dairy, composed dishes)
        // Additive/E-number patterns belong exclusively in IngredientPatterns
        var entriesWithENumber = GutRiskData.WholeFoodRiskPatterns
            .Where(e => !string.IsNullOrEmpty(e.Info.ENumber))
            .Select(e => $"{e.Pattern} ({e.Info.ENumber} - {e.Info.Name})")
            .ToList();

        _output.WriteLine($"WholeFoodRiskPatterns total: {GutRiskData.WholeFoodRiskPatterns.Count}, E-number entries found: {entriesWithENumber.Count}");

        entriesWithENumber.Should().BeEmpty(
            because: $"WholeFoodRiskPatterns must contain zero entries with an E-number, but found: {string.Join(", ", entriesWithENumber)}");
    }

    [Fact]
    public void WholeFoodRiskPatterns_DoesNotContainPastedAdditivePatterns()
    {
        // Regression pins on the exact pasted content (e.g. anti-caking slice)
        string[] forbiddenPatterns =
        [
            "calcium aluminium silicate",
            "talcum",
            "sodium aluminosilicate",
        ];

        var wholeFoodPatterns = new HashSet<string>(
            GutRiskData.WholeFoodRiskPatterns.Select(e => e.Pattern),
            StringComparer.OrdinalIgnoreCase);

        var leakedPatterns = forbiddenPatterns.Where(p => wholeFoodPatterns.Contains(p)).ToList();

        _output.WriteLine($"WholeFoodRiskPatterns count: {GutRiskData.WholeFoodRiskPatterns.Count}");

        leakedPatterns.Should().BeEmpty(
            because: $"WholeFoodRiskPatterns must not contain pasted additive patterns, but found: {string.Join(", ", leakedPatterns)}");
    }

    [Fact]
    public void WholeFoodRiskPatterns_CountMeetsThreshold()
    {
        var count = GutRiskData.WholeFoodRiskPatterns.Count;
        _output.WriteLine($"WholeFoodRiskPatterns count: {count}");

        count.Should().BeGreaterThanOrEqualTo(50,
            because: $"WholeFoodRiskPatterns should have at least 50 valid whole food patterns (current count: {count})");
    }

    [Fact]
    public void IngredientPatterns_SeveritiesDeriveFromSharedCanonicalMap()
    {
        // The duplicated-block removal exposed hardcoded severities that contradicted
        // SharedFodmapSeverities (they had been shadowed by derived duplicates winning
        // first-match). This guard keeps IngredientPatterns locked to the canonical map.
        var mismatches = GutRiskData.IngredientPatterns
            .Where(e => SharedFodmapSeverities.Severities.ContainsKey(e.Pattern.Trim()))
            .Select(e => new
            {
                e.Pattern,
                Actual = e.Info.RiskLevel,
                Canonical = SharedFodmapSeverities.ToRiskLevel(SharedFodmapSeverities.Severities[e.Pattern.Trim()]),
            })
            .Where(x => x.Actual != x.Canonical)
            .Select(x => $"{x.Pattern}: {x.Actual} != canonical {x.Canonical}")
            .ToList();

        _output.WriteLine($"Checked {GutRiskData.IngredientPatterns.Count} ingredient patterns against {SharedFodmapSeverities.Severities.Count} canonical keys");

        mismatches.Should().BeEmpty(
            because: $"IngredientPatterns entries matching a SharedFodmapSeverities key must derive their risk level from it, but found: {string.Join("; ", mismatches)}");
    }

    [Fact]
    public void WholeFoodRiskPatterns_SeveritiesDeriveFromSharedCanonicalMap()
    {
        var mismatches = GutRiskData.WholeFoodRiskPatterns
            .Where(e => SharedFodmapSeverities.Severities.ContainsKey(e.Pattern.Trim()))
            .Select(e => new
            {
                e.Pattern,
                Actual = e.Info.RiskLevel,
                Canonical = SharedFodmapSeverities.ToRiskLevel(SharedFodmapSeverities.Severities[e.Pattern.Trim()]),
            })
            .Where(x => x.Actual != x.Canonical)
            .Select(x => $"{x.Pattern}: {x.Actual} != canonical {x.Canonical}")
            .ToList();

        mismatches.Should().BeEmpty(
            because: $"WholeFoodRiskPatterns entries matching a SharedFodmapSeverities key must derive their risk level from it, but found: {string.Join("; ", mismatches)}");
    }
}
