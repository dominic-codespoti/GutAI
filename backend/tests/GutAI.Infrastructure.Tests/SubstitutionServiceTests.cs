using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class SubstitutionServiceTests
{
    private readonly SubstitutionService _sut = new();

    static FoodProductDto MakeProduct(string name = "Test", string? ingredients = null,
        string[]? allergens = null, int? nova = null, decimal? sodium = null, decimal? sugar = null)
        => new()
        {
            Name = name,
            Ingredients = ingredients,
            AllergensTags = allergens ?? [],
            NovaGroup = nova,
            Sodium100g = sodium,
            Sugar100g = sugar,
        };

    // ─── Dairy Substitutions ────────────────────────────────────────────

    [Fact]
    public void Milk_SuggestsOatMilk()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "milk, sugar, cocoa"));
        result.Suggestions.Should().Contain(s => s.Original == "Milk" && s.Substitute.Contains("oat milk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cream_SuggestsCoconutCream()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "cream, sugar"));
        result.Suggestions.Should().Contain(s => s.Original == "Cream");
    }

    [Fact]
    public void Butter_SuggestsGhee()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "butter, flour, sugar"));
        result.Suggestions.Should().Contain(s => s.Original == "Butter" && s.Substitute.Contains("Ghee"));
    }

    [Fact]
    public void Cheese_SuggestsAgedCheese()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "cheese, crackers"));
        result.Suggestions.Should().Contain(s => s.Original == "Cheese");
    }

    [Fact]
    public void Whey_SuggestsPeaProtein()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "whey protein, cocoa, stevia"));
        result.Suggestions.Should().Contain(s => s.Original == "Whey protein" && s.Substitute.Contains("Pea protein"));
    }

    [Fact]
    public void Yogurt_SuggestsCoconutYogurt()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "yogurt, strawberry"));
        result.Suggestions.Should().Contain(s => s.Original == "Yogurt");
    }

    [Fact]
    public void CreamCheese_MatchesBeforeCream()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "cream cheese, bagel"));
        result.Suggestions.Should().Contain(s => s.Original == "Cream cheese");
    }

    // ─── Gluten/Wheat Substitutions ─────────────────────────────────────

    [Fact]
    public void WheatFlour_SuggestsRiceFlour()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "wheat flour, sugar, eggs"));
        result.Suggestions.Should().Contain(s => s.Original == "Wheat flour" && s.Substitute.Contains("Rice flour"));
    }

    [Fact]
    public void Gluten_SuggestsGlutenFree()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "gluten, water"));
        result.Suggestions.Should().Contain(s => s.Original == "Gluten");
    }

    [Fact]
    public void Barley_SuggestsBrownRice()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "barley, water"));
        result.Suggestions.Should().Contain(s => s.Original == "Barley");
    }

    [Fact]
    public void Rye_SuggestsSourdough()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "rye flour, water, yeast"));
        result.Suggestions.Should().Contain(s => s.Original == "Rye");
    }

    // ─── FODMAP Substitutions ───────────────────────────────────────────

    [Fact]
    public void Garlic_SuggestsInfusedOil()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "garlic, olive oil"));
        result.Suggestions.Should().Contain(s => s.Original == "Garlic" && s.Substitute.Contains("Garlic-infused oil"));
    }

    [Fact]
    public void Onion_SuggestsSpringOnion()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "onion, tomato, basil"));
        result.Suggestions.Should().Contain(s => s.Original == "Onion" && s.Substitute.Contains("spring onion"));
    }

    [Fact]
    public void Honey_SuggestsMapleSyrup()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "honey, oats"));
        result.Suggestions.Should().Contain(s => s.Original == "Honey" && s.Substitute.Contains("Maple syrup"));
    }

    [Fact]
    public void HFCS_SuggestsGlucoseSyrup()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "high fructose corn syrup, water"));
        result.Suggestions.Should().Contain(s => s.Original == "High fructose corn syrup");
    }

    [Fact]
    public void Inulin_SuggestsPsyllium()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "inulin, protein isolate"));
        result.Suggestions.Should().Contain(s => s.Original == "Inulin" && s.Substitute.Contains("Psyllium"));
    }

    [Fact]
    public void Agave_SuggestsMapleSyrup()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "agave nectar, water"));
        result.Suggestions.Should().Contain(s => s.Original == "Agave syrup" && s.Substitute.Contains("Maple syrup"));
    }

    // ─── Polyol Substitutions ───────────────────────────────────────────

    [Fact]
    public void Sorbitol_SuggestsErythritol()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "sorbitol, gum base"));
        result.Suggestions.Should().Contain(s => s.Original.Contains("Sorbitol") && s.Substitute.Contains("Erythritol"));
    }

    [Fact]
    public void Maltitol_SuggestsErythritol()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "maltitol, cocoa"));
        result.Suggestions.Should().Contain(s => s.Original.Contains("Maltitol") && s.Substitute.Contains("Erythritol"));
    }

    [Fact]
    public void Xylitol_SuggestsErythritol()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "xylitol, mint"));
        result.Suggestions.Should().Contain(s => s.Original.Contains("Xylitol"));
    }

    // ─── Additive Substitutions ─────────────────────────────────────────

    [Fact]
    public void Carrageenan_SuggestsGellanGum()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "milk, carrageenan"));
        result.Suggestions.Should().Contain(s => s.Original.Contains("Carrageenan"));
    }

    [Fact]
    public void SodiumBenzoate_SuggestsNatural()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "water, sodium benzoate"));
        result.Suggestions.Should().Contain(s => s.Original.Contains("Sodium benzoate"));
    }

    // ─── Sweetener Substitutions ────────────────────────────────────────

    [Fact]
    public void Sucralose_SuggestsStevia()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "water, sucralose"));
        result.Suggestions.Should().Contain(s => s.Original == "Sucralose" && s.Substitute.Contains("Stevia"));
    }

    [Fact]
    public void Aspartame_SuggestsStevia()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "carbonated water, aspartame"));
        result.Suggestions.Should().Contain(s => s.Original == "Aspartame");
    }

    // ─── Oil Substitutions ──────────────────────────────────────────────

    [Fact]
    public void PalmOil_SuggestsOliveOil()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "palm oil, sugar, cocoa"));
        result.Suggestions.Should().Contain(s => s.Original == "Palm oil" && s.Substitute.Contains("Olive oil"));
    }

    [Fact]
    public void VegetableOil_SuggestsOliveOil()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "vegetable oil, flour"));
        result.Suggestions.Should().Contain(s => s.Original == "Vegetable oil");
    }

    // ─── NOVA Group & Nutritional Flags ─────────────────────────────────

    [Fact]
    public void Nova4_SuggestsHomemade()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Processed Snack", nova: 4));
        result.Suggestions.Should().Contain(s => s.Category == "Processing");
    }

    [Fact]
    public void Nova3_NoProcessingSuggestion()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Cheese", nova: 3));
        result.Suggestions.Should().NotContain(s => s.Category == "Processing");
    }

    [Fact]
    public void HighSodium_SuggestsLowSodium()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Salty Chips", sodium: 1.5m));
        result.Suggestions.Should().Contain(s => s.Category == "Sodium");
    }

    [Fact]
    public void NormalSodium_NoSodiumSuggestion()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Rice", sodium: 0.1m));
        result.Suggestions.Should().NotContain(s => s.Category == "Sodium");
    }

    [Fact]
    public void HighSugar_SuggestsLowSugar()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Candy", sugar: 55m));
        result.Suggestions.Should().Contain(s => s.Category == "Sugar");
    }

    [Fact]
    public void NormalSugar_NoSugarSuggestion()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Bread", sugar: 3m));
        result.Suggestions.Should().NotContain(s => s.Category == "Sugar");
    }

    // ─── Allergen Tag Substitutions ─────────────────────────────────────

    [Fact]
    public void GlutenAllergenTag_SuggestsGlutenFree()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Bread", allergens: ["en:gluten"]));
        result.Suggestions.Should().Contain(s => s.Category == "Gluten/Wheat");
    }

    [Fact]
    public void MilkAllergenTag_SuggestsDairyFree()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Yogurt", allergens: ["en:milk"]));
        result.Suggestions.Should().Contain(s => s.Category == "Dairy");
    }

    // ─── No Triggers ────────────────────────────────────────────────────

    [Fact]
    public void CleanProduct_NoSuggestions()
    {
        var result = _sut.GetSubstitutions(MakeProduct("White Rice", "rice"));
        result.SuggestionCount.Should().Be(0);
        result.Summary.Should().Contain("No common gut-related ingredient concerns identified");
    }

    [Fact]
    public void NullIngredients_NoSuggestions()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Unknown Product"));
        result.SuggestionCount.Should().Be(0);
    }

    // ─── Deduplication ──────────────────────────────────────────────────

    [Fact]
    public void DuplicateIngredients_NoDuplicateSuggestions()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "milk, milk powder, skim milk"));
        result.Suggestions.Count(s => s.Original == "Milk").Should().Be(1);
    }

    // ─── Complex Products ───────────────────────────────────────────────

    [Fact]
    public void NutellaLike_MultipleSuggestions()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Nutella",
            "sugar, palm oil, hazelnuts, cocoa, skim milk, whey, soy lecithin, vanillin",
            nova: 4, sugar: 56m));
        result.SuggestionCount.Should().BeGreaterThanOrEqualTo(3);
        result.Suggestions.Should().Contain(s => s.Original == "Palm oil");
        result.Suggestions.Should().Contain(s => s.Category == "Sugar");
        result.Suggestions.Should().Contain(s => s.Category == "Processing");
    }

    [Fact]
    public void SugarFreeGum_PolyolSuggestions()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Sugar Free Gum",
            "sorbitol, gum base, xylitol, mannitol, aspartame, acesulfame k"));
        result.Suggestions.Should().Contain(s => s.Original.Contains("Sorbitol"));
        result.Suggestions.Should().Contain(s => s.Original.Contains("Xylitol"));
        result.Suggestions.Should().Contain(s => s.Original == "Aspartame");
    }

    [Fact]
    public void GarlicBread_FodmapSuggestions()
    {
        var result = _sut.GetSubstitutions(MakeProduct("Garlic Bread",
            "wheat flour, butter, garlic, salt"));
        result.Suggestions.Should().Contain(s => s.Original == "Garlic");
        result.Suggestions.Should().Contain(s => s.Original == "Butter");
        result.Suggestions.Should().Contain(s => s.Original == "Wheat flour");
    }

    // ─── GetSubstitutionsForText ─────────────────────────────────────────

    [Fact]
    public void TextAnalysis_FindsTriggers()
    {
        var result = _sut.GetSubstitutionsForText("garlic bread with cream cheese");
        result.SuggestionCount.Should().BeGreaterThan(0);
        result.Suggestions.Should().Contain(s => s.Original == "Garlic");
    }

    [Fact]
    public void TextAnalysis_NoTriggers()
    {
        var result = _sut.GetSubstitutionsForText("grilled chicken with rice");
        result.SuggestionCount.Should().Be(0);
    }

    // ─── Confidence Levels ──────────────────────────────────────────────

    [Fact]
    public void HighConfidence_FodmapSubstitutions()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "garlic, onion"));
        result.Suggestions.Should().OnlyContain(s => s.Confidence == "High");
    }

    [Fact]
    public void MediumConfidence_SomeSubstitutions()
    {
        var result = _sut.GetSubstitutions(MakeProduct(ingredients: "sucralose, water"));
        result.Suggestions.Should().Contain(s => s.Confidence == "Medium");
    }

    // ─── Category Coverage ──────────────────────────────────────────────

    [Fact]
    public void AllCategories_Represented()
    {
        var result = _sut.GetSubstitutions(MakeProduct(
            "Everything Product",
            "milk, wheat flour, garlic, sorbitol, sucralose, carrageenan, palm oil, caffeine",
            nova: 4, sodium: 2m, sugar: 30m));

        var categories = result.Suggestions.Select(s => s.Category).Distinct().ToList();
        categories.Should().Contain("Dairy");
        categories.Should().Contain("Gluten/Wheat");
        categories.Should().Contain("FODMAP");
        categories.Should().Contain("Polyol");
        categories.Should().Contain("Sweetener");
        categories.Should().Contain("Additive");
        categories.Should().Contain("Fat");
        categories.Should().Contain("Processing");
        categories.Should().Contain("Sodium");
        categories.Should().Contain("Sugar");
    }
}
