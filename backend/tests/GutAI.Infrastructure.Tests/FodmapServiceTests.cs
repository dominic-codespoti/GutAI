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

    // ─── Confidence ceiling ──────────────────────────────────────────────
    // FODMAP status is portion-dependent and cannot be measured from ingredient text alone,
    // so this rule-based screen never returns "High" confidence regardless of trigger count.

    [Fact]
    public void Confidence_DetailedIngredientList_ReturnsMedium()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "water, onion, garlic, chicken stock, salt, pepper, olive oil"));

        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Confidence_ShortIngredientString_ReturnsLow()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "onion"));

        result.Confidence.Should().Be("Low");
    }

    [Fact]
    public void Confidence_NoIngredients_UntrustedBrandedSource_ReturnsLow()
    {
        var product = MakeProduct() with
        {
            DataSource = "OpenFoodFacts",
            FoodKind = GutAI.Domain.Enums.FoodKind.Branded,
        };

        var result = _sut.Assess(product);

        result.Confidence.Should().Be("Low");
    }

    [Fact]
    public void Confidence_NoIngredients_TrustedWholeFoodSource_ReturnsMedium()
    {
        var product = MakeProduct() with
        {
            DataSource = "USDA",
            FoodKind = GutAI.Domain.Enums.FoodKind.WholeFood,
        };

        var result = _sut.Assess(product);

        result.Confidence.Should().Be("Medium");
    }

    [Fact]
    public void Confidence_NeverReturnsHigh()
    {
        var result = _sut.Assess(MakeProduct(
            ingredients: "water, wheat flour, sugar, onion, garlic, palm oil, salt, natural flavouring, inulin"));

        result.Confidence.Should().NotBe("High");
    }

    // ─── Status semantics — the core of this redesign ───────────────────
    // "No trigger detected" and "nothing to screen at all" must never collapse into the same
    // result. The old design's "no triggers -> score 100 -> Low FODMAP" conflated both.

    [Fact]
    public void NoIngredients_UntrustedSource_ReturnsInsufficientInformation_NotNoTriggers()
    {
        var product = MakeProduct() with
        {
            DataSource = "OpenFoodFacts",
            FoodKind = GutAI.Domain.Enums.FoodKind.Branded,
        };

        var result = _sut.Assess(product);

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.InsufficientInformation));
        result.MissingEvidence.Should().NotBeEmpty();
        result.Summary.Should().Contain("does not mean it is Low FODMAP");
    }

    [Fact]
    public void EmptyProduct_ReturnsInsufficientInformation()
    {
        var result = _sut.Assess(new FoodProductDto { Name = "" });

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.InsufficientInformation));
        result.MissingEvidence.Should().Contain("an ingredient list");
        result.MissingEvidence.Should().Contain("a verified catalog identity");
    }

    [Fact]
    public void NoIngredients_TrustedWholeFoodSource_ReturnsNoKnownTriggersDetected_NotInsufficient()
    {
        // A trusted catalog identity (USDA/AUSNUT/WholeFood) is itself evidence that this is a
        // real, recognized food — the screen ran, even without an ingredient list.
        var product = MakeProduct() with
        {
            DataSource = "USDA",
            FoodKind = GutAI.Domain.Enums.FoodKind.WholeFood,
        };

        var result = _sut.Assess(product);

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.NoKnownTriggersDetected));
    }

    [Fact]
    public void HasIngredients_NoTriggers_ReturnsNoKnownTriggersDetected()
    {
        var result = _sut.Assess(MakeProduct("Rice", "white rice, water, salt"));

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.NoKnownTriggersDetected));
        result.TriggerCount.Should().Be(0);
    }

    [Fact]
    public void HasTriggers_ReturnsPotentialTriggersDetected()
    {
        var result = _sut.Assess(MakeProduct("Garlic Sauce", "garlic, oil, salt"));

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.PotentialTriggersDetected));
    }

    [Fact]
    public void AssessText_EmptyDescription_ReturnsInsufficientInformation()
    {
        var result = _sut.AssessText("");

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.InsufficientInformation));
        result.MissingEvidence.Should().Contain("a non-empty food description");
    }

    [Fact]
    public void AssessText_WhitespaceOnlyDescription_ReturnsInsufficientInformation()
    {
        var result = _sut.AssessText("   ");

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.InsufficientInformation));
    }

    [Fact]
    public void AssessText_NonTrivialDescription_ReturnsNoKnownTriggersDetected_NotInsufficient()
    {
        var result = _sut.AssessText("grilled chicken with rice");

        result.Status.Should().Be(nameof(FodmapAssessmentStatus.NoKnownTriggersDetected));
    }

    // ─── Ingredient screening score (renamed from FodmapScore) ───────────
    // Same computation as before — a numeric signal for PersonalizedScoreDto's composite, not
    // a standalone serving classification.

    [Fact]
    public void NoTriggers_ScreeningScoreIs100()
    {
        var result = _sut.Assess(MakeProduct("Rice", "white rice, water, salt"));
        result.IngredientScreeningScore.Should().Be(100);
        result.TriggerCount.Should().Be(0);
    }

    [Fact]
    public void SingleHighTrigger_Drops25Points()
    {
        var result = _sut.Assess(MakeProduct("Garlic Sauce", "garlic, oil, salt"));
        result.IngredientScreeningScore.Should().Be(40);
    }

    [Fact]
    public void TwoDistinctFructansAndModerateLactose_Score14()
    {
        var result = _sut.Assess(MakeProduct("Garlic Onion Dip", "onion, garlic, cream"));
        // Name-level dedup keeps distinct foods individually visible:
        // onion → Onion (Fructan) High (×0.40); garlic → Garlic (Fructan) High (×0.40);
        // cream → Lactose Moderate (×0.85)
        // Total: 100 × 0.40 × 0.40 × 0.85 = 13.6 → 14
        result.IngredientScreeningScore.Should().Be(14);
    }

    [Fact]
    public void ManyHighTriggers_ClampedAt0()
    {
        var result = _sut.Assess(MakeProduct("Everything Bagel", "wheat flour, onion, garlic, honey, apple, inulin"));
        // Name-level dedup counts every distinct food:
        // wheat flour/onion/garlic/inulin → 4 distinct Fructan High triggers (×0.40 each)
        // honey → Honey (Excess Fructose) High (×0.40); apple → Apple (Fructose + Sorbitol) High (×0.40)
        // Total: 100 × 0.40⁶ = 0.41 → 0
        result.IngredientScreeningScore.Should().Be(0);
    }

    [Fact]
    public void ModerateTrigger_Drops12Points()
    {
        var result = _sut.Assess(MakeProduct("Asparagus Soup", "asparagus, water, salt"));
        result.IngredientScreeningScore.Should().Be(85);
    }

    [Fact]
    public void LowTrigger_Drops5Points()
    {
        var result = _sut.Assess(MakeProduct("Diet Gum", "erythritol, gum base"));
        result.IngredientScreeningScore.Should().Be(95);
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
        result.IngredientScreeningScore.Should().BeLessThanOrEqualTo(25);
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
        // Name-level dedup keeps each fructan source individually visible and counted:
        // onion + garlic + wheat flour → 3 distinct Fructan High triggers
        result.Triggers.Should().Contain(t => t.Name == "Onion (Fructan)");
        result.Triggers.Should().Contain(t => t.Name == "Garlic (Fructan)");
        result.Triggers.Should().Contain(t => t.Name == "Wheat (Fructan)");
        // 4th row: the product NAME matches the whole-food "pasta" entry ("Pasta (Fructan)").
        result.TriggerCount.Should().Be(4);
        result.IngredientScreeningScore.Should().Be(3); // 100 × 0.40⁴
    }

    [Fact]
    public void PureRice_NoTriggers()
    {
        var result = _sut.Assess(MakeProduct("White Rice", "rice"));
        result.IngredientScreeningScore.Should().Be(100);
        result.TriggerCount.Should().Be(0);
    }

    [Fact]
    public void PlainChicken_NoTriggers()
    {
        var result = _sut.Assess(MakeProduct("Grilled Chicken", "chicken breast, salt, pepper"));
        result.IngredientScreeningScore.Should().Be(100);
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
        result.Summary.Should().Contain("not a serving-size FODMAP classification");
    }

    [Fact]
    public void HighTriggers_SummaryMentionsAvoid()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, onion"));
        result.Summary.Should().Contain("portion-dependent");
    }

    [Fact]
    public void ModerateOnly_SummaryMentionsMonitor()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "asparagus, oil"));
        result.Summary.Should().Contain("cannot classify a serving");
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
        result.ModerateCount.Should().Be(1); // asparagus — distinct food, no longer deduped away
        result.LowCount.Should().Be(1); // erythritol
        result.TriggerCount.Should().Be(3);
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
    public void AssessText_NoTriggers_ScreeningScoreIs100()
    {
        var result = _sut.AssessText("grilled chicken with rice");
        result.IngredientScreeningScore.Should().Be(100);
    }

    [Fact]
    public void AssessText_MultipleWheatAndGarlic()
    {
        var result = _sut.AssessText("wheat pasta with garlic sauce");
        result.Triggers.Should().Contain(t => t.Name.Contains("Wheat") || t.SubCategory.Contains("Fructan"));
        result.TriggerCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AssessText_WholeFoodMatching_UsesWordBoundaries_NotSubstring()
    {
        // Previously AssessText used naive .Contains for whole-food name matching (unlike Assess,
        // which already used word-boundary regex) — "pita" inside "pepitas" must not false-positive.
        var result = _sut.AssessText("a snack with pepitas and sunflower seeds");
        result.Triggers.Should().NotContain(t => t.Name.Contains("Pita"));
    }

    [Fact]
    public void AssessText_AppliesSameMitigationsAsAssess()
    {
        // Product and text assessment must share mitigation coverage — lactose-free suppression
        // previously only applied to Assess(product), never AssessText.
        var result = _sut.AssessText("lactose-free milk with cereal");
        result.Triggers.Should().NotContain(t => t.SubCategory == "Lactose");
    }

    // ─── Edge Cases ─────────────────────────────────────────────────────

    [Fact]
    public void NullIngredients_NoError()
    {
        var result = _sut.Assess(MakeProduct("Test Product", null));
        result.IngredientScreeningScore.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void EmptyIngredients_NoTriggers()
    {
        var result = _sut.Assess(MakeProduct(ingredients: ""));
        result.TriggerCount.Should().Be(0);
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

    // ─── Score Magnitude Checks (rating boundaries no longer exist — the underlying score
    //     math is unchanged, verified directly instead of via the removed rating labels) ────

    [Fact]
    public void TwoModerateTriggers_Score80()
    {
        // asparagus=Moderate(×0.85) + erythritol=Low(×0.95) → 100 × 0.85 × 0.95 = 80.75 → 81
        var result = _sut.Assess(MakeProduct(ingredients: "asparagus, erythritol"));
        result.IngredientScreeningScore.Should().Be(81);
    }

    [Fact]
    public void TwoModerateTriggers_DifferentCategories_Score72()
    {
        // asparagus=Moderate(×0.85) + cream=Moderate(×0.85) → 100 × 0.85 × 0.85 = 72.25 → 72
        var result = _sut.Assess(MakeProduct(ingredients: "asparagus, cream"));
        result.IngredientScreeningScore.Should().Be(72);
    }

    [Fact]
    public void SingleHighTrigger_Score40()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, salt"));
        result.IngredientScreeningScore.Should().Be(40);
        result.Status.Should().Be(nameof(FodmapAssessmentStatus.PotentialTriggersDetected));
    }

    [Fact]
    public void ThreeCategoryStacking_Score27()
    {
        // garlic=High(×0.40) + cream=Moderate(×0.85) + avocado=Moderate(×0.85) → 3 distinct categories
        // 100 × 0.40 × 0.85 × 0.85 × 0.92^(3-2) = 26.6 → 27
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, cream, avocado"));
        result.IngredientScreeningScore.Should().Be(27);
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

    [Fact]
    public void LeekGreenTops_SuppressesLeekFructanTrigger()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "green tops of leek, olive oil, salt"));
        result.Triggers.Should().NotContain(t => t.Name == "Leek (Fructan)");
    }

    [Fact]
    public void PlainLeek_StillTriggersLeekFructan()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "leek, olive oil, salt"));
        result.Triggers.Should().Contain(t => t.Name == "Leek (Fructan)");
    }

    // ─── Processing mitigations — garlic oil, firm tofu, canned legumes ─
    // Named, tested exceptions where raw pattern-matching would otherwise misclassify a
    // specific, well-documented preparation (see FodmapService.ApplyProcessingMitigations).

    [Fact]
    public void GarlicInfusedOil_SuppressesGarlicTrigger_WhenNoOtherGarlicPresent()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "garlic-infused olive oil, salt, pepper"));
        result.Triggers.Should().NotContain(t => t.Name == "Garlic (Fructan)");
    }

    [Fact]
    public void GarlicOil_SuppressesGarlicTrigger()
    {
        var result = _sut.Assess(MakeProduct(ingredients: "garlic oil, salt"));
        result.Triggers.Should().NotContain(t => t.Name == "Garlic (Fructan)");
    }

    [Fact]
    public void GarlicOilPlusSolidGarlic_StillFlagsGarlic()
    {
        // Fructans aren't oil-soluble, but real garlic solids alongside the oil still carry them.
        var result = _sut.Assess(MakeProduct(ingredients: "garlic oil, garlic, salt"));
        result.Triggers.Should().Contain(t => t.Name == "Garlic (Fructan)");
    }

    [Fact]
    public void FirmTofu_SuppressesSoybeanTrigger()
    {
        var result = _sut.Assess(MakeProduct("Firm Tofu", "soybeans, water, calcium sulfate"));
        result.Triggers.Should().NotContain(t => t.Name == "Soybean (GOS)");
    }

    [Fact]
    public void ExtraFirmTofu_SuppressesSoybeanTrigger()
    {
        var result = _sut.Assess(MakeProduct("Extra Firm Tofu", "soybeans, water, magnesium chloride"));
        result.Triggers.Should().NotContain(t => t.Name == "Soybean (GOS)");
    }

    [Fact]
    public void SilkenTofu_DoesNotSuppressSoybeanTrigger()
    {
        // Soft/silken tofu retains more GOS than firm/extra-firm — not covered by the exception.
        var result = _sut.Assess(MakeProduct("Silken Tofu", "soybeans, water, calcium sulfate"));
        result.Triggers.Should().Contain(t => t.Name == "Soybean (GOS)");
    }

    [Fact]
    public void CannedChickpeas_DowngradesFromHighToModerate()
    {
        var result = _sut.Assess(MakeProduct("Canned Chickpeas", "chickpeas, water, salt (canned)"));
        var trigger = result.Triggers.FirstOrDefault(t => t.Name.Contains("Chickpea"));
        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be("Moderate");
        trigger.Explanation.Should().Contain("Canned");
    }

    [Fact]
    public void DriedChickpeas_StaysHigh()
    {
        var result = _sut.Assess(MakeProduct("Dried Chickpeas", "chickpeas"));
        var trigger = result.Triggers.FirstOrDefault(t => t.Name.Contains("Chickpea"));
        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be("High");
    }

    [Fact]
    public void CannedLentils_DowngradesFromHighToModerate()
    {
        var result = _sut.Assess(MakeProduct("Canned Lentils", "canned lentils, water"));
        var trigger = result.Triggers.FirstOrDefault(t => t.Name.Contains("Lentil"));
        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be("Moderate");
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
        // Cane sugar may trigger a Low-severity "Excess Fructose" entry — that's fine.
        // The key assertion is no High/Moderate excess fructose false positives.
        result.Triggers.Should().NotContain(t => t.SubCategory == "Excess Fructose" && t.Severity != "Low");
    }

    [Fact]
    public void Smoothie_NameAlone_NoLongerInfersExcessFructose()
    {
        var result = _sut.Assess(MakeProduct(name: "Berry Smoothie"));
        result.Triggers.Should().NotContain(t => t.SubCategory == "Excess Fructose");
    }

    [Fact]
    public void Test_Pepitas_DoesNotMatchPita()
    {
        var result = _sut.Assess(MakeProduct("Carman's Protein Bar", "pepitas, sunflower seeds, soy protein"));
        result.Triggers.Should().NotContain(t => t.Name.Contains("Pita"));
    }

    [Fact]
    public void Test_PitaBread_MatchesPita()
    {
        var result = _sut.Assess(MakeProduct("Pita Bread", "pita, water, yeast"));
        result.Triggers.Should().Contain(t => t.Name.Contains("Pita"));
    }

    // ─── FodmapData Wiring Verification ────────────────────────────────

    [Theory]
    [InlineData("Cherries", "cherries, sugar", "Moderate")]
    [InlineData("Blackberry Jam", "blackberry, sugar, pectin", "Moderate")]
    [InlineData("Cauliflower Soup", "cauliflower, water, salt", "High")]
    public void FodmapData_NewIngredientEntries_CorrectSeverity(string name, string ingredients, string expectedSeverity)
    {
        var result = _sut.Assess(MakeProduct(name, ingredients));
        result.TriggerCount.Should().BeGreaterThan(0);
        result.Triggers.Should().Contain(t => t.Severity == expectedSeverity);
    }

    [Theory]
    [InlineData("Cherries")]
    [InlineData("Blackberry")]
    [InlineData("Blackberries")]
    [InlineData("Cauliflower")]
    [InlineData("Pita Bread")]
    public void FodmapData_NewWholeFoodEntries_Flagged(string productName)
    {
        var result = _sut.Assess(MakeProduct(productName));
        result.TriggerCount.Should().BeGreaterThan(0, $"'{productName}' should trigger at least one FODMAP flag via FodmapData");
    }

    [Fact]
    public void FodmapData_StoneFruits_UseModerateSeverity()
    {
        // Per SharedFodmapSeverities.cs, stone fruits are "Moderate" not "High"
        var fruits = new[] { "apricot", "cherry", "nectarine", "peach", "plum" };
        foreach (var fruit in fruits)
        {
            var result = _sut.Assess(MakeProduct(fruit, $"{fruit}, water"));
            var trigger = result.Triggers.FirstOrDefault(t => t.Name.Contains(fruit, StringComparison.OrdinalIgnoreCase));
            trigger.Should().NotBeNull($"'{fruit}' should be detected as a trigger");
            trigger!.Severity.Should().Be("Moderate", $"'{fruit}' should have Moderate severity per SharedFodmapSeverities");
        }
    }

    [Fact]
    public void FodmapData_DairyProducts_UseModerateSeverity()
    {
        // Per SharedFodmapSeverities.cs, dairy items are "Moderate" not "High"
        var dairyProducts = new[] { "yogurt", "cream", "buttermilk" };
        foreach (var dairy in dairyProducts)
        {
            var result = _sut.Assess(MakeProduct(dairy, $"{dairy}, water"));
            var trigger = result.Triggers.FirstOrDefault(t => t.SubCategory == "Lactose");
            trigger.Should().NotBeNull($"'{dairy}' should be detected as a lactose trigger");
            trigger!.Severity.Should().Be("Moderate", $"'{dairy}' should have Moderate severity per SharedFodmapSeverities");
        }
    }

    // ─── Clinical Classification Verification ──────────────────────────

    [Fact]
    public void SingleHighTrigger_ProducesPotentialTriggersDetected()
    {
        // A single High FODMAP trigger (e.g. garlic → fructan) must never be classified as
        // "no known triggers" — score magnitude doesn't change the status.
        var result = _sut.Assess(MakeProduct("Onion Rings", "onion, flour, oil"));
        result.IngredientScreeningScore.Should().BeLessThan(60);
        result.Status.Should().Be(nameof(FodmapAssessmentStatus.PotentialTriggersDetected));
        result.Triggers.Should().Contain(t => t.Severity == "High");
    }

    // ─── Regression probes: dedup, boundaries, negation, mitigations ────

    [Fact]
    public void DistinctFructans_AreIndividuallyListedAndCounted()
    {
        var result = _sut.Assess(MakeProduct("Dip Mix", "onion, garlic, salt"));
        result.Triggers.Should().Contain(t => t.Name == "Onion (Fructan)");
        result.Triggers.Should().Contain(t => t.Name == "Garlic (Fructan)");
        result.TriggerCount.Should().Be(2);
    }

    [Fact]
    public void SynonymPatterns_StillCollapseToOneTrigger()
    {
        // "wheat flour", "whole wheat" and the \bwheat\b regex share the canonical
        // Name "Wheat (Fructan)" — synonym collapsing must survive Name-level dedup.
        var result = _sut.Assess(MakeProduct("Bread", "wheat flour, whole wheat, wheat bran, water"));
        result.Triggers.Count(t => t.Name == "Wheat (Fructan)").Should().Be(1);
    }

    [Fact]
    public void BreadedChicken_DoesNotFalsePositiveOnBread()
    {
        var result = _sut.Assess(MakeProduct("Breaded Chicken", "breaded chicken breast, salt, pepper"));
        result.Triggers.Should().NotContain(t => t.Name.Contains("Bread"));
    }

    [Theory]
    [InlineData("shallots")]
    [InlineData("chickpeas")]
    public void PluralIngredientForms_StillMatch(string ingredient)
    {
        var result = _sut.Assess(MakeProduct(ingredients: $"water, {ingredient}, salt"));
        result.TriggerCount.Should().BeGreaterThan(0, $"'{ingredient}' must still be detected");
    }

    [Fact]
    public void NegatedLactoseFreeClaim_DoesNotSuppressLactoseTriggers()
    {
        var result = _sut.Assess(MakeProduct("Choc Drink", "not lactose-free chocolate drink, whole milk, sugar"));
        result.Triggers.Should().Contain(t => t.SubCategory == "Lactose");
    }

    [Fact]
    public void TinnedChickpeas_DowngradeFromHighToModerate()
    {
        var result = _sut.Assess(MakeProduct("Tinned Chickpeas", "chickpeas, water, salt"));
        var trigger = result.Triggers.FirstOrDefault(t => t.Name.Contains("Chickpea"));
        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be("Moderate");
        trigger.Explanation.Should().Contain("Canned");
    }

    [Fact]
    public void CannedKidneyBeans_DowngradeFromHighToModerate()
    {
        var result = _sut.Assess(MakeProduct("Salad Topping", "canned kidney beans, water, salt"));
        var trigger = result.Triggers.FirstOrDefault(t => t.Name.Contains("Kidney Bean"));
        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be("Moderate");
    }

    [Fact]
    public void FirmTofu_WithIndependentSoyFlour_KeepsSoybeanEvidence()
    {
        // Soy flour is its own GOS source — the firm-tofu exception must not swallow it.
        var result = _sut.Assess(MakeProduct("Firm Tofu Bowl", "firm tofu, soybean flour, water"));
        result.Triggers.Should().Contain(t => t.Name == "Soybean (GOS)");
    }

    [Fact]
    public void ExcessFructoseHeuristic_IsSuppressedWhenNamedSourcePresent()
    {
        var result = _sut.Assess(MakeProduct("Fruit Punch", "apple juice, water", sugar: 31m));
        // Apple Juice (Excess Fructose) is already flagged; the sugar heuristic previously
        // added a second excess-fructose row and squared the penalty for one substance.
        result.Triggers.Count(t => t.SubCategory == "Excess Fructose").Should().Be(1);
    }

    [Theory]
    [InlineData(30, false)]   // boundary: strictly greater-than 30 fires
    [InlineData(30.01, true)]
    public void ExcessFructoseHeuristic_SugarThresholdBoundary(decimal sugar, bool shouldFlag)
    {
        var result = _sut.Assess(MakeProduct("Sweet Base", "water, fructose syrup", sugar: sugar));
        result.Triggers.Any(t => t.SubCategory == "Excess Fructose").Should().Be(shouldFlag);
    }

    [Fact]
    public void Pistachio_ProducesBothChemistryRows()
    {
        var result = _sut.Assess(MakeProduct("Nut Mix", "pistachios, almonds, salt"));
        result.Triggers.Should().Contain(t => t.Name == "Pistachio (Fructan)");
        result.Triggers.Should().Contain(t => t.Name == "Pistachio (GOS)");
    }

    [Fact]
    public void SorbitolIngredient_And_E420Tag_CollapseToSingleRow()
    {
        var result = _sut.Assess(MakeProduct(additiveTags: ["en:e420"], ingredients: "water, sorbitol"));
        result.Triggers.Count(t => t.Name.StartsWith("Sorbitol", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void CherrySingularAndPlural_ProduceOneRow()
    {
        var result = _sut.Assess(MakeProduct("Cherry Pie", "cherries, sugar, pastry"));
        result.Triggers.Count(t => t.Name == "Cherry (Fructose + Sorbitol)").Should().Be(1);
    }

    // ─── Free-text confidence grading (W15) ─────────────────────────────

    [Theory]
    [InlineData("pizza", "Low")]
    [InlineData("rice bowl", "Low")]
    [InlineData("grilled chicken with rice", "Medium")]
    [InlineData("big bowl of pasta with tomato sauce and basil", "Medium")]
    public void AssessText_ConfidenceGradesByDescriptionSpecificity(string description, string expectedConfidence)
    {
        var result = _sut.AssessText(description);
        result.Confidence.Should().Be(expectedConfidence);
    }

    [Fact]
    public void AssessText_VagueDescription_StillRunsTheScreen()
    {
        // Lowered confidence must not be confused with "nothing to screen" — a one-word
        // description still runs; it just cannot claim Medium evidence quality.
        var result = _sut.AssessText("pizza");
        result.Status.Should().NotBe(nameof(FodmapAssessmentStatus.InsufficientInformation));
    }

    // ─── Whole-food plural tolerance (hardening #2) ─────────────────────

    [Theory]
    [InlineData("Roasted Pistachios", "Pistachio")]
    [InlineData("Portobellos with herbs", "Portobello")]
    [InlineData("Barley grains", "Barley")]
    public void PluralProductNames_MatchSingularWholeFoodEntries(string productName, string expectedNamePart)
    {
        var result = _sut.Assess(MakeProduct(productName));
        result.Triggers.Should().Contain(t => t.Name.Contains(expectedNamePart, StringComparison.OrdinalIgnoreCase),
            $"'{productName}' must match the singular-authored whole-food entry");
    }

    [Fact]
    public void Pepitas_StillDoNotMatchPita_AfterPluralTolerance()
    {
        var result = _sut.Assess(MakeProduct("Seed Mix", "pepitas, sunflower seeds"));
        result.Triggers.Should().NotContain(t => t.Name.Contains("Pita"));
    }
    // ─── Chemistry-family breadth parsing (hardening #5) ────────────────

    [Fact]
    public void DualClassFood_CountsBothChemistries_TowardStacking()
    {
        // garlic Fructan(High ×0.40) + cream Lactose(Moderate ×0.85)
        // + apple {Excess Fructose + Sorbitol} = two more families.
        // Families: Fructan, Lactose, Excess Fructose, Polyol = 4 → ×0.92^(4-2)
        // 100 × 0.40 × 0.85 × 0.40 × 0.8464 = 11.51 → 12
        // (Pre-fix first-token parsing counted only 3 families → 13.)
        var result = _sut.Assess(MakeProduct(ingredients: "garlic, cream, apple"));
        result.IngredientScreeningScore.Should().Be(12);
    }

    [Fact]
    public void DualClassFood_Alone_NoStackingPenalty()
    {
        // A lone dual-class food has breadth 2 (< 3) — no penalty either way.
        var result = _sut.Assess(MakeProduct(ingredients: "apple"));
        result.IngredientScreeningScore.Should().Be(40);
    }
}
