using System.Runtime.CompilerServices;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class CoachChatServiceTests
{
    [Fact]
    public async Task StreamResponse_AttachesCurrentNutritionSnapshotToModelContext()
    {
        var userId = Guid.NewGuid();
        var meal = new MealLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MealType = MealType.Lunch,
            LoggedAt = DateTime.UtcNow,
            TotalCalories = 1422,
            TotalProteinG = 97,
            TotalCarbsG = 134,
            TotalFatG = 60,
        };
        var user = new User
        {
            Id = userId,
            Email = "coach-test@example.com",
            TimezoneId = "UTC",
            DailyCalorieGoal = 2000,
            DailyProteinGoalG = 50,
            DailyCarbGoalG = 250,
            DailyFatGoalG = 65,
            DailyFiberGoalG = 25,
        };

        var store = new Mock<ITableStore>();
        store.Setup(s => s.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        store.Setup(s => s.GetCoachSessionStateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CoachSessionState?)null);
        store.Setup(s => s.GetRecentCoachMessagesAsync(userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.GetMealLogsByDateRangeAsync(
                userId,
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([meal]);
        store.Setup(s => s.GetMealItemsAsync(userId, meal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MealItem
                {
                    Id = Guid.NewGuid(),
                    MealLogId = meal.Id,
                    FoodName = "Logged meal item",
                    FiberG = 8,
                },
            ]);
        store.Setup(s => s.UpsertCoachMessageAsync(
                userId,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chatClient = new RecordingChatClient();
        var fodmap = new FodmapService();
        var gutRisk = new GutRiskService();
        var budgetService = CreateBudgetMock();
        var service = new CoachChatService(
            chatClient,
            store.Object,
            new Mock<ICorrelationEngine>().Object,
            new Mock<IFoodDiaryAnalysisService>().Object,
            new Mock<IFoodSearchService>().Object,
            new Mock<INutritionApiService>().Object,
            fodmap,
            gutRisk,
            new PersonalizedScoringService(gutRisk, fodmap),
            new Mock<IMealDraftService>().Object,
            new Mock<IAgentMealItemResolver>().Object,
            budgetService.Object,
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            NullLogger<CoachChatService>.Instance);

        await foreach (var _ in service.StreamResponseAsync(
                           userId,
                           "How's my nutrition today?",
                           CancellationToken.None,
                           "UTC"))
        {
        }

        var snapshot = Assert.Single(
            chatClient.LastMessages,
            message => message.Text.Contains("<current_nutrition_snapshot>", StringComparison.Ordinal)
                && message.Text.Contains("\"totalCalories\":1422", StringComparison.Ordinal));
        Assert.Contains("\"totalCalories\":1422", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("\"totalProteinG\":97", snapshot.Text, StringComparison.Ordinal);
        budgetService.Verify(service => service.GetBudgetAsync(userId, null, "UTC", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("\"remaining\":{\"calories\":578", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("\"mealCount\":1", snapshot.Text, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ProposeMeal_CreatesCoachDraftAndReturnsServerComputedNumbers()
    {
        var userId = Guid.NewGuid();
        var draft = MakeDraft();
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(service => service.CreateAsync(userId, It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var resolver = new Mock<IAgentMealItemResolver>();
        resolver.Setup(service => service.ResolveAsync(
                It.IsAny<IReadOnlyList<AgentMealItemInput>>(), It.IsAny<string?>(),
                It.IsAny<GutAI.Domain.Enums.FoodRegion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft.Items);
        var fake = new ScriptedToolChatClient("propose_meal", new Dictionary<string, object?>
        {
            ["meal_type"] = "Lunch",
            ["items"] = new[] { new Dictionary<string, object?> { ["name"] = "Rice", ["servings"] = 1m } }
        });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var store = CreateStore(userId, region: FoodRegion.Au);
        var service = CreateService(userId, client, store, drafts, resolver);

        await Drain(service.StreamResponseAsync(userId, "Log rice for lunch."));

        drafts.Verify(d => d.CreateAsync(userId,
            It.Is<MealDraftCreateRequest>(request => request.Origin == MealDraftOrigins.Coach
                && request.MealType == "Lunch" && request.Items.Single().Calories == 120),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("120", fake.LastToolResult, StringComparison.Ordinal);
        Assert.Contains("LATER message", fake.LastToolResult, StringComparison.Ordinal);
        resolver.Verify(service => service.ResolveAsync(
            It.IsAny<IReadOnlyList<AgentMealItemInput>>(), It.IsAny<string?>(), FoodRegion.Au,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProposeMeal_RejectsInvalidMealTypeWithoutResolvingOrCreatingDraft()
    {
        var userId = Guid.NewGuid();
        var drafts = new Mock<IMealDraftService>();
        var resolver = new Mock<IAgentMealItemResolver>();
        var fake = new ScriptedToolChatClient("propose_meal", new Dictionary<string, object?>
        {
            ["meal_type"] = "Meal",
            ["items"] = new[] { new Dictionary<string, object?> { ["name"] = "Rice", ["servings"] = 1m } }
        });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var service = CreateService(userId, client, CreateStore(userId), drafts, resolver);

        await Drain(service.StreamResponseAsync(userId, "Log rice for lunch."));

        Assert.Contains($"meal_type must be {GutAI.Application.Common.Helpers.MealValidation.MealTypeNames}", fake.LastToolResult, StringComparison.Ordinal);
        Assert.Contains("Nothing was saved", fake.LastToolResult, StringComparison.Ordinal);
        drafts.Verify(d => d.CreateAsync(It.IsAny<Guid>(), It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        resolver.Verify(r => r.ResolveAsync(
            It.IsAny<IReadOnlyList<AgentMealItemInput>>(), It.IsAny<string?>(),
            It.IsAny<FoodRegion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProposeMeal_CanonicalizesCaseInsensitiveMealTypeBeforeCreatingDraft()
    {
        var userId = Guid.NewGuid();
        var draft = MakeDraft();
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(service => service.CreateAsync(userId, It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var resolver = new Mock<IAgentMealItemResolver>();
        resolver.Setup(service => service.ResolveAsync(
                It.IsAny<IReadOnlyList<AgentMealItemInput>>(), It.IsAny<string?>(),
                It.IsAny<FoodRegion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft.Items);
        var fake = new ScriptedToolChatClient("propose_meal", new Dictionary<string, object?>
        {
            ["meal_type"] = "lunch",
            ["items"] = new[] { new Dictionary<string, object?> { ["name"] = "Rice", ["servings"] = 1m } }
        });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var service = CreateService(userId, client, CreateStore(userId), drafts, resolver);

        await Drain(service.StreamResponseAsync(userId, "Log rice for lunch."));

        drafts.Verify(d => d.CreateAsync(userId,
            It.Is<MealDraftCreateRequest>(request => request.MealType == "Lunch"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task SuggestMealsTool_IsRegisteredOnlyWhenEnabledAndAvailable(bool enabled, bool registered, bool expected)
    {
        var userId = Guid.NewGuid();
        var client = new RecordingChatClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:MealSuggestions"] = enabled.ToString()
        }).Build();
        var suggestions = registered ? new Mock<IMealSuggestionService>().Object : null;
        var service = CreateService(userId, client, CreateStore(userId), config: config, suggestions: suggestions);

        await Drain(service.StreamResponseAsync(userId, "Any meal ideas?"));

        Assert.Equal(expected, client.ToolNames.Contains("suggest_meals"));
    }

    [Fact]
    public async Task SuggestMealsTool_ReturnsGroundedDraftAndBudgetShape()
    {
        var userId = Guid.NewGuid();
        var draft = MakeDraft();
        var result = new MealSuggestionResultDto
        {
            Budget = new NutritionBudgetDto
            {
                Date = new DateOnly(2026, 9, 25),
                Goals = new NutritionTargetsDto { Calories = 2000 },
                Consumed = new NutritionTargetsDto { Calories = 1100 },
                Remaining = new NutritionTargetsDto { Calories = 900 },
                MealType = "Lunch",
                MealTarget = new NutritionTargetsDto { Calories = 450 }
            },
            Suggestions = [new MealSuggestionDto { Title = "Rice bowl", Rationale = "Fits your remaining budget.", Draft = draft }],
            PromptVersion = "test",
            RejectedCount = 2
        };
        var suggestions = new Mock<IMealSuggestionService>();
        suggestions.Setup(s => s.SuggestAsync(userId, It.IsAny<MealSuggestionRequest>(), "UTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        var fake = new ScriptedToolChatClient("suggest_meals", new Dictionary<string, object?>
        {
            ["meal_type"] = "Lunch",
            ["preferences"] = "vegetarian"
        });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:MealSuggestions"] = "true"
        }).Build();
        var service = CreateService(userId, client, CreateStore(userId), config: config, suggestions: suggestions.Object);

        await Drain(service.StreamResponseAsync(userId, "Suggest lunch.", timezoneId: "UTC"));

        using var doc = JsonDocument.Parse(fake.LastToolResult);
        var root = doc.RootElement;
        var card = Assert.Single(root.GetProperty("suggestions").EnumerateArray());
        Assert.Equal(draft.DraftId, card.GetProperty("draft_id").GetGuid());
        Assert.Equal("Rice bowl", card.GetProperty("title").GetString());
        Assert.Equal("Fits your remaining budget.", card.GetProperty("rationale").GetString());
        Assert.Equal("Lunch", card.GetProperty("meal_type").GetString());
        Assert.Equal(120, card.GetProperty("calories").GetDecimal());
        Assert.Equal(2.4m, card.GetProperty("protein_g").GetDecimal());
        Assert.Equal(27m, card.GetProperty("carbs_g").GetDecimal());
        Assert.Equal(0.3m, card.GetProperty("fat_g").GetDecimal());
        Assert.Equal(new[] { "Rice" }, card.GetProperty("items").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(900, root.GetProperty("budget").GetProperty("remaining_calories").GetDecimal());
        Assert.Equal(450, root.GetProperty("budget").GetProperty("meal_target_calories").GetDecimal());
        Assert.Equal(2, root.GetProperty("rejected_count").GetInt32());
    }

    [Fact]
    public async Task SearchWebNutrition_UsesUsersPreferredRegion()
    {
        var userId = Guid.NewGuid();
        var lookup = new Mock<IWebNutritionLookup>();
        lookup.Setup(service => service.LookupAsync("sushi", FoodRegion.Au, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebNutritionResult
            {
                CaloriesKcal = 150,
                ProteinG = 5,
                CarbsG = 20,
                FatG = 4,
                SourceName = "Nutrition source",
                SourceUrl = "https://example.test/sushi"
            });
        var fake = new ScriptedToolChatClient("search_web_nutrition", new Dictionary<string, object?> { ["query"] = "sushi" });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var service = CreateService(userId, client, CreateStore(userId, region: FoodRegion.Au), webLookup: lookup.Object);

        await Drain(service.StreamResponseAsync(userId, "Look up sushi nutrition."));

        lookup.Verify(service => service.LookupAsync("sushi", FoodRegion.Au, It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task CommitMeal_PassesCoachOriginAndTurnStartGuard()
    {
        var userId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 9, 24, 8, 30, 0, TimeSpan.Zero);
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(service => service.CommitAsync(
                userId, It.IsAny<Guid>(), null, It.IsAny<MealDraftCommitGuard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MealDraftCommitResult { MealId = Guid.NewGuid(), TotalCalories = 120 });
        var fake = new ScriptedToolChatClient("commit_meal",
            new Dictionary<string, object?> { ["draft_id"] = Guid.NewGuid().ToString() });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var store = CreateStore(userId);
        var service = CreateService(userId, client, store, drafts, time: new FixedTimeProvider(start));

        await Drain(service.StreamResponseAsync(userId, "Yes, confirm."));

        drafts.Verify(d => d.CommitAsync(userId, It.IsAny<Guid>(), null,
            It.Is<MealDraftCommitGuard>(guard => guard.RequiredOrigin == MealDraftOrigins.Coach && guard.CreatedBefore == start),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitMeal_SameTurnErrorIsReturnedAsExplanatoryToolText()
    {
        var userId = Guid.NewGuid();
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(service => service.CommitAsync(
                userId, It.IsAny<Guid>(), null, It.IsAny<MealDraftCommitGuard>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MealDraftException(MealDraftErrorCode.SameTurnCommit, "guarded"));
        var fake = new ScriptedToolChatClient("commit_meal",
            new Dictionary<string, object?> { ["draft_id"] = Guid.NewGuid().ToString() });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var service = CreateService(userId, client, CreateStore(userId), drafts);

        await Drain(service.StreamResponseAsync(userId, "Confirm that."));

        Assert.Contains("The user has not confirmed yet", fake.LastToolResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchFoods_LimitsResultsAndPersistsIdlessProducts()
    {
        var userId = Guid.NewGuid();
        var foods = Enumerable.Range(1, 7).Select(index => new FoodProductDto
        {
            Name = $"Food {index}",
            DataSource = "USDA",
            Calories100g = 100,
            MatchConfidence = 0.9m
        }).ToList();
        var search = new Mock<IFoodSearchService>();
        search.Setup(service => service.SearchAsync("rice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(foods);
        var store = CreateStore(userId);
        var fake = new ScriptedToolChatClient("search_foods", new Dictionary<string, object?> { ["query"] = "rice" });
        using var client = new ChatClientBuilder(fake).UseFunctionInvocation().Build();
        var service = CreateService(userId, client, store, foodSearch: search);

        await Drain(service.StreamResponseAsync(userId, "Find rice."));

        using var result = JsonDocument.Parse(fake.LastToolResult);
        Assert.Equal(5, result.RootElement.GetProperty("results").GetArrayLength());
        Assert.All(result.RootElement.GetProperty("results").EnumerateArray(),
            item => Assert.NotEqual(Guid.Empty, item.GetProperty("id").GetGuid()));
        store.Verify(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task SessionState_IsInjectedAndDeletedWhenHistoryIsCleared()
    {
        var userId = Guid.NewGuid();
        var state = new CoachSessionState
        {
            ResolvedFoods = [new CoachResolvedFood("Rice", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                new NutritionPer100gDto { CaloriesKcal = 130 }, 0.91m)],
            OpenDraftIds = [Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
        };
        var store = CreateStore(userId, state);
        var fake = new ScriptedToolChatClient(null, null);
        using var client = new ChatClientBuilder(fake).Build();
        var service = CreateService(userId, client, store);

        await Drain(service.StreamResponseAsync(userId, "What was that food?"));
        var block = Assert.Single(fake.LastMessages, message => message.Text.Contains("<session_state>", StringComparison.Ordinal));
        Assert.Contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", block.Text, StringComparison.Ordinal);
        Assert.Contains("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", block.Text, StringComparison.Ordinal);

        await service.ClearHistoryAsync(userId);
        store.Verify(s => s.DeleteCoachSessionStateAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static MealDraftDto MakeDraft()
    {
        var item = new MealDraftItemDto
        {
            ItemId = Guid.NewGuid(),
            Name = "Rice",
            Source = "usda",
            Grams = 100,
            MatchConfidence = 0.95m,
            Calories = 120,
            ProteinG = 2.4m,
            CarbsG = 27m,
            FatG = 0.3m
        };
        return new MealDraftDto
        {
            DraftId = Guid.NewGuid(),
            Origin = MealDraftOrigins.Coach,
            Status = MealDraftStatuses.PendingReview,
            MealType = "Lunch",
            Items = [item],
            Warnings = [],
            ReferenceObjectVisible = false,
            OverallConfidence = 0.9m,
            Totals = new MealDraftTotalsDto { Calories = 120, ProteinG = 2.4m, CarbsG = 27m, FatG = 0.3m },
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    private static CoachChatService CreateService(
        Guid userId,
        IChatClient client,
        Mock<ITableStore> store,
        Mock<IMealDraftService>? drafts = null,
        Mock<IAgentMealItemResolver>? resolver = null,
        Mock<IFoodSearchService>? foodSearch = null,
        TimeProvider? time = null,
        IConfiguration? config = null,
        IWebNutritionLookup? webLookup = null,
        IMealSuggestionService? suggestions = null,
        Mock<INutritionBudgetService>? nutritionBudget = null)
    {
        var fodmap = new FodmapService();
        var gutRisk = new GutRiskService();
        return new CoachChatService(client, store.Object,
            new Mock<ICorrelationEngine>().Object,
            new Mock<IFoodDiaryAnalysisService>().Object,
            (foodSearch ?? new Mock<IFoodSearchService>()).Object,
            new Mock<INutritionApiService>().Object, fodmap, gutRisk,
            new PersonalizedScoringService(gutRisk, fodmap),
            (drafts ?? new Mock<IMealDraftService>()).Object,
            (resolver ?? new Mock<IAgentMealItemResolver>()).Object,
            (nutritionBudget ?? CreateBudgetMock()).Object,
            config ?? new ConfigurationBuilder().Build(), time ?? TimeProvider.System,
            NullLogger<CoachChatService>.Instance,
            webLookup: webLookup,
            suggestions: suggestions);
    }

    private static Mock<INutritionBudgetService> CreateBudgetMock()
    {
        var budget = new NutritionBudgetDto
        {
            Date = new DateOnly(2026, 9, 25),
            Goals = new NutritionTargetsDto { Calories = 2000, ProteinG = 50, CarbsG = 250, FatG = 65, FiberG = 25 },
            Consumed = new NutritionTargetsDto { Calories = 1422, ProteinG = 97, CarbsG = 134, FatG = 60, FiberG = 8 },
            Remaining = new NutritionTargetsDto { Calories = 578, ProteinG = 0, CarbsG = 116, FatG = 5, FiberG = 17 },
            MealCount = 1
        };
        var service = new Mock<INutritionBudgetService>();
        service.Setup(s => s.GetBudgetAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(budget);
        return service;
    }

    private static Mock<ITableStore> CreateStore(Guid userId, CoachSessionState? state = null, FoodRegion region = FoodRegion.Default)
    {
        var store = new Mock<ITableStore>();
        store.Setup(s => s.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, Email = "coach@example.com", TimezoneId = "UTC", PreferredFoodRegion = region });
        store.Setup(s => s.GetCoachSessionStateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        store.Setup(s => s.GetRecentCoachMessagesAsync(userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.GetMealLogsByDateRangeAsync(userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.GetMealItemsAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.UpsertCoachMessageAsync(userId, It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.UpsertCoachSessionStateAsync(userId, It.IsAny<CoachSessionState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.DeleteCoachMessagesAsync(userId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.DeleteCoachSessionStateAsync(userId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.GetMealLogAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MealLog?)null);
        store.Setup(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return store;
    }

    private static async Task Drain(IAsyncEnumerable<ChatStreamEvent> events)
    {
        await foreach (var _ in events) { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ScriptedToolChatClient(string? toolName, Dictionary<string, object?>? arguments) : IChatClient
    {
        private bool _sentCall;
        public string LastToolResult { get; private set; } = "";
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            var functionResult = LastMessages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().LastOrDefault();
            if (functionResult is not null)
            {
                LastToolResult = functionResult.Result?.ToString() ?? "";
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
                yield break;
            }
            if (!_sentCall && toolName is not null)
            {
                _sentCall = true;
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", toolName, arguments!)]);
                yield break;
            }
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];
        public IReadOnlyList<string> ToolNames { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ToolNames = options?.Tools?.Select(tool => tool.Name).Where(name => name is not null).Select(name => name!).ToArray() ?? [];
            LastMessages = messages.ToList();
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "I see your logged meal.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
