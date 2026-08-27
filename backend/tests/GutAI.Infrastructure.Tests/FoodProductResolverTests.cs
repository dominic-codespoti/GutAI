using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodProductResolverTests
{
    private readonly Mock<ITableStore> _storeMock = new();
    private readonly Mock<IOfflineFoodDatabase> _offlineDbMock = new();
    private readonly Mock<IExternalFoodAggregator> _foodApiMock = new();
    private readonly Mock<ILogger> _loggerMock = new();

    [Fact]
    public async Task GetEnrichedCatalogProductAsync_OffProductMissingIngredientsWithBarcode_EnrichesFromOfflineAndPersists()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var initialProduct = new FoodProduct
        {
            Id = productId,
            Name = "OFF Product",
            DataSource = DataSources.OpenFoodFacts,
            Barcode = "1234567890",
            Ingredients = null,
            NovaGroup = null,
            NutriScore = null,
            Calories100g = 50m
        };

        var offlineDto = new FoodProductDto
        {
            Barcode = "1234567890",
            Name = "OFF Product",
            Ingredients = "Water, Sugar, Salt",
            NovaGroup = 3,
            NutriScore = "c",
            ServingSize = "100g",
            ServingQuantity = 100m,
            ImageUrl = "http://example.com/image.jpg",
            AllergensTags = ["en:gluten"],
            Calories100g = 150m,
            Protein100g = 2m,
            Carbs100g = 30m,
            Fat100g = 1m,
            Fiber100g = 2m,
            Sugar100g = 20m,
            SodiumMg100g = 400m
        };

        _storeMock.Setup(s => s.GetFoodProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialProduct);

        _offlineDbMock.Setup(o => o.LookupByBarcodeAsync("1234567890", It.IsAny<CancellationToken>()))
            .ReturnsAsync(offlineDto);

        // Act
        var result = await FoodProductResolver.GetEnrichedCatalogProductAsync(
            productId,
            _storeMock.Object,
            _offlineDbMock.Object,
            _foodApiMock.Object,
            CancellationToken.None,
            _loggerMock.Object);

        // Assert
        result.Should().NotBeNull();
        result!.Ingredients.Should().Be("Water, Sugar, Salt");
        result.NovaGroup.Should().Be(3);
        result.NutriScore.Should().Be("c");
        result.ServingSize.Should().Be("100g");
        result.ServingQuantity.Should().Be(100m);
        result.ImageUrl.Should().Be("http://example.com/image.jpg");
        result.AllergensTags.Should().BeEquivalentTo(new[] { "en:gluten" });
        result.Calories100g.Should().Be(150m);
        result.Protein100g.Should().Be(2m);
        result.Carbs100g.Should().Be(30m);
        result.Fat100g.Should().Be(1m);
        result.Fiber100g.Should().Be(2m);
        result.Sugar100g.Should().Be(20m);
        result.SodiumMg100g.Should().Be(400m);

        _offlineDbMock.Verify(o => o.LookupByBarcodeAsync("1234567890", It.IsAny<CancellationToken>()), Times.Once);
        _foodApiMock.Verify(f => f.LookupBarcodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _storeMock.Verify(s => s.UpsertFoodProductAsync(initialProduct, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEnrichedCatalogProductAsync_NonOffProduct_ReturnedUntouchedWithNoLookups()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var usdaProduct = new FoodProduct
        {
            Id = productId,
            Name = "USDA Apple",
            DataSource = DataSources.Usda,
            Barcode = "9876543210",
            Ingredients = null,
            Calories100g = 52m
        };

        _storeMock.Setup(s => s.GetFoodProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usdaProduct);

        // Act
        var result = await FoodProductResolver.GetEnrichedCatalogProductAsync(
            productId,
            _storeMock.Object,
            _offlineDbMock.Object,
            _foodApiMock.Object,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(productId);
        result.Ingredients.Should().BeNull();
        result.Calories100g.Should().Be(52m);

        _offlineDbMock.Verify(o => o.LookupByBarcodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _foodApiMock.Verify(f => f.LookupBarcodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _storeMock.Verify(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetEnrichedCatalogProductAsync_OfflineMiss_FallsThroughToAggregator()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var offProduct = new FoodProduct
        {
            Id = productId,
            Name = "OFF Cereal",
            DataSource = DataSources.OpenFoodFacts,
            Barcode = "5555555555",
            Ingredients = "",
            Calories100g = 200m
        };

        var aggregatorDto = new FoodProductDto
        {
            Barcode = "5555555555",
            Name = "OFF Cereal",
            Ingredients = "Whole Oats, Sugar, Honey",
            NovaGroup = 4,
            Calories100g = 380m
        };

        _storeMock.Setup(s => s.GetFoodProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(offProduct);

        _offlineDbMock.Setup(o => o.LookupByBarcodeAsync("5555555555", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FoodProductDto?)null);

        _foodApiMock.Setup(f => f.LookupBarcodeAsync("5555555555", It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregatorDto);

        // Act
        var result = await FoodProductResolver.GetEnrichedCatalogProductAsync(
            productId,
            _storeMock.Object,
            _offlineDbMock.Object,
            _foodApiMock.Object,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Ingredients.Should().Be("Whole Oats, Sugar, Honey");
        result.NovaGroup.Should().Be(4);
        result.Calories100g.Should().Be(380m);

        _offlineDbMock.Verify(o => o.LookupByBarcodeAsync("5555555555", It.IsAny<CancellationToken>()), Times.Once);
        _foodApiMock.Verify(f => f.LookupBarcodeAsync("5555555555", It.IsAny<CancellationToken>()), Times.Once);
        _storeMock.Verify(s => s.UpsertFoodProductAsync(offProduct, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEnrichedCatalogProductAsync_EnrichmentException_ReturnsUnenrichedEntityAndLogsWarning()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var offProduct = new FoodProduct
        {
            Id = productId,
            Name = "OFF Broken Product",
            DataSource = DataSources.OpenFoodFacts,
            Barcode = "9999999999",
            Ingredients = null,
            Calories100g = 100m
        };

        _storeMock.Setup(s => s.GetFoodProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(offProduct);

        _offlineDbMock.Setup(o => o.LookupByBarcodeAsync("9999999999", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error contacting offline store"));

        // Act
        var result = await FoodProductResolver.GetEnrichedCatalogProductAsync(
            productId,
            _storeMock.Object,
            _offlineDbMock.Object,
            _foodApiMock.Object,
            CancellationToken.None,
            _loggerMock.Object);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(productId);
        result.Ingredients.Should().BeNull();
        result.Calories100g.Should().Be(100m);

        _storeMock.Verify(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>()), Times.Never);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }
}
