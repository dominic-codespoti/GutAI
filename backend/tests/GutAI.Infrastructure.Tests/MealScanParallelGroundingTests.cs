using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GutAI.Infrastructure.Tests;
using Xunit;

public sealed class MealScanParallelGroundingTests
{
    [Fact]
    public async Task GroundComponentsAsync_PreservesOrder_AndBoundsResolverCalls()
    {
        var maxComponentConcurrency = 2;
        var activeResolverCalls = 0;
        var maxActiveResolverCalls = 0;

        var search = new Mock<IFoodSearchService>();
        search
            .Setup(f => f.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<Guid>, CancellationToken>(async (_, _, _) =>
            {
                var active = Interlocked.Increment(ref activeResolverCalls);
                UpdateMax(ref maxActiveResolverCalls, active);
                await Task.Delay(25);
                Interlocked.Decrement(ref activeResolverCalls);
                return new FoodResolutionDto();
            });

        var components = Enumerable.Range(0, 8)
            .Select(index => new ScannedComponent
            {
                Name = $"component {index}",
                EstimatedGramsLow = 100,
                EstimatedGramsMidpoint = 150,
                EstimatedGramsHigh = 200,
                Confidence = 0.9m,
            })
            .ToList();

        var service = CreateService(search.Object, maxComponentConcurrency);
        var grounded = await service.GroundComponentsAsync(components, CancellationToken.None);

        Assert.Equal(components.Count, grounded.Length);
        for (var index = 0; index < components.Count; index++)
            Assert.Same(components[index], grounded[index].Original);

        // ComponentGroundingEngine issues up to three resolver queries per component.
        // This asserts the component-level bound is honored without coupling the test
        // to its internal query count.
        Assert.InRange(maxActiveResolverCalls, 2, maxComponentConcurrency * 3);
    }

    [Fact]
    public async Task GroundComponentsAsync_EmptyComponents_ReturnsEmpty()
    {
        var service = CreateService(new Mock<IFoodSearchService>().Object, 4);
        var grounded = await service.GroundComponentsAsync([], CancellationToken.None);

        Assert.Empty(grounded);
    }

    private static MealScanService CreateService(IFoodSearchService search, int maxConcurrency)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MealScan:MaxConcurrentGrounding"] = maxConcurrency.ToString(),
            })
            .Build();

        return new MealScanService(
            new Mock<IChatClient>().Object,
            new Mock<ITableStore>().Object,
            config,
            search,
            new Mock<IWebNutritionLookup>().Object,
            new FodmapService(),
            new GutRiskService(),
            NullLogger<MealScanService>.Instance);
    }

    private static void UpdateMax(ref int location, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (value <= current) return;
            if (Interlocked.CompareExchange(ref location, value, current) == current) return;
        }
    }
}
