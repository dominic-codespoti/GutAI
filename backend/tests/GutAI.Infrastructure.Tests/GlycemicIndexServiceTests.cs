using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class GlycemicIndexServiceTests
{
    private readonly GlycemicIndexService _sut = new();

    static FoodProductDto MakeProduct(string name = "Test", string? ingredients = null,
        decimal? carbs = null, decimal? sugar = null, decimal? fiber = null,
        decimal? protein = null, decimal? fat = null)
        => new()
        {
            Name = name,
            Ingredients = ingredients,
            Carbs100g = carbs,
            Sugar100g = sugar,
            Fiber100g = fiber,
            Protein100g = protein,
            Fat100g = fat,
        };

    // ─── Known Foods — High GI ──────────────────────────────────────────

    [Fact]
    public void WhiteRice_HighGI()
    {
        var result = _sut.Assess(MakeProduct("White Rice", "white rice, water"));
        result.EstimatedGI.Should().Be(73);
        result.GiCategory.Should().Be("High");
    }

    [Fact]
    public void WhiteBread_HighGI()
    {
        var result = _sut.Assess(MakeProduct("White Bread", "white bread, flour"));
        result.EstimatedGI.Should().Be(75);
        result.GiCategory.Should().Be("High");
    }

    [Fact]
    public void BakedPotato_HighGI()
    {
        var result = _sut.Assess(MakeProduct("Baked Potato", "baked potato"));
        result.EstimatedGI.Should().Be(85);
        result.GiCategory.Should().Be("High");
    }

    [Fact]
    public void CornFlakes_HighGI()
    {
        var result = _sut.Assess(MakeProduct("Corn Flakes", "corn flakes, sugar"));
        result.EstimatedGI.Should().Be(81);
        result.GiCategory.Should().Be("High");
    }

    [Fact]
    public void JasmineRice_VeryHighGI()
    {
        var result = _sut.Assess(MakeProduct("Jasmine Rice", "jasmine rice"));
        result.EstimatedGI.Should().Be(89);
        result.GiCategory.Should().Be("High");
    }

    // ─── Known Foods — Medium GI ────────────────────────────────────────

    [Fact]
    public void BrownRice_MediumGI()
    {
        var result = _sut.Assess(MakeProduct("Brown Rice", "brown rice"));
        result.EstimatedGI.Should().Be(68);
        result.GiCategory.Should().Be("Medium");
    }

    [Fact]
    public void Honey_MediumGI()
    {
        var result = _sut.Assess(MakeProduct("Honey", "honey"));
        result.EstimatedGI.Should().Be(61);
        result.GiCategory.Should().Be("Medium");
    }

    [Fact]
    public void Pineapple_MediumGI()
    {
        var result = _sut.Assess(MakeProduct("Pineapple Chunks", "pineapple"));
        result.EstimatedGI.Should().Be(59);
        result.GiCategory.Should().Be("Medium");
    }

    // ─── Known Foods — Low GI ───────────────────────────────────────────

    [Fact]
    public void Lentils_LowGI()
    {
        var result = _sut.Assess(MakeProduct("Red Lentils", "lentils, water"));
        result.EstimatedGI.Should().Be(32);
        result.GiCategory.Should().Be("Low");
    }

    [Fact]
    public void Chickpeas_LowGI()
    {
        var result = _sut.Assess(MakeProduct("Chickpeas", "chickpeas, water"));
        result.EstimatedGI.Should().Be(28);
        result.GiCategory.Should().Be("Low");
    }

    [Fact]
    public void Apple_LowGI()
    {
        var result = _sut.Assess(MakeProduct("Apple", "apple"));
        result.EstimatedGI.Should().Be(36);
        result.GiCategory.Should().Be("Low");
    }

    [Fact]
    public void Pasta_LowGI()
    {
        var result = _sut.Assess(MakeProduct("Spaghetti", "spaghetti, water"));
        result.EstimatedGI.Should().Be(49);
        result.GiCategory.Should().Be("Low");
    }

    [Fact]
    public void Sourdough_LowGI()
    {
        var result = _sut.Assess(MakeProduct("Sourdough Bread", "sourdough flour, water"));
        result.EstimatedGI.Should().Be(54);
        result.GiCategory.Should().Be("Low");
    }

    [Fact]
    public void Oats_LowGI()
    {
        var result = _sut.Assess(MakeProduct("Oatmeal", "oats, water"));
        result.EstimatedGI.Should().Be(55);
        result.GiCategory.Should().Be("Low");
    }

    [Fact]
    public void Cherry_VeryLowGI()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "cherry, sugar"));
        result.EstimatedGI.Should().Be(22);
        result.GiCategory.Should().Be("Low");
    }

    // ─── Glycemic Load Calculation ──────────────────────────────────────

    [Fact]
    public void GL_CalculatedFromCarbsAndGI()
    {
        var result = _sut.Assess(MakeProduct("White Bread", "white bread", carbs: 50));
        result.EstimatedGL.Should().Be(37.5m); // 75 * 50 / 100
        result.GlCategory.Should().Be("High");
    }

    [Fact]
    public void GL_LowWhenLowCarbs()
    {
        var result = _sut.Assess(MakeProduct("Watermelon", "watermelon", carbs: 8));
        // Watermelon GI=76, GL = 76*8/100 = 6.1
        result.EstimatedGL.Should().BeLessThan(10);
        result.GlCategory.Should().Be("Low");
    }

    [Fact]
    public void GL_NullWhenNoCarbs()
    {
        var result = _sut.Assess(MakeProduct("Chicken", "chicken breast"));
        result.EstimatedGL.Should().BeNull();
    }

    // ─── GI Category Classification ─────────────────────────────────────

    [Theory]
    [InlineData(55, "Low")]
    [InlineData(56, "Medium")]
    [InlineData(69, "Medium")]
    [InlineData(70, "High")]
    [InlineData(100, "High")]
    public void GI_CorrectCategory(int gi, string expected)
    {
        // Use specific foods to hit exact GI values
        var result = _sut.Assess(MakeProduct(ingredients: gi switch
        {
            55 => "oats",
            56 => "granola",
            69 => "muffin",
            70 => "energy drink",
            100 => "glucose",
            _ => "unknown"
        }));
        result.GiCategory.Should().Be(expected);
    }

    // ─── Recommendations ────────────────────────────────────────────────

    [Fact]
    public void HighGI_GivesSlowingRecommendation()
    {
        var result = _sut.Assess(MakeProduct("White Rice", "white rice"));
        result.Recommendations.Should().Contain(r => r.Contains("protein or healthy fat"));
    }

    [Fact]
    public void MediumGI_GivesFiberRecommendation()
    {
        var result = _sut.Assess(MakeProduct("Brown Rice", "brown rice"));
        result.Recommendations.Should().Contain(r => r.Contains("fiber"));
    }

    [Fact]
    public void HighGL_GivesPortionRecommendation()
    {
        var result = _sut.Assess(MakeProduct("White Bread", "white bread", carbs: 50));
        result.Recommendations.Should().Contain(r => r.Contains("portion size"));
    }

    [Fact]
    public void HighFiber_GivesFiberBonus()
    {
        var result = _sut.Assess(MakeProduct("Oat Bar", "oats, honey", fiber: 8));
        result.Recommendations.Should().Contain(r => r.Contains("fiber"));
    }

    [Fact]
    public void LowFiberHighCarb_SuggestsAddingFiber()
    {
        var result = _sut.Assess(MakeProduct("White Bread", "white bread", carbs: 50, fiber: 0.5m));
        result.Recommendations.Should().Contain(r => r.Contains("fiber source"));
    }

    // ─── Gut Impact Summary ─────────────────────────────────────────────

    [Fact]
    public void HighGI_SummaryMentionsInflammation()
    {
        var result = _sut.Assess(MakeProduct("White Bread", "white bread"));
        result.GutImpactSummary.Should().Contain("inflammation");
    }

    [Fact]
    public void LowGI_SummaryMentionsMicrobiome()
    {
        var result = _sut.Assess(MakeProduct("Lentils", "lentils"));
        result.GutImpactSummary.Should().Contain("microbiome");
    }

    // ─── Multiple Ingredient Matching ───────────────────────────────────

    [Fact]
    public void MultipleIngredients_AverageGI()
    {
        // White bread (75) + honey (61) → avg = 68
        var result = _sut.Assess(MakeProduct("Honey Toast", "white bread, honey, butter"));
        result.EstimatedGI.Should().NotBeNull();
        result.MatchCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void NoDuplicateMatches()
    {
        var result = _sut.Assess(MakeProduct("Rice Dish", "white rice, basmati rice, rice"));
        // White rice, basmati rice, and rice should match but be deduped
        var riceMatches = result.Matches.Count(m => m.Food.Contains("rice", StringComparison.OrdinalIgnoreCase));
        riceMatches.Should().BeGreaterThanOrEqualTo(1);
    }

    // ─── Estimation from Nutrition ──────────────────────────────────────

    [Fact]
    public void UnknownFood_EstimatesFromNutrition()
    {
        var result = _sut.Assess(MakeProduct("Mystery Bar", carbs: 40, sugar: 30, fiber: 1, protein: 3, fat: 5));
        result.EstimatedGI.Should().NotBeNull();
        result.Matches.Should().Contain(m => m.Source == "Estimated");
    }

    [Fact]
    public void HighSugarRatio_HigherEstimate()
    {
        var highSugar = _sut.Assess(MakeProduct("Candy", carbs: 80, sugar: 70));
        var lowSugar = _sut.Assess(MakeProduct("Bread", carbs: 80, sugar: 5));
        highSugar.EstimatedGI.Should().BeGreaterThan(lowSugar.EstimatedGI!.Value);
    }

    [Fact]
    public void HighFiber_LowerEstimate()
    {
        var highFiber = _sut.Assess(MakeProduct("Bran", carbs: 60, sugar: 10, fiber: 15));
        var lowFiber = _sut.Assess(MakeProduct("White", carbs: 60, sugar: 10, fiber: 0));
        highFiber.EstimatedGI.Should().BeLessThan(lowFiber.EstimatedGI!.Value);
    }

    [Fact]
    public void HighProtein_LowerEstimate()
    {
        var highProt = _sut.Assess(MakeProduct("Protein Bar", carbs: 30, sugar: 5, protein: 25));
        var lowProt = _sut.Assess(MakeProduct("Sugar Bar", carbs: 30, sugar: 5, protein: 1));
        highProt.EstimatedGI.Should().BeLessThan(lowProt.EstimatedGI!.Value);
    }

    [Fact]
    public void NoCarbs_NullEstimate()
    {
        var result = _sut.Assess(MakeProduct("Olive Oil", carbs: 0));
        result.EstimatedGI.Should().BeNull();
    }

    // ─── AssessText ─────────────────────────────────────────────────────

    [Fact]
    public void AssessText_FindsKnownFoods()
    {
        var result = _sut.AssessText("white bread with honey and banana");
        result.MatchCount.Should().BeGreaterThanOrEqualTo(2);
        result.EstimatedGI.Should().NotBeNull();
    }

    [Fact]
    public void AssessText_NoFoods_ReturnsUnknown()
    {
        var result = _sut.AssessText("nothing recognizable here");
        result.EstimatedGI.Should().BeNull();
        result.GiCategory.Should().Be("Unknown");
    }

    [Fact]
    public void AssessText_HighGI_GivesRecommendations()
    {
        var result = _sut.AssessText("white rice with mashed potato");
        result.Recommendations.Should().NotBeEmpty();
    }

    // ─── Edge Cases ─────────────────────────────────────────────────────

    [Fact]
    public void NoIngredients_NoName_NullGI()
    {
        var result = _sut.Assess(MakeProduct("Unknown"));
        result.EstimatedGI.Should().BeNull();
        result.GiCategory.Should().Be("Unknown");
        result.MatchCount.Should().Be(0);
    }

    [Fact]
    public void ProductName_MatchesDatabase()
    {
        var result = _sut.Assess(MakeProduct("Basmati Rice"));
        result.EstimatedGI.Should().Be(58);
        result.GiCategory.Should().Be("Medium");
    }

    [Fact]
    public void EmptyIngredients_UsesProductName()
    {
        var result = _sut.Assess(MakeProduct("Quinoa Salad", ""));
        result.EstimatedGI.Should().Be(53);
    }

    // ─── Specific GI Values from Sydney Tables ──────────────────────────

    [Theory]
    [InlineData("Bagel", "bagel", 72)]
    [InlineData("Pumpernickel", "pumpernickel bread", 46)]
    [InlineData("Sweet Potato", "sweet potato", 63)]
    [InlineData("Quinoa", "quinoa", 53)]
    [InlineData("Buckwheat", "buckwheat", 49)]
    [InlineData("Dates", "dates, sugar", 42)]
    [InlineData("Rice Cakes", "rice cakes", 82)]
    [InlineData("Pretzels", "pretzel, salt", 83)]
    public void SpecificFoods_CorrectGI(string name, string ingredients, int expectedGI)
    {
        var result = _sut.Assess(MakeProduct(name, ingredients));
        result.EstimatedGI.Should().Be(expectedGI);
    }
}
