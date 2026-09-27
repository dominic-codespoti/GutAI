using System.Text.Json;
using Azure;
using Azure.AI.ContentUnderstanding;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class ContentUnderstandingDescribeTests
{
    [Fact]
    public async Task DescribeGroundsCatalogComponentsAndUsesSanityCheckedFallbackForUnmatchedOnes()
    {
        var product = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Plain yogurt",
            DataSource = "USDA",
            MatchConfidence = 0.95m,
            Calories100g = 200m,
            Protein100g = 10m,
            Carbs100g = 20m,
            Fat100g = 8m,
            Fiber100g = 0m,
            Sugar100g = 0m,
            SodiumMg100g = 0m
        };
        var search = new Mock<IFoodSearchService>();
        search.Setup(s => s.ResolveAsync("plain yogurt", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto
            {
                OriginalQuery = "plain yogurt",
                Status = FoodResolutionStatus.Exact,
                Selected = product,
                MatchConfidence = 0.95m
            });
        search.Setup(s => s.ResolveAsync("blueberry", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto { OriginalQuery = "blueberry", Status = FoodResolutionStatus.Unresolved });

        var service = CreateService(search.Object);
        var result = await service.FinalizeDescribedDishAsync(new DescribedDish
        {
            Name = "Yogurt with blueberries",
            Serving = new() { Unit = "bowl", Grams = 100m },
            Confidence = 0.8m,
            Components =
            [
                new() { Name = "yogurt", Grams = 50m, SearchQueries = ["plain yogurt"], FallbackPer100G = new() { Kcal = 500m } },
                new() { Name = "blueberries", Grams = 50m, SearchQueries = ["blueberry"], FallbackPer100G = new() { Kcal = 100m, ProteinG = 5m, CarbsG = 10m, FatG = 4m } }
            ]
        }, "fallback", CancellationToken.None);

        Assert.NotNull(result);
        // Each half-serving scales by 50/100: yogurt contributes (100 kcal, 5 P, 10 C, 4 F);
        // berries contribute (50 kcal, 2.5 P, 5 C, 2 F), for the totals below.
        Assert.Equal(150m, result.Calories);
        Assert.Equal(7.5m, result.ProteinG);
        Assert.Equal(15m, result.CarbG);
        Assert.Equal(6m, result.FatG);
        Assert.Equal("ModelEstimated", result.NutritionProvenance);
        Assert.Equal(0.38m, result.ExtractionConfidence);
        Assert.Collection(result.DescribedComponents!,
            grounded => { Assert.Equal("Sourced", grounded.NutritionProvenance); Assert.Equal(product.Id, grounded.FoodProductId); Assert.Equal(100m, grounded.Calories); },
            estimated => { Assert.Equal("ModelEstimated", estimated.NutritionProvenance); Assert.Null(estimated.FoodProductId); Assert.Equal(50m, estimated.Calories); });
    }

    [Fact]
    public async Task DescribeReportsSourcedOnlyWhenEverySurvivingComponentIsGrounded()
    {
        var product = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Banana",
            DataSource = "USDA",
            MatchConfidence = 0.9m,
            Calories100g = 89m,
            Protein100g = 1.1m,
            Carbs100g = 22.8m,
            Fat100g = 0.3m
        };
        var search = new Mock<IFoodSearchService>();
        search.Setup(s => s.ResolveAsync("banana", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto
            {
                OriginalQuery = "banana",
                Status = FoodResolutionStatus.Exact,
                Selected = product,
                MatchConfidence = 0.9m
            });
        var service = CreateService(search.Object);

        var result = await service.FinalizeDescribedDishAsync(new DescribedDish
        {
            Name = "Banana",
            Serving = new() { Unit = "g", Grams = 100m },
            Confidence = 0.8m,
            Components = [new() { Name = "banana", Grams = 100m, SearchQueries = ["banana"] }]
        }, "Banana", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sourced", result.NutritionProvenance);
        Assert.Equal(0.72m, result.ExtractionConfidence);
        Assert.Equal("usda", Assert.Single(result.DescribedComponents!).Source);
    }

    [Fact]
    public async Task DescribeReturnsNullWhenEveryFallbackFailsNutritionSanity()
    {
        var search = new Mock<IFoodSearchService>();
        search.Setup(s => s.ResolveAsync("kale", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto { OriginalQuery = "kale", Status = FoodResolutionStatus.Unresolved });
        var service = CreateService(search.Object);

        var result = await service.FinalizeDescribedDishAsync(new DescribedDish
        {
            Name = "Kale",
            Serving = new() { Unit = "g", Grams = 100m },
            Components = [new() { Name = "kale", Grams = 100m, SearchQueries = ["kale"], FallbackPer100G = new() { Kcal = 800m, ProteinG = 1m, CarbsG = 2m, FatG = 1m } }]
        }, "Kale", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void LabelFinalizerRejectsKjMisreadAndLabelWireRecordHasNoServerOwnedFields()
    {
        var dto = new CustomFoodDto
        {
            Name = "Bad label",
            ServingSize = 100m,
            ServingSizeUnit = "g",
            Calories = 690m,
            ProteinG = 10m,
            CarbG = 20m,
            FatG = 5m
        };

        Assert.False(ContentUnderstandingService.FinalizeGeneratedFood(dto));
        Assert.DoesNotContain(typeof(NutritionLabelExtraction).GetProperties(), p => p.Name is "NutritionProvenance" or "DescribedComponents");
    }

    [Fact]
    public void DescribeWireShapeUsesSpecifiedSnakeCaseFallbackFields()
    {
        var serialized = JsonSerializer.Serialize(new DescribedDish
        {
            Name = "Oatmeal",
            Serving = new() { Unit = "bowl", Grams = 220m },
            Components = [new() { Name = "oats", Grams = 50m, SearchQueries = ["rolled oats"], FallbackPer100G = new() { Kcal = 380m, ProteinG = 13m, CarbsG = 68m, FatG = 7m } }],
            Confidence = 0.8m
        });

        using var document = JsonDocument.Parse(serialized);
        var component = document.RootElement.GetProperty("components")[0];
        Assert.True(component.TryGetProperty("search_queries", out _));
        var estimate = component.GetProperty("fallback_per_100g");
        Assert.Equal(380m, estimate.GetProperty("kcal").GetDecimal());
        Assert.Equal(13m, estimate.GetProperty("protein_g").GetDecimal());
        Assert.Equal(68m, estimate.GetProperty("carbs_g").GetDecimal());
    }

    [Fact]
    public void DescribePromptVersionIsPublicAndVersioned()
    {
        Assert.Equal("2026-09-25.v1", ContentUnderstandingService.DescribeFoodPromptVersion);
        Assert.Equal("2026-09-25.v1", ContentUnderstandingService.NutritionLabelPromptVersion);
    }

    private static ContentUnderstandingService CreateService(IFoodSearchService? search = null)
        => new(new ContentUnderstandingClient(new Uri("https://localhost"), new AzureKeyCredential("unit-test")),
            new ConfigurationBuilder().Build(), foodSearch: search);
}
