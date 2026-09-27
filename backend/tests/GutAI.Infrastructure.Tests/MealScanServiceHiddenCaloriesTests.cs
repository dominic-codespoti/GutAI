using System.Runtime.CompilerServices;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class MealScanServiceHiddenCaloriesTests
{
    private const string VisibleComponentJson = "\"components\":[{\"name\":\"rice\",\"estimated_grams_low\":90,\"estimated_grams_midpoint\":100,\"estimated_grams_high\":110,\"confidence\":0.9,\"portion_confidence\":0.8,\"search_queries\":[]}]";

    [Fact]
    public async Task FlagOff_UsesV11SchemaAndVersion()
    {
        var chat = new RecordingVisionClient($"{{{VisibleComponentJson},\"reference_object_visible\":true,\"scale_notes\":\"\",\"overall_confidence\":0.9}}");
        var service = CreateService(chat, new Dictionary<string, string?>());

        var result = await service.DecomposeAsync(new MemoryStream([1]), "image/jpeg");

        Assert.Equal(MealScanService.VisionPromptVersion, result.PromptVersion);
        Assert.NotEmpty(chat.ResponseFormatSchema);
        Assert.Contains("components", chat.ResponseFormatSchema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inferred_components", chat.ResponseFormatSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.InferredComponents);
    }

    [Fact]
    public async Task FlagOn_ScanCreatesOptInInferredItemAndExcludesItFromDraftTotals()
    {
        var chat = new RecordingVisionClient($"{{{VisibleComponentJson},\"reference_object_visible\":true,\"scale_notes\":\"\",\"overall_confidence\":0.9,\"inferred_components\":[{{\"name\":\"cooking oil\",\"estimated_grams_low\":4,\"estimated_grams_midpoint\":8,\"estimated_grams_high\":12,\"confidence\":0.8,\"cue\":\"glossy\"}}]}}");
        var config = new Dictionary<string, string?>
        {
            ["Features:HiddenCalories"] = "true",
            ["MealScan:MaxWebQueriesPerScan"] = "0",
        };
        var store = CreateStore();
        var productId = Guid.NewGuid();
        var search = new Mock<IFoodSearchService>();
        search.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, IReadOnlyCollection<Guid> _, CancellationToken _) => new FoodResolutionDto
            {
                OriginalQuery = query,
                Status = FoodResolutionStatus.Exact,
                MatchConfidence = 0.95m,
                Selected = new FoodProductDto
                {
                    Id = productId,
                    Name = query,
                    DataSource = "usda",
                    Calories100g = 200,
                    Protein100g = 4,
                    Carbs100g = 30,
                    Fat100g = 6,
                    MatchConfidence = 0.95m,
                },
            });
        var drafts = new MealDraftService(store.Object, CreateConfig(config), TimeProvider.System, NullLogger<MealDraftService>.Instance);
        var service = CreateService(chat, config, store, search.Object, drafts: drafts);

        var draft = await service.ScanMealImageAsync(Guid.NewGuid(), new MemoryStream([1]), "image/jpeg");

        var visible = Assert.Single(draft.Items, item => !item.IsInferred);
        Assert.Equal(200m, visible.Calories);
        var inferred = Assert.Single(draft.Items, item => item.IsInferred);
        Assert.Equal("cooking oil", inferred.Name);
        Assert.False(inferred.IncludedByDefault);
        Assert.Equal("inferred_cue", inferred.PortionMethod);
        Assert.Null(inferred.FodmapStatus);
        Assert.Equal(200m, draft.Totals.Calories);
        store.Verify(x => x.UpsertMealDraftAsync(
            It.Is<MealDraftRecord>(record => record.PromptVersion == MealScanService.VisionPromptVersion + "+hidden-calories.v1"),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("off by default", Assert.Single(draft.Warnings, warning => warning.Contains("likely-added items")));
        Assert.Contains("visible cue", chat.DeveloperInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never output calories", chat.DeveloperInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nutrition", chat.ResponseFormatSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inferred_components", chat.ResponseFormatSchema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanPassesUsersPreferredRegionToWebLookup()
    {
        var chat = new RecordingVisionClient($"{{{VisibleComponentJson},\"reference_object_visible\":true,\"scale_notes\":\"\",\"overall_confidence\":0.9}}");
        var store = CreateStore(FoodRegion.Au);
        var lookup = new RecordingWebLookup();
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(x => x.CreateAsync(It.IsAny<Guid>(), It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, MealDraftCreateRequest request, CancellationToken _) => new MealDraftDto
            {
                DraftId = Guid.NewGuid(),
                Origin = MealDraftOrigins.Photo,
                Status = MealDraftStatuses.PendingReview,

                Items = request.Items,
                Warnings = request.Warnings,
                Totals = new MealDraftTotalsDto(),
                ReferenceObjectVisible = request.ReferenceObjectVisible,
                OverallConfidence = request.OverallConfidence,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            });
        var search = new Mock<IFoodSearchService>();
        search.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto());
        var service = CreateService(chat, new Dictionary<string, string?>(), store, search.Object, lookup: lookup, drafts: drafts.Object,
            extraSettings: new Dictionary<string, string?> { ["MealScan:MaxWebQueriesPerScan"] = "1" });

        await service.ScanMealImageAsync(Guid.NewGuid(), new MemoryStream([1]), "image/jpeg");

        Assert.Equal(new[] { FoodRegion.Au }, lookup.Regions);
    }

    private static MealScanService CreateService(
        RecordingVisionClient vision,
        Dictionary<string, string?> settings,
        Mock<ITableStore>? store = null,
        IFoodSearchService? search = null,
        IWebNutritionLookup? lookup = null,
        IMealDraftService? drafts = null,
        Dictionary<string, string?>? extraSettings = null)
    {
        var combined = new Dictionary<string, string?>(settings)
        {
            ["MealScan:DeadlineSeconds"] = "60",
            ["MealScan:MinSecondsForSelection"] = "100",
            ["MealScan:MinSecondsForWeb"] = "0",
        };
        if (extraSettings is not null)
            foreach (var setting in extraSettings) combined[setting.Key] = setting.Value;
        var config = CreateConfig(combined);
        store ??= CreateStore();
        var searchService = search ?? new Mock<IFoodSearchService>().Object;
        return new MealScanService(vision, new RecordingVisionClient("{}"), store.Object, config,
            searchService, lookup ?? new RecordingWebLookup(), new FodmapService(), new GutRiskService(),
            drafts ?? new Mock<IMealDraftService>().Object,
            new VisionResultCache(new InMemoryCache(), config), new PortionCalibrator(config),
            NullLogger<MealScanService>.Instance);
    }

    private static Mock<ITableStore> CreateStore(FoodRegion region = FoodRegion.Default)
    {
        var store = new Mock<ITableStore>();
        store.Setup(x => x.GetAllUserMealItemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(x => x.GetUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { PreferredFoodRegion = region });
        store.Setup(x => x.UpsertMealDraftAsync(It.IsAny<MealDraftRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return store;
    }

    private static IConfiguration CreateConfig(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private sealed class RecordingVisionClient(string json) : IChatClient
    {
        public string ResponseFormatSchema { get; private set; } = "";
        public string DeveloperInstructions { get; private set; } = "";

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            DeveloperInstructions = string.Join("\n", messages.Where(message => message.Role == new ChatRole("developer")).Select(message => message.Text));
            ResponseFormatSchema = options?.ResponseFormat is { } format ? DescribeFormat(format) : "";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
        }

        private static string DescribeFormat(object format)
        {
            var parts = new List<string> { format.ToString() ?? "" };
            foreach (var property in format.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                try
                {
                    var value = property.GetValue(format);
                    if (value is JsonElement element) parts.Add(element.GetRawText());
                    else if (value is JsonDocument document) parts.Add(document.RootElement.GetRawText());
                    else if (value is not null) parts.Add(JsonSerializer.Serialize(value));
                }
                catch (System.Reflection.TargetInvocationException) { }
            }
            return string.Join("\n", parts);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.CompletedTask; yield break; }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class RecordingWebLookup : IWebNutritionLookup
    {
        public List<FoodRegion> Regions { get; } = [];
        public Task<WebNutritionResult?> LookupAsync(string foodName, FoodRegion region, CancellationToken ct = default)
        {
            Regions.Add(region);
            return Task.FromResult<WebNutritionResult?>(null);
        }
    }

    private sealed class InMemoryCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }
}
