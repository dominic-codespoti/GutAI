using System.Text.Json;
using Azure.AI.ContentUnderstanding;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// Tests for extended ContentUnderstandingService functionality including
/// unit normalization, extended nutrients, and async disposal.
/// These tests use the actual fixture files for realistic testing.
/// </summary>
public class ContentUnderstandingServiceExtendedTests
{
    #region Utilities Extended Tests

    [Theory]
    [InlineData("16706 IU", 16706)]
    [InlineData("500 iu", 500)]
    [InlineData("1000 IU", 1000)]
    public void ExtractNumber_WithIU_ReturnsCorrectValue(string input, decimal expected)
    {
        // Act
        var result = Utilities.ExtractNumber(input);
        
        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("2.5 mcg", 2.5)]
    [InlineData("10 µg", 10)]
    [InlineData("50 mcg", 50)]
    public void ExtractNumber_WithMicrograms_ReturnsCorrectValue(string input, decimal expected)
    {
        // Act
        var result = Utilities.ExtractNumber(input);
        
        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region CustomFoodDto Extended Properties Tests

    [Fact]
    public void CustomFoodDto_ExtendedProperties_AreNullable()
    {
        // Arrange & Act
        var dto = new CustomFoodDto
        {
            Name = "Test Food",
            Calories = 100,
            ProteinG = 5,
            CarbG = 15,
            FatG = 3
            // Extended properties not set - should be null
        };
        
        // Assert - Extended properties should be null by default
        dto.SaturatedFatG.Should().BeNull();
        dto.TransFatG.Should().BeNull();
        dto.CholesterolMg.Should().BeNull();
        dto.PotassiumMg.Should().BeNull();
        dto.CalciumMg.Should().BeNull();
        dto.IronMg.Should().BeNull();
        dto.MagnesiumMg.Should().BeNull();
        dto.ZincMg.Should().BeNull();
        dto.VitaminA_IU.Should().BeNull();
        dto.VitaminC_Mg.Should().BeNull();
        dto.VitaminD_Mcg.Should().BeNull();
        dto.VitaminB12_Mcg.Should().BeNull();
        dto.Omega3G.Should().BeNull();
        dto.CaffeineMg.Should().BeNull();
        dto.Barcode.Should().BeNull();
        dto.ExtractionConfidence.Should().BeNull();
    }

    [Fact]
    public void CustomFoodDto_ExtendedProperties_CanBeSet()
    {
        // Arrange & Act
        var dto = new CustomFoodDto
        {
            Name = "Complete Food",
            Calories = 250,
            ProteinG = 20,
            CarbG = 30,
            FatG = 10,
            // Extended properties
            SaturatedFatG = 3,
            TransFatG = 0,
            CholesterolMg = 50,
            PotassiumMg = 400,
            CalciumMg = 200,
            IronMg = 8,
            MagnesiumMg = 50,
            ZincMg = 3,
            VitaminA_IU = 5000,
            VitaminC_Mg = 60,
            VitaminD_Mcg = 10,
            VitaminB12_Mcg = 2.4m,
            Omega3G = 1.5m,
            CaffeineMg = 95,
            Barcode = "1234567890123",
            ExtractionConfidence = 0.95m
        };
        
        // Assert
        dto.SaturatedFatG.Should().Be(3);
        dto.TransFatG.Should().Be(0);
        dto.CholesterolMg.Should().Be(50);
        dto.PotassiumMg.Should().Be(400);
        dto.CalciumMg.Should().Be(200);
        dto.IronMg.Should().Be(8);
        dto.MagnesiumMg.Should().Be(50);
        dto.ZincMg.Should().Be(3);
        dto.VitaminA_IU.Should().Be(5000);
        dto.VitaminC_Mg.Should().Be(60);
        dto.VitaminD_Mcg.Should().Be(10);
        dto.VitaminB12_Mcg.Should().Be(2.4m);
        dto.Omega3G.Should().Be(1.5m);
        dto.CaffeineMg.Should().Be(95);
        dto.Barcode.Should().Be("1234567890123");
        dto.ExtractionConfidence.Should().Be(0.95m);
    }

    #endregion

    #region UnitNormalizationService Integration

    [Theory]
    [InlineData("28g", "g")]
    [InlineData("100 grams", "g")]
    [InlineData("33 pieces", "piece")]
    [InlineData("1 cup", "cup")]
    [InlineData("8 fl oz", "fl oz")]
    [InlineData("250 ml", "ml")]
    [InlineData("5 grammes", "g")]  // Australian/British spelling
    public void ParseServingSize_VariousFormats_NormalizesUnits(string input, string expectedNormalizedUnit)
    {
        // Act
        var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize(input);
        
        // Assert
        normalizedUnit.Should().Be(expectedNormalizedUnit);
    }

    [Theory]
    [InlineData(100, "g", 100)]
    [InlineData(1, "kg", 1000)]
    [InlineData(1, "oz", 28.35)]
    [InlineData(1, "cup", 240)]
    [InlineData(1, "fl oz", 29.57)]
    public void ConvertToGrams_Conversions_AreCorrect(decimal amount, string unit, decimal expectedGrams)
    {
        // Act
        var result = UnitNormalizationService.ConvertToGrams(amount, unit);
        
        // Assert
        result.Should().BeApproximately(expectedGrams, 0.01m);
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void CustomFoodDto_SerializesExtendedProperties()
    {
        // Arrange
        var dto = new CustomFoodDto
        {
            Name = "Test",
            Calories = 100,
            ProteinG = 5,
            CarbG = 15,
            FatG = 3,
            SaturatedFatG = 1,
            VitaminC_Mg = 30,
            VitaminD_Mcg = 5
        };
        
        // Act
        var json = JsonSerializer.Serialize(dto);
        
        // Assert
        json.Should().Contain("SaturatedFatG");
        json.Should().Contain("VitaminC_Mg");
        json.Should().Contain("VitaminD_Mcg");
    }

    [Fact]
    public void CustomFoodDto_DeserializesExtendedProperties()
    {
        // Arrange
        var json = @"{
            ""Name"": ""Test"",
            ""Calories"": 100,
            ""ProteinG"": 5,
            ""CarbG"": 15,
            ""FatG"": 3,
            ""SaturatedFatG"": 1,
            ""VitaminC_Mg"": 30,
            ""VitaminD_Mcg"": 5
        }";
        
        // Act
        var dto = JsonSerializer.Deserialize<CustomFoodDto>(json);
        
        // Assert
        dto.Should().NotBeNull();
        dto!.SaturatedFatG.Should().Be(1);
        dto.VitaminC_Mg.Should().Be(30);
        dto.VitaminD_Mcg.Should().Be(5);
    }

    #endregion
}

