using System.IO;
using System.Text.Json;
using Xunit;
using Azure.AI.ContentUnderstanding;
using GutAI.Infrastructure.Services;
using GutAI.Application.Common.DTOs;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Configuration;

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

            var documentContent = ModelReaderWriter.Read<DocumentContent>(BinaryData.FromString(jsonContent))!;

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
            Assert.Equal("g", result.ServingSizeUnit);
            Assert.Equal(0.901m, result.ExtractionConfidence);
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

            var documentContent = ModelReaderWriter.Read<DocumentContent>(BinaryData.FromString(jsonToRead))!;

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
            Assert.Equal("g", result.ServingSizeUnit);
            Assert.Equal(0.901m, result.ExtractionConfidence);
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

            var documentContent = ModelReaderWriter.Read<DocumentContent>(BinaryData.FromString(jsonToRead))!;

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

        [Fact]
        public void HasMeaningfulExtraction_ShouldAcceptPartialLabelData()
        {
            var dto = new CustomFoodDto { Name = "Test Product" };

            Assert.True(ContentUnderstandingService.HasMeaningfulExtraction(dto));
        }

        [Theory]
        [InlineData(nameof(CustomFoodDto.BrandName))]
        [InlineData(nameof(CustomFoodDto.ServingSize))]
        [InlineData(nameof(CustomFoodDto.Ingredients))]
        [InlineData(nameof(CustomFoodDto.Barcode))]
        [InlineData(nameof(CustomFoodDto.Calories))]
        [InlineData(nameof(CustomFoodDto.ProteinG))]
        [InlineData(nameof(CustomFoodDto.CarbG))]
        [InlineData(nameof(CustomFoodDto.FatG))]
        [InlineData(nameof(CustomFoodDto.FiberG))]
        [InlineData(nameof(CustomFoodDto.SugarG))]
        [InlineData(nameof(CustomFoodDto.SodiumMg))]
        [InlineData(nameof(CustomFoodDto.SaturatedFatG))]
        [InlineData(nameof(CustomFoodDto.TransFatG))]
        [InlineData(nameof(CustomFoodDto.CholesterolMg))]
        [InlineData(nameof(CustomFoodDto.PotassiumMg))]
        [InlineData(nameof(CustomFoodDto.CalciumMg))]
        [InlineData(nameof(CustomFoodDto.IronMg))]
        [InlineData(nameof(CustomFoodDto.MagnesiumMg))]
        [InlineData(nameof(CustomFoodDto.ZincMg))]
        [InlineData(nameof(CustomFoodDto.VitaminA_IU))]
        [InlineData(nameof(CustomFoodDto.VitaminC_Mg))]
        [InlineData(nameof(CustomFoodDto.VitaminD_Mcg))]
        [InlineData(nameof(CustomFoodDto.VitaminB12_Mcg))]
        [InlineData(nameof(CustomFoodDto.Omega3G))]
        [InlineData(nameof(CustomFoodDto.CaffeineMg))]
        public void HasMeaningfulExtraction_ShouldAcceptAnyMappedNonEmptyField(string fieldName)
        {
            var dto = new CustomFoodDto();

            switch (fieldName)
            {
                case nameof(CustomFoodDto.BrandName):
                    dto.BrandName = "Brand";
                    break;
                case nameof(CustomFoodDto.ServingSize):
                    dto.ServingSize = 1;
                    break;
                case nameof(CustomFoodDto.Ingredients):
                    dto.Ingredients = "Salt, Sugar";
                    break;
                case nameof(CustomFoodDto.Barcode):
                    dto.Barcode = "1234567890123";
                    break;
                case nameof(CustomFoodDto.Calories):
                    dto.Calories = 1;
                    break;
                case nameof(CustomFoodDto.ProteinG):
                    dto.ProteinG = 1;
                    break;
                case nameof(CustomFoodDto.CarbG):
                    dto.CarbG = 1;
                    break;
                case nameof(CustomFoodDto.FatG):
                    dto.FatG = 1;
                    break;
                case nameof(CustomFoodDto.FiberG):
                    dto.FiberG = 1;
                    break;
                case nameof(CustomFoodDto.SugarG):
                    dto.SugarG = 1;
                    break;
                case nameof(CustomFoodDto.SodiumMg):
                    dto.SodiumMg = 1;
                    break;
                case nameof(CustomFoodDto.SaturatedFatG):
                    dto.SaturatedFatG = 1;
                    break;
                case nameof(CustomFoodDto.TransFatG):
                    dto.TransFatG = 1;
                    break;
                case nameof(CustomFoodDto.CholesterolMg):
                    dto.CholesterolMg = 1;
                    break;
                case nameof(CustomFoodDto.PotassiumMg):
                    dto.PotassiumMg = 1;
                    break;
                case nameof(CustomFoodDto.CalciumMg):
                    dto.CalciumMg = 1;
                    break;
                case nameof(CustomFoodDto.IronMg):
                    dto.IronMg = 1;
                    break;
                case nameof(CustomFoodDto.MagnesiumMg):
                    dto.MagnesiumMg = 1;
                    break;
                case nameof(CustomFoodDto.ZincMg):
                    dto.ZincMg = 1;
                    break;
                case nameof(CustomFoodDto.VitaminA_IU):
                    dto.VitaminA_IU = 1;
                    break;
                case nameof(CustomFoodDto.VitaminC_Mg):
                    dto.VitaminC_Mg = 1;
                    break;
                case nameof(CustomFoodDto.VitaminD_Mcg):
                    dto.VitaminD_Mcg = 1;
                    break;
                case nameof(CustomFoodDto.VitaminB12_Mcg):
                    dto.VitaminB12_Mcg = 1;
                    break;
                case nameof(CustomFoodDto.Omega3G):
                    dto.Omega3G = 1;
                    break;
                case nameof(CustomFoodDto.CaffeineMg):
                    dto.CaffeineMg = 1;
                    break;
            }

            Assert.True(ContentUnderstandingService.HasMeaningfulExtraction(dto));
        }

        [Theory]
        [InlineData(nameof(CustomFoodDto.SugarG))]
        [InlineData(nameof(CustomFoodDto.SodiumMg))]
        [InlineData(nameof(CustomFoodDto.VitaminA_IU))]
        [InlineData(nameof(CustomFoodDto.VitaminD_Mcg))]
        [InlineData(nameof(CustomFoodDto.VitaminB12_Mcg))]
        [InlineData(nameof(CustomFoodDto.ExtractionConfidence))]
        public void HasMeaningfulExtraction_ShouldAcceptZeroValueNullableFieldWhenPresent(string fieldName)
        {
            var dto = new CustomFoodDto();

            switch (fieldName)
            {
                case nameof(CustomFoodDto.SugarG):
                    dto.SugarG = 0m;
                    break;
                case nameof(CustomFoodDto.SodiumMg):
                    dto.SodiumMg = 0m;
                    break;
                case nameof(CustomFoodDto.VitaminA_IU):
                    dto.VitaminA_IU = 0m;
                    break;
                case nameof(CustomFoodDto.VitaminD_Mcg):
                    dto.VitaminD_Mcg = 0m;
                    break;
                case nameof(CustomFoodDto.VitaminB12_Mcg):
                    dto.VitaminB12_Mcg = 0m;
                    break;
                case nameof(CustomFoodDto.ExtractionConfidence):
                    dto.ExtractionConfidence = 0m;
                    break;
            }

            Assert.True(ContentUnderstandingService.HasMeaningfulExtraction(dto));
        }

        [Fact]
        public void HasMeaningfulExtraction_ShouldAcceptServingSizeUnit_WhenServingSizePresent()
        {
            var dto = new CustomFoodDto
            {
                ServingSize = 1,
                ServingSizeUnit = "oz"
            };

            Assert.True(ContentUnderstandingService.HasMeaningfulExtraction(dto));
        }

        [Fact]
        public void HasMeaningfulExtraction_ShouldAcceptZeroOnlyPayload()
        {
            var dto = new CustomFoodDto
            {
                Calories = 0m,
                ProteinG = 0m,
                CarbG = 0m,
                FatG = 0m,
                SugarG = 0m,
                SodiumMg = 0m,
                VitaminD_Mcg = 0m
            };

            Assert.True(ContentUnderstandingService.HasMeaningfulExtraction(dto));
        }

        [Fact]
        public void HasMeaningfulExtraction_ShouldAcceptMixedZeroAndPositivePayload()
        {
            var dto = new CustomFoodDto
            {
                Calories = 0m,
                SugarG = 0m,
                SodiumMg = 0m,
                ProteinG = 12m
            };

            Assert.True(ContentUnderstandingService.HasMeaningfulExtraction(dto));
        }

        [Fact]
        public void HasMeaningfulExtraction_ShouldRejectTrulyEmptyResult()
        {
            var dto = new CustomFoodDto();

            Assert.False(ContentUnderstandingService.HasMeaningfulExtraction(dto));
        }

        [Fact]
        public void ResolveVisionDeploymentName_NoDedicatedVisionKeyConfigured_FallsBackToTextDeployment()
        {
            // Regression: this used to fall back to a hardcoded "gpt-4o" default unrelated to
            // whatever text deployment was actually configured/working, so environments with
            // only AzureOpenAI:DeploymentName set (the common single-deployment setup) had a
            // 100%-failure-rate image-analysis fallback pointed at a deployment that doesn't
            // exist on their Azure OpenAI resource.
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureOpenAI:DeploymentName"] = "gpt-4o-mini",
                })
                .Build();

            Assert.Equal("gpt-4o-mini", ContentUnderstandingService.ResolveVisionDeploymentName(config));
        }

        [Fact]
        public void ResolveVisionDeploymentName_DedicatedVisionKeyConfigured_TakesPrecedenceOverTextDeployment()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureOpenAI:DeploymentName"] = "gpt-4o-mini",
                    ["AzureOpenAI:VisionDeploymentName"] = "gpt-4-vision-preview",
                })
                .Build();

            Assert.Equal("gpt-4-vision-preview", ContentUnderstandingService.ResolveVisionDeploymentName(config));
        }

        [Fact]
        public void ResolveVisionDeploymentName_NothingConfigured_FallsBackToSameHardcodedDefaultAsText()
        {
            var config = new ConfigurationBuilder().Build();

            Assert.Equal(
                ContentUnderstandingService.ResolveTextDeploymentName(config),
                ContentUnderstandingService.ResolveVisionDeploymentName(config));
        }
    }
}
