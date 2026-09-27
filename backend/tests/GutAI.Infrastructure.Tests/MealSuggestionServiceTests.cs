using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class MealSuggestionServiceTests
{
    [Fact]
    public async Task SuggestAsync_FiltersUnsafeAndExcludedFrequentFoodsAndPersistsServerComputedSuggestion()
    {
        var userId = Guid.NewGuid();
        var rice = Product("Rice", 91m);
        var onion = Product("Onion", 40m);
        var peanut = Product("Peanut", 500m);
        var chat = new FakeChatClient("""{"suggestions":[{"title":"Rice 200 calories","items":[{"pool_index":0,"grams":80}],"rationale":"Provides 91 kcal"},{"title":"Unsafe","items":[{"pool_index":1,"grams":100}],"rationale":"bad"},{"title":"Allergen","items":[{"pool_index":2,"grams":100}],"rationale":"bad"}]}""");
        var drafts = new Mock<IMealDraftService>();
        MealDraftCreateRequest? captured = null;
        drafts.Setup(d => d.CreateAsync(userId, It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, MealDraftCreateRequest request, CancellationToken _) =>
            {
                captured = request;
                var computed = request.Items.Select(i => NutritionCalculator.Compute(i.Per100g!, i.Grams)).ToList();
                var returnedItems = request.Items.Select(i =>
                {
                    var n = NutritionCalculator.Compute(i.Per100g!, i.Grams);
                    i.Calories = n.Calories; i.ProteinG = n.ProteinG; i.CarbsG = n.CarbsG; i.FatG = n.FatG;
                    return i;
                }).ToList();
                return Task.FromResult(new MealDraftDto
                {
                    DraftId = Guid.NewGuid(),
                    Origin = request.Origin,
                    Status = MealDraftStatuses.PendingReview,
                    MealType = request.MealType,
                    Items = returnedItems,
                    Warnings = [],
                    ReferenceObjectVisible = false,
                    OverallConfidence = 1m,
                    Totals = new MealDraftTotalsDto { Calories = computed.Sum(x => x.Calories), ProteinG = computed.Sum(x => x.ProteinG), CarbsG = computed.Sum(x => x.CarbsG), FatG = computed.Sum(x => x.FatG) },
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                });
            });
        var store = new Mock<ITableStore>();
        store.Setup(s => s.GetUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = userId, Allergies = ["peanut"] });
        var meal = new MealLog { Id = Guid.NewGuid(), UserId = userId, LoggedAt = DateTime.UtcNow };
        store.Setup(s => s.GetMealLogsByDateRangeAsync(userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([meal]);
        store.Setup(s => s.GetMealItemsAsync(userId, meal.Id, It.IsAny<CancellationToken>())).ReturnsAsync([
            new MealItem { FoodProductId = rice.Id, FoodName = rice.Name }, new MealItem { FoodProductId = onion.Id, FoodName = onion.Name }, new MealItem { FoodProductId = peanut.Id, FoodName = peanut.Name }]);
        store.Setup(s => s.GetFoodProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) => new[] { rice, onion, peanut }.Single(p => p.Id == id));
        store.Setup(s => s.SearchFoodProductsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var diary = new Mock<IFoodDiaryAnalysisService>();
        diary.Setup(s => s.GetEliminationStatusAsync(userId, store.Object, null)).ReturnsAsync(new EliminationDietStatusDto());
        var correlations = EmptyCorrelations(userId);
        var fodmap = new Mock<IFodmapService>();
        fodmap.Setup(f => f.Assess(It.IsAny<FoodProductDto>())).Returns((FoodProductDto p) => new FodmapAssessmentDto { Status = p.Name == "Onion" ? nameof(FodmapAssessmentStatus.PotentialTriggersDetected) : nameof(FodmapAssessmentStatus.NoKnownTriggersDetected) });
        var service = new MealSuggestionService(store.Object, diary.Object, correlations.Object, fodmap.Object, new SubstitutionService(), Budget(userId, 91m), drafts.Object, chat, Config(), NullLogger<MealSuggestionService>.Instance);

        var result = await service.SuggestAsync(userId, new MealSuggestionRequest { MealType = "dinner" });

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(2, result.RejectedCount);
        Assert.Equal(91m, suggestion.Draft.Totals.Calories);
        Assert.Equal(MealDraftOrigins.Suggestion, captured!.Origin);
        Assert.Equal("Dinner", captured.MealType);
        Assert.Equal("2026-09-25.v1-grounded-meal-suggestions", captured.PromptVersion);
        Assert.Equal("Sourced", captured.Items[0].NutritionProvenance);
        Assert.Equal("suggestion", captured.Items[0].PortionMethod);
        Assert.True(captured.Items[0].IncludedByDefault);
        Assert.Equal(rice.Id, captured.Items[0].FoodProductId);
        Assert.DoesNotContain("200", suggestion.Title);
        Assert.DoesNotContain("91 kcal", suggestion.Rationale.ToLowerInvariant());
        Assert.False(chat.Messages[0][1].Text.Contains("peanut", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(suggestion.Draft.Items, i => i.FoodProductId == onion.Id || i.FoodProductId == peanut.Id);
    }

    [Fact]
    public async Task SuggestAsync_DropsInvalidPoolIndicesAndOutOfBoundsGrams()
    {
        var fixture = MakeFixture("""{"suggestions":[{"title":"bad index","items":[{"pool_index":99,"grams":100}],"rationale":"x"},{"title":"bad grams","items":[{"pool_index":0,"grams":900}],"rationale":"x"}]}""");
        var result = await fixture.Service.SuggestAsync(fixture.UserId, new MealSuggestionRequest { MealType = "Breakfast" });
        Assert.Empty(result.Suggestions);
        Assert.Equal(2, result.RejectedCount);
        fixture.Drafts.Verify(d => d.CreateAsync(It.IsAny<Guid>(), It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static FoodProduct Product(string name, decimal kcal) => new() { Id = Guid.NewGuid(), Name = name, DataSource = "USDA", Calories100g = kcal, Protein100g = 4, Carbs100g = 20, Fat100g = 2, ServingQuantity = 100, FoodKind = GutAI.Domain.Enums.FoodKind.WholeFood };
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MealSuggestions:KcalTolerance"] = "0.10" }).Build();
    private static INutritionBudgetService Budget(Guid id, decimal target)
    {
        var mock = new Mock<INutritionBudgetService>();
        foreach (var type in new[] { "Dinner", "Breakfast" })
            mock.Setup(b => b.GetBudgetAsync(id, type, null, It.IsAny<CancellationToken>())).ReturnsAsync(new NutritionBudgetDto { Date = DateOnly.FromDateTime(DateTime.UtcNow), Goals = new(), Consumed = new(), Remaining = new(), MealType = type, MealTarget = new NutritionTargetsDto { Calories = target } });
        return mock.Object;
    }
    private static Mock<ICorrelationEngine> EmptyCorrelations(Guid id)
    {
        var mock = new Mock<ICorrelationEngine>();
        mock.Setup(c => c.ComputeCorrelationsAsync(id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>(), null)).ReturnsAsync([]);
        return mock;
    }
    [Fact]
    public async Task SuggestAsync_EliminationSafePotentialTriggerNeverEntersSuggestionPool()
    {
        var fixture = MakeFixture("""{"suggestions":[{"title":"Unsafe","items":[{"pool_index":0,"grams":100}],"rationale":"x"}]}""", "Elimination-safe food", eliminationSafe: true);
        fixture.Fodmap.Setup(f => f.Assess(It.IsAny<FoodProductDto>()))
            .Returns(new FodmapAssessmentDto { Status = nameof(FodmapAssessmentStatus.PotentialTriggersDetected) });

        var result = await fixture.Service.SuggestAsync(fixture.UserId, new MealSuggestionRequest { MealType = "Dinner" });

        Assert.Empty(result.Suggestions);
        fixture.Drafts.Verify(d => d.CreateAsync(It.IsAny<Guid>(), It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuggestAsync_RejectsCorrelatedTriggerSubstitute()
    {
        var fixture = MakeFixture("""{"suggestions":[{"title":"Milk meal","items":[{"pool_index":0,"grams":100}],"rationale":"x"}]}""", "Milk", correlatedTrigger: "Oat");
        var oatMilk = Product("Oat milk", 100m);
        fixture.Store.Setup(s => s.SearchFoodProductsAsync("Oat milk", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([oatMilk]);
        var assessments = 0;
        fixture.Fodmap.Setup(f => f.Assess(It.IsAny<FoodProductDto>())).Returns((FoodProductDto _) =>
            new FodmapAssessmentDto
            {
                Status = Interlocked.Increment(ref assessments) == 2
                ? nameof(FodmapAssessmentStatus.PotentialTriggersDetected)
                : nameof(FodmapAssessmentStatus.NoKnownTriggersDetected)
            });

        var result = await fixture.Service.SuggestAsync(fixture.UserId, new MealSuggestionRequest { MealType = "Dinner" });

        Assert.Empty(result.Suggestions);
        fixture.Drafts.Verify(d => d.CreateAsync(It.IsAny<Guid>(), It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuggestAsync_ReportsDistinctValidationRatesForAcceptedRepairedAndRejectedProposals()
    {
        var json = """{"suggestions":[{"title":"accepted","items":[{"pool_index":0,"grams":100}],"rationale":"x"},{"title":"repaired","items":[{"pool_index":0,"grams":100}],"rationale":"x"},{"title":"over budget","items":[{"pool_index":1,"grams":100}],"rationale":"x"}]}""";
        // Three proposals are validated: accepted and repaired fit the budget; the resolved, safe high-calorie item remains valid but fails budget fit.
        var highCalorieFood = Product("Energy dense food", 500m);
        var fixture = MakeFixture(json, "Milk", eliminationSafe: true, additionalFood: highCalorieFood);
        var oatMilk = Product("Oat milk", 100m);
        fixture.Store.Setup(s => s.SearchFoodProductsAsync("Oat milk", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([oatMilk]);
        var assessments = 0;
        fixture.Fodmap.Setup(f => f.Assess(It.IsAny<FoodProductDto>())).Returns((FoodProductDto _) =>
            new FodmapAssessmentDto
            {
                Status = Interlocked.Increment(ref assessments) == 5
                ? nameof(FodmapAssessmentStatus.PotentialTriggersDetected)
                : nameof(FodmapAssessmentStatus.NoKnownTriggersDetected)
            });

        var result = await fixture.Service.SuggestAsync(fixture.UserId, new MealSuggestionRequest { MealType = "Dinner" });

        Assert.All(result.Suggestions.SelectMany(s => s.Draft.Items), item =>
            Assert.True(item.FoodProductId is { } id && (id == fixture.Food.Id || id == oatMilk.Id)));
        var metrics = fixture.Logger.Metrics;
        Assert.Equal(3, metrics.RootElement.GetProperty("proposals_validated").GetInt32());
        Assert.Equal(3, metrics.RootElement.GetProperty("valid").GetInt32());
        Assert.Equal(1, metrics.RootElement.GetProperty("repaired").GetInt32());
        Assert.Equal(2, metrics.RootElement.GetProperty("budget_fit").GetInt32());
        Assert.Equal(1m, metrics.RootElement.GetProperty("validity_rate").GetDecimal());
        Assert.Equal(2m / 3m, metrics.RootElement.GetProperty("budget_fit_rate").GetDecimal());
        Assert.Equal(0, metrics.RootElement.GetProperty("trigger_violations").GetInt32());
        Assert.Equal(0m, metrics.RootElement.GetProperty("trigger_violation_rate").GetDecimal());
        Assert.Equal(2, result.Suggestions.Count);
    }

    [Fact]
    public async Task SuggestAsync_RepairsUnsafeFrequentFoodWithScreenedSubstitute()
    {
        var fixture = MakeFixture("""{"suggestions":[{"title":"Milk meal","items":[{"pool_index":0,"grams":100}],"rationale":"A balanced option"}]}""", "Milk");
        var oatMilk = Product("Oat milk", 100m);
        fixture.Store.Setup(s => s.SearchFoodProductsAsync("Oat milk", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([oatMilk]);
        var assessments = 0;
        fixture.Fodmap.Setup(f => f.Assess(It.IsAny<FoodProductDto>())).Returns((FoodProductDto _) =>
            new FodmapAssessmentDto { Status = Interlocked.Increment(ref assessments) == 2 ? nameof(FodmapAssessmentStatus.PotentialTriggersDetected) : nameof(FodmapAssessmentStatus.NoKnownTriggersDetected) });

        var result = await fixture.Service.SuggestAsync(fixture.UserId, new MealSuggestionRequest { MealType = "Dinner" });

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(oatMilk.Id, Assert.Single(suggestion.Draft.Items).FoodProductId);
        fixture.Drafts.Verify(d => d.CreateAsync(fixture.UserId, It.Is<MealDraftCreateRequest>(r =>
            r.Origin == MealDraftOrigins.Suggestion && r.Items.Count == 1 && r.Items[0].FoodProductId == oatMilk.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (MealSuggestionService Service, Guid UserId, Mock<IMealDraftService> Drafts, Mock<ITableStore> Store, Mock<IFodmapService> Fodmap, FoodProduct Food, CapturingLogger<MealSuggestionService> Logger) MakeFixture(
        string json, string foodName = "Rice", FoodProduct? substitute = null, string? correlatedTrigger = null, bool eliminationSafe = false, FoodProduct? additionalFood = null)
    {
        var id = Guid.NewGuid(); var food = Product(foodName, 100m); var store = new Mock<ITableStore>();
        store.Setup(s => s.GetUserAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = id });
        var meal = new MealLog { Id = Guid.NewGuid(), UserId = id, LoggedAt = DateTime.UtcNow };
        store.Setup(s => s.GetMealLogsByDateRangeAsync(id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([meal]);
        var mealItems = new List<MealItem> { new() { FoodProductId = food.Id, FoodName = food.Name } };
        if (additionalFood is not null)
        {
            mealItems.Add(new MealItem { FoodProductId = food.Id, FoodName = food.Name });
            mealItems.Add(new MealItem { FoodProductId = additionalFood.Id, FoodName = additionalFood.Name });
        }
        store.Setup(s => s.GetMealItemsAsync(id, meal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mealItems);
        store.Setup(s => s.GetFoodProductAsync(food.Id, It.IsAny<CancellationToken>())).ReturnsAsync(food);
        if (additionalFood is not null)
            store.Setup(s => s.GetFoodProductAsync(additionalFood.Id, It.IsAny<CancellationToken>())).ReturnsAsync(additionalFood);
        store.Setup(s => s.SearchFoodProductsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, int _, CancellationToken _) => substitute is not null && query.Equals("Oat milk", StringComparison.OrdinalIgnoreCase) ? [substitute] : []);
        var diary = new Mock<IFoodDiaryAnalysisService>();
        diary.Setup(x => x.GetEliminationStatusAsync(id, store.Object, null)).ReturnsAsync(new EliminationDietStatusDto { SafeFoods = eliminationSafe ? [foodName] : [] });
        var fodmap = new Mock<IFodmapService>(); fodmap.Setup(x => x.Assess(It.IsAny<FoodProductDto>())).Returns(new FodmapAssessmentDto { Status = nameof(FodmapAssessmentStatus.NoKnownTriggersDetected) });
        var drafts = new Mock<IMealDraftService>();
        drafts.Setup(d => d.CreateAsync(id, It.IsAny<MealDraftCreateRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, MealDraftCreateRequest request, CancellationToken _) => Task.FromResult(MakeDraft(request)));
        var logger = new CapturingLogger<MealSuggestionService>();
        var service = new MealSuggestionService(store.Object, diary.Object, Correlations(id, correlatedTrigger).Object, fodmap.Object, new SubstitutionService(), Budget(id, 100m), drafts.Object, new FakeChatClient(json), Config(), logger);
        return (service, id, drafts, store, fodmap, food, logger);
    }
    private static Mock<ICorrelationEngine> Correlations(Guid id, string? trigger)
    {
        var mock = new Mock<ICorrelationEngine>();
        List<CorrelationDto> rows = trigger is null ? [] : [new CorrelationDto { FoodOrAdditive = trigger, Occurrences = 2, AverageSeverity = 4 }];
        mock.Setup(c => c.ComputeCorrelationsAsync(id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>(), null)).ReturnsAsync(rows);
        return mock;
    }
    private static MealDraftDto MakeDraft(MealDraftCreateRequest request)
    {
        var amounts = request.Items.Select(i => NutritionCalculator.Compute(i.Per100g!, i.Grams)).ToList();
        return new MealDraftDto
        {
            DraftId = Guid.NewGuid(),
            Origin = request.Origin,
            Status = MealDraftStatuses.PendingReview,
            MealType = request.MealType,
            Items = request.Items,
            Warnings = [],
            ReferenceObjectVisible = false,
            OverallConfidence = 1m,
            Totals = new MealDraftTotalsDto { Calories = amounts.Sum(a => a.Calories), ProteinG = amounts.Sum(a => a.ProteinG), CarbsG = amounts.Sum(a => a.CarbsG), FatG = amounts.Sum(a => a.FatG) },
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };
    }
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public JsonDocument Metrics { get; private set; } = JsonDocument.Parse("{}");
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values
                && values.FirstOrDefault(value => value.Key.TrimStart('@') == "Metrics").Value is { } metrics)
                Metrics = JsonDocument.Parse(JsonSerializer.Serialize(metrics));
        }
    }
    private sealed class FakeChatClient(string json) : IChatClient
    {
        public List<List<ChatMessage>> Messages { get; } = [];
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        { Messages.Add(messages.ToList()); return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))); }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
