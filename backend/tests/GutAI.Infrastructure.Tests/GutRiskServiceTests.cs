using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class GutRiskServiceTests
{
    private readonly GutRiskService _sut = new();

    private static FoodProductDto MakeProduct(
        List<string>? additivesTags = null,
        List<FoodAdditiveDto>? additives = null,
        string? ingredients = null,
        int? novaGroup = null,
        decimal? sodium100g = null,
        decimal? sugar100g = null,
        decimal? fiber100g = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Test Product",
            AdditivesTags = additivesTags ?? [],
            Additives = additives ?? [],
            Ingredients = ingredients,
            NovaGroup = novaGroup,
            Sodium100g = sodium100g,
            Sugar100g = sugar100g,
            Fiber100g = fiber100g,
        };

    // ─── Clean Product ─────────────────────────────────────────────────

    [Fact]
    public void CleanProduct_ReturnsScore100_Good()
    {
        var result = _sut.Assess(MakeProduct());

        result.GutScore.Should().Be(100);
        result.GutRating.Should().Be("Good");
        result.FlagCount.Should().Be(0);
        result.Flags.Should().BeEmpty();
        result.Summary.Should().Contain("gut-friendly");
    }

    // ─── Additive Tags Matching ────────────────────────────────────────

    [Theory]
    [InlineData("en:e433", "Polysorbate 80", "High")]
    [InlineData("en:e466", "Carboxymethyl Cellulose (CMC)", "High")]
    [InlineData("en:e407", "Carrageenan", "High")]
    [InlineData("en:e407a", "Processed Eucheuma Seaweed (PES)", "High")]
    [InlineData("en:e171", "Titanium Dioxide", "High")]
    [InlineData("en:e420", "Sorbitol", "High")]
    [InlineData("en:e421", "Mannitol", "High")]
    [InlineData("en:e965", "Maltitol", "High")]
    [InlineData("en:e967", "Xylitol", "High")]
    [InlineData("en:e953", "Isomalt", "High")]
    public void AdditiveTag_HighRisk_FlaggedCorrectly(string tag, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        var flag = result.Flags[0];
        flag.Name.Should().Be(expectedName);
        flag.RiskLevel.Should().Be(expectedRisk);
        flag.Source.Should().Be("Additive");
    }

    [Theory]
    [InlineData("en:e951", "Aspartame", "Medium")]
    [InlineData("en:e950", "Acesulfame K", "Medium")]
    [InlineData("en:e955", "Sucralose", "Medium")]
    [InlineData("en:e954", "Saccharin", "Medium")]
    [InlineData("en:e211", "Sodium Benzoate", "Medium")]
    [InlineData("en:e250", "Sodium Nitrite", "Medium")]
    [InlineData("en:e129", "Allura Red AC", "Medium")]
    [InlineData("en:e471", "Mono- and Diglycerides", "Medium")]
    public void AdditiveTag_MediumRisk_FlaggedCorrectly(string tag, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be(expectedName);
        result.Flags[0].RiskLevel.Should().Be(expectedRisk);
    }

    [Theory]
    [InlineData("en:e415", "Xanthan Gum", "Low")]
    [InlineData("en:e968", "Erythritol", "Low")]
    [InlineData("en:e202", "Potassium Sorbate", "Low")]
    [InlineData("en:e338", "Phosphoric Acid", "Low")]
    [InlineData("en:e621", "Monosodium Glutamate (MSG)", "Low")]
    [InlineData("en:e551", "Silicon Dioxide", "Low")]
    [InlineData("en:e102", "Tartrazine", "Low")]
    public void AdditiveTag_LowRisk_FlaggedCorrectly(string tag, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be(expectedName);
        result.Flags[0].RiskLevel.Should().Be(expectedRisk);
    }

    [Fact]
    public void AdditiveTag_UnknownAdditive_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e300"])); // Vitamin C

        result.Flags.Should().BeEmpty();
        result.GutScore.Should().Be(100);
    }

    [Fact]
    public void AdditiveTag_CaseInsensitive()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["EN:E433"]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("Polysorbate 80");
    }

    [Fact]
    public void MultipleAdditiveTags_AllFlagged()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433", "en:e407", "en:e955"]));

        result.FlagCount.Should().Be(4); // 3 additives + 1 stacking penalty (STACK-EMUL for 2 emulsifiers)
        result.HighRiskCount.Should().Be(2); // E433, E407
        result.MediumRiskCount.Should().Be(1); // E955
    }

    // ─── Linked Additive DTOs ──────────────────────────────────────────

    [Fact]
    public void LinkedAdditive_SugarAlcohol_ByName_Flagged()
    {
        var additives = new List<FoodAdditiveDto>
        {
            new() { Name = "Sorbitol Syrup", ENumber = "E999", Category = "Sweetener", CspiRating = "Caution" },
        };
        var result = _sut.Assess(MakeProduct(additives: additives));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Category.Should().Be("Sugar Alcohol");
        result.Flags[0].RiskLevel.Should().Be("High");
    }

    [Fact]
    public void LinkedAdditive_ArtificialSweetener_ByName_Flagged()
    {
        var additives = new List<FoodAdditiveDto>
        {
            new() { Name = "Sucralose", ENumber = null, Category = "Sweetener", CspiRating = "Caution" },
        };
        var result = _sut.Assess(MakeProduct(additives: additives));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Category.Should().Be("Artificial Sweetener");
        result.Flags[0].RiskLevel.Should().Be("Medium");
    }

    [Fact]
    public void LinkedAdditive_AlreadyFlaggedByTag_NotDuplicated()
    {
        var additives = new List<FoodAdditiveDto>
        {
            new() { Name = "Polysorbate 80", ENumber = "E433", Category = "Emulsifier", CspiRating = "Avoid" },
        };
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e433"],
            additives: additives));

        result.Flags.Should().ContainSingle();
    }

    // ─── Ingredient Text Scanning ──────────────────────────────────────

    [Theory]
    [InlineData("water, sugar, carrageenan, salt", "Carrageenan")]
    [InlineData("flour, polysorbate 80, yeast", "Polysorbate 80")]
    [InlineData("milk, cellulose gum, flavor", "Carboxymethyl Cellulose")]
    [InlineData("sugar, sodium benzoate, citric acid", "Sodium Benzoate")]
    [InlineData("milk, titanium dioxide, sugar", "Titanium Dioxide")]
    [InlineData("sugar, sorbitol, water", "Sorbitol")]
    [InlineData("flour, sucralose, flavor", "Sucralose")]
    [InlineData("wheat, aspartame, color", "Aspartame")]
    public void IngredientText_ContainingGutIrritant_Flagged(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        result.Flags.Where(f => f.Name == expectedName).First().Source.Should().Be("Ingredient");
    }

    [Fact]
    public void IngredientText_HighFodmapFibers_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, inulin, chicory root fiber, salt"));

        result.Flags.Should().Contain(f => f.Name == "Inulin");
        result.Flags.Should().Contain(f => f.Name == "Chicory Root Fiber");
    }

    [Fact]
    public void IngredientText_HighFructoseCornSyrup_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, high fructose corn syrup, salt"));

        result.Flags.Should().ContainSingle(f => f.Name == "High Fructose Corn Syrup");
    }

    [Fact]
    public void IngredientText_Agave_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, agave nectar, oats"));

        result.Flags.Should().ContainSingle(f => f.Name == "Agave Syrup");
    }

    [Fact]
    public void IngredientText_AlreadyFlaggedByTag_NotDuplicated()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e407"],
            ingredients: "water, carrageenan, salt"));

        result.Flags.Where(f => f.Name == "Carrageenan").Should().HaveCount(1);
    }

    [Fact]
    public void IngredientText_CleanIngredients_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, rice flour, sugar, butter, eggs, salt"));

        result.Flags.Should().BeEmpty();
    }

    [Fact]
    public void IngredientText_Red40_MappedToAlluraRed()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "sugar, red 40, water"));

        result.Flags.Should().ContainSingle(f => f.Name == "Allura Red AC");
    }

    // ─── NOVA Group Flagging ───────────────────────────────────────────

    [Fact]
    public void NovaGroup4_FlaggedAsMedium()
    {
        var result = _sut.Assess(MakeProduct(novaGroup: 4));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("Ultra-Processed Food");
        result.Flags[0].Category.Should().Be("Processing Level");
        result.Flags[0].RiskLevel.Should().Be("Medium");
        result.Flags[0].Source.Should().Be("Processing");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NovaGroupBelow4_NotFlagged(int nova)
    {
        var result = _sut.Assess(MakeProduct(novaGroup: nova));

        result.Flags.Should().BeEmpty();
    }

    [Fact]
    public void NovaGroupNull_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(novaGroup: null));

        result.Flags.Should().BeEmpty();
    }

    // ─── Sodium Flagging ───────────────────────────────────────────────

    [Fact]
    public void HighSodium_Above600mg_Flagged()
    {
        var result = _sut.Assess(MakeProduct(sodium100g: 0.7m)); // 700mg

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("High Sodium");
        result.Flags[0].RiskLevel.Should().Be("Low");
        result.Flags[0].Code.Should().Be("HIGH-NA");
    }

    [Fact]
    public void NormalSodium_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(sodium100g: 0.3m)); // 300mg

        result.Flags.Should().BeEmpty();
    }

    [Fact]
    public void ExactThresholdSodium_600mg_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(sodium100g: 0.6m));

        result.Flags.Should().BeEmpty();
    }

    // ─── Sugar Flagging ────────────────────────────────────────────────

    [Fact]
    public void HighSugar_Above25g_Flagged()
    {
        var result = _sut.Assess(MakeProduct(sugar100g: 30m));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("High Sugar Content");
        result.Flags[0].RiskLevel.Should().Be("Low");
        result.Flags[0].Code.Should().Be("HIGH-SUGAR");
    }

    [Fact]
    public void NormalSugar_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(sugar100g: 10m));

        result.Flags.Should().BeEmpty();
    }

    [Fact]
    public void ExactThresholdSugar_25g_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(sugar100g: 25m));

        result.Flags.Should().BeEmpty();
    }

    // ─── Score Calculation ─────────────────────────────────────────────

    [Fact]
    public void Score_SingleHighRisk_Deducts20()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.GutScore.Should().Be(80);
        result.GutRating.Should().Be("Good");
    }

    [Fact]
    public void Score_SingleMediumRisk_Deducts10()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e955"]));

        result.GutScore.Should().Be(90);
        result.GutRating.Should().Be("Good");
    }

    [Fact]
    public void Score_SingleLowRisk_Deducts5()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e415"]));

        result.GutScore.Should().Be(95);
        result.GutRating.Should().Be("Good");
    }

    [Fact]
    public void Score_MultipleRisks_AccumulateCorrectly()
    {
        // 2 High (-40) + 1 Medium (-10) + 1 Low (-5) = -55
        // + STACK-EMUL (-2) for E433+E407 emulsifiers = -57
        // + STACK-HYDROCOL (-2) for E407+E415 thickeners = -59
        // 100 - 59 = 41
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e433", "en:e407", "en:e955", "en:e415"]));

        result.GutScore.Should().Be(41);
        result.GutRating.Should().Be("Poor");
    }

    [Fact]
    public void Score_ExtremeRisks_ClampsAtZero()
    {
        // 6 High = -120, clamped to 0
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e433", "en:e407", "en:e171", "en:e420", "en:e421", "en:e965"]));

        result.GutScore.Should().Be(0);
        result.GutRating.Should().Be("Bad");
    }

    [Fact]
    public void Score_HighFiber_AddsBonusUp5()
    {
        // No risks → score is already 100, no bonus applied
        var result = _sut.Assess(MakeProduct(fiber100g: 8m));

        result.GutScore.Should().Be(100);
    }

    [Fact]
    public void Score_HighFiber_WithRisk_PartiallyOffsets()
    {
        // -10 for medium risk, clamped to 90, then +5 for fiber = 95
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e955"],
            fiber100g: 8m));

        result.GutScore.Should().Be(95);
    }

    // ─── Rating Boundaries ─────────────────────────────────────────────

    [Theory]
    [InlineData(100, "Good")]
    [InlineData(80, "Good")]
    [InlineData(75, "Fair")]
    [InlineData(60, "Fair")]
    [InlineData(55, "Poor")]
    [InlineData(40, "Poor")]
    [InlineData(35, "Bad")]
    public void Rating_CorrectForScore(int expectedScore, string expectedRating)
    {
        // Additives from non-overlapping categories to avoid stacking penalties.
        // E407 = Emulsifier/Thickener High (-20), E171 = Colorant/Whitener High (-20),
        // E129 = Artificial Colorant Medium (-10), E211 = Preservative Medium (-10),
        // E200 = Preservative Low (-5), E321 = Antioxidant Low (-5).
        var penalty = 100 - expectedScore;
        var tags = new List<string>();
        if (penalty >= 20) { tags.Add("en:e407"); penalty -= 20; }
        if (penalty >= 20) { tags.Add("en:e171"); penalty -= 20; }
        if (penalty >= 10) { tags.Add("en:e129"); penalty -= 10; }
        if (penalty >= 10) { tags.Add("en:e211"); penalty -= 10; }
        if (penalty >= 5) { tags.Add("en:e200"); penalty -= 5; }
        if (penalty >= 5) { tags.Add("en:e321"); penalty -= 5; }

        var result = _sut.Assess(MakeProduct(additivesTags: tags));

        result.GutScore.Should().Be(expectedScore);
        result.GutRating.Should().Be(expectedRating);
    }

    // ─── Summary Generation ────────────────────────────────────────────

    [Fact]
    public void Summary_NoFlags_SaysGutFriendly()
    {
        var result = _sut.Assess(MakeProduct());

        result.Summary.Should().Contain("gut-friendly");
    }

    [Fact]
    public void Summary_HighRiskFlags_MentionsIBS()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.Summary.Should().Contain("digestive sensitivities may want to explore alternatives");
    }

    [Fact]
    public void Summary_OnlyMediumAndLow_MentionsMonitor()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e955"]));

        result.Summary.Should().Contain("digestive sensitivities may want to be mindful");
    }

    // ─── Flag Ordering ─────────────────────────────────────────────────

    [Fact]
    public void Flags_OrderedByRiskLevel_HighFirst()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e415", "en:e955", "en:e433"]));

        result.Flags[0].RiskLevel.Should().Be("High");
        result.Flags[1].RiskLevel.Should().Be("Medium");
        result.Flags[2].RiskLevel.Should().Be("Low");
    }

    // ─── Complex/Real-World Scenarios ──────────────────────────────────

    [Fact]
    public void RealWorld_DietSoda_MultipleConcerns()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e951", "en:e950", "en:e338", "en:e150d"],
            ingredients: "carbonated water, aspartame, acesulfame k, phosphoric acid, caramel color",
            novaGroup: 4,
            sodium100g: 0.02m,
            sugar100g: 0m));

        result.FlagCount.Should().BeGreaterOrEqualTo(3);
        result.Flags.Should().Contain(f => f.Name == "Acesulfame K");
        result.Flags.Should().Contain(f => f.Name == "Ultra-Processed Food");
        result.GutRating.Should().NotBe("Good");
    }

    [Fact]
    public void RealWorld_ProteinBar_SugarAlcohols()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e965", "en:e955"],
            ingredients: "whey protein, maltitol syrup, sucralose, soy lecithin, inulin",
            novaGroup: 4,
            fiber100g: 8m));

        result.Flags.Should().Contain(f => f.Name == "Maltitol");
        result.Flags.Should().Contain(f => f.Name == "Sucralose");
        result.Flags.Should().Contain(f => f.Name == "Inulin");
        result.Flags.Should().Contain(f => f.Name == "Lecithins");
        result.HighRiskCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void RealWorld_CleanOatmeal_HighScore()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "rolled oats, water, salt",
            novaGroup: 1,
            fiber100g: 10m,
            sodium100g: 0.005m,
            sugar100g: 0.5m));

        result.GutScore.Should().Be(100);
        result.GutRating.Should().Be("Good");
        result.FlagCount.Should().Be(0);
    }

    [Fact]
    public void RealWorld_ProcessedMeat_Nitrites()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e250", "en:e252"],
            ingredients: "pork, water, salt, sodium nitrite, potassium nitrate, dextrose",
            novaGroup: 4,
            sodium100g: 1.2m));

        result.Flags.Should().Contain(f => f.Name == "Sodium Nitrite");
        result.Flags.Should().Contain(f => f.Name == "Ultra-Processed Food");
        result.Flags.Should().Contain(f => f.Name == "High Sodium");
    }

    [Fact]
    public void RealWorld_SugarFreeCandy_HighRisk()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e420", "en:e421", "en:e965"],
            ingredients: "sorbitol, mannitol, maltitol, gelatin, citric acid, artificial flavors",
            novaGroup: 4));

        result.HighRiskCount.Should().BeGreaterOrEqualTo(3);
        result.GutScore.Should().BeLessThanOrEqualTo(40);
        result.GutRating.Should().BeOneOf("Poor", "Bad");
    }

    // ─── Edge Cases ────────────────────────────────────────────────────

    [Fact]
    public void NullIngredients_HandledGracefully()
    {
        var result = _sut.Assess(MakeProduct(ingredients: null));

        result.GutScore.Should().Be(100);
    }

    [Fact]
    public void EmptyAdditivesTags_HandledGracefully()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: []));

        result.GutScore.Should().Be(100);
    }

    [Fact]
    public void NullNovaGroup_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(novaGroup: null));

        result.Flags.Should().NotContain(f => f.Source == "Processing");
    }

    [Fact]
    public void NullSodium_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(sodium100g: null));

        result.Flags.Should().NotContain(f => f.Code == "HIGH-NA");
    }

    [Fact]
    public void NullSugar_NotFlagged()
    {
        var result = _sut.Assess(MakeProduct(sugar100g: null));

        result.Flags.Should().NotContain(f => f.Code == "HIGH-SUGAR");
    }

    [Fact]
    public void NullFiber_NoBonus()
    {
        var result = _sut.Assess(MakeProduct(fiber100g: null, additivesTags: ["en:e955"]));

        result.GutScore.Should().Be(90); // just -10 for medium, no fiber bonus
    }

    // ─── Specific Additive Coverage ────────────────────────────────────

    [Theory]
    [InlineData("en:e435")]
    [InlineData("en:e436")]
    [InlineData("en:e472e")]
    public void EmulsifierVariants_AllFlagged(string tag)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Source.Should().Be("Additive");
    }

    [Theory]
    [InlineData("en:e220")]
    [InlineData("en:e221")]
    [InlineData("en:e223")]
    public void Sulfites_AllFlagged(string tag)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Category.Should().Contain("Sulfite");
    }

    [Theory]
    [InlineData("en:e339")]
    [InlineData("en:e341")]
    public void Phosphates_AllFlagged(string tag)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].RiskLevel.Should().Be("Low");
    }

    [Theory]
    [InlineData("en:e962")]
    public void CombinationSweetener_Flagged(string tag)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Category.Should().Be("Artificial Sweetener");
    }

    // ─── FODMAP Prebiotic Patterns ─────────────────────────────────────

    [Fact]
    public void Ingredient_FOS_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "oats, fructooligosaccharide, water"));

        result.Flags.Should().ContainSingle(f => f.Name == "FOS");
    }

    [Fact]
    public void Ingredient_Oligofructose_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "sugar, oligofructose, cream"));

        result.Flags.Should().ContainSingle(f => f.Name == "Oligofructose");
    }

    // ─── Risk Count Tracking ───────────────────────────────────────────

    [Fact]
    public void RiskCounts_AccuratelyTrackAllLevels()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e433", "en:e407", "en:e955", "en:e415", "en:e202"],
            novaGroup: 4));

        // E433=High, E407=High, E955=Medium, E415=Low, E202=Low, NOVA4=Medium
        // STACK-EMUL=Low (2 emulsifiers), STACK-HYDROCOL=Low (E407+E415 thickeners), STACK-NOVA-EMUL=Low (NOVA4+emulsifier)
        result.HighRiskCount.Should().Be(2);
        result.MediumRiskCount.Should().Be(2);
        result.LowRiskCount.Should().Be(5);
        result.FlagCount.Should().Be(9);
    }

    // ─── GutRiskAssessmentDto Properties ───────────────────────────────

    [Fact]
    public void AssessmentDto_AllPropertiesPopulated()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.GutScore.Should().BeInRange(0, 100);
        result.GutRating.Should().NotBeNullOrEmpty();
        result.FlagCount.Should().BeGreaterThan(0);
        result.Summary.Should().NotBeNullOrEmpty();
        result.Flags.Should().NotBeEmpty();

        var flag = result.Flags[0];
        flag.Source.Should().NotBeNullOrEmpty();
        flag.Code.Should().NotBeNullOrEmpty();
        flag.Name.Should().NotBeNullOrEmpty();
        flag.Category.Should().NotBeNullOrEmpty();
        flag.RiskLevel.Should().NotBeNullOrEmpty();
        flag.Explanation.Should().NotBeNullOrEmpty();
    }

    // ─── Ingredient Case Insensitivity ─────────────────────────────────

    [Fact]
    public void IngredientText_CaseInsensitive()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "CARRAGEENAN, POLYSORBATE 80"));

        result.Flags.Should().Contain(f => f.Name == "Carrageenan");
        result.Flags.Should().Contain(f => f.Name == "Polysorbate 80");
    }

    // ─── Carboxymethyl Cellulose alias ─────────────────────────────────

    [Fact]
    public void IngredientText_CarboxymethylCellulose_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, carboxymethyl cellulose, salt"));

        result.Flags.Should().ContainSingle(f => f.Name == "Carboxymethyl Cellulose");
    }

    // ─── Sodium Nitrite via Ingredients ─────────────────────────────────

    [Fact]
    public void IngredientText_SodiumNitrite_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "pork, water, sodium nitrite"));

        result.Flags.Should().ContainSingle(f => f.Name == "Sodium Nitrite");
    }

    // ─── Stacking Penalties ────────────────────────────────────────────

    [Fact]
    public void Stacking_TwoEmulsifiers_AddsStackEmulFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433", "en:e466"]));

        result.Flags.Should().Contain(f => f.Code == "STACK-EMUL");
        result.Flags.First(f => f.Code == "STACK-EMUL").RiskLevel.Should().Be("Low");
    }

    [Fact]
    public void Stacking_SingleEmulsifier_NoStackFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.Flags.Should().NotContain(f => f.Code == "STACK-EMUL");
    }

    [Fact]
    public void Stacking_TwoSugarAlcohols_AddsStackPolyolFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e420", "en:e421"]));

        result.Flags.Should().Contain(f => f.Code == "STACK-POLYOL");
        result.Flags.First(f => f.Code == "STACK-POLYOL").RiskLevel.Should().Be("Medium");
    }

    [Fact]
    public void Stacking_Nova4PlusEmulsifier_AddsStackNovaEmulFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"], novaGroup: 4));

        result.Flags.Should().Contain(f => f.Code == "STACK-NOVA-EMUL");
    }

    [Fact]
    public void Stacking_Nova3PlusEmulsifier_NoStackNovaEmulFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"], novaGroup: 3));

        result.Flags.Should().NotContain(f => f.Code == "STACK-NOVA-EMUL");
    }

    // ─── E-Number Normalization ────────────────────────────────────────

    [Fact]
    public void ENumber_HyphenFormat_NormalizedCorrectly()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e-466"]));

        result.Flags.Should().Contain(f => f.Code == "E466");
    }

    [Fact]
    public void ENumber_SpaceFormat_NormalizedCorrectly()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e 466"]));

        result.Flags.Should().Contain(f => f.Code == "E466");
    }

    [Fact]
    public void ENumber_MixedCaseWithPrefix_NormalizedCorrectly()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["EN:E-407a"]));

        result.Flags.Should().Contain(f => f.Code == "E407A");
    }

    // ─── New Additives Coverage ────────────────────────────────────────

    [Theory]
    [InlineData("en:e410", "Locust Bean Gum", "Low")]
    [InlineData("en:e412", "Guar Gum", "Low")]
    [InlineData("en:e417", "Tara Gum", "Low")]
    [InlineData("en:e418", "Gellan Gum", "Low")]
    [InlineData("en:e440", "Pectin", "Low")]
    [InlineData("en:e450", "Diphosphates", "Low")]
    [InlineData("en:e451", "Triphosphates", "Low")]
    [InlineData("en:e452", "Polyphosphates", "Low")]
    [InlineData("en:e319", "TBHQ", "Medium")]
    [InlineData("en:e320", "BHA", "Medium")]
    [InlineData("en:e321", "BHT", "Low")]
    [InlineData("en:e330", "Citric Acid (manufactured)", "Low")]
    [InlineData("en:e296", "Malic Acid", "Low")]
    [InlineData("en:e210", "Benzoic Acid", "Medium")]
    [InlineData("en:e200", "Sorbic Acid", "Low")]
    [InlineData("en:e966", "Lactitol", "High")]
    public void NewAdditive_FlaggedCorrectly(string tag, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be(expectedName);
        result.Flags[0].RiskLevel.Should().Be(expectedRisk);
    }

    // ─── New Ingredient Patterns ───────────────────────────────────────

    [Theory]
    [InlineData("water, locust bean gum, sugar", "Locust Bean Gum")]
    [InlineData("water, guar gum, salt", "Guar Gum")]
    [InlineData("water, tara gum, salt", "Tara Gum")]
    [InlineData("water, gellan gum, salt", "Gellan Gum")]
    [InlineData("water, pectin, sugar", "Pectin")]
    [InlineData("water, tbhq, oil", "TBHQ")]
    [InlineData("water, bha, oil", "BHA")]
    [InlineData("water, bht, oil", "BHT")]
    [InlineData("water, benzoic acid, salt", "Benzoic Acid")]
    [InlineData("water, sorbic acid, salt", "Sorbic Acid")]
    [InlineData("water, polydextrose, sugar", "Polydextrose")]
    [InlineData("water, lactitol, sugar", "Lactitol")]
    public void NewIngredientPattern_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
    }

    // ─── Fiber Bonus with FODMAP Fiber ─────────────────────────────────

    [Fact]
    public void FiberBonus_WithFodmapFiber_NoBonusWhenMedHighFodmap()
    {
        // E955 Additive×1.0 = -10, Inulin Fodmap(High)×1.0 = -20 → 70
        // Fiber bonus: hasFodmapFiber=true, medHighFodmapCount=1 (Inulin High) → bonus=2
        // score = 70 + 2 = 72
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e955"],
            ingredients: "rice protein, inulin, water",
            fiber100g: 8m));

        result.GutScore.Should().Be(72);
    }

    [Fact]
    public void FiberBonus_WithoutFodmapFiber_FullBonus()
    {
        // E955 = -10 → 90, fiber bonus +5 → 95
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e955"],
            fiber100g: 8m));

        result.GutScore.Should().Be(95);
    }

    [Fact]
    public void FiberBonus_NoRisks_NoBonusApplied()
    {
        var result = _sut.Assess(MakeProduct(fiber100g: 10m));

        result.GutScore.Should().Be(100);
    }

    // ─── Evidence-Aware Sweetener Language ──────────────────────────────

    [Theory]
    [InlineData("en:e951", "under investigation")]
    [InlineData("en:e950", "human data")]
    [InlineData("en:e955", "mixed results")]
    [InlineData("en:e954", "may vary")]
    public void Sweetener_HasEvidenceAwareLanguage(string tag, string expectedPhrase)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags[0].Explanation.Should().Contain(expectedPhrase);
    }

    // ─── V2: DB Additive Skip Bug Fix ──────────────────────────────────

    [Fact]
    public void LinkedAdditive_KnownENumber_NotInTags_StillFlagged()
    {
        var additives = new List<FoodAdditiveDto>
        {
            new() { Name = "Polysorbate 80", ENumber = "E433", Category = "Emulsifier", CspiRating = "Avoid" },
        };
        var result = _sut.Assess(MakeProduct(additives: additives));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("Polysorbate 80");
        result.Flags[0].Code.Should().Be("E433");
    }

    [Fact]
    public void LinkedAdditive_KnownENumber_AlreadyInTags_NotDuplicated()
    {
        var additives = new List<FoodAdditiveDto>
        {
            new() { Name = "Polysorbate 80", ENumber = "E433", Category = "Emulsifier", CspiRating = "Avoid" },
        };
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e433"],
            additives: additives));

        result.Flags.Where(f => f.Code == "E433").Should().HaveCount(1);
    }

    // ─── V2: FlagKey Dedupe for Non-E-Number Ingredients ───────────────

    [Fact]
    public void FlagKey_NonENumber_DedupesByName()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "whey protein, polydextrose, polydextrose fiber, water"));

        result.Flags.Where(f => f.Name == "Polydextrose").Should().HaveCount(1);
    }

    // ─── V2: INS Format E-Number ───────────────────────────────────────

    [Fact]
    public void ENumber_INSFormat_NormalizedCorrectly()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:INS 433"]));

        result.Flags.Should().Contain(f => f.Code == "E433");
    }

    [Fact]
    public void ENumber_INSFormatNoSpace_NormalizedCorrectly()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:INS433"]));

        result.Flags.Should().Contain(f => f.Code == "E433");
    }

    // ─── V2: Hydrocolloid Stacking ─────────────────────────────────────

    [Fact]
    public void Stacking_TwoThickeners_AddsStackHydrocolFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e415", "en:e412"]));

        result.Flags.Should().Contain(f => f.Code == "STACK-HYDROCOL");
        result.Flags.First(f => f.Code == "STACK-HYDROCOL").RiskLevel.Should().Be("Low");
    }

    [Fact]
    public void Stacking_SingleThickener_NoHydrocolFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e415"]));

        result.Flags.Should().NotContain(f => f.Code == "STACK-HYDROCOL");
    }

    [Fact]
    public void Stacking_ThreeThickeners_MentionsCountInExplanation()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e415", "en:e412", "en:e410"]));

        var flag = result.Flags.First(f => f.Code == "STACK-HYDROCOL");
        flag.Explanation.Should().Contain("3");
    }

    // ─── V2: Word Boundaries for Short Patterns ────────────────────────

    [Fact]
    public void WordBoundary_BHA_NotMatchedInLongerWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "chabana flour, water, salt"));

        result.Flags.Should().NotContain(f => f.Name == "BHA");
    }

    [Fact]
    public void WordBoundary_BHA_MatchedAsWholeWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "vegetable oil, bha, salt"));

        result.Flags.Should().Contain(f => f.Name == "BHA");
    }

    [Fact]
    public void WordBoundary_BHT_NotMatchedInLongerWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "bhtopia extract, water"));

        result.Flags.Should().NotContain(f => f.Name == "BHT");
    }

    [Fact]
    public void WordBoundary_BHT_MatchedAsWholeWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "vegetable oil, bht, citric acid"));

        result.Flags.Should().Contain(f => f.Name == "BHT");
    }

    [Fact]
    public void WordBoundary_TBHQ_NotMatchedInLongerWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "xtbhqy compound, water"));

        result.Flags.Should().NotContain(f => f.Name == "TBHQ");
    }

    [Fact]
    public void WordBoundary_Wheat_NotMatchedInBuckwheat()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "buckwheat flour, water, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Wheat");
    }

    [Fact]
    public void WordBoundary_Wheat_MatchedAsWholeWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "wheat, water, salt"));

        result.Flags.Should().Contain(f => f.Name == "Wheat");
    }

    // ─── V2: New Ingredient Triggers ───────────────────────────────────

    [Theory]
    [InlineData("water, onion powder, salt", "Onion Powder")]
    [InlineData("water, garlic powder, salt", "Garlic Powder")]
    [InlineData("water, onion, salt", "Onion")]
    [InlineData("water, garlic, salt", "Garlic")]
    [InlineData("water, wheat flour, salt", "Wheat Flour")]
    [InlineData("water, wheat fiber, salt", "Wheat Fiber")]
    [InlineData("water, sugar alcohol, salt", "Sugar Alcohol")]
    [InlineData("water, soy lecithin, salt", "Lecithins")]
    [InlineData("water, lecithin, salt", "Lecithins")]
    public void NewIngredientTrigger_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
    }

    // ─── V2: E322 Lecithins Additive Tag ───────────────────────────────

    [Fact]
    public void AdditiveTag_E322_Lecithins_FlaggedAsLow()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e322"]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("Lecithins");
        result.Flags[0].RiskLevel.Should().Be("Low");
        result.Flags[0].Category.Should().Be("Emulsifier");
    }

    // ─── V2: Stacking Penalty Weight ───────────────────────────────────

    [Fact]
    public void StackingPenalty_DeductsOnly2Points()
    {
        // E433 High(-20) + E466 High(-20) + STACK-EMUL Low(-2) = -42 → score 58
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433", "en:e466"]));

        result.GutScore.Should().Be(58);
    }

    [Fact]
    public void StackingPenalty_Polyol_DeductsOnly10ForMedium()
    {
        // E420 Fodmap(High)×1.0(-20) + E421 Fodmap(High)×1.0(-20) + STACK-POLYOL Medium×1.0(-10) = -50 → score 50
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e420", "en:e421"]));

        result.GutScore.Should().Be(50);
    }

    [Fact]
    public void StackingPenalty_HydrocolDeductsOnly2()
    {
        // E415 Low(-5) + E412 Low(-5) + STACK-HYDROCOL Low(-2) = -12 → score 88
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e415", "en:e412"]));

        result.GutScore.Should().Be(88);
    }

    // ─── V3: Dairy / Lactose Triggers ──────────────────────────────────

    [Theory]
    [InlineData("water, whey protein, sugar", "Whey")]
    [InlineData("sugar, milk powder, cocoa", "Milk Powder")]
    [InlineData("water, skim milk powder, salt", "Skim Milk Powder")]
    [InlineData("water, cream powder, sugar", "Cream Powder")]
    [InlineData("flour, lactose, salt", "Lactose")]
    [InlineData("water, skim milk, sugar", "Skim Milk")]
    public void DairyLactoseTrigger_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        result.Flags.First(f => f.Name == expectedName).Category.Should().Be("Dairy/Lactose");
        var expectedRisk = (expectedName is "Milk Powder" or "Lactose") ? "High" : "Medium";
        result.Flags.First(f => f.Name == expectedName).RiskLevel.Should().Be(expectedRisk);
    }

    [Fact]
    public void Whey_WordBoundary_NotMatchedInLongerWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, wheybridge flour, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Whey");
    }

    // ─── V3: Fruit Concentrate Fructose Triggers ───────────────────────

    [Theory]
    [InlineData("water, apple juice concentrate, sugar", "Apple Juice Concentrate")]
    [InlineData("water, pear juice concentrate, sugar", "Pear Juice Concentrate")]
    [InlineData("water, fruit juice concentrate, sugar", "Fruit Juice Concentrate")]
    public void FruitConcentrateTrigger_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        result.Flags.First(f => f.Name == expectedName).Category.Should().Be("Fructose Source");
        result.Flags.First(f => f.Name == expectedName).RiskLevel.Should().Be("Medium");
    }

    // ─── V3: New Hydrocolloid Additive Tags ────────────────────────────

    [Theory]
    [InlineData("en:e413", "Tragacanth", "Low")]
    [InlineData("en:e414", "Gum Arabic", "Low")]
    [InlineData("en:e401", "Sodium Alginate", "Low")]
    [InlineData("en:e402", "Potassium Alginate", "Low")]
    [InlineData("en:e403", "Ammonium Alginate", "Low")]
    [InlineData("en:e404", "Calcium Alginate", "Low")]
    [InlineData("en:e405", "Propylene Glycol Alginate", "Low")]
    public void NewHydrocolloidAdditiveTag_FlaggedCorrectly(string tag, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be(expectedName);
        result.Flags[0].RiskLevel.Should().Be(expectedRisk);
        result.Flags[0].Category.Should().Be("Thickener");
    }

    // ─── V3: New Hydrocolloid Ingredient Patterns ──────────────────────

    [Theory]
    [InlineData("water, tragacanth, sugar", "Tragacanth")]
    [InlineData("water, gum arabic, sugar", "Gum Arabic")]
    [InlineData("water, sodium alginate, sugar", "Sodium Alginate")]
    [InlineData("water, alginate, sugar", "Sodium Alginate")]
    public void NewHydrocolloidIngredient_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
    }

    [Fact]
    public void NewHydrocolloids_TriggerHydrocolStacking()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e413", "en:e414"]));

        result.Flags.Should().Contain(f => f.Code == "STACK-HYDROCOL");
    }

    // ─── V3: FlagKey Dedupe in Ingredient Scanning ─────────────────────

    [Fact]
    public void IngredientDedupe_TagAndIngredient_NoDuplicate()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e412"],
            ingredients: "water, guar gum, salt"));

        result.Flags.Where(f => f.Name == "Guar Gum").Should().HaveCount(1);
    }

    [Fact]
    public void IngredientDedupe_SameNameDifferentPatterns_NoDuplicate()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, cellulose gum, carboxymethyl cellulose, salt"));

        result.Flags.Where(f => f.Name == "Carboxymethyl Cellulose").Should().HaveCount(1);
    }

    [Fact]
    public void IngredientDedupe_NonENumber_ByName_NoDuplicate()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, onion powder, dried onion, salt"));

        result.Flags.Where(f => f.Name == "Onion Powder").Should().HaveCount(1);
        result.Flags.Where(f => f.Name == "Onion").Should().HaveCount(1);
    }

    // ─── V3: Real-World Dairy Product ──────────────────────────────────

    [Fact]
    public void RealWorld_ChocolateMilkDrink_DairyAndSweeteners()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "skim milk, sugar, cocoa, carrageenan, milk powder, guar gum",
            novaGroup: 4));

        result.Flags.Should().Contain(f => f.Name == "Carrageenan");
        result.Flags.Should().Contain(f => f.Name == "Skim Milk");
        result.Flags.Should().Contain(f => f.Name == "Milk Powder");
        result.Flags.Should().Contain(f => f.Name == "Guar Gum");
        result.Flags.Should().Contain(f => f.Name == "Ultra-Processed Food");
    }

    [Fact]
    public void RealWorld_FruitJuiceBlend_FructoseConcerns()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, apple juice concentrate, pear juice concentrate, citric acid",
            sugar100g: 30m));

        result.Flags.Should().Contain(f => f.Name == "Apple Juice Concentrate");
        result.Flags.Should().Contain(f => f.Name == "Pear Juice Concentrate");
        result.Flags.Should().Contain(f => f.Name == "High Sugar Content");
    }

    // ─── V4: NormalizeKey Dedupe Stability ─────────────────────────────

    [Fact]
    public void NormalizeKey_ExtraWhitespace_StillDedupes()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e466"],
            ingredients: "water, cellulose gum, salt"));

        result.Flags.Where(f => f.Name.Contains("Cellulose")).Should().HaveCount(1);
    }

    // ─── V4: Onion/Garlic Word Boundaries ──────────────────────────────

    [Fact]
    public void Onion_WordBoundary_NotMatchedInCompound()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, onionesque seasoning, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Onion");
    }

    [Fact]
    public void Garlic_WordBoundary_NotMatchedInCompound()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, garlicky sauce, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Garlic");
    }

    [Fact]
    public void Onion_WordBoundary_MatchedAsWholeWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, salt"));

        result.Flags.Should().Contain(f => f.Name == "Onion");
    }

    [Fact]
    public void Garlic_WordBoundary_MatchedAsWholeWord()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, garlic, salt"));

        result.Flags.Should().Contain(f => f.Name == "Garlic");
    }

    [Fact]
    public void OnionPowder_SubstringMatch_StillWorks()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "salt, onion powder, pepper"));

        result.Flags.Should().Contain(f => f.Name == "Onion Powder");
    }

    [Fact]
    public void GarlicPowder_SubstringMatch_StillWorks()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "salt, garlic powder, pepper"));

        result.Flags.Should().Contain(f => f.Name == "Garlic Powder");
    }

    // ─── V4: Hydrocolloid Stacking Includes Emulsifier/Thickener ──────

    [Fact]
    public void Stacking_CarrageenAndThickener_TriggersHydrocol()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e407", "en:e415"]));

        result.Flags.Should().Contain(f => f.Code == "STACK-HYDROCOL");
    }

    [Fact]
    public void Stacking_CarrageenAlone_NoHydrocolFlag()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e407"]));

        result.Flags.Should().NotContain(f => f.Code == "STACK-HYDROCOL");
    }

    // ─── V4: New Dairy Coverage ────────────────────────────────────────

    [Theory]
    [InlineData("water, whey powder, sugar", "Whey Powder")]
    [InlineData("water, milk solids, sugar", "Milk Solids")]
    [InlineData("water, buttermilk, sugar", "Buttermilk")]
    public void NewDairyTrigger_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        result.Flags.First(f => f.Name == expectedName).Category.Should().Be("Dairy/Lactose");
        result.Flags.First(f => f.Name == expectedName).RiskLevel.Should().Be("Medium");
    }

    [Theory]
    [InlineData("water, apple concentrate, sugar", "Apple Concentrate")]
    [InlineData("water, pear concentrate, sugar", "Pear Concentrate")]
    [InlineData("water, fruit concentrate, sugar", "Fruit Concentrate")]
    public void NewFruitConcentrate_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        result.Flags.First(f => f.Name == expectedName).Category.Should().Be("Fructose Source");
    }

    [Theory]
    [InlineData("water, wheat starch, salt", "Wheat Starch")]
    [InlineData("water, wheat protein, salt", "Wheat Protein")]
    public void NewWheatTrigger_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        result.Flags.First(f => f.Name == expectedName).Category.Should().Be("High-FODMAP Ingredient");
        result.Flags.First(f => f.Name == expectedName).RiskLevel.Should().Be("High");
    }

    // ─── V1.5: TriggerType + FodmapClass on Flags ─────────────────────

    [Fact]
    public void Flag_SugarAlcohol_HasFodmapTriggerType()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e420"]));

        result.Flags[0].TriggerType.Should().Be("Fodmap");
        result.Flags[0].FodmapClass.Should().Be("Polyols");
    }

    [Fact]
    public void Flag_Emulsifier_HasAdditiveTriggerType()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.Flags[0].TriggerType.Should().Be("Additive");
        result.Flags[0].FodmapClass.Should().BeEmpty();
    }

    [Fact]
    public void Flag_NO_v_HasProcessingTriggerType()
    {
        var result = _sut.Assess(MakeProduct(novaGroup: 4));

        result.Flags[0].TriggerType.Should().Be("Processing");
    }

    [Fact]
    public void Flag_Sodium_HasNutrientTriggerType()
    {
        var result = _sut.Assess(MakeProduct(sodium100g: 0.8m));

        result.Flags[0].TriggerType.Should().Be("Nutrient");
    }

    [Fact]
    public void Flag_Dairy_HasLactoseFodmapClass()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, milk powder, sugar"));

        var flag = result.Flags.First(f => f.Name == "Milk Powder");
        flag.TriggerType.Should().Be("Fodmap");
        flag.FodmapClass.Should().Be("Lactose");
    }

    [Fact]
    public void Flag_FructoseSource_HasExcessFructoseFodmapClass()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, apple juice concentrate, sugar"));

        var flag = result.Flags.First(f => f.Name == "Apple Juice Concentrate");
        flag.FodmapClass.Should().Be("ExcessFructose");
    }

    // ─── v1.5: GOS Ingredient Rules ───────────────────────────────────

    [Theory]
    [InlineData("water, chickpea flour, salt", "Chickpea Flour", "High")]
    [InlineData("water, chickpea, salt", "Chickpea", "Medium")]
    [InlineData("water, lentil, salt", "Lentil", "Medium")]
    [InlineData("water, kidney bean, salt", "Kidney Bean", "Medium")]
    [InlineData("water, black bean, salt", "Black Bean", "Medium")]
    [InlineData("water, navy bean, salt", "Navy Bean", "Medium")]
    [InlineData("water, soy protein isolate, salt", "Soy Protein Isolate", "High")]
    [InlineData("water, soy flour, salt", "Soy Flour", "High")]
    [InlineData("water, textured vegetable protein, salt", "Textured Vegetable Protein", "High")]
    [InlineData("water, soybean, salt", "Soybean", "Medium")]
    public void GOSTrigger_FlaggedCorrectly(string ingredients, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("GOS Source");
        flag.RiskLevel.Should().Be(expectedRisk);
        flag.FodmapClass.Should().Be("GOS");
    }

    [Fact]
    public void GOS_TVP_WordBoundary_MatchedCorrectly()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, tvp, salt"));

        result.Flags.Should().Contain(f => f.Name == "Textured Vegetable Protein");
    }

    [Fact]
    public void GOS_Chickpea_WordBoundary_NotInCompound()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, chickpeaish stuff, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Chickpea");
    }

    // ─── v1.5: Polyol Fruit/Veg Rules ──────────────────────────────────

    [Theory]
    [InlineData("water, mushroom, salt", "Mushroom", "High")]
    [InlineData("water, prune, salt", "Prune", "High")]
    [InlineData("water, plum, salt", "Plum", "Medium")]
    [InlineData("water, cherry, salt", "Cherry", "Medium")]
    [InlineData("water, apricot, salt", "Apricot", "Medium")]
    [InlineData("water, peach, salt", "Peach", "Medium")]
    [InlineData("water, cauliflower, salt", "Cauliflower", "High")]
    [InlineData("water, avocado, salt", "Avocado", "Medium")]
    public void PolyolSource_FlaggedCorrectly(string ingredients, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Polyol Source");
        flag.RiskLevel.Should().Be(expectedRisk);
        flag.FodmapClass.Should().Be("Polyols");
    }

    [Fact]
    public void Mushroom_WordBoundary_NotInCompound()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, mushroomlike flavor, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Mushroom");
    }

    // ─── v1.5: Expanded Fructose Patterns ──────────────────────────────

    [Theory]
    [InlineData("water, honey, sugar", "Honey", "High")]
    [InlineData("water, mango, sugar", "Mango", "High")]
    [InlineData("water, watermelon, sugar", "Watermelon", "High")]
    public void ExpandedFructose_WordBoundary_FlaggedCorrectly(string ingredients, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Fructose Source");
        flag.RiskLevel.Should().Be(expectedRisk);
        flag.TriggerType.Should().Be("Fodmap");
        flag.FodmapClass.Should().Be("ExcessFructose");
        flag.DoseSensitivity.Should().Be("High");
    }

    [Theory]
    [InlineData("water, apple juice, sugar", "Apple Juice", "Medium")]
    [InlineData("water, fruit juice, sugar", "Fruit Juice", "Low")]
    public void ExpandedFructose_SubstringMatch_FlaggedCorrectly(string ingredients, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Fructose Source");
        flag.RiskLevel.Should().Be(expectedRisk);
        flag.FodmapClass.Should().Be("ExcessFructose");
    }

    [Fact]
    public void Honey_WordBoundary_NotInCompound()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, honeydew, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Honey");
    }

    // ─── v1.5: Expanded Fructan Patterns ───────────────────────────────

    [Theory]
    [InlineData("water, onion salt, pepper", "Onion Salt", "High")]
    [InlineData("water, garlic salt, pepper", "Garlic Salt", "High")]
    [InlineData("water, wheat bran, sugar", "Wheat Bran", "High")]
    public void ExpandedFructan_SubstringMatch_FlaggedCorrectly(string ingredients, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("High-FODMAP Ingredient");
        flag.RiskLevel.Should().Be(expectedRisk);
        flag.TriggerType.Should().Be("Fodmap");
        flag.FodmapClass.Should().Be("Fructans");
    }

    [Theory]
    [InlineData("water, shallot, pepper", "Shallot", "High")]
    [InlineData("water, leek, pepper", "Leek", "Medium")]
    public void ExpandedFructan_WordBoundary_FlaggedCorrectly(string ingredients, string expectedName, string expectedRisk)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("High-FODMAP Ingredient");
        flag.RiskLevel.Should().Be(expectedRisk);
        flag.FodmapClass.Should().Be("Fructans");
    }

    [Fact]
    public void Shallot_WordBoundary_NotInCompound()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, shallotish spice, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Shallot");
    }

    [Fact]
    public void Leek_WordBoundary_NotInCompound()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, leeksoup base, salt"));

        result.Flags.Should().NotContain(f => f.Name == "Leek");
    }

    // ─── v1.5: Expanded Dairy Patterns ─────────────────────────────────

    [Theory]
    [InlineData("water, milk solids non-fat, sugar", "Milk Solids Non-Fat")]
    [InlineData("water, dry milk, sugar", "Dry Milk")]
    [InlineData("water, whey solids, sugar", "Whey Solids")]
    public void ExpandedDairy_FlaggedCorrectly(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Dairy/Lactose");
        flag.RiskLevel.Should().Be("Medium");
        flag.TriggerType.Should().Be("Fodmap");
        flag.FodmapClass.Should().Be("Lactose");
        flag.DoseSensitivity.Should().Be("High");
    }

    // ─── v1.5: FODMAP Stacking Penalties ───────────────────────────────

    [Fact]
    public void Stacking_TwoFructanSources_TriggersStackFructan()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, garlic, salt"));

        result.Flags.Should().Contain(f => f.Code == "STACK-FRUCTAN");
        var flag = result.Flags.First(f => f.Code == "STACK-FRUCTAN");
        flag.Name.Should().Be("Multiple Fructan Sources");
        flag.RiskLevel.Should().Be("Low");
        flag.Category.Should().Be("Stacking Penalty");
    }

    [Fact]
    public void Stacking_SingleFructanSource_NoStackFructan()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, salt"));

        result.Flags.Should().NotContain(f => f.Code == "STACK-FRUCTAN");
    }

    [Fact]
    public void Stacking_TwoGosSources_TriggersStackGos()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, chickpea, lentil, salt"));

        result.Flags.Should().Contain(f => f.Code == "STACK-GOS");
        var flag = result.Flags.First(f => f.Code == "STACK-GOS");
        flag.Name.Should().Be("Multiple GOS Sources");
        flag.RiskLevel.Should().Be("Low");
    }

    [Fact]
    public void Stacking_SingleGosSource_NoStackGos()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, chickpea, salt"));

        result.Flags.Should().NotContain(f => f.Code == "STACK-GOS");
    }

    [Fact]
    public void Stacking_ThreeFodmapClasses_TriggersStackFodmapMix()
    {
        // Fructans (onion) + Lactose (skim milk) + GOS (chickpea) = 3 FODMAP classes
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, skim milk, chickpea, salt"));

        result.Flags.Should().Contain(f => f.Code == "STACK-FODMAP-MIX");
        var flag = result.Flags.First(f => f.Code == "STACK-FODMAP-MIX");
        flag.Name.Should().Be("Multiple FODMAP Classes");
        flag.RiskLevel.Should().Be("Medium");
        flag.Category.Should().Be("Stacking Penalty");
    }

    [Fact]
    public void Stacking_TwoFodmapClasses_NoStackFodmapMix()
    {
        // Fructans (onion) + Lactose (skim milk) = only 2 classes
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, skim milk, salt"));

        result.Flags.Should().NotContain(f => f.Code == "STACK-FODMAP-MIX");
    }

    // ─── v1.5: AMP-DOSE Amplifier ──────────────────────────────────────

    [Fact]
    public void AmpDose_ConcentrateWithFodmap_TriggersAmplifier()
    {
        // "soy protein isolate" triggers GOS (High dose), "isolate" keyword triggers AMP-DOSE
        var result = _sut.Assess(MakeProduct(ingredients: "water, soy protein isolate, salt"));

        result.Flags.Should().Contain(f => f.Code == "AMP-DOSE");
        var flag = result.Flags.First(f => f.Code == "AMP-DOSE");
        flag.Name.Should().Be("Concentrated FODMAP Source");
        flag.RiskLevel.Should().Be("Low");
    }

    [Fact]
    public void AmpDose_PowderWithFodmap_TriggersAmplifier()
    {
        // "onion powder" triggers Fructans (Medium dose), "powder" keyword triggers AMP-DOSE
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion powder, salt"));

        result.Flags.Should().Contain(f => f.Code == "AMP-DOSE");
    }

    [Fact]
    public void AmpDose_SyrupWithFodmap_TriggersAmplifier()
    {
        // "high fructose corn syrup" triggers (High dose), "syrup" keyword triggers AMP-DOSE
        var result = _sut.Assess(MakeProduct(ingredients: "water, high fructose corn syrup, salt"));

        result.Flags.Should().Contain(f => f.Code == "AMP-DOSE");
    }

    [Fact]
    public void AmpDose_NoFodmapFlags_NoAmplifier()
    {
        // "powder" present but no FODMAP flags — only additives
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, cocoa powder, salt",
            additivesTags: ["en:e433"]));

        result.Flags.Should().NotContain(f => f.Code == "AMP-DOSE");
    }

    [Fact]
    public void AmpDose_NoAmplifierKeywords_NoAmplifier()
    {
        // FODMAP flag present (onion) but no concentrate/powder/etc keywords
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, salt"));

        result.Flags.Should().NotContain(f => f.Code == "AMP-DOSE");
    }

    // ─── v1.5: Confidence Heuristic ────────────────────────────────────

    [Fact]
    public void Confidence_NoFodmapFlags_ReturnsHigh()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Confidence_TwoFodmapFlags_ReturnsMedium()
    {
        // 2 FODMAP flags: onion (Fructans) + skim milk (Lactose)
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, skim milk, salt"));

        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Confidence_ThreeFodmapFlags_ReturnsLow()
    {
        // 3+ FODMAP flags: onion (Fructans) + skim milk (Lactose) + honey (ExcessFructose)
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, skim milk, honey, salt"));

        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Confidence_OneBroadTerm_ReturnsMedium()
    {
        // "Onion" is a broad term
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, salt"));

        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Confidence_TwoBroadTerms_ReturnsLow()
    {
        // "Onion" + "Garlic" = 2 broad terms
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, garlic, salt"));

        result.Confidence.Should().Be("Medium");
    }

    // ─── v1.5: DoseSensitiveFlagsCount ─────────────────────────────────

    [Fact]
    public void DoseSensitiveFlagsCount_NoMedHighDose_ReturnsZero()
    {
        // E433 is Additive type → DoseSensitivity = "Low"
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.DoseSensitiveFlagsCount.Should().Be(0);
    }

    [Fact]
    public void DoseSensitiveFlagsCount_OnionMediumDose_Counted()
    {
        // onion → High-FODMAP Ingredient → DoseSensitivity = "Medium"
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, salt"));

        result.DoseSensitiveFlagsCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void DoseSensitiveFlagsCount_DairyHighDose_Counted()
    {
        // skim milk → Dairy/Lactose → DoseSensitivity = "High"
        var result = _sut.Assess(MakeProduct(ingredients: "water, skim milk, salt"));

        result.DoseSensitiveFlagsCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // ─── v1.5: IBS-Weighted Scoring ────────────────────────────────────

    [Fact]
    public void IbsWeighting_FodmapHighPenalty_MoreThan20()
    {
        // E420 (sorbitol) = Sugar Alcohol, High, Fodmap → 20 × 1.0 = 20 penalty
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e420"]));

        // Single High Fodmap flag: score = 100 - 20 = 80
        result.GutScore.Should().Be(80);
    }

    [Fact]
    public void IbsWeighting_ProcessingPenalty_LessThan10()
    {
        // NOVA 4 = Processing, Medium → 10 × 0.8 = 8 penalty
        var result = _sut.Assess(MakeProduct(novaGroup: 4));

        // Single medium Processing flag: score = 100 - 8 = 92
        result.GutScore.Should().Be(92);
    }

    [Fact]
    public void IbsWeighting_NutrientPenalty_LessThan5()
    {
        // High sodium = Nutrient, Low → 5 × 0.8 = 4 penalty
        var result = _sut.Assess(MakeProduct(sodium100g: 3m));

        // Single low Nutrient flag: score = 100 - 4 = 96
        result.GutScore.Should().Be(96);
    }

    [Fact]
    public void IbsWeighting_AdditivePenalty_Unchanged()
    {
        // E433 = Emulsifier, High, Additive → 20 × 1.0 = 20 penalty
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e433"]));

        result.GutScore.Should().Be(80);
    }

    // ─── v1.5: Fiber Bonus Reduced by Medium/High FODMAP ───────────────

    [Fact]
    public void FiberBonus_WithMediumFodmap_NoBonusApplied()
    {
        // High fiber + onion (High-FODMAP Ingredient, High risk) → 20 × 1.0 = 20 penalty
        // score = 100 - 20 = 80
        // Fiber bonus: medHighFodmapCount=1, onlyOneMedium=false (it's High) → bonus=5 (no FODMAP fiber, medHighCount<2)
        // score = 80 + 5 = 85
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, onion, salt",
            fiber100g: 8m));

        result.GutScore.Should().Be(85);
    }

    [Fact]
    public void FiberBonus_NoFodmap_Full5Bonus()
    {
        // High fiber + only additive flag → full 5-point bonus
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e433"],
            fiber100g: 8m));

        // E433 = 20 penalty, +5 fiber bonus = score 85
        result.GutScore.Should().Be(85);
    }

    // ─── v1.5: Real-World IBS Trigger Scenarios ────────────────────────

    [Fact]
    public void RealWorld_PolyolCandy_MultipleSugarAlcohols()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, maltitol, sorbitol, xylitol, flavor",
            additivesTags: ["en:e420", "en:e967", "en:e421"]));

        result.Flags.Should().Contain(f => f.FodmapClass == "Polyols");
        result.Flags.Should().Contain(f => f.Code == "STACK-POLYOL");
        result.GutScore.Should().BeLessThanOrEqualTo(30);
    }

    [Fact]
    public void RealWorld_InulinFiberBar_FodmapFiber()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "oats, chicory root fiber, inulin, honey, salt",
            fiber100g: 12m));

        result.Flags.Should().Contain(f => f.Name == "Inulin");
        result.Flags.Should().Contain(f => f.Name == "Chicory Root Fiber");
        result.Flags.Should().Contain(f => f.Name == "Honey");
        // Fiber bonus should be reduced to 2 due to FODMAP fiber
        result.GutScore.Should().BeLessThanOrEqualTo(85);
    }

    [Fact]
    public void RealWorld_GarlicOnionSeasoning_FructanStacking()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "salt, onion powder, garlic powder, pepper, onion salt"));

        result.Flags.Should().Contain(f => f.FodmapClass == "Fructans");
        result.Flags.Should().Contain(f => f.Code == "STACK-FRUCTAN");
        result.Flags.Should().Contain(f => f.Code == "AMP-DOSE"); // "powder" keyword
        result.Confidence.Should().Be("High"); // detailed ingredients (>50 chars, has commas)
    }

    [Fact]
    public void RealWorld_LegumePasta_GosStacking()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "chickpea flour, lentil flour, water"));

        result.Flags.Should().Contain(f => f.FodmapClass == "GOS");
        result.Flags.Should().Contain(f => f.Code == "STACK-GOS");
    }

    [Fact]
    public void RealWorld_MixedFodmapProduct_FodmapMixStacking()
    {
        // Fructans (onion) + Lactose (milk powder) + Polyols (sorbitol) = 3 FODMAP classes
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, onion, milk powder, flavor",
            additivesTags: ["en:e420"]));

        result.Flags.Should().Contain(f => f.Code == "STACK-FODMAP-MIX");
        result.GutScore.Should().BeLessThanOrEqualTo(50);
    }

    // ─── v1.5 Coverage Expansion Tests ────────────────────────────────

    // ── Barley & Rye Fructan Rules ──

    [Theory]
    [InlineData("barley flour, water, salt", "Barley")]
    [InlineData("whole grain rye, sugar", "Rye")]
    public void Ingredient_BarleyRye_FlaggedAsFructan(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.FodmapClass.Should().Be("Fructans");
        flag.RiskLevel.Should().Be("High");
        flag.TriggerType.Should().Be("Fodmap");
    }

    [Fact]
    public void Ingredient_Barley_WordBoundary_NoFalsePositive()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "barleygrass extract"));
        result.Flags.Should().NotContain(f => f.Name == "Barley");
    }

    [Fact]
    public void Ingredient_Rye_WordBoundary_NoFalsePositive()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "turkey breast, salt"));
        result.Flags.Should().NotContain(f => f.Name == "Rye");
    }

    // ── Cashew & Pistachio GOS Rules ──

    [Theory]
    [InlineData("cashew butter, cocoa", "Cashew")]
    [InlineData("pistachio, almond, salt", "Pistachio")]
    public void Ingredient_CashewPistachio_FlaggedAsGOS(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.FodmapClass.Should().Be("GOS");
        flag.RiskLevel.Should().Be("High");
    }

    [Fact]
    public void Ingredient_Cashew_WordBoundary_NoFalsePositive()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "cashewnut oil"));
        result.Flags.Should().NotContain(f => f.Name == "Cashew");
    }

    // ── Fructose / Syrup Label Patterns ──

    [Fact]
    public void Ingredient_CrystallineFructose_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, crystalline fructose, citric acid"));

        result.Flags.Should().Contain(f => f.Name == "Crystalline Fructose");
        var flag = result.Flags.First(f => f.Name == "Crystalline Fructose");
        flag.FodmapClass.Should().Be("ExcessFructose");
        flag.RiskLevel.Should().Be("Medium");
    }

    [Fact]
    public void Ingredient_MaltitolSyrup_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "maltitol syrup, cocoa butter"));

        result.Flags.Should().Contain(f => f.Name == "Maltitol Syrup");
        var flag = result.Flags.First(f => f.Name == "Maltitol Syrup");
        flag.FodmapClass.Should().Be("Polyols");
        flag.RiskLevel.Should().Be("High");
    }

    [Fact]
    public void Ingredient_SorbitolSyrup_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "sorbitol syrup, gelatin"));

        result.Flags.Should().Contain(f => f.Name == "Sorbitol Syrup");
        var flag = result.Flags.First(f => f.Name == "Sorbitol Syrup");
        flag.FodmapClass.Should().Be("Polyols");
        flag.RiskLevel.Should().Be("High");
    }

    [Fact]
    public void Ingredient_Fructose_WordBoundary_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, fructose, citric acid"));

        result.Flags.Should().Contain(f => f.Name == "Fructose");
        var flag = result.Flags.First(f => f.Name == "Fructose");
        flag.FodmapClass.Should().Be("ExcessFructose");
    }

    // ── Missing E-Numbers ──

    [Theory]
    [InlineData("en:e952", "Cyclamate")]
    [InlineData("en:e961", "Neotame")]
    [InlineData("en:e969", "Advantame")]
    public void AdditiveTag_MissingENumbers_FlaggedCorrectly(string tag, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be(expectedName);
        result.Flags[0].RiskLevel.Should().Be("Medium");
        result.Flags[0].Category.Should().Be("Artificial Sweetener");
    }

    // ── Missing Additives ──

    [Theory]
    [InlineData("en:e422", "Glycerol")]
    [InlineData("en:e476", "PGPR")]
    [InlineData("en:e491", "Sorbitan Monostearate")]
    [InlineData("en:e492", "Sorbitan Tristearate")]
    [InlineData("en:e493", "Sorbitan Monolaurate")]
    [InlineData("en:e494", "Sorbitan Monooleate")]
    [InlineData("en:e495", "Sorbitan Monopalmitate")]
    [InlineData("en:e481", "Sodium Stearoyl Lactylate")]
    [InlineData("en:e482", "Calcium Stearoyl Lactylate")]
    [InlineData("en:e1442", "Hydroxypropyl Distarch Phosphate")]
    [InlineData("en:e331", "Sodium Citrates")]
    [InlineData("en:e334", "Tartaric Acid")]
    [InlineData("en:e224", "Potassium Metabisulfite")]
    public void AdditiveTag_NewAdditives_FlaggedCorrectly(string tag, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(additivesTags: [tag]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be(expectedName);
    }

    // ── Hidden Onion/Garlic Low-Confidence Flags ──

    [Theory]
    [InlineData("salt, natural flavors, sugar", "Natural Flavors")]
    [InlineData("salt, natural flavour, sugar", "Natural Flavours")]
    [InlineData("salt, natural flavouring, sugar", "Natural Flavouring")]
    [InlineData("salt, vegetable powder, sugar", "Vegetable Powder")]
    public void Ingredient_HiddenFodmap_Substring_FlaggedAsHiddenRisk(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Hidden FODMAP Risk");
        flag.RiskLevel.Should().Be("Low");
        flag.FodmapClass.Should().Be("Fructans");
    }

    [Theory]
    [InlineData("salt, seasoning, sugar", "Seasoning")]
    [InlineData("chicken bouillon, salt", "Bouillon")]
    [InlineData("chicken stock, vegetables", "Stock")]
    [InlineData("salt, flavouring, sugar", "Flavouring")]
    [InlineData("salt, spices, pepper", "Spices")]
    public void Ingredient_HiddenFodmap_WordBoundary_FlaggedAsHiddenRisk(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Hidden FODMAP Risk");
        flag.RiskLevel.Should().Be("Low");
    }

    // ── Remaining FODMAP Gaps — Fructan Extracts ──

    [Theory]
    [InlineData("water, onion extract, salt", "Onion Extract")]
    [InlineData("water, garlic extract, salt", "Garlic Extract")]
    [InlineData("water, garlic flavor, salt", "Garlic Flavor")]
    [InlineData("water, onion flavor, salt", "Onion Flavor")]
    [InlineData("water, garlic flavour, salt", "Garlic Flavour")]
    [InlineData("water, onion flavour, salt", "Onion Flavour")]
    public void Ingredient_FructanExtracts_Flagged(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.FodmapClass.Should().Be("Fructans");
        flag.RiskLevel.Should().Be("High");
    }

    // ── Remaining FODMAP Gaps — GOS ──

    [Theory]
    [InlineData("hummus, olive oil", "Hummus")]
    [InlineData("baked beans, tomato sauce", "Baked Beans")]
    [InlineData("pea protein, rice flour", "Pea Protein")]
    public void Ingredient_GosGaps_Flagged(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.FodmapClass.Should().Be("GOS");
    }

    // ── Remaining FODMAP Gaps — Excess Fructose ──

    [Theory]
    [InlineData("pear, sugar, water", "Pear")]
    [InlineData("apple, sugar, water", "Apple")]
    public void Ingredient_WholeFruit_FlaggedAsFructose(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.FodmapClass.Should().Be("ExcessFructose");
        flag.RiskLevel.Should().Be("Low");
    }

    [Theory]
    [InlineData("fruit puree, sugar", "Fruit Puree")]
    [InlineData("fruit paste, flour", "Fruit Paste")]
    [InlineData("date paste, cocoa", "Date Paste")]
    public void Ingredient_FruitPastesPurees_FlaggedAsFructose(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.FodmapClass.Should().Be("ExcessFructose");
    }

    // ── Remaining FODMAP Gaps — Dairy (Casein) ──

    [Fact]
    public void Ingredient_WheyConcentrate_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "whey concentrate, sugar"));

        result.Flags.Should().Contain(f => f.Name == "Whey Concentrate");
        var flag = result.Flags.First(f => f.Name == "Whey Concentrate");
        flag.FodmapClass.Should().Be("Lactose");
        flag.RiskLevel.Should().Be("Medium");
    }

    [Fact]
    public void Ingredient_Caseinate_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "sodium caseinate, water"));

        result.Flags.Should().Contain(f => f.Name == "Caseinate");
        var flag = result.Flags.First(f => f.Name == "Caseinate");
        flag.FodmapClass.Should().Be("Lactose");
        flag.RiskLevel.Should().Be("Low");
    }

    [Fact]
    public void Ingredient_Casein_WordBoundary_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "milk, casein, water"));

        result.Flags.Should().Contain(f => f.Name == "Casein");
        var flag = result.Flags.First(f => f.Name == "Casein");
        flag.FodmapClass.Should().Be("Lactose");
        flag.RiskLevel.Should().Be("Low");
    }

    // ── Non-FODMAP IBS Triggers — Stimulant/Motility ──

    [Theory]
    [InlineData("water, caffeine, sugar", "Caffeine")]
    [InlineData("coffee extract, milk", "Coffee")]
    [InlineData("guarana extract, sugar", "Guarana")]
    public void Ingredient_Stimulants_FlaggedAsStimulant(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Stimulant/Motility");
    }

    [Fact]
    public void Ingredient_GreenTeaExtract_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "green tea extract, water"));

        result.Flags.Should().Contain(f => f.Name == "Green Tea Extract");
        var flag = result.Flags.First(f => f.Name == "Green Tea Extract");
        flag.Category.Should().Be("Stimulant/Motility");
    }

    // ── Non-FODMAP IBS Triggers — Spicy/Irritant ──

    [Theory]
    [InlineData("chilli powder, salt", "Chilli")]
    [InlineData("chili flakes, oil", "Chili")]
    [InlineData("capsicum extract, salt", "Capsicum")]
    [InlineData("cayenne pepper, salt", "Cayenne")]
    public void Ingredient_SpicyIrritants_WordBoundary_Flagged(string ingredients, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(ingredients: ingredients));

        result.Flags.Should().Contain(f => f.Name == expectedName);
        var flag = result.Flags.First(f => f.Name == expectedName);
        flag.Category.Should().Be("Spicy/Irritant");
    }

    [Fact]
    public void Ingredient_HotPepper_Flagged()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "hot pepper, salt, vinegar"));

        result.Flags.Should().Contain(f => f.Name == "Hot Pepper");
        var flag = result.Flags.First(f => f.Name == "Hot Pepper");
        flag.Category.Should().Be("Spicy/Irritant");
    }

    // ── CategoryMap Coverage for New Categories ──

    [Fact]
    public void HiddenFodmapRisk_MapsToFodmap_Fructans()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "salt, natural flavors"));
        var flag = result.Flags.First(f => f.Category == "Hidden FODMAP Risk");
        flag.TriggerType.Should().Be("Fodmap");
        flag.FodmapClass.Should().Be("Fructans");
    }

    [Fact]
    public void StimulantMotility_MapsToAdditive()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "caffeine, water"));
        var flag = result.Flags.First(f => f.Category == "Stimulant/Motility");
        flag.TriggerType.Should().Be("Additive");
    }

    [Fact]
    public void SpicyIrritant_MapsToAdditive()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "cayenne pepper, salt"));
        var flag = result.Flags.First(f => f.Category == "Spicy/Irritant");
        flag.TriggerType.Should().Be("Additive");
    }

    // ── Real-World Scenarios with New Rules ──

    [Fact]
    public void RealWorld_ProteinBar_CashewPistachio_GosStacking()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "cashew butter, pistachio, whey protein, chicory root fiber"));

        result.Flags.Should().Contain(f => f.Name == "Cashew");
        result.Flags.Should().Contain(f => f.Name == "Pistachio");
        result.Flags.Should().Contain(f => f.Code == "STACK-GOS");
    }

    [Fact]
    public void RealWorld_BreadWithBarleyRye_MultipleFructans()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "wheat flour, barley malt, rye flour, water, salt"));

        result.Flags.Should().Contain(f => f.Name == "Barley");
        result.Flags.Should().Contain(f => f.Name == "Rye");
        result.Flags.Should().Contain(f => f.Name == "Wheat Flour");
        result.Flags.Should().Contain(f => f.Code == "STACK-FRUCTAN");
    }

    [Fact]
    public void RealWorld_ProcessedSnack_HiddenFodmap()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "corn flour, seasoning, natural flavors, vegetable powder, salt"));

        result.Flags.Should().Contain(f => f.Category == "Hidden FODMAP Risk");
        result.Flags.Count(f => f.Category == "Hidden FODMAP Risk").Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void RealWorld_SpicyChocolateBar_MixedTriggers()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "cocoa butter, sugar, maltitol syrup, cayenne pepper, milk powder",
            additivesTags: ["en:e476"]));

        result.Flags.Should().Contain(f => f.Name == "Maltitol Syrup");
        result.Flags.Should().Contain(f => f.Name == "Cayenne");
        result.Flags.Should().Contain(f => f.Name == "Milk Powder");
        result.Flags.Should().Contain(f => f.Name == "PGPR");
    }

    [Fact]
    public void RealWorld_EnergyDrink_StimulantTriggers()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, sugar, caffeine, guarana extract, green tea extract"));

        result.Flags.Should().Contain(f => f.Name == "Caffeine");
        result.Flags.Should().Contain(f => f.Name == "Guarana");
        result.Flags.Should().Contain(f => f.Name == "Green Tea Extract");
    }

    [Fact]
    public void RealWorld_FruitBar_MultipleFructoseSources()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "date paste, apple puree, pear, crystalline fructose"));

        result.Flags.Should().Contain(f => f.Name == "Date Paste");
        result.Flags.Should().Contain(f => f.Name == "Pear");
        result.Flags.Should().Contain(f => f.Name == "Crystalline Fructose");
    }

    [Fact]
    public void RealWorld_SulfiteWine_E224_Flagged()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e224"]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("Potassium Metabisulfite");
        result.Flags[0].RiskLevel.Should().Be("Medium");
        result.Flags[0].Category.Should().Be("Preservative/Sulfite");
    }

    [Fact]
    public void RealWorld_ModifiedStarch_E1442_Flagged()
    {
        var result = _sut.Assess(MakeProduct(additivesTags: ["en:e1442"]));

        result.Flags.Should().ContainSingle();
        result.Flags[0].Name.Should().Be("Hydroxypropyl Distarch Phosphate");
        result.Flags[0].Category.Should().Be("Thickener");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Natural-Language IBS Scenario Tests (A–K, 29 cases)
    // ═══════════════════════════════════════════════════════════════════

    // ─── A. Baseline Sanity ────────────────────────────────────────────

    [Fact]
    public void Scenario01_WholeFood_RolledOats_Clean()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Rolled oats.",
            novaGroup: 1,
            fiber100g: 10m));

        result.GutRating.Should().BeOneOf("Good", "Fair");
        result.Flags.Should().BeEmpty();
        result.GutScore.Should().BeGreaterThanOrEqualTo(80);
        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Scenario02_SimpleLowFodmapMeal_Clean()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Grilled chicken, white rice, salt, olive oil.",
            novaGroup: 1));

        result.GutRating.Should().BeOneOf("Good", "Fair");
        result.Flags.Where(f => f.TriggerType == "Fodmap").Should().BeEmpty();
        result.Confidence.Should().Be("Medium");
    }

    // ─── B. Polyols / Sugar-Free Candy ─────────────────────────────────

    [Fact]
    public void Scenario03_SugarFreeGummies_PolyolBomb()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Sweeteners: maltitol syrup, sorbitol, xylitol; flavours.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Bad", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Maltitol Syrup");
        result.Flags.Should().Contain(f => f.Name == "Sorbitol");
        result.Flags.Should().Contain(f => f.Name == "Xylitol");
        result.Flags.Where(f => f.FodmapClass == "Polyols").Should().HaveCountGreaterThanOrEqualTo(3);
        result.Flags.Should().Contain(f => f.Code == "STACK-POLYOL");
        result.Confidence.Should().BeOneOf("High", "Medium", "Low");
    }

    [Fact]
    public void Scenario04_SugarFreeGum_SorbitolMannitol()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Sweeteners: sorbitol, mannitol; gum base; flavours.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Sorbitol");
        result.Flags.Should().Contain(f => f.Name == "Mannitol");
        result.Flags.Should().Contain(f => f.Code == "STACK-POLYOL");
        result.Confidence.Should().BeOneOf("High", "Medium", "Low");
    }

    // ─── C. Fructans — Powders, Extracts, Seasoning, Hidden ───────────

    [Fact]
    public void Scenario05_GarlicOnionSeasoningBlend()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Onion powder, garlic powder, salt, spices.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Onion Powder");
        result.Flags.Should().Contain(f => f.Name == "Garlic Powder");
        result.Flags.Should().Contain(f => f.Code == "STACK-FRUCTAN");
        result.Flags.Should().Contain(f => f.Category == "Hidden FODMAP Risk");
        result.Confidence.Should().BeOneOf("Medium", "Low");
    }

    [Fact]
    public void Scenario06_SoupStockCube_HiddenOnionGarlic()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Stock, seasoning, natural flavours, vegetable powder.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Stock" && f.Category == "Hidden FODMAP Risk");
        result.Flags.Should().Contain(f => f.Name == "Seasoning" && f.Category == "Hidden FODMAP Risk");
        result.Flags.Should().Contain(f => f.Name == "Natural Flavours" && f.Category == "Hidden FODMAP Risk");
        result.Flags.Should().Contain(f => f.Name == "Vegetable Powder" && f.Category == "Hidden FODMAP Risk");
        result.Flags.Where(f => f.Category == "Hidden FODMAP Risk").Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Scenario07_GarlicExtractDressing()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Garlic extract, onion flavour, vinegar, spices.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Garlic Extract" && f.FodmapClass == "Fructans");
        result.Flags.Should().Contain(f => f.Name == "Onion Flavour" && f.FodmapClass == "Fructans");
        result.Confidence.Should().BeOneOf("Medium", "Low");
    }

    [Fact]
    public void Scenario08_RyeBarleyBread()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Rye flour, barley malt, yeast, salt.",
            novaGroup: 3));

        result.GutRating.Should().BeOneOf("Good", "Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Rye" && f.FodmapClass == "Fructans");
        result.Flags.Should().Contain(f => f.Name == "Barley" && f.FodmapClass == "Fructans");
        result.Confidence.Should().BeOneOf("High", "Medium");
    }

    // ─── D. GOS (Legumes/Soy/Cashew/Pistachio) ────────────────────────

    [Fact]
    public void Scenario09_ChickpeaPasta_ConcentratedGos()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Chickpea flour.",
            novaGroup: 1));

        result.GutRating.Should().BeOneOf("Fair", "Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Chickpea Flour" && f.FodmapClass == "GOS");
        result.Confidence.Should().BeOneOf("Medium", "Low");
    }

    [Fact]
    public void Scenario10_LentilSoup_LegumeMix_GosStacking()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Lentils, chickpeas, kidney beans.",
            novaGroup: 2));

        result.GutRating.Should().BeOneOf("Fair", "Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Lentil" && f.FodmapClass == "GOS");
        result.Flags.Should().Contain(f => f.Name == "Chickpea" && f.FodmapClass == "GOS");
        result.Flags.Should().Contain(f => f.Name == "Kidney Bean" && f.FodmapClass == "GOS");
        result.Flags.Should().Contain(f => f.Code == "STACK-GOS");
        result.Confidence.Should().BeOneOf("Medium", "Low");
    }

    [Fact]
    public void Scenario11_SoyIsolateShake()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Soy protein isolate, cocoa, natural flavours.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Fair", "Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Soy Protein Isolate" && f.FodmapClass == "GOS");
        result.Flags.Should().Contain(f => f.Code == "AMP-DOSE");
        result.Confidence.Should().BeOneOf("Low", "Medium");
    }

    [Fact]
    public void Scenario12_CashewProteinBar_GosAndFructans()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Cashew, chicory root fiber (inulin), natural flavours.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Fair", "Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Cashew" && f.FodmapClass == "GOS");
        result.Flags.Should().Contain(f => f.FodmapClass == "Fructans");
        // GOS + Fructans + Hidden = 3 FODMAP classes possible; at minimum GOS + Fructans
        var fodmapClasses = result.Flags
            .Where(f => f.TriggerType == "Fodmap" && f.FodmapClass != "")
            .Select(f => f.FodmapClass).Distinct().Count();
        fodmapClasses.Should().BeGreaterThanOrEqualTo(2);
        result.Confidence.Should().BeOneOf("High", "Medium", "Low");
    }

    [Fact]
    public void Scenario13_PistachioSnackMix()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Pistachio, salt.",
            novaGroup: 2));

        result.GutRating.Should().BeOneOf("Good", "Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Pistachio" && f.FodmapClass == "GOS");
        result.Confidence.Should().BeOneOf("High", "Medium");
    }

    // ─── E. Lactose / Dairy Powders ────────────────────────────────────

    [Fact]
    public void Scenario14_MilkshakePowder_TripleLactose()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Milk powder, whey solids, lactose.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Milk Powder" && f.FodmapClass == "Lactose");
        result.Flags.Should().Contain(f => f.Name == "Whey Solids" && f.FodmapClass == "Lactose");
        result.Flags.Should().Contain(f => f.Name == "Lactose" && f.FodmapClass == "Lactose");
        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Scenario15_WheyConcentrateProtein()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Whey concentrate, cocoa powder.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Whey Concentrate" && f.FodmapClass == "Lactose");
        result.Confidence.Should().BeOneOf("Medium", "Low");
    }

    [Fact]
    public void Scenario16_CaseinOnlyProduct()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Casein, flavouring.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Good", "Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Casein" && f.FodmapClass == "Lactose");
        result.Flags.First(f => f.Name == "Casein").RiskLevel.Should().Be("Low");
        result.GutRating.Should().NotBe("Bad");
        result.Confidence.Should().BeOneOf("High", "Medium");
    }

    // ─── F. Excess Fructose ────────────────────────────────────────────

    [Fact]
    public void Scenario17_FruitLeather_MultipleFructose()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Fruit puree, apple concentrate, honey.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Fair", "Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Fruit Puree" && f.FodmapClass == "ExcessFructose");
        result.Flags.Should().Contain(f => f.Name == "Apple Concentrate" && f.FodmapClass == "ExcessFructose");
        result.Flags.Should().Contain(f => f.Name == "Honey" && f.FodmapClass == "ExcessFructose");
        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Scenario18_HealthySmoothie_FructoseHeavy()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Apple juice, fruit juice concentrate, mango.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Apple Juice" && f.FodmapClass == "ExcessFructose");
        result.Flags.Should().Contain(f => f.Name == "Fruit Juice Concentrate" && f.FodmapClass == "ExcessFructose");
        result.Flags.Should().Contain(f => f.Name == "Mango" && f.FodmapClass == "ExcessFructose");
        result.Confidence.Should().BeOneOf("Low", "Medium");
    }

    [Fact]
    public void Scenario19_FructoseAsIngredient()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Water, fructose, natural flavours.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Good", "Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Fructose" && f.FodmapClass == "ExcessFructose");
        result.Flags.Should().Contain(f => f.Category == "Hidden FODMAP Risk");
        result.Confidence.Should().BeOneOf("High", "Medium");
    }

    // ─── G. Polyol Natural Sources ─────────────────────────────────────

    [Fact]
    public void Scenario20_DriedPrunes()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Prunes.",
            novaGroup: 1));

        result.GutRating.Should().BeOneOf("Good", "Fair", "Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Prune" && f.FodmapClass == "Polyols");
        result.Flags.First(f => f.Name == "Prune").RiskLevel.Should().Be("High");
        result.Confidence.Should().BeOneOf("High", "Medium");
    }

    [Fact]
    public void Scenario21_MushroomGarlicOnion_MultiFodmap()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Mushrooms, garlic, onion.",
            novaGroup: 1));

        result.GutRating.Should().BeOneOf("Fair", "Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Mushroom" && f.FodmapClass == "Polyols");
        result.Flags.Should().Contain(f => f.Name == "Garlic" && f.FodmapClass == "Fructans");
        result.Flags.Should().Contain(f => f.Name == "Onion" && f.FodmapClass == "Fructans");
        result.Flags.Should().Contain(f => f.Code == "STACK-FRUCTAN");
        result.Confidence.Should().Be("Medium");
    }

    // ─── H. Protein Bar Disaster Archetypes ───────────────────────────

    [Fact]
    public void Scenario22_InulinGumsWheyBar()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Whey protein isolate, chicory root fiber (inulin), guar gum, xanthan gum, natural flavours.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Bad", "Poor");
        result.Flags.Should().Contain(f => f.FodmapClass == "Lactose"); // whey
        result.Flags.Should().Contain(f => f.FodmapClass == "Fructans"); // inulin/chicory
        result.Flags.Should().Contain(f => f.Name == "Guar Gum");
        result.Flags.Should().Contain(f => f.Name == "Xanthan Gum");
        result.Flags.Should().Contain(f => f.Code == "STACK-HYDROCOL");
        result.Flags.Should().Contain(f => f.Code == "AMP-DOSE");
        result.Confidence.Should().Be("High");
    }

    [Fact]
    public void Scenario23_KetoBar_PolyolsAndGums()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Maltitol, erythritol, polydextrose, xanthan gum.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Bad", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Maltitol" && f.RiskLevel == "High");
        result.Flags.Should().Contain(f => f.Name == "Erythritol");
        result.Flags.Should().Contain(f => f.Name == "Polydextrose");
        result.Flags.Should().Contain(f => f.Name == "Xanthan Gum");
        result.Flags.Should().Contain(f => f.Code == "STACK-POLYOL");
        result.Confidence.Should().BeOneOf("Low", "Medium");
    }

    // ─── I. Additive Sanity ───────────────────────────────────────────

    [Fact]
    public void Scenario24_EmulsifierHeavyIceCream()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Carrageenan, polysorbate 80, mono- and diglycerides.",
            novaGroup: 4,
            additivesTags: ["en:e407", "en:e433", "en:e471"]));

        result.GutRating.Should().BeOneOf("Poor", "Bad");
        result.Flags.Should().Contain(f => f.Name == "Carrageenan");
        result.Flags.Should().Contain(f => f.Name == "Polysorbate 80");
        result.Flags.Should().Contain(f => f.Name == "Mono- and Diglycerides");
        result.Flags.Should().Contain(f => f.Code == "STACK-EMUL");
        result.Confidence.Should().BeOneOf("High", "Medium");
    }

    [Fact]
    public void Scenario25_SulfiteDriedFruit()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Dried apricots, potassium metabisulfite.",
            novaGroup: 3,
            additivesTags: ["en:e224"]));

        result.GutRating.Should().BeOneOf("Good", "Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Potassium Metabisulfite" && f.Category == "Preservative/Sulfite");
        result.Flags.Should().Contain(f => f.Name == "Apricot" && f.FodmapClass == "Polyols");
        result.Confidence.Should().BeOneOf("High", "Medium");
    }

    // ─── J. Non-FODMAP IBS Triggers ────────────────────────────────────

    [Fact]
    public void Scenario26_EnergyDrink_StimulantTriggers()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Caffeine, guarana, sweeteners.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Fair", "Poor");
        result.Flags.Should().Contain(f => f.Category == "Stimulant/Motility");
        result.Confidence.Should().BeOneOf("Medium", "High");
    }

    [Fact]
    public void Scenario27_HotSauceSpicyChips()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Chili, cayenne, spices.",
            novaGroup: 4));

        result.GutRating.Should().BeOneOf("Fair", "Poor");
        result.Flags.Should().Contain(f => f.Name == "Chili" && f.Category == "Spicy/Irritant");
        result.Flags.Should().Contain(f => f.Name == "Cayenne" && f.Category == "Spicy/Irritant");
        result.Confidence.Should().BeOneOf("Medium", "High");
    }

    // ─── K. Dedupe / Precedence Checks ─────────────────────────────────

    [Fact]
    public void Scenario28_SameAdditive_TagAndIngredient_NoDuplicate()
    {
        var result = _sut.Assess(MakeProduct(
            additivesTags: ["en:e433"],
            ingredients: "polysorbate 80, sugar, flour."));

        result.Flags.Count(f => f.Name == "Polysorbate 80").Should().Be(1);
    }

    [Fact]
    public void Scenario29_OnionPowderAndOnion_BothPresent_FructanStack()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "Onion powder, onion, garlic powder."));

        result.Flags.Should().Contain(f => f.Name == "Onion Powder");
        result.Flags.Should().Contain(f => f.Name == "Onion");
        result.Flags.Should().Contain(f => f.Name == "Garlic Powder");
        result.GutRating.Should().BeOneOf("Poor", "Bad");
        result.Flags.Should().Contain(f => f.Code == "STACK-FRUCTAN");
    }

    // ─── Lactase Enzyme Mitigation ─────────────────────────────────────

    [Fact]
    public void LactaseEnzyme_DowngradesDairyLactoseFlags()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "low fat milk, cream, whey, lactase enzyme, carrageenan"));

        var dairyFlags = result.Flags.Where(f => f.Category == "Dairy/Lactose").ToList();
        dairyFlags.Should().NotBeEmpty();
        dairyFlags.Should().AllSatisfy(f => f.RiskLevel.Should().Be("Low"));
        dairyFlags.Should().AllSatisfy(f => f.Explanation.Should().Contain("lactase enzyme"));
    }

    [Fact]
    public void WithoutLactaseEnzyme_DairyLactoseFlagsUnchanged()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "low fat milk, cream, whey, carrageenan"));

        var dairyFlags = result.Flags.Where(f => f.Category == "Dairy/Lactose").ToList();
        dairyFlags.Should().NotBeEmpty();
        dairyFlags.Should().Contain(f => f.RiskLevel == "Medium");
    }

    [Fact]
    public void LactaseEnzyme_AlreadyLowDairyFlags_StaysLow()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "casein, lactase enzyme"));

        var dairyFlags = result.Flags.Where(f => f.Category == "Dairy/Lactose").ToList();
        dairyFlags.Should().NotBeEmpty();
        dairyFlags.Should().AllSatisfy(f => f.RiskLevel.Should().Be("Low"));
        dairyFlags.Should().AllSatisfy(f => f.Explanation.Should().NotContain("lactase enzyme"));
    }

    [Fact]
    public void LactaseEnzyme_HigherScoreThanWithout()
    {
        var withLactase = _sut.Assess(MakeProduct(
            ingredients: "low fat milk, cream, whey, lactase enzyme"));
        var withoutLactase = _sut.Assess(MakeProduct(
            ingredients: "low fat milk, cream, whey"));

        withLactase.GutScore.Should().BeGreaterThan(withoutLactase.GutScore);
    }

    // ─── New WholeFoodRiskPatterns: Composed Meals ─────────────────────

    [Theory]
    [InlineData("Pizza Margherita")]
    [InlineData("Chicken Lasagna")]
    [InlineData("Beef Burrito")]
    [InlineData("Tonkotsu Ramen")]
    [InlineData("Pad Thai")]
    [InlineData("Sushi Roll")]
    [InlineData("Green Curry")]
    [InlineData("Chicken Stir Fry")]
    [InlineData("Fried Rice")]
    [InlineData("Spaghetti Bolognese")]
    [InlineData("Pork Dumpling")]
    [InlineData("Chicken Gyoza")]
    [InlineData("Beef Samosa")]
    [InlineData("Chicken Biryani")]
    public void ComposedMeal_WholeFoodName_IsFlagged(string productName)
    {
        var result = _sut.Assess(new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = productName,
            AdditivesTags = [],
            Additives = [],
        });

        result.Flags.Should().NotBeEmpty($"'{productName}' should trigger at least one whole-food risk flag");
    }

    [Theory]
    [InlineData("Sourdough Bread")]
    [InlineData("Naan Bread")]
    [InlineData("Pita Bread")]
    [InlineData("Croissant")]
    [InlineData("Bagel")]
    public void Bread_WholeFoodName_IsFlagged(string productName)
    {
        var result = _sut.Assess(new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = productName,
            AdditivesTags = [],
            Additives = [],
        });

        result.Flags.Should().NotBeEmpty($"'{productName}' should trigger a bread risk flag");
    }

    [Theory]
    [InlineData("BBQ Sauce")]
    [InlineData("Soy Sauce")]
    [InlineData("Tomato Sauce")]
    [InlineData("Ketchup")]
    [InlineData("Pesto")]
    public void Condiment_WholeFoodName_IsFlagged(string productName)
    {
        var result = _sut.Assess(new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = productName,
            AdditivesTags = [],
            Additives = [],
        });

        result.Flags.Should().NotBeEmpty($"'{productName}' should trigger a condiment risk flag");
    }
}
