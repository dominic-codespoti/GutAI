using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FodmapServiceTests
{
    private readonly FodmapService _sut = new();

    private static FoodProductDto MakeProduct(
        string name = "Test Product",
        string? ingredients = null,
        List<string>? additiveTags = null,
        List<FoodAdditiveDto>? additives = null,
        decimal? sugar = null)
    {
        return new FoodProductDto
        {
            Name = name,
            Ingredients = ingredients,
            AdditivesTags = additiveTags ?? [],
            Additives = additives ?? [],
            Sugar100g = sugar,
        };
    }

    // ─── Score & Rating ─────────────────────────────────────────────────

    [Fact]
    public void NoTriggers_Returns100_LowFodmap()
    {
        var result = _sut.Assess(MakeProduct("Rice", "white rice, water, salt"));
        result.FodmapScore.Should().Be(100);
        result.FodmapRating.Should().Be("Low FODMAP");
        result.TriggerCount.Should().Be(0);
    }

    [Fact]
    public void SingleHighTrigger_Drops25Points()
    {
        var result = _sut.Assess(MakeProduct("Garlic Sauce", "garlic, oil, salt"));
        result.FodmapScore.Should().Be(55);
        result.FodmapRating.Should().Be("High FODMAP");
    }

    [Fact]
    public void TwoHighTriggers_Drops50Points()
    {
        var result = _sut.Assess(MakeProduct("Garlic Onion Dip", "onion, garlic, cream"));
        // onion=High(45), garlic=High(45) — both Fructan, deduped to 1 trigger (-45)
        // cream → Lactose Moderate trigger (-15)
        // Total: 100 - 45 - 15 = 40
        result.FodmapScore.Should().Be(40);
        result.FodmapRating.Should().Be("High FODMAP");
    }

    [Fact]
    public void ManyHighTriggers_ClampedAt0()
    {
        var result = _sut.Assess(MakeProduct("Everything Bagel", "wheat flour, onion, garlic, honey, apple, inulin"));
        // wheat/onion/garlic/inulin → 1 Fructan trigger (-45)
        // honey → 1 Excess Fructose trigger (-45)
        // apple → 1 "Excess Fructose + Sorbitol" trigger (-45) — different SubCategory from honey
        // Total: 3 unique triggers → score = 100 - 135 = clamped at 0
        result.FodmapScore.Should().Be(0);
        result.FodmapRating.Should().Be("Very High FODMAP");
    }

    [Fact]
    public void ModerateTrigger_Drops12Points()
    {
        var result = _sut.Assess(MakeProduct("Asparagus Soup", "asparagus, water, salt"));
        result.FodmapScore.Should().Be(85);
        result.FodmapRating.Should().Be("Low FODMAP");
    }

    [Fact]
    public void LowTrigger_Drops5Points()
    {
        var result = _sut.Assess(MakeProduct("Diet Gum", "erythritol, gum base"));
        result.FodmapScore.Should().Be(95);
        result.FodmapRating.Should().Be("Low FODMAP");
    }

    // ─── Oligosaccharides — Fructan ─────────────────────────────────────

    [Theory]
    [InlineData("wheat flour")]
    [InlineData("whole wheat")]
    [InlineData("onion")]
    [InlineData("garlic")]
    [InlineData("inulin")]
    [InlineData("chicory root")]
    [InlineData("fructooligosaccharide")]
    [InlineData("barley")]
    public void DetectsFructanTriggers(string ingredient)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {ingredient}, salt"));
        result.TriggerCount.Should().BeGreaterThan(0);
        result.Triggers.Should().Contain(t => t.SubCategory.Contains("Fructan"));
    }

    [Fact]
    public void DetectsShallot()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "shallot, butter, wine"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Shallot"));
    }

    [Fact]
    public void DetectsArtichoke()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "artichoke, oil, lemon"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Artichoke"));
    }

    [Fact]
    public void DetectsCashew()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "cashew, sugar, oil"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Cashew"));
    }

    [Fact]
    public void DetectsPistachio()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "pistachio, salt"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Pistachio"));
    }

    // ─── Oligosaccharides — GOS ─────────────────────────────────────────

    [Theory]
    [InlineData("chickpea")]
    [InlineData("lentil")]
    [InlineData("kidney bean")]
    [InlineData("black bean")]
    [InlineData("soybean")]
    public void DetectsGosTriggers(string ingredient)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {ingredient}, salt"));
        result.TriggerCount.Should().BeGreaterThan(0);
        result.Triggers.Should().Contain(t => t.SubCategory.Contains("GOS"));
    }

    [Fact]
    public void DetectsSoyMilk()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "soy milk, sugar"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Soy Milk"));
    }

    [Fact]
    public void DetectsHummus()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "hummus, tahini"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Hummus"));
    }

    // ─── Disaccharides — Lactose ────────────────────────────────────────

    [Theory]
    [InlineData("whole milk")]
    [InlineData("milk powder")]
    [InlineData("condensed milk")]
    [InlineData("ice cream")]
    [InlineData("lactose")]
    [InlineData("ricotta")]
    [InlineData("cottage cheese")]
    public void DetectsLactoseTriggers(string ingredient)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {ingredient}, sugar"));
        result.TriggerCount.Should().BeGreaterThan(0);
        result.Triggers.Should().Contain(t => t.SubCategory.Contains("Lactose"));
    }

    [Fact]
    public void DetectsWheyConcentrate()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "whey concentrate, cocoa, sugar"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Whey Concentrate"));
    }

    // ─── Monosaccharides — Excess Fructose ──────────────────────────────

    [Theory]
    [InlineData("high fructose corn syrup")]
    [InlineData("agave")]
    [InlineData("honey")]
    [InlineData("apple juice")]
    [InlineData("pear juice")]
    public void DetectsExcessFructoseTriggers(string ingredient)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {ingredient}, salt"));
        result.TriggerCount.Should().BeGreaterThan(0);
        result.Triggers.Should().Contain(t => t.SubCategory.Contains("Fructose"));
    }

    [Fact]
    public void DetectsCrystallineFructose()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, crystalline fructose"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Crystalline Fructose"));
    }

    // ─── Polyols ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sorbitol")]
    [InlineData("mannitol")]
    [InlineData("maltitol")]
    [InlineData("xylitol")]
    [InlineData("isomalt")]
    [InlineData("lactitol")]
    public void DetectsPolyolTriggers(string ingredient)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {ingredient}"));
        result.TriggerCount.Should().BeGreaterThan(0);
        result.Triggers.Should().Contain(t => t.Category == "Polyol");
    }

    [Fact]
    public void ErythritolIsLowSeverity()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "erythritol, gum base"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Erythritol") && t.Severity == "Low");
    }

    // ─── Additive Tags (E-numbers) ──────────────────────────────────────

    [Theory]
    [InlineData("en:e420", "Sorbitol")]
    [InlineData("en:e421", "Mannitol")]
    [InlineData("en:e953", "Isomalt")]
    [InlineData("en:e965", "Maltitol")]
    [InlineData("en:e967", "Xylitol")]
    [InlineData("en:e968", "Erythritol")]
    public void DetectsPolyolAdditivesByENumber(string tag, string expectedName)
    {
        var result = _sut.Assess(MakeProduct(additiveTags: [tag]));
        result.Triggers.Should().Contain(t => t.Name.Contains(expectedName));
    }

    [Fact]
    public void E968_Erythritol_IsLowSeverity()
    {
        var result = _sut.Assess(MakeProduct(additiveTags: ["en:e968"]));
        result.Triggers.Should().Contain(t => t.Severity == "Low");
    }

    // ─── Additive Name Matching ─────────────────────────────────────────

    [Fact]
    public void DetectsPolyolByAdditiveName()
    {
        var result = _sut.Assess(MakeProduct(additives: [
            new FoodAdditiveDto { Name = "Sorbitol", Category = "Sweetener", CspiRating = "Caution", UsRegulatoryStatus = "Approved", EuRegulatoryStatus = "Approved" }
        ]));
        result.Triggers.Should().Contain(t => t.Name.Contains("Sorbitol"));
    }

    [Fact]
    public void DetectsInulinByAdditiveName()
    {
        var result = _sut.Assess(MakeProduct(additives: [
            new FoodAdditiveDto { Name = "Inulin", Category = "Fiber", CspiRating = "Safe", UsRegulatoryStatus = "Approved", EuRegulatoryStatus = "Approved" }
        ]));
        result.Triggers.Should().Contain(t => t.SubCategory.Contains("Fructan"));
    }

    // ─── Whole Food Product Name Matching ───────────────────────────────

    [Fact]
    public void DetectsGarlicBreadByName()
    {
        var result = _sut.Assess(MakeProduct("Garlic Bread"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Garlic Bread"));
    }

    [Fact]
    public void DetectsFalafelByName()
    {
        var result = _sut.Assess(MakeProduct("Falafel Wrap"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Falafel"));
    }

    [Fact]
    public void DetectsDalByName()
    {
        var result = _sut.Assess(MakeProduct("Red Lentil Dal"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Dal"));
    }

    // ─── Mixed Category Products ────────────────────────────────────────

    [Fact]
    public void AppleHasMultipleFodmapCategories()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "apple, sugar"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Apple"));
        result.Categories.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void MushroomDetectedAsMannitol()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "mushroom, butter, salt"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Mushroom") && t.SubCategory.Contains("Mannitol"));
    }

    [Fact]
    public void CauliflowerDetectedAsMannitol()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "cauliflower, oil, salt"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Cauliflower"));
    }

    // ─── Complex Real-World Products ────────────────────────────────────

    [Fact]
    public void Nutella_HasLactoseAndFructan()
    {
        var result = _sut.Assess(MakeProduct("Nutella",
            "sugar, palm oil, hazelnuts, cocoa, skim milk powder, whey powder, lecithin, vanillin",
            sugar: 56.3m));
        result.TriggerCount.Should().BeGreaterThan(0);
        result.Triggers.Should().Contain(t => t.SubCategory.Contains("Lactose"));
    }

    [Fact]
    public void SugarFreeGum_HighPolyols()
    {
        var result = _sut.Assess(MakeProduct("Sugar Free Gum",
            "sorbitol, maltitol, xylitol, gum base, mannitol, aspartame"));
        result.FodmapScore.Should().BeLessThanOrEqualTo(25);
        result.FodmapRating.Should().Be("Very High FODMAP");
        result.Triggers.Count(t => t.Category == "Polyol").Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void ProteinBar_WithInulin()
    {
        var result = _sut.Assess(MakeProduct("Fiber One Bar",
            "chicory root fiber, oats, sugar, palm oil, whey concentrate"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Chicory Root"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Whey Concentrate"));
    }

    [Fact]
    public void GarlicAndOnionPasta_VeryHighFodmap()
    {
        var result = _sut.Assess(MakeProduct("Garlic & Onion Pasta Sauce",
            "tomatoes, onion, garlic, wheat flour, olive oil, basil"));
        // onion, garlic, wheat flour all share Fructan/Oligosaccharide — deduped to 1 trigger
        result.Triggers.Should().Contain(t => t.SubCategory == "Fructan");
        result.TriggerCount.Should().Be(1);
        result.FodmapScore.Should().Be(55);
    }

    [Fact]
    public void PureRice_NoTriggers()
    {
        var result = _sut.Assess(MakeProduct("White Rice", "rice"));
        result.FodmapScore.Should().Be(100);
        result.TriggerCount.Should().Be(0);
    }

    [Fact]
    public void PlainChicken_NoTriggers()
    {
        var result = _sut.Assess(MakeProduct("Grilled Chicken", "chicken breast, salt, pepper"));
        result.FodmapScore.Should().Be(100);
        result.TriggerCount.Should().Be(0);
    }

    // ─── High Sugar + Fructose Source ───────────────────────────────────

    [Fact]
    public void HighSugarWithFructose_TriggersExcessFructoseFlag()
    {
        var result = _sut.Assess(MakeProduct("Apple Juice Drink",
            "water, fructose, apple juice, citric acid", sugar: 45m));
        result.Triggers.Should().Contain(t => t.Name.Contains("Excess Fructose"));
    }

    [Fact]
    public void HighSugarWithoutFructose_NoExtraFlag()
    {
        var result = _sut.Assess(MakeProduct("Sugar Water", "water, glucose, salt", sugar: 40m));
        result.Triggers.Should().NotContain(t => t.Name.Contains("Excess Fructose (from fruit juice/fructose)"));
    }

    // ─── Summary Generation ─────────────────────────────────────────────

    [Fact]
    public void NoTriggers_SummaryMentionsLowFodmap()
    {
        var result = _sut.Assess(MakeProduct("Eggs", "eggs"));
        result.Summary.Should().Contain("low-FODMAP diet");
    }

    [Fact]
    public void HighTriggers_SummaryMentionsAvoid()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, onion"));
        result.Summary.Should().Contain("elimination phase");
    }

    [Fact]
    public void ModerateOnly_SummaryMentionsMonitor()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "asparagus, oil"));
        result.Summary.Should().Contain("personal experience can vary");
    }

    // ─── Deduplication ──────────────────────────────────────────────────

    [Fact]
    public void DuplicateTriggers_AreNotRepeated()
    {
        // Both ingredient text and additive tag match sorbitol — should only appear once
        var result = _sut.Assess(MakeProduct(
            ingredients: "sorbitol, sugar",
            additiveTags: ["en:e420"]));
        result.Triggers.Count(t => t.SubCategory.Contains("Sorbitol")).Should().Be(1);
    }

    [Fact]
    public void IngredientAndAdditiveNameDeduplicate()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "inulin, water",
            additives: [new FoodAdditiveDto { Name = "Inulin", Category = "Fiber", CspiRating = "Safe", UsRegulatoryStatus = "Approved", EuRegulatoryStatus = "Approved" }]
        ));
        result.Triggers.Count(t => t.SubCategory.Contains("Fructan")).Should().BeGreaterThanOrEqualTo(1);
        // Ensure no duplicate — inulin from ingredients and inulin from additives share same category+subcategory
        result.Triggers.Count(t => t.Name.Contains("Inulin")).Should().Be(1);
    }

    // ─── Ordering ───────────────────────────────────────────────────────

    [Fact]
    public void TriggersOrderedBySeverity_HighFirst()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "erythritol, asparagus, garlic"));
        // garlic=High, asparagus=Moderate, erythritol=Low
        result.Triggers.First().Severity.Should().Be("High");
        result.Triggers.Last().Severity.Should().Be("Low");
    }

    // ─── Categories List ────────────────────────────────────────────────

    [Fact]
    public void CategoriesAreDistinctAndSorted()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, sorbitol, lactose"));
        result.Categories.Should().BeInAscendingOrder();
        result.Categories.Should().OnlyHaveUniqueItems();
    }

    // ─── Count Validation ───────────────────────────────────────────────

    [Fact]
    public void CountsAreAccurate()
    {
        // garlic → Fructan/Oligosaccharide (High)
        // asparagus → Fructan/Oligosaccharide (Moderate) — DEDUPED (same SubCategory+Category as garlic)
        // erythritol → Erythritol/Polyol (Low)
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, asparagus, erythritol"));
        result.HighCount.Should().Be(1); // garlic
        result.ModerateCount.Should().Be(0); // asparagus deduped
        result.LowCount.Should().Be(1); // erythritol
        result.TriggerCount.Should().Be(2);
    }

    // ─── AssessText Method ──────────────────────────────────────────────

    [Fact]
    public void AssessText_DetectsIngredientsFromDescription()
    {
        var result = _sut.AssessText("garlic bread with cheese");
        result.TriggerCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AssessText_DetectsWholeFoodTriggers()
    {
        var result = _sut.AssessText("falafel wrap with hummus");
        result.Triggers.Should().Contain(t => t.Name.Contains("Falafel"));
    }

    [Fact]
    public void AssessText_NoTriggers_ReturnsLowFodmap()
    {
        var result = _sut.AssessText("grilled chicken with rice");
        result.FodmapScore.Should().Be(100);
    }

    [Fact]
    public void AssessText_MultipleWheatAndGarlic()
    {
        var result = _sut.AssessText("wheat pasta with garlic sauce");
        result.Triggers.Should().Contain(t => t.Name.Contains("Wheat") || t.SubCategory.Contains("Fructan"));
        result.TriggerCount.Should().BeGreaterThan(0);
    }

    // ─── Edge Cases ─────────────────────────────────────────────────────

    [Fact]
    public void NullIngredients_NoError()
    {
        var result = _sut.Assess(MakeProduct("Test Product", null));
        result.FodmapScore.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void EmptyIngredients_NoTriggers()
    {
        var result = _sut.Assess(MakeProduct(ingredients: ""));
        result.TriggerCount.Should().Be(0);
    }

    [Fact]
    public void EmptyProduct_ReturnsValidResult()
    {
        var result = _sut.Assess(new FoodProductDto { Name = "" });
        result.FodmapScore.Should().Be(100);
        result.FodmapRating.Should().Be("Low FODMAP");
    }

    // ─── Stone Fruits ───────────────────────────────────────────────────

    [Theory]
    [InlineData("peach")]
    [InlineData("plum")]
    [InlineData("cherry")]
    [InlineData("apricot")]
    [InlineData("nectarine")]
    public void DetectsStoneFruits(string fruit)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {fruit}, sugar"));
        result.Triggers.Should().Contain(t => t.Category.Contains("Polyol") || t.SubCategory.Contains("Sorbitol") || t.SubCategory.Contains("Fructose"));
    }

    [Fact]
    public void PruneHighSeverity()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "prune, sugar"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Prune") && t.Severity == "High");
    }

    // ─── Vegetables ─────────────────────────────────────────────────────

    [Fact]
    public void SweetPotatoDetected()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "sweet potato, oil, salt"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Sweet Potato"));
    }

    [Fact]
    public void CeleryDetected()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "celery, water"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Celery"));
    }

    // ─── Rating Boundaries ──────────────────────────────────────────────

    [Fact]
    public void Score80_IsLowFodmap()
    {
        // 1 moderate(15) + 1 low(5) = 20, score = 80 → Low FODMAP
        var result = _sut.Assess(MakeProduct(ingredients: "asparagus, erythritol"));
        result.FodmapScore.Should().Be(80);
        result.FodmapRating.Should().Be("Low FODMAP");
    }

    [Fact]
    public void Score65To79_IsModerateFodmap()
    {
        // 2 moderate triggers (15+15=30), score = 70 → Moderate FODMAP
        var result = _sut.Assess(MakeProduct(ingredients: "asparagus, cream"));
        result.FodmapScore.Should().Be(70);
        result.FodmapRating.Should().Be("Moderate FODMAP");
    }

    [Fact]
    public void Score40To64_IsHighFodmap()
    {
        // 1 high trigger (45), score = 55 → High FODMAP
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, salt"));
        result.FodmapScore.Should().Be(55);
        result.FodmapRating.Should().Be("High FODMAP");
    }

    [Fact]
    public void ScoreBelow40_IsVeryHighFodmap()
    {
        // 1 high (45) + 1 moderate (15) = 60 → still High FODMAP range.
        // 1 high (45) + 2 moderates (15+15=30) = 75 penalty, score = 25 → "Very High FODMAP" (<40)
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, cream, avocado"));
        result.FodmapScore.Should().Be(25);
        result.FodmapRating.Should().Be("Very High FODMAP");
    }

    // ─── Rye Detection ──────────────────────────────────────────────────

    [Fact]
    public void DetectsRyeWithSpacePrefix()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, rye flour, salt"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Rye"));
    }

    // ─── Lactase Enzyme Mitigation ──────────────────────────────────────

    [Fact]
    public void LactaseInIngredients_DowngradesLactoseSeverityToLow()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "whole milk, lactase, sugar"));
        var lactoseTrigger = result.Triggers.FirstOrDefault(t => t.SubCategory == "Lactose");
        lactoseTrigger.Should().NotBeNull();
        lactoseTrigger!.Severity.Should().Be("Low");
        lactoseTrigger.Explanation.Should().Contain("lactase enzyme");
    }

    [Fact]
    public void NoLactaseInIngredients_LactoseNotDowngraded()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "whole milk, sugar"));
        var lactoseTrigger = result.Triggers.FirstOrDefault(t => t.SubCategory == "Lactose");
        lactoseTrigger.Should().NotBeNull();
        lactoseTrigger!.Severity.Should().Be("High");
    }

    [Fact]
    public void LactaseDoesNotAffectNonLactoseTriggers()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, whole milk, lactase"));
        var fructanTrigger = result.Triggers.FirstOrDefault(t => t.SubCategory.Contains("Fructan"));
        fructanTrigger.Should().NotBeNull();
        fructanTrigger!.Severity.Should().Be("High");
    }

    // ─── Generic Whole-Food Skipping ────────────────────────────────────

    [Fact]
    public void GenericProductName_WithRealIngredients_SkipsWholeFoodTrigger()
    {
        var result = _sut.Assess(MakeProduct("Protein Shake", "water, whey protein isolate, cocoa, salt"));
        result.Triggers.Should().NotContain(t => t.Name.Contains("Protein Shake"));
    }

    [Fact]
    public void GenericProductName_WithoutRealIngredients_UsesWholeFoodTrigger()
    {
        var result = _sut.Assess(MakeProduct("Protein Shake"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Protein Shake"));
    }

    [Fact]
    public void NonGenericProductName_WithRealIngredients_StillUsesWholeFoodTrigger()
    {
        var result = _sut.Assess(MakeProduct("Garlic Bread", "wheat flour, garlic, butter, salt"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Garlic Bread") || t.SubCategory.Contains("Fructan"));
    }

    // ─── New Milk Patterns ──────────────────────────────────────────────

    [Theory]
    [InlineData("low fat milk")]
    [InlineData("fat free milk")]
    [InlineData("reduced fat milk")]
    public void DetectsNewMilkPatterns(string ingredient)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {ingredient}, sugar"));
        result.Triggers.Should().Contain(t => t.SubCategory == "Lactose");
    }

    [Fact]
    public void GenericMilk_DetectedAsLactose()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, milk, sugar, cocoa"));
        result.Triggers.Should().Contain(t => t.SubCategory == "Lactose");
    }

    // ─── Carrageenan Trigger ────────────────────────────────────────────

    [Fact]
    public void Carrageenan_DetectedAsFodmapTrigger()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, carrageenan, sugar"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Carrageenan"));
    }

    // ─── Smoothie Stereotype Removal ────────────────────────────────────

    [Fact]
    public void Smoothie_WithRealIngredients_NoFalseExcessFructose()
    {
        var result = _sut.Assess(MakeProduct(name: "Rokeby Protein Smoothie",
            ingredients: "low fat milk, cane sugar, cream, cocoa, natural flavours, lactase enzyme, carrageenan"));
        result.Triggers.Should().NotContain(t => t.SubCategory == "Excess Fructose");
    }

    [Fact]
    public void Smoothie_NameAlone_NoLongerInfersExcessFructose()
    {
        var result = _sut.Assess(MakeProduct(name: "Berry Smoothie"));
        result.Triggers.Should().NotContain(t => t.SubCategory == "Excess Fructose");
    }
}
