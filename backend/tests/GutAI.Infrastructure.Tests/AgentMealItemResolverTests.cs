using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class AgentMealItemResolverTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly NutritionPer100gDto Basis = new()
    {
        CaloriesKcal = 200m,
        ProteinG = 10m,
        CarbsG = 20m,
        FatG = 5m,
        FiberG = 3m,
        SugarG = 4m,
        SodiumMg = 100m,
    };
    public static TheoryData<decimal?, decimal?, decimal, decimal> ProductPortionCases => new()
    {
        { 80m, 2m, 160m, 0.6m },
        { null, 75m, 150m, 0.5m },
        { 2500m, 75m, 4000m, 0.6m },
        { null, null, 360m, 0.3m },
    };

    public static TheoryData<FoodResolutionStatus, decimal, decimal?, bool> GroundingParityCases => new()
    {
        { FoodResolutionStatus.Exact, 0.95m, 52m, true },
        { FoodResolutionStatus.Probable, 0.84m, 52m, false },
        { FoodResolutionStatus.Ambiguous, 0.95m, 52m, false },
        { FoodResolutionStatus.Exact, 0.95m, null, false },
    };

    [Theory]
    [MemberData(nameof(ProductPortionCases))]
    public async Task ProductIdPath_UsesExplicitServingThenCatalogThenEstimate(
        decimal? explicitGrams, decimal? productServing, decimal expectedGrams, decimal expectedConfidence)
    {
        var product = Product(servingQuantity: productServing);
        var resolver = Create(store => store.Setup(s => s.GetFoodProductAsync(ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product));

        var result = Assert.Single(await resolver.ResolveAsync([
            new AgentMealItemInput("apple", ProductId, Servings: 2, ServingWeightG: explicitGrams),
        ]));

        Assert.Equal(expectedGrams, result.Grams);
        Assert.Equal(expectedConfidence, result.PortionConfidence);
        Assert.Equal("agent_estimate", result.PortionMethod);
        Assert.Equal(nameof(NutritionProvenance.Sourced), result.NutritionProvenance);
        Assert.Equal(expectedGrams * 2m, result.Calories);
        Assert.Equal(0.5m, result.MatchConfidence);
    }

    [Fact]
    public async Task ProductIdPath_CarriesSearchConfidenceAndKeepsUnverifiedIdBelowTrustThreshold()
    {
        var product = Product(servingQuantity: 100m);
        var resolver = Create(store => store.Setup(s => s.GetFoodProductAsync(ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product));

        var result = Assert.Single(await resolver.ResolveAsync([
            new AgentMealItemInput(null, ProductId, ServingWeightG: 300m, MatchConfidence: 0.92m),
        ]));
        Assert.Equal(0.92m, result.MatchConfidence);

        var defaultConfidence = Assert.Single(await resolver.ResolveAsync([
            new AgentMealItemInput("apple", ProductId),
        ]));
        Assert.Equal(0.5m, defaultConfidence.MatchConfidence);
        Assert.True(defaultConfidence.MatchConfidence < GroundingPolicy.MinAutoSelectConfidence - 0.25m);
    }

    [Fact]
    public async Task NamePath_AutoSelectsThroughPolicyAndPersistsCandidate()
    {
        var candidate = ProductDto("Apple", calories: 52m, confidence: 0.95m);
        var resolution = new FoodResolutionDto
        {
            OriginalQuery = "apple",
            Status = FoodResolutionStatus.Exact,
            Selected = candidate,
            MatchConfidence = 0.95m,
        };
        Guid? persistedId = null;
        var resolver = Create(
            store =>
            {
                store.Setup(s => s.SearchFoodProductsAsync("Apple", 10, It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);
                store.Setup(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>()))
                    .Callback<FoodProduct, CancellationToken>((food, _) => persistedId = food.Id)
                    .Returns(Task.CompletedTask);
            },
            food => food.Setup(s => s.ResolveAsync("apple", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolution));

        var result = Assert.Single(await resolver.ResolveAsync([new AgentMealItemInput("apple", null, ServingWeightG: 100m)]));

        Assert.Equal(52m, result.Per100g!.CaloriesKcal);
        Assert.Equal(nameof(NutritionProvenance.Sourced), result.NutritionProvenance);
        Assert.Equal(persistedId, result.FoodProductId);
        Assert.NotNull(persistedId);
        Assert.Equal("apple", result.Grounding!.Query);
        Assert.True(result.Grounding.AutoSelected);
        Assert.Equal("exact", result.Grounding.ResolutionStatus);
        Assert.Equal("resolve_async", result.Grounding.Method);
        Assert.Equal(ProductId, result.Grounding.Candidates[0].FoodProductId);
        Assert.Equal(52m, result.Calories);
    }

    [Fact]
    public async Task NamePath_KeepsBasisButLeavesProductIdNullWhenPersistenceFails()
    {
        var candidate = ProductDto("Apple", calories: 52m, confidence: 0.95m);
        var resolution = new FoodResolutionDto
        {
            OriginalQuery = "apple",
            Status = FoodResolutionStatus.Exact,
            Selected = candidate,
            MatchConfidence = 0.95m,
        };
        var resolver = Create(
            store =>
            {
                store.Setup(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("storage unavailable"));
            },
            food => food.Setup(s => s.ResolveAsync("apple", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolution));

        var result = Assert.Single(await resolver.ResolveAsync([new AgentMealItemInput("apple", null, ServingWeightG: 100m)]));

        Assert.Null(result.FoodProductId);
        Assert.Null(result.Grounding!.SelectedFoodProductId);
        Assert.Equal(52m, result.Per100g!.CaloriesKcal);
        Assert.Equal(52m, result.Calories);
    }

    [Fact]
    public async Task NamePath_AmbiguousNeedsChoiceWithRankedCandidatesAndNoBasis()
    {
        var first = ProductDto("Apple", calories: 52m, confidence: 0.8m);
        var second = ProductDto("Apple pie", calories: 240m, confidence: 0.7m);
        var resolution = new FoodResolutionDto
        {
            OriginalQuery = "apple",
            Status = FoodResolutionStatus.Ambiguous,
            Selected = first,
            Alternatives = [second],
            MatchConfidence = 0.8m,
        };
        var resolver = Create(
            setupFood: food => food.Setup(s => s.ResolveAsync("apple", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolution));

        var result = Assert.Single(await resolver.ResolveAsync([new AgentMealItemInput("apple", null)]));

        Assert.True(result.NeedsChoice);
        Assert.Null(result.Per100g);
        Assert.Null(result.Calories);
        Assert.Equal(nameof(NutritionProvenance.Unknown), result.NutritionProvenance);
        Assert.Null(result.FoodProductId);
        Assert.Equal(new[] { "Apple", "Apple pie" }, result.CandidateNames);
        Assert.Equal(2, result.Grounding!.Candidates.Count);
        Assert.All(result.Grounding.Candidates, candidate => Assert.NotNull(candidate.CandidateKey));
        Assert.False(result.Grounding.AutoSelected);
    }

    [Fact]
    public async Task NameWithoutCandidates_UsesNaturalLanguageMapping()
    {
        var parsed = new ParsedFoodItemDto
        {
            Name = "generic soup",
            ServingWeightG = 250m,
            Per100g = Basis,
            MatchConfidence = 0.1m,
            PortionConfidence = 0.7m,
            NutritionProvenance = nameof(NutritionProvenance.Estimated),
        };
        var resolver = Create(
            setupFood: food => food.Setup(s => s.ResolveAsync("soup", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FoodResolutionDto { OriginalQuery = "soup", Status = FoodResolutionStatus.Unresolved }),
            setupNutrition: nutrition => nutrition.Setup(s => s.ParseNaturalLanguageAsync("soup", It.IsAny<GutAI.Domain.Enums.FoodRegion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([parsed]));

        var result = Assert.Single(await resolver.ResolveAsync([new AgentMealItemInput("soup", null, Servings: 2)]));

        Assert.Equal("generic soup", result.Name);
        Assert.Equal("estimate", result.Source);
        Assert.Equal(500m, result.Grams);
        Assert.Equal(Basis, result.Per100g);
        Assert.Equal(1000m, result.Calories);
        Assert.Equal(nameof(NutritionProvenance.Estimated), result.NutritionProvenance);
    }

    [Fact]
    public async Task FallbackDescriptionParsesWhenItemsEmptyOrSkipped()
    {
        var parsed = new ParsedFoodItemDto
        {
            Name = "milk",
            ServingWeightG = 200m,
            Per100g = Basis,
            NutritionProvenance = nameof(NutritionProvenance.Sourced),
        };
        var resolver = Create(
            setupStore: store => store.Setup(s => s.GetFoodProductAsync(ProductId, It.IsAny<CancellationToken>())).ReturnsAsync((FoodProduct?)null),
            setupNutrition: nutrition => nutrition.Setup(s => s.ParseNaturalLanguageAsync("a glass of milk", It.IsAny<GutAI.Domain.Enums.FoodRegion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([parsed]));

        var skipped = Assert.Single(await resolver.ResolveAsync(
            [new AgentMealItemInput(null, ProductId)], "a glass of milk"));
        var empty = Assert.Single(await resolver.ResolveAsync([], "a glass of milk"));
        Assert.Equal("milk", skipped.Name);
        Assert.Equal("milk", empty.Name);

        Assert.Equal(200m, skipped.Grams);
        Assert.Equal("db", skipped.Source);
        Assert.Equal(nameof(NutritionProvenance.Sourced), skipped.NutritionProvenance);
    }

    [Fact]
    public async Task EmptyAndUnidentifiedItemsAreSkippedWithoutFallback()
    {
        var resolver = Create(
            store => store.Setup(s => s.GetFoodProductAsync(ProductId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((FoodProduct?)null));

        Assert.Empty(await resolver.ResolveAsync([]));
        Assert.Empty(await resolver.ResolveAsync([new AgentMealItemInput(null, ProductId)]));
    }

    [Theory]
    [MemberData(nameof(GroundingParityCases))]
    public async Task NamePath_SelectionParityMatchesGroundingPolicy(
        FoodResolutionStatus status, decimal confidence, decimal? calories, bool expectedAutoSelect)
    {
        var candidate = ProductDto("apple", calories, confidence);
        var fixture = new FoodResolutionDto
        {
            OriginalQuery = "apple",
            Status = status,
            Selected = candidate,
            MatchConfidence = confidence,
        };
        var resolver = Create(
            store => store.Setup(s => s.SearchFoodProductsAsync("apple", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]),
            food => food.Setup(s => s.ResolveAsync("apple", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(fixture));

        var result = Assert.Single(await resolver.ResolveAsync([new AgentMealItemInput("apple", null)]));

        Assert.Equal(GroundingPolicy.Decide(fixture).AutoSelected, expectedAutoSelect);
        Assert.Equal(expectedAutoSelect, result.Grounding?.AutoSelected);
        Assert.Equal(expectedAutoSelect, !result.NeedsChoice);
        Assert.Equal(expectedAutoSelect, result.Per100g is not null);
    }

    private static AgentMealItemResolver Create(
        Action<Mock<ITableStore>>? setupStore = null,
        Action<Mock<IFoodSearchService>>? setupFood = null,
        Action<Mock<INutritionApiService>>? setupNutrition = null)
    {
        var store = new Mock<ITableStore>();
        setupStore?.Invoke(store);
        var food = new Mock<IFoodSearchService>();
        setupFood?.Invoke(food);
        var nutrition = new Mock<INutritionApiService>();
        setupNutrition?.Invoke(nutrition);
        return new AgentMealItemResolver(store.Object, food.Object, nutrition.Object, NullLogger<AgentMealItemResolver>.Instance);
    }

    private static FoodProduct Product(decimal? servingQuantity = null) => new()
    {
        Id = ProductId,
        Name = "Apple",
        Calories100g = 200m,
        Protein100g = 10m,
        Carbs100g = 20m,
        Fat100g = 5m,
        Fiber100g = 3m,
        Sugar100g = 4m,
        SodiumMg100g = 100m,
        ServingQuantity = servingQuantity,
        DataSource = "USDA FDC",
    };

    private static FoodProductDto ProductDto(string name, decimal? calories, decimal confidence) => new()
    {
        Id = ProductId,
        Name = name,
        Calories100g = calories,
        Protein100g = 1m,
        Carbs100g = 2m,
        Fat100g = 3m,
        DataSource = "USDA",
        MatchConfidence = confidence,
    };
}
