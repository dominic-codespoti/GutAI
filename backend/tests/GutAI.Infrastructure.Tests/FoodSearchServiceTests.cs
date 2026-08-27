using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.ExternalApis;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodSearchServiceTests
{
    private readonly Mock<ITableStore> _storeMock = new();
    private readonly Mock<IExternalFoodAggregator> _aggregatorMock = new();
    private readonly IFoodRanker _ranker = new FoodRanker();

    private FoodSearchService CreateService()
    {
        return new FoodSearchService(
            _storeMock.Object,
            _aggregatorMock.Object,
            _ranker,
            NullLogger<FoodSearchService>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_ConfidentLocalExact_ReturnsWithoutAggregatorCall()
    {
        // Arrange
        var localFood = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = "Banana",
            FoodKind = FoodKind.WholeFood,
            DataSource = "AzureTable",
        };

        _storeMock
            .Setup(s => s.SearchFoodProductsAsync("banana", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([localFood]);

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync("banana", []);

        // Assert
        result.Status.Should().Be(FoodResolutionStatus.Exact);
        result.Selected.Should().NotBeNull();
        result.Selected!.Name.Should().Be("Banana");
        _aggregatorMock.Verify(
            a => a.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_ConfidentLocalProbable_ReturnsWithoutAggregatorCall()
    {
        // Arrange
        var localFood = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = "Chicken, breast, raw",
            FoodKind = FoodKind.WholeFood,
            DataSource = "AzureTable",
        };

        _storeMock
            .Setup(s => s.SearchFoodProductsAsync("chicken breast", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([localFood]);

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync("chicken breast", []);

        // Assert
        result.Status.Should().Be(FoodResolutionStatus.Probable);
        result.Selected.Should().NotBeNull();
        result.Selected!.Name.Should().Be("Chicken, breast, raw");
        _aggregatorMock.Verify(
            a => a.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_LocalMiss_TriggersAggregator()
    {
        // Arrange
        _storeMock
            .Setup(s => s.SearchFoodProductsAsync("dragonfruit", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var externalFood = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Dragonfruit, raw",
            DataSource = "USDA",
            FoodKind = FoodKind.WholeFood,
        };

        _aggregatorMock
            .Setup(a => a.SearchAsync("dragonfruit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSearchOutcome([externalFood], [
                new ProviderSearchResult("USDA", ProviderSearchStatus.Success, 1, TimeSpan.FromMilliseconds(50))
            ]));

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync("dragonfruit", []);

        // Assert
        result.Status.Should().BeOneOf(FoodResolutionStatus.Exact, FoodResolutionStatus.Probable);
        result.Selected.Should().NotBeNull();
        result.Selected!.Name.Should().Be("Dragonfruit, raw");
        _aggregatorMock.Verify(
            a => a.SearchAsync("dragonfruit", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_LocalAmbiguous_TriggersAggregator()
    {
        // Arrange: two close local candidates produce Ambiguous resolution
        var localSalmon1 = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = "Salmon, sockeye, raw",
            Calories100g = 168,
            Protein100g = 20,
            Carbs100g = 0,
            Fat100g = 9,
            FoodKind = FoodKind.WholeFood,
            DataSource = "AzureTable",
        };
        var localSalmon2 = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = "Salmon, coho, raw",
            Calories100g = 168,
            Protein100g = 20,
            Carbs100g = 0,
            Fat100g = 9,
            FoodKind = FoodKind.WholeFood,
            DataSource = "AzureTable",
        };

        _storeMock
            .Setup(s => s.SearchFoodProductsAsync("salmon", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([localSalmon1, localSalmon2]);

        var externalSalmonExact = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Salmon",
            FoodKind = FoodKind.WholeFood,
            DataSource = "USDA",
        };

        _aggregatorMock
            .Setup(a => a.SearchAsync("salmon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSearchOutcome([externalSalmonExact], [
                new ProviderSearchResult("USDA", ProviderSearchStatus.Success, 1, TimeSpan.FromMilliseconds(40))
            ]));

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync("salmon", []);

        // Assert
        _aggregatorMock.Verify(
            a => a.SearchAsync("salmon", It.IsAny<CancellationToken>()),
            Times.Once);
        result.Selected.Should().NotBeNull();
    }
}
