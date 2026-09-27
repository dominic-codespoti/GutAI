using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class MealScanAgentReviewServiceTests
{
    [Fact]
    public async Task ReviewAsync_ExecutesGroundingInspectionToolAndSelectsReturnedCandidate()
    {
        var fakeInner = new ToolLoopFakeChatClient();
        using var selectionClient = new ChatClientBuilder(fakeInner)
            .UseFunctionInvocation()
            .Build();
        using var visionClient = new ChatClientBuilder(new VisionReanalysisFakeChatClient())
            .Build();

        var search = new Mock<GutAI.Application.Common.Interfaces.IFoodSearchService>();
        var grounding = new ComponentGroundingEngine(search.Object);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MealScan:AgentMaxReanalysisEffort"] = "high",
                ["MealScan:MinCandidateSelectionConfidence"] = "0.85",
            })
            .Build();
        var service = new MealScanAgentReviewService(
            visionClient,
            selectionClient,
            grounding,
            config,
            NullLogger<MealScanAgentReviewService>.Instance);

        var first = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "generic grilled fish",
            DataSource = "USDA",
            MatchConfidence = 0.91m,
        };
        var second = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "packaged fish snack",
            DataSource = "OpenFoodFacts",
            MatchConfidence = 0.88m,
            Brand = "Example",
        };
        var grounded = new GroundedItem(
            new ScannedComponent
            {
                Name = "fish fillet",
                EstimatedGramsLow = 100,
                EstimatedGramsMidpoint = 150,
                EstimatedGramsHigh = 220,
                Confidence = 0.7m,
                PortionConfidence = 0.7m,
                PreparationNote = "grilled",
            },
            null,
            new GroundingAttemptDto
            {
                Query = "fish fillet",
                ResolutionStatus = "ambiguous",
                AutoSelected = false,
                MatchConfidence = 0.62m,
                Method = "resolve_async",
                Candidates = [],
            },
            [first, second]);

        var result = await service.ReviewAsync(grounded, [1, 2, 3], "image/jpeg", CancellationToken.None);

        Assert.Same(first, result.ResolvedProduct);
        Assert.Equal("agent_selected", result.Attempt.ResolutionStatus);
        Assert.Equal("agent_tool_review", result.Attempt.Method);
        Assert.Equal(1, fakeInner.InspectionToolCalls);
    }

    [Fact]
    public async Task ReviewAsync_AllowsOneBoundedReanalysisToolCall()
    {
        var fakeInner = new ReanalysisToolFakeChatClient();
        var visionFake = new VisionReanalysisFakeChatClient();
        using var selectionClient = new ChatClientBuilder(fakeInner)
            .UseFunctionInvocation()
            .Build();
        using var visionClient = new ChatClientBuilder(visionFake).Build();

        var search = new Mock<GutAI.Application.Common.Interfaces.IFoodSearchService>();
        search
            .Setup(f => f.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto());
        var grounding = new ComponentGroundingEngine(search.Object);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MealScan:AgentMaxReanalysisEffort"] = "high",
            })
            .Build();
        var service = new MealScanAgentReviewService(
            visionClient,
            selectionClient,
            grounding,
            config,
            NullLogger<MealScanAgentReviewService>.Instance);

        var grounded = new GroundedItem(
            new ScannedComponent
            {
                Name = "fish fillet",
                EstimatedGramsLow = 100,
                EstimatedGramsMidpoint = 150,
                EstimatedGramsHigh = 220,
                Confidence = 0.7m,
                PortionConfidence = 0.7m,
            },
            null,
            new GroundingAttemptDto
            {
                Query = "fish fillet",
                ResolutionStatus = "ambiguous",
                AutoSelected = false,
                MatchConfidence = 0.62m,
                Method = "resolve_async",
                Candidates = [],
            },
            [
                new FoodProductDto { Id = Guid.NewGuid(), Name = "candidate one", DataSource = "USDA", MatchConfidence = 0.9m },
                new FoodProductDto { Id = Guid.NewGuid(), Name = "candidate two", DataSource = "USDA", MatchConfidence = 0.88m },
            ]);

        var result = await service.ReviewAsync(grounded, [1, 2, 3], "image/jpeg", CancellationToken.None);

        Assert.Null(result.ResolvedProduct);
        Assert.Equal("medium", fakeInner.RequestedEffort);
        Assert.Equal(1, visionFake.ReanalysisInvocations);
        Assert.True(fakeInner.AgentInvocations >= 3);
    }

    [Fact]
    public async Task ReviewAsync_AcceptedReanalysisKeepsStageAComponentAndGrams()
    {
        var result = await ReviewReanalysisWithProposalAsync(candidateIndex: 0, requireCompatibilityAgreement: false);

        Assert.Equal("fish fillet", result.Original.Name);
        Assert.Equal(100m, result.Original.EstimatedGramsLow);
        Assert.Equal(150m, result.Original.EstimatedGramsMidpoint);
        Assert.Equal(220m, result.Original.EstimatedGramsHigh);
        Assert.Equal(0.7m, result.Original.Confidence);
        Assert.Equal(0.7m, result.Original.PortionConfidence);
        Assert.Equal("grilled", result.Original.PreparationNote);
        Assert.Equal("fillet", result.Original.ServingHintUnit);
        Assert.Equal(150m, result.Original.ServingHintUnitGrams);
        Assert.Equal("agent_tool_review_reanalysis", result.Attempt.Method);
        Assert.NotNull(result.ResolvedProduct);
    }

    [Fact]
    public async Task ReviewAsync_CompatibilityAgreementRejectsNonTopCandidate()
    {
        var result = await ReviewReanalysisWithProposalAsync(candidateIndex: 1, requireCompatibilityAgreement: true);

        Assert.Null(result.ResolvedProduct);
    }

    [Fact]
    public async Task ReviewAsync_CompatibilityAgreementAcceptsTopCandidate()
    {
        var result = await ReviewReanalysisWithProposalAsync(candidateIndex: 0, requireCompatibilityAgreement: true);

        Assert.NotNull(result.ResolvedProduct);
        Assert.Equal("agent_tool_review_reanalysis", result.Attempt.Method);
    }

    [Fact]
    public async Task ReviewAsync_CompatibilityAgreementDisabledAllowsNonTopCandidate()
    {
        var result = await ReviewReanalysisWithProposalAsync(candidateIndex: 1, requireCompatibilityAgreement: false);

        Assert.NotNull(result.ResolvedProduct);
        Assert.Equal("agent_tool_review_reanalysis", result.Attempt.Method);
    }

    private static async Task<GroundedItem> ReviewReanalysisWithProposalAsync(
        int candidateIndex,
        bool requireCompatibilityAgreement)
    {
        var selectionFake = new ReanalysisToolFakeChatClient(candidateIndex, abstain: false);
        using var selectionClient = new ChatClientBuilder(selectionFake).UseFunctionInvocation().Build();
        using var visionClient = new ChatClientBuilder(new VisionReanalysisFakeChatClient()).Build();
        var first = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "grilled fish fillet",
            DataSource = "USDA",
            MatchConfidence = 0.95m,
        };
        var second = new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = "grilled fish portion",
            DataSource = "USDA",
            MatchConfidence = 0.94m,
        };
        var search = new Mock<GutAI.Application.Common.Interfaces.IFoodSearchService>();
        search
            .Setup(f => f.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodResolutionDto
            {
                MatchConfidence = 0.9m,
                Alternatives = [first, second],
            });
        var grounding = new ComponentGroundingEngine(search.Object);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MealScan:AgentMaxReanalysisEffort"] = "high",
                ["MealScan:RequireCompatibilityAgreement"] = requireCompatibilityAgreement.ToString(),
            })
            .Build();
        var service = new MealScanAgentReviewService(
            visionClient,
            selectionClient,
            grounding,
            config,
            NullLogger<MealScanAgentReviewService>.Instance);
        var grounded = new GroundedItem(
            new ScannedComponent
            {
                Name = "fish fillet",
                EstimatedGramsLow = 100,
                EstimatedGramsMidpoint = 150,
                EstimatedGramsHigh = 220,
                Confidence = 0.7m,
                PortionConfidence = 0.7m,
                PreparationNote = "grilled",
                ServingHintUnit = "fillet",
                ServingHintUnitGrams = 150,
            },
            null,
            new GroundingAttemptDto
            {
                Query = "fish fillet",
                ResolutionStatus = "ambiguous",
                AutoSelected = false,
                MatchConfidence = 0.62m,
                Method = "resolve_async",
                Candidates = [],
            },
            [first, second]);

        return await service.ReviewAsync(grounded, [1, 2, 3], "image/jpeg", CancellationToken.None);
    }


    [Fact]
    public void DecisionGate_RejectsCandidateWithoutIdentityOverlap()
    {
        var snapshot = MakeSnapshot(
            observedName: "fish fillet",
            preparation: "grilled",
            queries: ["grilled fish fillet"],
            candidates:
            [
                new FoodProductDto { Id = Guid.NewGuid(), Name = "chocolate cake", Brand = "Bakery", DataSource = "USDA", MatchConfidence = 0.91m },
            ]);

        var rejection = MealScanAgentDecisionGate.GetRejection(
            snapshot,
            candidateIndex: 0,
            confidence: 0.95m,
            observedSearchQueries: snapshot.Original.SearchQueries,
            minimumConfidence: 0.90m,
            inspectionId: 0,
            preReanalysisMatchConfidence: snapshot.Attempt.MatchConfidence,
            minimumReanalysisImprovement: 0.05m);

        Assert.NotNull(rejection);
        Assert.Contains("identity", rejection);
    }

    [Fact]
    public void DecisionGate_RequiresPostReanalysisConfidenceImprovement()
    {
        var snapshot = MakeSnapshot(
            observedName: "grilled tomato",
            preparation: "charred",
            queries: ["grilled tomato"],
            candidates:
            [
                new FoodProductDto { Id = Guid.NewGuid(), Name = "Roasted Tomatoes", DataSource = "USDA", MatchConfidence = 0.70m },
            ]);

        var rejection = MealScanAgentDecisionGate.GetRejection(
            snapshot,
            candidateIndex: 0,
            confidence: 0.95m,
            observedSearchQueries: snapshot.Original.SearchQueries,
            minimumConfidence: 0.90m,
            inspectionId: 1,
            preReanalysisMatchConfidence: 0.80m,
            minimumReanalysisImprovement: 0.05m);

        Assert.NotNull(rejection);
        Assert.Contains("did not improve", rejection);
    }

    private static GroundedItem MakeSnapshot(
        string observedName,
        string preparation,
        string[] queries,
        IReadOnlyList<FoodProductDto> candidates)
        => new(
            new ScannedComponent
            {
                Name = observedName,
                EstimatedGramsLow = 100,
                EstimatedGramsMidpoint = 150,
                EstimatedGramsHigh = 220,
                Confidence = 0.7m,
                PortionConfidence = 0.7m,
                PreparationNote = preparation,
                SearchQueries = [.. queries],
            },
            null,
            new GroundingAttemptDto
            {
                Query = observedName,
                ResolutionStatus = "ambiguous",
                AutoSelected = false,
                MatchConfidence = 0.62m,
                Method = "resolve_async",
                Candidates = [],
            },
            candidates);

    private sealed class ToolLoopFakeChatClient : IChatClient
    {
        public int InspectionToolCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var messageList = messages.ToList();
            if (messageList.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any())
            {
                return Task.FromResult(new ChatResponse(
                    new ChatMessage(ChatRole.Assistant,
                        "{\"inspection_id\":0,\"candidate_index\":0,\"confidence\":0.95,\"reason\":\"The generic candidate matches the visible grilled fish and the packaged candidate does not.\",\"abstain\":false}")));
            }

            var tool = options?.Tools?.OfType<AIFunction>().SingleOrDefault(t => t.Name == "inspect_meal_grounding");
            Assert.NotNull(tool);
            InspectionToolCalls++;

            return Task.FromResult(new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent(
                        "call-1",
                        "inspect_meal_grounding",
                        new Dictionary<string, object?> { ["inspection_id"] = 0 })])));
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

        public void Dispose()
        {
        }
    }
    private sealed class ReanalysisToolFakeChatClient(int? candidateIndex = null, bool abstain = true) : IChatClient
    {
        public int AgentInvocations { get; private set; }
        public string? RequestedEffort { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            AgentInvocations++;
            var functionResults = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Count();

            if (functionResults == 0)
            {
                RequestedEffort = "medium";
                return Task.FromResult(new ChatResponse(
                    new ChatMessage(
                        ChatRole.Assistant,
                        [new FunctionCallContent(
                            "call-reanalyze",
                            "reanalyze_meal_component",
                            new Dictionary<string, object?> { ["effort"] = "medium" })])));
            }

            if (functionResults == 1)
            {
                return Task.FromResult(new ChatResponse(
                    new ChatMessage(
                        ChatRole.Assistant,
                        [new FunctionCallContent(
                            "call-inspect",
                            "inspect_meal_grounding",
                            new Dictionary<string, object?> { ["inspection_id"] = 1 })])));
            }

            var decision = abstain
                ? "{\"inspection_id\":1,\"candidate_index\":null,\"confidence\":0.4,\"reason\":\"No grounded candidate was returned after reanalysis.\",\"abstain\":true}"
                : $"{{\"inspection_id\":1,\"candidate_index\":{candidateIndex},\"confidence\":0.95,\"reason\":\"The inspected candidate matches the component.\",\"abstain\":false}}";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, decision)));
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

        public void Dispose()
        {
        }
    }

    private sealed class VisionReanalysisFakeChatClient : IChatClient
    {
        public int ReanalysisInvocations { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReanalysisInvocations++;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    "{\"name\":\"grilled fish fillet\",\"estimated_grams_low\":400,\"estimated_grams_midpoint\":500,\"estimated_grams_high\":600,\"confidence\":0.9,\"portion_confidence\":0.8,\"is_garnish\":false,\"preparation_note\":\"grilled\",\"search_queries\":[\"grilled fish fillet\"]}")));
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

        public void Dispose()
        {
        }
    }
}
