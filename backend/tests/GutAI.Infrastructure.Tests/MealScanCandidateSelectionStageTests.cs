using System.Runtime.CompilerServices;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class MealScanCandidateSelectionStageTests
{
    [Fact]
    public async Task SelectAsync_DoesNotRunAgentReviewWhenDisabled()
    {
        var chat = new FakeChatClient("{\"choices\":[]}");
        var item = MakeItem("rice");

        var result = await CreateStage(chat, new Dictionary<string, string?>
        {
            ["MealScan:EnableAgentGroundingReview"] = "false",
        }).SelectAsync([item], [1], "image/jpeg", Meter(), CancellationToken.None);

        Assert.Equal(1, chat.Calls);
        Assert.Same(item, result[0]);
    }
    [Fact]
    public async Task SelectAsync_BatchesAmbiguousItemsAndSendsImageOnce()
    {
        var chat = new FakeChatClient("""
            {"choices":[
              {"component_index":0,"candidate_index":1,"confidence":0.96,"reason":"matches"},
              {"component_index":1,"candidate_index":0,"confidence":0.96,"reason":"matches"},
              {"component_index":2,"candidate_index":1,"confidence":0.96,"reason":"matches"}]}
            """);
        var items = new[] { MakeItem("rice"), MakeItem("fish"), MakeItem("vegetables") };
        var stage = CreateStage(chat, new Dictionary<string, string?>
        {
            ["MealScan:EnableAgentGroundingReview"] = "false",
        });

        var result = await stage.SelectAsync(items, [1, 2, 3], "image/jpeg", Meter(), CancellationToken.None);

        Assert.Equal(1, chat.Calls);
        Assert.Single(chat.Messages);
        var contents = chat.Messages[0].SelectMany(message => message.Contents).ToArray();
        Assert.Single(contents.OfType<DataContent>());
        Assert.Equal(3, result.Count);
        Assert.Same(items[0].CandidateProducts[1], result[0].ResolvedProduct);
        Assert.Same(items[1].CandidateProducts[0], result[1].ResolvedProduct);
        Assert.Same(items[2].CandidateProducts[1], result[2].ResolvedProduct);
        Assert.All(result, item => Assert.Equal("vision_selected", item.Attempt.ResolutionStatus));
    }

    [Fact]
    public async Task SelectAsync_AbstainsOnLowConfidenceAndOutOfRangeIndex()
    {
        var chat = new FakeChatClient("""
            {"choices":[
              {"component_index":0,"candidate_index":0,"confidence":0.84,"reason":"weak"},
              {"component_index":1,"candidate_index":2,"confidence":0.99,"reason":"invalid"}]}
            """);
        var items = new[] { MakeItem("rice"), MakeItem("fish") };
        var result = await CreateStage(chat).SelectAsync(items, [4], "image/png", Meter(), CancellationToken.None);

        Assert.Equal(1, chat.Calls);
        Assert.Null(result[0].ResolvedProduct);
        Assert.Null(result[1].ResolvedProduct);
    }

    [Fact]
    public async Task SelectAsync_CompatibilityAgreementRequiresFirstRankedCandidate()
    {
        var rejected = await CreateStage(new FakeChatClient(
            "{\"choices\":[{\"component_index\":0,\"candidate_index\":1,\"confidence\":0.99}] }"),
            new Dictionary<string, string?> { ["MealScan:RequireCompatibilityAgreement"] = "true" })
            .SelectAsync([MakeItem("rice")], [1], "image/jpeg", Meter(), CancellationToken.None);
        var accepted = await CreateStage(new FakeChatClient(
            "{\"choices\":[{\"component_index\":0,\"candidate_index\":0,\"confidence\":0.99}] }"),
            new Dictionary<string, string?> { ["MealScan:RequireCompatibilityAgreement"] = "true" })
            .SelectAsync([MakeItem("rice")], [1], "image/jpeg", Meter(), CancellationToken.None);

        Assert.Null(rejected[0].ResolvedProduct);
        Assert.NotNull(accepted[0].ResolvedProduct);
    }

    [Fact]
    public async Task SelectAsync_RejectsImplausibleNutrition()
    {
        var item = MakeItem("rice") with
        {
            CandidateProducts = [
                Product("implausible energy", 1550m, 10m, 60m, 12m),
                Product("plausible rice", 130m, 2.5m, 28m, 0.3m)],
        };
        var chat = new FakeChatClient("{\"choices\":[{\"component_index\":0,\"candidate_index\":0,\"confidence\":0.99}]}");

        var result = await CreateStage(chat).SelectAsync([item], [1], "image/jpeg", Meter(), CancellationToken.None);

        Assert.Null(result[0].ResolvedProduct);
    }

    [Fact]
    public async Task SelectAsync_NoEligibleItemsDoesNotCallModel()
    {
        var chat = new FakeChatClient("{\"choices\":[]}");
        var ineligible = MakeItem("rice") with { Attempt = MakeAttempt("exact", false) };

        var result = await CreateStage(chat).SelectAsync([ineligible], [1], "image/jpeg", Meter(), CancellationToken.None);

        Assert.Equal(0, chat.Calls);
        Assert.Same(ineligible, result[0]);
    }

    [Fact]
    public async Task SelectAsync_ModelExceptionLeavesItemsUnchanged()
    {
        var item = MakeItem("rice");
        var chat = new FakeChatClient(exception: new InvalidOperationException("model unavailable"));

        var result = await CreateStage(chat).SelectAsync([item], [1], "image/jpeg", Meter(), CancellationToken.None);

        Assert.Same(item, result[0]);
        Assert.Null(result[0].ResolvedProduct);
    }

    private static MealScanCandidateSelectionStage CreateStage(
        FakeChatClient chat,
        Dictionary<string, string?>? settings = null)
    {
        settings ??= [];
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var search = new Mock<IFoodSearchService>();
        var grounding = new ComponentGroundingEngine(search.Object);
        var review = new MealScanAgentReviewService(chat, chat, grounding, config,
            NullLogger<MealScanAgentReviewService>.Instance);
        return new MealScanCandidateSelectionStage(chat, review, config,
            NullLogger<MealScanCandidateSelectionStage>.Instance);
    }

    private static AiUsageMeter Meter() => new(new ConfigurationBuilder().Build(), NullLogger<AiUsageMeter>.Instance);

    private static GroundedItem MakeItem(string name) => new(
        new ScannedComponent
        {
            Name = name,
            PreparationNote = "cooked",
            EstimatedGramsLow = 80,
            EstimatedGramsMidpoint = 100,
            EstimatedGramsHigh = 120,
        },
        null,
        MakeAttempt("ambiguous", false),
        [Product($"generic {name}", 130m, 3m, 25m, 1m), Product($"specific {name}", 145m, 4m, 26m, 2m)]);

    private static GroundingAttemptDto MakeAttempt(string status, bool autoSelected) => new()
    {
        Query = "food",
        ResolutionStatus = status,
        AutoSelected = autoSelected,
        MatchConfidence = 0.6m,
        Method = "resolve_async",
        Candidates = [],
    };

    private static FoodProductDto Product(string name, decimal calories, decimal protein, decimal carbs, decimal fat) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DataSource = "USDA",
        Calories100g = calories,
        Protein100g = protein,
        Carbs100g = carbs,
        Fat100g = fat,
        MatchConfidence = 0.9m,
    };

    private sealed class FakeChatClient(string? json = null, Exception? exception = null) : IChatClient
    {
        public int Calls { get; private set; }
        public List<List<ChatMessage>> Messages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Messages.Add(messages.ToList());
            if (exception is not null)
                throw exception;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json ?? "{}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
