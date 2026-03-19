using System.IO;
using System.Text.Json;
using Xunit;
using Azure.AI.ContentUnderstanding;
using GutAI.Infrastructure.Services;
using GutAI.Application.Common.DTOs;
using System.ClientModel.Primitives;

namespace GutAI.Infrastructure.Tests
{
    public class ContentUnderstandingServiceMappingTests
    {
        [Fact]
        public void MapDocumentContentToDto_ShouldMapRealEjectedPayload()
        {
            // Arrange
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "food_label_result.json");
            var jsonContent = File.ReadAllText(filePath);

            var documentContent = ModelReaderWriter.Read<DocumentContent>(BinaryData.FromString(jsonContent));

            // Act
            var result = ContentUnderstandingService.MapDocumentContentToDto(documentContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("CHESTER'S® brand Flamin Hot flavored Corn Snacks", result.Name);
            Assert.Contains("ENRICHED CORN MEAL", result.Ingredients);
            Assert.Equal(150m, result.Calories);
            Assert.Equal(8m, result.FatG);
            Assert.Equal(2m, result.ProteinG);
            Assert.Equal(18m, result.CarbG);
            Assert.Equal(0m, result.SugarG);
        }

        [Fact]
        public void MapDocumentContentToDto_ShouldMapRealEjectedPayloadUS()
        {
            // Arrange
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "food_label_result_us.json");
            var jsonContent = File.ReadAllText(filePath);

            // Extract the first 'contents' item where fields actually reside
            var jsonDoc = JsonDocument.Parse(jsonContent);
            var contentObj = jsonDoc.RootElement.GetProperty("result").GetProperty("contents")[0];
            var jsonToRead = contentObj.GetRawText();

            var documentContent = ModelReaderWriter.Read<DocumentContent>(BinaryData.FromString(jsonToRead));

            // Act
            var result = ContentUnderstandingService.MapDocumentContentToDto(documentContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("CHESTER'S® brand Flamin Hot flavored Corn Snacks", result.Name);
            Assert.Contains("ENRICHED CORN MEAL", result.Ingredients);
            Assert.Equal(150m, result.Calories);
            Assert.Equal(8m, result.FatG);
            Assert.Equal(2m, result.ProteinG);
            Assert.Equal(18m, result.CarbG);
            Assert.Equal(0m, result.SugarG);
        }

        [Fact]
        public void MapDocumentContentToDto_ShouldMapRealEjectedPayloadAussie_NutriGrain()
        {
            // Arrange
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "food_label_result_aussie_nutri.json");
            var jsonContent = File.ReadAllText(filePath);

            var jsonDoc = JsonDocument.Parse(jsonContent);
            var contentObj = jsonDoc.RootElement.GetProperty("result").GetProperty("contents")[0];
            var jsonToRead = contentObj.GetRawText();

            var documentContent = ModelReaderWriter.Read<DocumentContent>(BinaryData.FromString(jsonToRead));

            // Act
            var result = ContentUnderstandingService.MapDocumentContentToDto(documentContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Nutri-Grain Protein Packed Choc Balls", result.Name);
            Assert.Contains("Cereals (44%)", result.Ingredients);
            Assert.Equal(12.7m, result.ProteinG);
            Assert.Equal(1.6m, result.FatG);
            Assert.Equal(26.2m, result.CarbG);
            Assert.Equal(9.4m, result.SugarG);
            Assert.Equal(176.9m, result.Calories);
        }
    }
}
