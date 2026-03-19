using System;
using System.Text.Json;
using Xunit;
using Azure.AI.ContentUnderstanding;
using GutAI.Infrastructure.Services;
using GutAI.Application.Common.DTOs;
using System.ClientModel.Primitives;

namespace GutAI.Infrastructure.Tests
{
    public class ExtractNutrientTheoryTests
    {
        private DocumentContent BuildDoc(string jsonContent)
        {
            return ModelReaderWriter.Read<DocumentContent>(BinaryData.FromString(jsonContent));
        }

        [Theory]
        [InlineData("ProteinAmount", "15.5", 15.5)]
        [InlineData("total_protein", "30", 30)]
        [InlineData("PROTEIN", "12", 12)]
        [InlineData("Protein_Per_Serve", "22", 22)]
        public void ExtractProtein_VariousNamings_ReturnsCorrectValue(string fieldKey, string fieldValue, decimal expected)
        {
            var json = $$"""
            {
                "fields": {
                    "{{fieldKey}}": { "type": "number", "valueNumber": {{fieldValue}} }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            Assert.Equal(expected, result.ProteinG);
        }

        [Theory]
        [InlineData("Energy(kcal)", "150", 150)]
        [InlineData("Calories", "200", 200)]
        [InlineData("energy_per_serving", "250", 250)]
        [InlineData("CALORIES", "300", 300)]
        public void ExtractCalories_VariousNamings_ReturnsCorrectValue(string fieldKey, string fieldValue, decimal expected)
        {
            var json = $$"""
            {
                "fields": {
                    "{{fieldKey}}": { "type": "number", "valueNumber": {{fieldValue}} }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            Assert.Equal(expected, result.Calories);
        }

        [Theory]
        [InlineData("FatAmount", "10", 10)]
        [InlineData("TotalFat", "15", 15)]
        [InlineData("fat_g", "5.5", 5.5)]
        public void ExtractFat_VariousNamings_ReturnsCorrectValue(string fieldKey, string fieldValue, decimal expected)
        {
            var json = $$"""
            {
                "fields": {
                    "{{fieldKey}}": { "type": "number", "valueNumber": {{fieldValue}} }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            Assert.Equal(expected, result.FatG);
        }

        [Theory]
        [InlineData("Carbohydrates", "40", 40)]
        [InlineData("TotalCarb", "45", 45)]
        [InlineData("carb_amount", "50", 50)]
        public void ExtractCarb_VariousNamings_ReturnsCorrectValue(string fieldKey, string fieldValue, decimal expected)
        {
            var json = $$"""
            {
                "fields": {
                    "{{fieldKey}}": { "type": "number", "valueNumber": {{fieldValue}} }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            Assert.Equal(expected, result.CarbG);
        }

        [Fact]
        public void ExtractNutrient_PrioritizesServeOver100g()
        {
            var json = """
            {
                "fields": {
                    "ProteinPer100g": { "type": "number", "valueNumber": 50 },
                    "ProteinPerServe": { "type": "number", "valueNumber": 12.5 }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            
            // Should pick 12.5 because it contains "serve", ignoring the 50 which has "100g" or lacks "serve"
            Assert.Equal(12.5m, result.ProteinG);
        }

        [Fact]
        public void ExtractNutrient_Ignores100gIfOtherOptionExists()
        {
            var json = """
            {
                "fields": {
                    "FatPer100g": { "type": "number", "valueNumber": 100 },
                    "FatAmount": { "type": "number", "valueNumber": 5 }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            
            // Should pick 5 because it filters out "100g" when falling back
            Assert.Equal(5m, result.FatG);
        }

        [Fact]
        public void ExtractNutrient_Uses100gIfNoOtherOptionExists()
        {
            var json = """
            {
                "fields": {
                    "SodiumPer100g": { "type": "number", "valueNumber": 300 }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            
            // Should fall back to taking the only valid match
            Assert.Equal(300m, result.SodiumMg);
        }

        [Fact]
        public void ExtractNutrient_HandlesNestedProperties()
        {
            var json = """
            {
                "fields": {
                    "NutritionInformation": {
                        "type": "object",
                        "valueObject": {
                            "Protein": {
                                "type": "object",
                                "valueObject": {
                                    "AmountPerServing": { "type": "string", "valueString": "15g" }
                                }
                            },
                            "Fibre": {
                                "type": "string",
                                "valueString": "3g"
                            }
                        }
                    }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            
            Assert.Equal(15m, result.ProteinG);
            Assert.Equal(3m, result.FiberG);
        }

        [Fact]
        public void ExtractNutrient_ExtractsValueStringWithUnits()
        {
            var json = """
            {
                "fields": {
                    "Calories": { "type": "string", "valueString": "250 kcal" },
                    "Protein": { "type": "string", "valueString": "10 g" }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            
            Assert.Equal(250m, result.Calories);
            Assert.Equal(10m, result.ProteinG);
        }
        
        [Fact]
        public void ExtractNutrient_ExtractskJAsCalories()
        {
            var json = """
            {
                "fields": {
                    "Energy": { "type": "string", "valueString": "1000 kJ" }
                }
            }
            """;
            var docContent = BuildDoc(json);
            var result = ContentUnderstandingService.MapDocumentContentToDto(docContent);
            
            // 1000 kJ / 4.184 = 239.0 rounded
            Assert.Equal(239.0m, result.Calories);
        }
    }
}
