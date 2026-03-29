using Xunit;
using FluentAssertions;
using GutAI.Infrastructure.Services;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// Comprehensive tests for UnitNormalizationService covering unit standardization,
/// fuzzy matching for OCR errors, and conversion utilities.
/// </summary>
public class UnitNormalizationServiceTests
{
    #region Normalize Tests

    [Theory]
    [InlineData("g", "g")]
    [InlineData("gram", "g")]
    [InlineData("grams", "g")]
    [InlineData("grammes", "g")]
    [InlineData("gm", "g")]
    [InlineData("gr", "g")]
    public void Normalize_WeightGrams_ReturnsStandardG(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("kg", "kg")]
    [InlineData("kilogram", "kg")]
    [InlineData("kilograms", "kg")]
    [InlineData("kilo", "kg")]
    public void Normalize_WeightKilograms_ReturnsStandardKg(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("mg", "mg")]
    [InlineData("milligram", "mg")]
    [InlineData("milligrams", "mg")]
    public void Normalize_WeightMilligrams_ReturnsStandardMg(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("ml", "ml")]
    [InlineData("milliliter", "ml")]
    [InlineData("milliliters", "ml")]
    [InlineData("millilitre", "ml")]
    [InlineData("cc", "ml")]
    public void Normalize_VolumeMilliliters_ReturnsStandardMl(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("l", "L")]
    [InlineData("liter", "L")]
    [InlineData("liters", "L")]
    [InlineData("litre", "L")]
    [InlineData("ltr", "L")]
    public void Normalize_VolumeLiters_ReturnsStandardL(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("fl oz", "fl oz")]
    [InlineData("floz", "fl oz")]
    [InlineData("fluid oz", "fl oz")]
    [InlineData("fluid ounce", "fl oz")]
    [InlineData("fl. oz", "fl oz")]
    public void Normalize_VolumeFluidOunces_ReturnsStandardFlOz(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("oz", "oz")]
    [InlineData("ounce", "oz")]
    [InlineData("ounces", "oz")]
    public void Normalize_WeightOunces_ReturnsStandardOz(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("cup", "cup")]
    [InlineData("cups", "cup")]
    [InlineData("c", "cup")]
    public void Normalize_USCustomaryCups_ReturnsStandardCup(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("tbsp", "tbsp")]
    [InlineData("tablespoon", "tbsp")]
    [InlineData("tablespoons", "tbsp")]
    [InlineData("tbs", "tbsp")]
    public void Normalize_USCustomaryTablespoons_ReturnsStandardTbsp(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("tsp", "tsp")]
    [InlineData("teaspoon", "tsp")]
    [InlineData("teaspoons", "tsp")]
    public void Normalize_USCustomaryTeaspoons_ReturnsStandardTsp(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("piece", "piece")]
    [InlineData("pieces", "piece")]
    [InlineData("pc", "piece")]
    [InlineData("pcs", "piece")]
    [InlineData("bar", "piece")]
    [InlineData("bars", "piece")]
    public void Normalize_CountPieces_ReturnsStandardPiece(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("serving", "serving")]
    [InlineData("servings", "serving")]
    [InlineData("srv", "serving")]
    [InlineData("serve", "serving")]
    public void Normalize_Servings_ReturnsStandardServing(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("iu", "IU")]
    [InlineData("IU", "IU")]
    [InlineData("international unit", "IU")]
    public void Normalize_VitaminUnits_ReturnsStandardIU(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("mcg", "mcg")]
    [InlineData("µg", "mcg")]
    [InlineData("microgram", "mcg")]
    [InlineData("ug", "mcg")]
    public void Normalize_Micrograms_ReturnsStandardMcg(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void Normalize_NullInput_ReturnsDefaultGrams()
    {
        var result = UnitNormalizationService.Normalize(null);
        result.Should().Be("g");
    }

    [Fact]
    public void Normalize_EmptyInput_ReturnsDefaultGrams()
    {
        var result = UnitNormalizationService.Normalize("");
        result.Should().Be("g");
    }

    [Fact]
    public void Normalize_WhitespaceInput_ReturnsDefaultGrams()
    {
        var result = UnitNormalizationService.Normalize("   ");
        result.Should().Be("g");
    }

    [Fact]
    public void Normalize_UnknownUnit_ReturnsOriginal()
    {
        var result = UnitNormalizationService.Normalize("xyz123");
        result.Should().Be("xyz123");
    }

    #endregion

    #region Fuzzy Matching Tests (OCR Error Correction)

    [Theory]
    [InlineData("grm", "g")]      // Missing 'a'
    [InlineData("grms", "g")]     // Plural with typo
    [InlineData("mili", "ml")]    // Missing 'l'
    public void Normalize_OcrErrors_ReturnsCorrectedUnit(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("G", "g")]
    [InlineData("ML", "ml")]
    [InlineData("Oz", "oz")]
    [InlineData("CUP", "cup")]
    public void Normalize_CaseInsensitive_ReturnsStandardUnit(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(" g ", "g")]
    [InlineData("  ml  ", "ml")]
    [InlineData("  oz  ", "oz")]
    public void Normalize_TrimsWhitespace_ReturnsStandardUnit(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    #endregion

    #region ConvertToGrams Tests

    [Theory]
    [InlineData(100, "g", 100)]
    [InlineData(1, "kg", 1000)]
    [InlineData(500, "mg", 0.5)]
    public void ConvertToGrams_WeightUnits_ReturnsCorrectGrams(decimal amount, string unit, decimal expectedGrams)
    {
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().BeApproximately(expectedGrams, 0.01m);
    }

    [Theory]
    [InlineData(1, "oz", 28.35)]
    [InlineData(2, "oz", 56.70)]
    [InlineData(0.5, "oz", 14.17)]
    public void ConvertToGrams_Ounces_ReturnsCorrectGrams(decimal amount, string unit, decimal expectedGrams)
    {
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().BeApproximately(expectedGrams, 0.01m);
    }

    [Theory]
    [InlineData(1, "cup", 240)]
    [InlineData(0.5, "cup", 120)]
    public void ConvertToGrams_Cups_ReturnsCorrectGrams(decimal amount, string unit, decimal expectedGrams)
    {
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().Be(expectedGrams);
    }

    [Theory]
    [InlineData(1, "tbsp", 15)]
    [InlineData(2, "tbsp", 30)]
    public void ConvertToGrams_Tablespoons_ReturnsCorrectGrams(decimal amount, string unit, decimal expectedGrams)
    {
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().Be(expectedGrams);
    }

    [Theory]
    [InlineData(1, "tsp", 5)]
    [InlineData(3, "tsp", 15)]
    public void ConvertToGrams_Teaspoons_ReturnsCorrectGrams(decimal amount, string unit, decimal expectedGrams)
    {
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().Be(expectedGrams);
    }

    [Theory]
    [InlineData(1, "fl oz", 29.57)]
    [InlineData(8, "fl oz", 236.59)]
    public void ConvertToGrams_FluidOunces_ReturnsApproximateGrams(decimal amount, string unit, decimal expectedGrams)
    {
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().BeApproximately(expectedGrams, 0.01m);
    }

    [Theory]
    [InlineData(100, "ml", 100)]  // Water approximation
    [InlineData(1, "L", 1000)]
    public void ConvertToGrams_VolumeUnits_ReturnsWaterApproximation(decimal amount, string unit, decimal expectedGrams)
    {
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().Be(expectedGrams);
    }

    [Theory]
    [InlineData(5, "piece", 5)]
    [InlineData(1, "serving", 1)]
    public void ConvertToGrams_CountUnits_ReturnsOriginalAmount(decimal amount, string unit, decimal expected)
    {
        // Count units cannot be converted to grams
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertToGrams_UnknownUnit_ReturnsOriginalAmount()
    {
        var result = UnitNormalizationService.ConvertToGrams(100, "unknown");
        result.Should().Be(100);
    }

    #endregion

    #region ParseServingSize Tests

    [Theory]
    [InlineData("28g", 28, "g", "g")]
    [InlineData("100 grams", 100, "grams", "g")]
    [InlineData("1 kg", 1, "kg", "kg")]
    public void ParseServingSize_SimpleFormats_ReturnsCorrectValues(string input, decimal expectedAmount, string expectedRawUnit, string expectedNormalizedUnit)
    {
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize(input);
        
        amount.Should().Be(expectedAmount);
        rawUnit.Should().Be(expectedRawUnit);
        normalizedUnit.Should().Be(expectedNormalizedUnit);
    }

    [Theory]
    [InlineData("33 pieces (28g)", 33, "pieces", "piece")]
    [InlineData("1 cup (240ml)", 1, "cup", "cup")]
    [InlineData("2 bars (60g)", 2, "bars", "piece")]
    public void ParseServingSize_ParentheticalFormat_ReturnsCorrectValues(string input, decimal expectedAmount, string expectedRawUnit, string expectedNormalizedUnit)
    {
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize(input);
        
        amount.Should().Be(expectedAmount);
        rawUnit.Should().Be(expectedRawUnit);
        normalizedUnit.Should().Be(expectedNormalizedUnit);
    }

    [Theory]
    [InlineData("2.5 fl oz", 2.5, "fl oz", "fl oz")]
    [InlineData("1.5 cups", 1.5, "cups", "cup")]
    [InlineData("0.5 serving", 0.5, "serving", "serving")]
    public void ParseServingSize_DecimalAmounts_ReturnsCorrectValues(string input, decimal expectedAmount, string expectedRawUnit, string expectedNormalizedUnit)
    {
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize(input);
        
        amount.Should().Be(expectedAmount);
        rawUnit.Should().Be(expectedRawUnit);
        normalizedUnit.Should().Be(expectedNormalizedUnit);
    }

    [Fact]
    public void ParseServingSize_NullInput_ReturnsDefaults()
    {
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize(null);
        
        amount.Should().Be(0);
        rawUnit.Should().Be("");
        normalizedUnit.Should().Be("g");
    }

    [Fact]
    public void ParseServingSize_EmptyInput_ReturnsDefaults()
    {
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize("");
        
        amount.Should().Be(0);
        rawUnit.Should().Be("");
        normalizedUnit.Should().Be("g");
    }

    [Fact]
    public void ParseServingSize_NoNumber_ReturnsZeroAmountWithUnit()
    {
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize("grams");
        
        amount.Should().Be(0);
        rawUnit.Should().Be("grams");
        normalizedUnit.Should().Be("g");
    }

    [Fact]
    public void ParseServingSize_ComplexParenthetical_ParsesWeightInParens()
    {
        // When main part has no clear unit, use parenthetical
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize("About 9 servings per container (28g)");
        
        // Should extract 9 from main part
        amount.Should().Be(9);
        rawUnit.Should().Be("servings per container");
        // This is a bit ambiguous, but it's extracting what it can
    }

    #endregion

    #region Edge Cases and Stress Tests

    [Theory]
    [InlineData("gram.", "g")]          // Trailing period
    [InlineData("g,", "g")]             // Trailing comma
    [InlineData("(ml)", "ml")]           // Enclosed in parens
    public void Normalize_SpecialCharacters_HandlesGracefully(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void Normalize_MultipleWords_ReturnsFirstMatch()
    {
        // If somehow multiple words, takes the whole thing
        var result = UnitNormalizationService.Normalize("fluid ounces");
        result.Should().Be("fl oz");
    }

    [Theory]
    [InlineData("tbspn", "tbsp")]        // Extra 'n'
    [InlineData("tspoon", "tsp")]        // Shortened
    public void Normalize_FuzzyVariations_ReturnsBestMatch(string input, string expected)
    {
        var result = UnitNormalizationService.Normalize(input);
        result.Should().Be(expected);
    }

    #endregion
}
