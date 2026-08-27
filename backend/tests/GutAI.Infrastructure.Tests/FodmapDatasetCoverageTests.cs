using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FodmapDatasetCoverageTests
{
    private readonly FodmapService _sut = new();

    private static FoodProductDto MakeProduct(string name, string? ingredients = null, List<string>? additiveTags = null) =>
        new()
        {
            Name = name,
            Ingredients = ingredients,
            AdditivesTags = additiveTags ?? [],
            Additives = []
        };

    [Fact]
    public void Assess_BananaBread_YieldsBananaTrigger()
    {
        // (a) Assess("Banana Bread","ripe banana, flour, sugar") yields Banana trigger
        var result = _sut.Assess(MakeProduct("Banana Bread", "ripe banana, flour, sugar"));

        result.Triggers.Should().Contain(t => t.Name == "Banana (Fructan)" && t.Severity == "Moderate");
    }

    [Fact]
    public void AssessText_SweetCorn_YieldsSweetCornTrigger()
    {
        // (b) AssessText("sweet corn") yields Sweet Corn trigger
        var result = _sut.AssessText("sweet corn");

        result.Triggers.Should().Contain(t => t.Name == "Sweet Corn (Fructan)" && t.Severity == "Moderate");
    }

    [Fact]
    public void Hummus_ProductNameAndIngredient_ProduceEqualSeverity()
    {
        // (c) hummus product name and hummus ingredient produce triggers with EQUAL severity
        var productResult = _sut.Assess(MakeProduct("Hummus Dip", null));
        var ingredientResult = _sut.Assess(MakeProduct("Dip", "hummus, salt, olive oil"));

        var productTrigger = productResult.Triggers.FirstOrDefault(t => t.Name.StartsWith("Hummus", StringComparison.OrdinalIgnoreCase));
        var ingredientTrigger = ingredientResult.Triggers.FirstOrDefault(t => t.Name.StartsWith("Hummus", StringComparison.OrdinalIgnoreCase));

        productTrigger.Should().NotBeNull();
        ingredientTrigger.Should().NotBeNull();
        productTrigger!.Severity.Should().Be(ingredientTrigger!.Severity);
    }

    [Fact]
    public void Kimchi_ProductNameAndIngredient_ProduceEqualSeverity()
    {
        // (d) same for kimchi
        var productResult = _sut.Assess(MakeProduct("Spicy Kimchi", null));
        var ingredientResult = _sut.Assess(MakeProduct("Side Dish", "kimchi, salt"));

        var productTrigger = productResult.Triggers.FirstOrDefault(t => t.Name.StartsWith("Kimchi", StringComparison.OrdinalIgnoreCase));
        var ingredientTrigger = ingredientResult.Triggers.FirstOrDefault(t => t.Name.StartsWith("Kimchi", StringComparison.OrdinalIgnoreCase));

        productTrigger.Should().NotBeNull();
        ingredientTrigger.Should().NotBeNull();
        productTrigger!.Severity.Should().Be(ingredientTrigger!.Severity);
    }

    [Fact]
    public void Blackberry_IngredientVsProductName_ProducesExactlyOneDistinctTriggerName()
    {
        // (e) blackberry ingredient vs product-name produce exactly ONE distinct trigger Name
        var productResult = _sut.Assess(MakeProduct("Blackberry Jam", null));
        var ingredientResult = _sut.Assess(MakeProduct("Fruit Spread", "blackberry, sugar, pectin"));

        var productTrigger = productResult.Triggers.FirstOrDefault(t => t.Name.StartsWith("Blackberry", StringComparison.OrdinalIgnoreCase) || t.Name.StartsWith("Blackberries", StringComparison.OrdinalIgnoreCase));
        var ingredientTrigger = ingredientResult.Triggers.FirstOrDefault(t => t.Name.StartsWith("Blackberry", StringComparison.OrdinalIgnoreCase) || t.Name.StartsWith("Blackberries", StringComparison.OrdinalIgnoreCase));

        productTrigger.Should().NotBeNull();
        ingredientTrigger.Should().NotBeNull();
        productTrigger!.Name.Should().Be("Blackberry (Fructose + Sorbitol)");
        ingredientTrigger!.Name.Should().Be("Blackberry (Fructose + Sorbitol)");

        // Combined assessment produces exactly one trigger name without duplicates
        var combined = _sut.Assess(MakeProduct("Blackberry Snack", "blackberry"));
        var blackberryTriggers = combined.Triggers.Where(t => t.Name.StartsWith("Blackberry", StringComparison.OrdinalIgnoreCase)).ToList();
        blackberryTriggers.Select(t => t.Name).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public void Additives_E420_Sorbitol_Glycerin_CollapseCorrectly()
    {
        // (f) en:e420 + sorbitol + glycerin collapse correctly (no duplicate canonical names)
        var food = MakeProduct("Sugar Free Gum", "sorbitol, glycerin, natural flavors", ["en:e420", "en:e422"]);

        var result = _sut.Assess(food);

        // Sorbitol triggers from additive en:e420 and ingredient sorbitol should collapse to 1
        var sorbitolTriggers = result.Triggers.Where(t => t.Name == "Sorbitol (Polyol)").ToList();
        sorbitolTriggers.Should().HaveCount(1);

        // Glycerol triggers from additive en:e422 and ingredient glycerin should collapse to 1
        var glycerolTriggers = result.Triggers.Where(t => t.Name == "Glycerol (Polyol)").ToList();
        glycerolTriggers.Should().HaveCount(1);
    }

    [Fact]
    public void SharedFodmapSeverities_AgreesWithFodmapDataEntries()
    {
        // (g) every new SharedFodmapSeverities key agrees with FodmapData entries having that pattern
        var sharedSeverities = SharedFodmapSeverities.Severities;

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
}
