using System.Runtime.CompilerServices;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class MealScanServiceWebCascadeTests
{
    [Fact]
    public async Task ScanMealImageAsync_WebResultsPreserveStageAOrderAndUseComputedNutrition()
    {
        var vision = new VisionChatClient();
        var search = new Mock<IFoodSearchService>();
        search.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto());
        var lookup = new CountingWebLookup();
        var request = default(MealDraftCreateRequest);
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(x => x.CreateAsync(It.IsAny<Guid>(), It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, MealDraftCreateRequest, CancellationToken>((_, value, _) => request = value)
            .ReturnsAsync((Guid _, MealDraftCreateRequest value, CancellationToken _) => new MealDraftDto
            {
                DraftId = Guid.NewGuid(),
                Origin = MealDraftOrigins.Photo,
                Status = MealDraftStatuses.PendingReview,
                ReferenceObjectVisible = value.ReferenceObjectVisible,
                OverallConfidence = value.OverallConfidence,
                Items = value.Items,
                Warnings = value.Warnings,
                Totals = new MealDraftTotalsDto(),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            });
        var service = CreateService(vision, search.Object, lookup, drafts.Object, []);

        var result = await service.ScanMealImageAsync(Guid.NewGuid(), new MemoryStream([1, 2, 3]), "image/jpeg");

        Assert.Same(request!.Items, result.Items);
        Assert.Equal(new[] { "rice", "beans" }, result.Items.Select(item => item.Name));
        Assert.Equal(2, lookup.Calls);
        foreach (var item in result.Items)
        {
            Assert.Equal("web", item.Source);
            Assert.NotNull(item.Per100g);
            Assert.Equal("Web", item.NutritionProvenance);
            Assert.Equal(NutritionCalculator.Compute(item.Per100g!, item.Grams).Calories, item.Calories);
            Assert.Null(item.FodmapStatus);
            Assert.Null(item.FodmapTriggers);
            Assert.Null(item.GutRating);
        }
    }

    [Fact]
    public async Task ScanMealImageAsync_DeadlineExhaustionSkipsWebLookup()
    {
        var vision = new VisionChatClient();
        var search = new Mock<IFoodSearchService>();
        search.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto());
        var lookup = new CountingWebLookup();
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(x => x.CreateAsync(It.IsAny<Guid>(), It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, MealDraftCreateRequest value, CancellationToken _) => new MealDraftDto
            {
                DraftId = Guid.NewGuid(),
                Origin = MealDraftOrigins.Photo,
                Status = MealDraftStatuses.PendingReview,
                ReferenceObjectVisible = value.ReferenceObjectVisible,
                OverallConfidence = value.OverallConfidence,
                Items = value.Items,
                Warnings = value.Warnings,
                Totals = new MealDraftTotalsDto(),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            });
        // The whole budget is shorter than the web minimum, so the skip is deterministic and
        // no wall-clock time has to elapse (the deadline never fires during Stage A).
        var service = CreateService(vision, search.Object, lookup, drafts.Object,
            new Dictionary<string, string?> { ["MealScan:DeadlineSeconds"] = "30", ["MealScan:MinSecondsForWeb"] = "31" });

        var result = await service.ScanMealImageAsync(Guid.NewGuid(), new MemoryStream([1]), "image/jpeg");

        Assert.Equal(0, lookup.Calls);
        Assert.Contains("Skipped web cascade to stay within the time budget.", result.Warnings);
    }

    private static MealScanService CreateService(
        IChatClient vision, IFoodSearchService search, IWebNutritionLookup lookup,
        IMealDraftService drafts, Dictionary<string, string?> settings)
    {
        settings.TryAdd("MealScan:MinSecondsForSelection", "0");
        settings.TryAdd("MealScan:MinSecondsForWeb", "0");
        settings.TryAdd("MealScan:MaxWebQueriesPerScan", "5");
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var store = new Mock<ITableStore>();
        store.Setup(x => x.GetAllUserMealItemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var cache = new InMemoryCache();
        return new MealScanService(vision, new VisionChatClient(), store.Object, config, search, lookup,
            new FodmapService(), new GutRiskService(), drafts, new VisionResultCache(cache, config),
            new PortionCalibrator(config), NullLogger<MealScanService>.Instance);
    }

    private sealed class CountingWebLookup : IWebNutritionLookup
    {
        public int Calls { get; private set; }
        public Task<WebNutritionResult?> LookupAsync(string foodName, FoodRegion region, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<WebNutritionResult?>(new WebNutritionResult
            {
                CaloriesKcal = 130,
                ProteinG = 5,
                CarbsG = 25,
                FatG = 2,
                FiberG = 3,
                SourceName = $"verified {foodName}",
                SourceUrl = $"https://example.test/{foodName}",
            });
        }
    }

    private sealed class InMemoryCache : ICacheService
    {
        private readonly Dictionary<string, object> values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(values.TryGetValue(key, out var value) && value is T typed ? typed : default);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        { values[key] = value!; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct = default)
        { values.Remove(key); return Task.CompletedTask; }
    }

    private sealed class VisionChatClient : IChatClient
    {
        private const string Json = """{"components":[{"name":"rice","estimated_grams_low":90,"estimated_grams_midpoint":100,"estimated_grams_high":110,"confidence":0.9,"portion_confidence":0.8,"search_queries":[]},{"name":"beans","estimated_grams_low":190,"estimated_grams_midpoint":200,"estimated_grams_high":210,"confidence":0.9,"portion_confidence":0.8,"search_queries":[]}],"reference_object_visible":true,"scale_notes":"","overall_confidence":0.9}""";
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Json)));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.CompletedTask; yield break; }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
