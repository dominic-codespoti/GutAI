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
    public async Task SearchAsync_EmbeddedCatalogRanksWholeBoiledEggAndEggplantCorrectly()
    {
        var store = new Mock<ITableStore> { DefaultValue = DefaultValue.Empty };
        var aggregator = new ExternalFoodProviderAggregator(
            [new WholeFoodApiService(), new BrandedFoodApiService(), new AustralianFoodApiService()],
            NullLogger<ExternalFoodProviderAggregator>.Instance);
        var service = new FoodSearchService(
            store.Object, aggregator, new FoodRanker(), NullLogger<FoodSearchService>.Instance);
        const string boiledEgg = "Egg, whole, cooked, hard-boiled";
        var wholeFoodResults = await new WholeFoodApiService().SearchAsync("boiled egg");
        wholeFoodResults.Select(food => food.Name).Should().Contain(boiledEgg);
        var descriptiveWholeFoods = await new WholeFoodApiService().SearchAsync("large hard-boiled chicken egg");
        descriptiveWholeFoods.Select(food => food.Name).Should().Contain(boiledEgg);

        foreach (var query in new[] { "boiled egg", "hard boiled egg", "hard-boiled egg", "egg, whole, cooked, hard-boiled" })
        {
            var results = await service.SearchAsync(query);
            results[0].Name.Should().Be(boiledEgg, query);
        }

        var descriptiveEgg = await service.SearchAsync("large hard-boiled chicken egg");
        descriptiveEgg.Take(3).Select(food => food.Name).Should().Contain(boiledEgg);

        var egg = await service.SearchAsync("egg");
        egg[0].Name.Should().StartWith("Egg, whole,");

        var eggplant = await service.SearchAsync("eggplant");
        eggplant[0].Name.Should().StartWith("Eggplant");

        var boiledEggplant = await service.SearchAsync("boiled eggplant");
        boiledEggplant[0].Name.Should().Contain("Eggplant").And.Contain("boiled");
    }

    [Fact]
    public async Task ResolveAsync_BrandlessSpaghettiStaysAmbiguous_BrandQueryCanResolveBarilla()
    {
        var store = new Mock<ITableStore> { DefaultValue = DefaultValue.Empty };
        var aggregator = new ExternalFoodProviderAggregator(
            [new WholeFoodApiService(), new BrandedFoodApiService(), new AustralianFoodApiService()],
            NullLogger<ExternalFoodProviderAggregator>.Instance);
        var service = new FoodSearchService(
            store.Object, aggregator, new FoodRanker(), NullLogger<FoodSearchService>.Instance);

        var genericResult = await service.ResolveAsync("spaghetti", []);

        genericResult.Status.Should().Be(FoodResolutionStatus.Ambiguous);
        genericResult.Selected!.Name.Should().NotStartWith("Barilla");

        var brandedResult = await service.ResolveAsync("barilla spaghetti", []);
        brandedResult.Status.Should().BeOneOf(FoodResolutionStatus.Exact, FoodResolutionStatus.Probable);
        brandedResult.MatchConfidence.Should().BeGreaterThanOrEqualTo(0.85m);
        brandedResult.Selected.Should().NotBeNull();
        brandedResult.Selected!.Brand.Should().StartWith("Barilla");
    }

    [Fact]
    public async Task ResolveAsync_PartialOrBrandedMatchesStayAmbiguous()
    {
        var store = new Mock<ITableStore> { DefaultValue = DefaultValue.Empty };
        var aggregator = new ExternalFoodProviderAggregator(
            [new WholeFoodApiService(), new BrandedFoodApiService(), new AustralianFoodApiService()],
            NullLogger<ExternalFoodProviderAggregator>.Instance);
        var service = new FoodSearchService(
            store.Object, aggregator, new FoodRanker(), NullLogger<FoodSearchService>.Instance);

        foreach (var query in new[]
        {
            "English breakfast baked beans",
            "large hard-boiled chicken egg",
            "cooked bacon rashers",
            "cooked mushrooms with browning",
            "egg scramble with sautéed vegetables",
            "frozen blackberries bowl",
            "parsley sprig on pasta",
            "pickles served with katsu curry",
            "roasted asparagus",
            "spaghetti with tomato sauce",
        })
        {
            var result = await service.ResolveAsync(query, []);
            result.Status.Should().Be(FoodResolutionStatus.Ambiguous, query);
        }

        foreach (var query in new[] { "hard boiled egg", "hard-boiled egg" })
        {
            var result = await service.ResolveAsync(query, []);
            if (result.Status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable)
            {
                result.Selected.Should().NotBeNull();
                result.Selected!.Brand.Should().BeNull();
                result.Selected.Name.Should().Be("Egg, whole, cooked, hard-boiled");
            }
        }
    }

    [Fact]
    public async Task ResolveAsync_BrandlessQueriesPreserveBaselineBrandedSelections()
    {
        var store = new Mock<ITableStore> { DefaultValue = DefaultValue.Empty };
        var aggregator = new ExternalFoodProviderAggregator(
            [new WholeFoodApiService(), new BrandedFoodApiService(), new AustralianFoodApiService()],
            NullLogger<ExternalFoodProviderAggregator>.Instance);
        var service = new FoodSearchService(
            store.Object, aggregator, new FoodRanker(), NullLogger<FoodSearchService>.Instance);
        var cases = new[]
        {
            (Query: "bacon", ExpectedName: "Bacon"),
            (Query: "baked beans", ExpectedName: "Baked Beans"),
            (Query: "guacamole", ExpectedName: "Guacamole"),
            (Query: "pulled pork sandwich", ExpectedName: "Pulled Pork Sandwich"),
            (Query: "salsa", ExpectedName: "Salsa"),
        };

        foreach (var (query, expectedName) in cases)
        {
            var result = await service.ResolveAsync(query, []);
            result.Status.Should().BeOneOf(FoodResolutionStatus.Exact, FoodResolutionStatus.Probable);
            result.MatchConfidence.Should().BeGreaterThanOrEqualTo(0.85m);
            result.Selected.Should().NotBeNull();
            result.Selected!.Name.Should().Be(expectedName);
        }
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
