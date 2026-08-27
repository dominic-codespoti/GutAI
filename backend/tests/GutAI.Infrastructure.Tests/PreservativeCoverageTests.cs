using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

// Preservative coverage added after the dataset audit: propionates (E280-283),
// parabens (E214-219), dimethyl dicarbonate (E242), and US-spelling sulfite
// compound-name patterns that were previously only reachable via additive tags.
public class PreservativeCoverageTests
{
    private readonly GutRiskService _sut = new();

    private static FoodProductDto MakeProduct(
        string name = "Test Product",
        string? ingredients = null,
        List<string>? additiveTags = null)
    {
        return new FoodProductDto
        {
            Name = name,
            Ingredients = ingredients,
            AdditivesTags = additiveTags ?? [],
            Additives = [],
        };
    }

    [Theory]
    [InlineData("en:e282", "E282")]
    [InlineData("en:e218", "E218")]
    [InlineData("en:e242", "E242")]
    public void AdditiveTag_PreservativeENumbers_Flag(string tag, string expectedCode)
    {
        var result = _sut.Assess(MakeProduct(additiveTags: [tag]));
        result.Flags.Should().Contain(f => f.Code == expectedCode);
    }

    [Fact]
    public void CalciumPropionate_IngredientText_Flags()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "whole wheat flour, water, calcium propionate, yeast"));
        var flag = result.Flags.FirstOrDefault(f => f.Code == "E282");
        flag.Should().NotBeNull("calcium propionate is the standard bread preservative and must be detected from ingredient text");
        flag!.Category.Should().Be("Preservative");
        flag.RiskLevel.Should().Be("Low");
    }

    [Fact]
    public void SodiumMetabisulfite_FreeText_FlagsViaSulfitePath()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "dried apricots, sodium metabisulfite"));
        var flag = result.Flags.FirstOrDefault(f => f.Code == "E223");
        flag.Should().NotBeNull("US-spelling sulfite compound names must be detectable without structured additive tags");
        flag!.Category.Should().Be("Preservative/Sulfite");
    }

    [Fact]
    public void SulfurDioxide_BothSpellings_FlagOnce()
    {
        // Both spellings carry code E220 — HasFlag dedupe by code must collapse them.
        var result = _sut.Assess(MakeProduct(ingredients: "sulfur dioxide, sulphur dioxide"));
        result.Flags.Count(f => f.Code == "E220").Should().Be(1);
    }

    [Fact]
    public void Propylparaben_IngredientText_FlagsMedium()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, propylparaben, natural flavors"));
        var flag = result.Flags.FirstOrDefault(f => f.Code == "E216");
        flag.Should().NotBeNull();
        flag!.RiskLevel.Should().Be("Medium");
        flag.Explanation.Should().Contain("endocrine");
    }

    [Fact]
    public void Propionates_DoNotFalsePositiveOnThiodipropionates()
    {
        // Dilauryl thiodipropionate (antioxidant) contains 'propionate' as a substring;
        // the preservative patterns are full compound names and must not fire on it.
        var result = _sut.Assess(MakeProduct(ingredients: "vegetable oil, dilauryl thiodipropionate"));
        result.Flags.Where(f => f.Category == "Preservative").Should().BeEmpty();
        result.Flags.Should().Contain(f => f.Code == "E389");
    }

    // ─── Regulatory red flags + final sweep additions ───────────────────

    [Fact]
    public void PotassiumBromate_And_Azodicarbonamide_FlagFromIngredientText()
    {
        var bromate = _sut.Assess(MakeProduct(ingredients: "enriched wheat flour, potassium bromate, water"));
        var ada = _sut.Assess(MakeProduct(ingredients: "wheat flour, azodicarbonamide, water"));

        var bromateFlag = bromate.Flags.FirstOrDefault(f => f.Code == "E924");
        var adaFlag = ada.Flags.FirstOrDefault(f => f.Code == "E927a");

        bromateFlag.Should().NotBeNull("potassium bromate is IARC 2B and banned across major markets");
        bromateFlag!.RiskLevel.Should().Be("Medium");
        adaFlag.Should().NotBeNull();
        adaFlag!.Explanation.Should().Contain("banned");
    }

    [Fact]
    public void Maltodextrin_FlagsLow_NotAsFodmapAlarm()
    {
        // Maltodextrin is low FODMAP — it must appear as an honest Low-severity entry,
        // never as a high-concern trigger.
        var result = _sut.Assess(MakeProduct(ingredients: "protein blend (whey, maltodextrin), cocoa"));
        var flag = result.Flags.FirstOrDefault(f => f.Name == "Maltodextrin");
        flag.Should().NotBeNull();
        flag!.RiskLevel.Should().Be("Low");
    }

    [Fact]
    public void HiddenFodmap_PluralVariants_Flag()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "potato starch, seasonings, vegetable oils"));
        result.Flags.Should().Contain(f => f.Name == "Seasonings");
    }
}
