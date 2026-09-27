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
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class ScanUsageMeteringTests
{
    [Fact]
    public void BeginScope_RestoresPreviousMeterWhenDisposed()
    {
        var outer = Meter();
        var inner = Meter();
        using var outerScope = outer.BeginScope();
        Assert.Same(outer, AiUsageMeter.Current);
        using (inner.BeginScope())
            Assert.Same(inner, AiUsageMeter.Current);
        Assert.Same(outer, AiUsageMeter.Current);
        outerScope.Dispose();
        Assert.Null(AiUsageMeter.Current);
    }

    [Fact]
    public async Task WebExtraction_RecordsUsageOnlyInsideActiveScope()
    {
        var config = Config(("Features:WebGrounding", "true"), ("AzureOpenAI:Workloads:extraction:Deployment", "extract-deployment"));
        var store = new Mock<ITableStore>();
        store.Setup(x => x.GetWebNutritionCacheEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebNutritionCacheEntry?)null);
        store.Setup(x => x.UpsertWebNutritionCacheAsync(It.IsAny<WebNutritionResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var client = new UsageChatClient("""
            {"found":true,"calories_kcal":130,"protein_g":2.7,"carbs_g":28,"fat_g":0.3,"source_name":"USDA"}
            """);
        var cascade = new StubCascade(client, store.Object, config);
        var meter = new AiUsageMeter(config, NullLogger<AiUsageMeter>.Instance);

        await cascade.LookupAsync("rice", FoodRegion.Default);
        Assert.Empty(meter.Summarize().Stages);

        using (meter.BeginScope())
            await cascade.LookupAsync("beans", FoodRegion.Default);

        var stage = Assert.Single(meter.Summarize().Stages);
        Assert.Equal("web_extraction", stage.Stage);
        Assert.Equal("extract-deployment", stage.Deployment);
        Assert.Equal(17, stage.InputTokens);
        Assert.Equal(9, stage.OutputTokens);
    }

    [Fact]
    public async Task AgentReview_RecordsSelectionModelResponseIntoPassedMeter()
    {
        var config = Config(("MealScan:EnableAgentGroundingReview", "true"));
        var client = new UsageChatClient("""
            {"abstain":true,"candidate_index":null,"confidence":0.1,"reason":"uncertain","inspection_id":0}
            """);
        var search = new Mock<IFoodSearchService>();
        var grounding = new ComponentGroundingEngine(search.Object);
        var review = new MealScanAgentReviewService(client, client, grounding, config,
            NullLogger<MealScanAgentReviewService>.Instance);
        var stage = new MealScanCandidateSelectionStage(client, review, config,
            NullLogger<MealScanCandidateSelectionStage>.Instance);
        var meter = new AiUsageMeter(config, NullLogger<AiUsageMeter>.Instance);

        await stage.SelectAsync([Item("rice")], [1], "image/jpeg", meter, CancellationToken.None);

        var agentCall = Assert.Single(meter.Summarize().Stages, x => x.Stage == "agent_review");
        Assert.Equal(17, agentCall.InputTokens);
        Assert.Equal(9, agentCall.OutputTokens);
    }

    [Fact]
    public async Task Selection_BatchesMoreThanFourAmbiguousComponents()
    {
        var client = new UsageChatClient("{\"choices\":[]}");
        var config = Config(("MealScan:EnableAgentGroundingReview", "false"));
        var search = new Mock<IFoodSearchService>();
        var review = new MealScanAgentReviewService(client, client, new ComponentGroundingEngine(search.Object), config,
            NullLogger<MealScanAgentReviewService>.Instance);
        var stage = new MealScanCandidateSelectionStage(client, review, config,
            NullLogger<MealScanCandidateSelectionStage>.Instance);
        var items = Enumerable.Range(1, 7).Select(i => Item($"component-{i}")).ToArray();

        await stage.SelectAsync(items, [1], "image/jpeg", Meter(), CancellationToken.None);

        Assert.Equal(1, client.Calls);
        var prompt = string.Join(" ", client.Messages.Single().SelectMany(x => x.Contents).OfType<TextContent>().Select(x => x.Text));
        foreach (var item in items)
            Assert.Contains(item.Original.Name, prompt);
    }

    private static AiUsageMeter Meter() => new(Config(), NullLogger<AiUsageMeter>.Instance);

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();

    private static GroundedItem Item(string name) => new(
        new ScannedComponent { Name = name, EstimatedGramsLow = 80, EstimatedGramsMidpoint = 100, EstimatedGramsHigh = 120 },
        null,
        new GroundingAttemptDto { Query = name, ResolutionStatus = "ambiguous", AutoSelected = false, MatchConfidence = 0.6m, Method = "test" },
        [Product($"generic {name}"), Product($"specific {name}")]);

    private static FoodProductDto Product(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DataSource = "USDA",
        Calories100g = 130m,
        Protein100g = 3m,
        Carbs100g = 25m,
        Fat100g = 1m,
        MatchConfidence = 0.9m,
    };

    private sealed class StubCascade(IChatClient client, ITableStore store, IConfiguration config)
        : WebNutritionCascade(client, store, config, new HttpClient(), NullLogger<WebNutritionCascade>.Instance)
    {
        internal override Task<List<(string Title, string Url)>> SearchDuckDuckGo(string query, CancellationToken ct)
            => Task.FromResult(new List<(string, string)> { ("nutrition", "https://fdc.nal.usda.gov/rice") });

        internal override Task<string?> FetchViaJina(string url, CancellationToken ct)
            => Task.FromResult<string?>("Nutrition facts per 100g");
    }

    private sealed class UsageChatClient(string json) : IChatClient
    {
        public int Calls { get; private set; }
        public List<List<ChatMessage>> Messages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Messages.Add(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                Usage = new UsageDetails { InputTokenCount = 17, OutputTokenCount = 9 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
