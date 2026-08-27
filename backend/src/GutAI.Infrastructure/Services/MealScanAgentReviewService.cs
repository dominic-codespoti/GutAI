using System.Text.Json.Serialization;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Bounded Agent Framework review loop for ambiguous Stage-B grounding.
/// The agent can inspect only server-owned grounding snapshots and request one
/// server-controlled reanalysis. It cannot search arbitrary providers, mutate data,
/// select an unreturned candidate, or choose an unapproved effort level.
/// </summary>
internal sealed class MealScanAgentReviewService
{
    private const int MaxInspectionCalls = 2;
    private const int MaxReanalysisCalls = 1;
    private static readonly ChatRole DeveloperRole = new("developer");

    private const string ReviewInstructions = """
        You are reviewing one ambiguous food component from a meal photo.

        Required process:
        1. Call inspect_meal_grounding with inspection_id=0 before deciding.
        2. Compare the visible component in the image against ONLY the returned candidates.
        3. If the candidates are poor or the observed identity is unsupported, you may call
           reanalyze_meal_component at most once. Use the lowest effort likely to resolve the
           ambiguity. The server will reject efforts above its configured safety cap.
        4. If reanalysis returns a new inspection_id, inspect that snapshot before deciding.
        5. Return one structured decision. candidate_index must reference the selected inspection
           snapshot, or null when the evidence is insufficient.

        Never invent a candidate, product, brand, species, nutrition value, or inspection ID.
        Prefer a generic candidate over unsupported specificity. Abstention is correct when
        candidates remain visually indistinguishable. Do not explain hidden reasoning.
        """;

    private const string ReanalysisInstructions = """
        Reinspect the requested meal component in the supplied photo.
        Return exactly one component using the schema. Focus on visible identity, preparation,
        and portion range. Do not produce nutrition values. Do not add components that are not
        the requested component. Preserve uncertainty instead of inventing a species, brand,
        or ingredient.
        """;

    private readonly IChatClient _chatClient;
    private readonly ComponentGroundingEngine _grounding;
    private readonly IConfiguration _config;
    private readonly ILogger _logger;
    private readonly string _maxReanalysisEffort;

    public MealScanAgentReviewService(
        IChatClient chatClient,
        ComponentGroundingEngine grounding,
        IConfiguration config,
        ILogger logger)
    {
        _chatClient = chatClient;
        _grounding = grounding;
        _config = config;
        _logger = logger;

        var configuredCap = config["MealScan:AgentMaxReanalysisEffort"];
        _maxReanalysisEffort = MealScanReasoningOptions.TryNormalize(configuredCap, out var normalizedCap)
            ? normalizedCap
            : "high";
    }

    public async Task<GroundedItem> ReviewAsync(
        GroundedItem grounded,
        byte[] imageBytes,
        string contentType,
        CancellationToken ct)
    {
        var inspections = new List<GroundedItem> { grounded };
        var inspectionCalls = 0;
        var reanalysisCalls = 0;

        async Task<GroundingInspectionView> InspectAsync(int? inspectionId, CancellationToken toolCt)
        {
            if (++inspectionCalls > MaxInspectionCalls)
            {
                return GroundingInspectionView.Failure("inspection call limit reached");
            }

            var index = inspectionId ?? inspections.Count - 1;
            if (index < 0 || index >= inspections.Count)
            {
                return GroundingInspectionView.Failure("inspection_id is not available");
            }

            var snapshot = inspections[index];
            return ToInspectionView(index, snapshot, reanalysisCalls < MaxReanalysisCalls);
        }

        async Task<ReanalysisToolResult> ReanalyzeAsync(string effort, CancellationToken toolCt)
        {
            if (reanalysisCalls >= MaxReanalysisCalls)
            {
                return ReanalysisToolResult.Failure("reanalysis call limit reached");
            }

            if (!MealScanReasoningOptions.TryNormalize(effort, out var normalizedEffort))
            {
                return ReanalysisToolResult.Failure("unsupported effort; use none, low, medium, high, xhigh, or max");
            }

            if (MealScanReasoningOptions.Rank(normalizedEffort) > MealScanReasoningOptions.Rank(_maxReanalysisEffort))
            {
                return ReanalysisToolResult.Failure($"effort exceeds configured cap ({_maxReanalysisEffort})");
            }

            reanalysisCalls++;
            try
            {
                var mediaType = contentType == "image/png" ? "image/png" : "image/jpeg";
                var messages = new List<ChatMessage>
                {
                    new(DeveloperRole, ReanalysisInstructions),
                    new(ChatRole.User,
                    [
                        new TextContent($"Reinspect this component: {grounded.Original.Name}\nPreparation note: {grounded.Original.PreparationNote}"),
                        new DataContent(imageBytes, mediaType),
                    ]),
                };

                var response = await _chatClient.GetResponseAsync<ScannedComponent>(
                    messages,
                    options: MealScanReasoningOptions.Create(normalizedEffort),
                    useJsonSchemaResponseFormat: true,
                    cancellationToken: toolCt);

                if (response.Result is not { } component)
                    return ReanalysisToolResult.Failure("reanalysis returned no component");

                var validated = MealVisionValidator.Validate(
                    new MealVisionResult
                    {
                        Components = [component],
                        OverallConfidence = component.Confidence,
                    },
                    maxComponents: 1).Components.SingleOrDefault();

                if (validated is null)
                    return ReanalysisToolResult.Failure("reanalysis failed semantic validation");

                var reanalyzedGrounding = await _grounding.GroundAsync(validated, toolCt);
                var newInspectionId = inspections.Count;
                inspections.Add(reanalyzedGrounding);

                return new ReanalysisToolResult
                {
                    Accepted = true,
                    InspectionId = newInspectionId,
                    Name = validated.Name,
                    EstimatedGramsMidpoint = validated.EstimatedGramsMidpoint,
                    Confidence = validated.Confidence,
                    PortionConfidence = validated.PortionConfidence,
                    PreparationNote = validated.PreparationNote,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent reanalysis failed for '{Component}'.", grounded.Original.Name);
                return ReanalysisToolResult.Failure("reanalysis failed");
            }
        }

        try
        {
            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(
                    (int? inspectionId, CancellationToken toolCt) => InspectAsync(inspectionId, toolCt),
                    name: "inspect_meal_grounding",
                    description: "Inspect one server-owned grounding snapshot and its returned database candidates. Pass inspection_id=0 first, then use an inspection_id returned by reanalysis."),
                AIFunctionFactory.Create(
                    (string effort, CancellationToken toolCt) => ReanalyzeAsync(effort, toolCt),
                    name: "reanalyze_meal_component",
                    description: $"Reinspect the current component once with a different reasoning effort. Allowed up to one call; server cap is {_maxReanalysisEffort}."),
            };

            var agent = new ChatClientAgent(
                _chatClient,
                new ChatClientAgentOptions
                {
                    UseProvidedChatClientAsIs = true,
                    ChatOptions = new ChatOptions { Tools = tools },
                });

            var mediaType = contentType == "image/png" ? "image/png" : "image/jpeg";
            var messages = new List<ChatMessage>
            {
                new(DeveloperRole, ReviewInstructions),
                new(ChatRole.User,
                [
                    new TextContent($"Review the ambiguous component '{grounded.Original.Name}'. Its preparation note is: {grounded.Original.PreparationNote}"),
                    new DataContent(imageBytes, mediaType),
                ]),
            };

            var response = await agent.RunAsync<MealScanAgentDecision>(messages, cancellationToken: ct);
            var decision = response.Result;
            var callSummary = $"inspections={inspectionCalls}, reanalyses={reanalysisCalls}";

            if (decision is null)
            {
                _logger.LogInformation(
                    "Agent grounding verdict for '{Component}': no structured decision ({Calls}).",
                    grounded.Original.Name, callSummary);
                return grounded;
            }

            if (decision.Abstain || decision.CandidateIndex is not { } candidateIndex)
            {
                _logger.LogInformation(
                    "Agent grounding verdict for '{Component}': abstain, confidence={Confidence:F2}, reason='{Reason}' ({Calls}).",
                    grounded.Original.Name, decision.Confidence, decision.Reason, callSummary);
                return grounded;
            }

            if (decision.InspectionId < 0 || decision.InspectionId >= inspections.Count)
            {
                _logger.LogInformation(
                    "Agent grounding verdict for '{Component}': rejected, invalid inspection_id={InspectionId} ({Calls}).",
                    grounded.Original.Name, decision.InspectionId, callSummary);
                return grounded;
            }

            var selectedGrounding = inspections[decision.InspectionId];
            var minConfidence = _config.GetValue("MealScan:AgentMinSelectionConfidence", 0.90m);
            var selectedIndex = MealScanCandidateSelector.SelectIndex(
                new MealScanCandidateChoice
                {
                    CandidateIndex = candidateIndex,
                    Confidence = decision.Confidence,
                    Reason = decision.Reason,
                },
                selectedGrounding.CandidateProducts.Count,
                minConfidence);

            if (selectedIndex is not { } index)
            {
                _logger.LogInformation(
                    "Agent grounding verdict for '{Component}': rejected, candidate/confidence below selector floor ({Calls}).",
                    grounded.Original.Name, callSummary);
                return grounded;
            }

            var rejection = MealScanAgentDecisionGate.GetRejection(
                selectedGrounding,
                index,
                decision.Confidence,
                grounded.Original.SearchQueries,
                minConfidence,
                decision.InspectionId,
                grounded.Attempt.MatchConfidence,
                _config.GetValue("MealScan:AgentReanalysisMinImprovement", 0.05m));

            if (rejection is not null)
            {
                _logger.LogInformation(
                    "Agent grounding verdict for '{Component}': rejected, {Rejection}; proposed='{Candidate}', confidence={Confidence:F2} ({Calls}).",
                    grounded.Original.Name,
                    rejection,
                    selectedGrounding.CandidateProducts[index].Name,
                    decision.Confidence,
                    callSummary);
                return grounded;
            }

            var selected = selectedGrounding.CandidateProducts[index];
            var attempt = selectedGrounding.Attempt with
            {
                ResolutionStatus = "agent_selected",
                AutoSelected = true,
                SelectedFoodProductId = selected.Id,
                CanonicalName = selected.Name,
                MatchConfidence = selected.MatchConfidence,
                Method = reanalysisCalls > 0 ? "agent_tool_review_reanalysis" : "agent_tool_review",
            };

            _logger.LogInformation(
                "Agent grounding verdict for '{Component}': accepted '{Candidate}', confidence={Confidence:F2}, reason='{Reason}' ({Calls}).",
                grounded.Original.Name,
                selected.Name,
                decision.Confidence,
                decision.Reason,
                callSummary);

            return selectedGrounding with
            {
                ResolvedProduct = selected,
                Attempt = attempt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent grounding review failed for '{Component}'.", grounded.Original.Name);
            return grounded;
        }
    }

    private static GroundingInspectionView ToInspectionView(
        int inspectionId,
        GroundedItem grounded,
        bool canReanalyze)
        => new()
        {
            InspectionId = inspectionId,
            ObservedName = grounded.Original.Name,
            PreparationNote = grounded.Original.PreparationNote,
            ResolutionStatus = grounded.Attempt.ResolutionStatus,
            MatchConfidence = grounded.Attempt.MatchConfidence,
            AutoSelected = grounded.Attempt.AutoSelected,
            Candidates = grounded.CandidateProducts
                .Select((product, index) => new GroundingCandidateView
                {
                    CandidateIndex = index,
                    Name = product.Name,
                    Source = product.DataSource,
                    MatchConfidence = product.MatchConfidence,
                    Brand = product.Brand,
                    ExternalId = product.ExternalId,
                    SourceUrl = product.SourceUrl,
                })
                .ToList(),
            CanReanalyze = canReanalyze,
            ReanalysisRemaining = canReanalyze ? 1 : 0,
        };

    private sealed class MealScanAgentDecision
    {
        [JsonPropertyName("inspection_id")]
        public int InspectionId { get; set; }

        [JsonPropertyName("candidate_index")]
        public int? CandidateIndex { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";

        [JsonPropertyName("abstain")]
        public bool Abstain { get; set; }
    }

    private sealed class GroundingInspectionView
    {
        [JsonPropertyName("inspection_id")]
        public int InspectionId { get; init; }

        [JsonPropertyName("observed_name")]
        public string ObservedName { get; init; } = "";

        [JsonPropertyName("preparation_note")]
        public string PreparationNote { get; init; } = "";

        [JsonPropertyName("resolution_status")]
        public string ResolutionStatus { get; init; } = "";

        [JsonPropertyName("match_confidence")]
        public decimal MatchConfidence { get; init; }

        [JsonPropertyName("auto_selected")]
        public bool AutoSelected { get; init; }

        [JsonPropertyName("candidates")]
        public IReadOnlyList<GroundingCandidateView> Candidates { get; init; } = [];

        [JsonPropertyName("can_reanalyze")]
        public bool CanReanalyze { get; init; }

        [JsonPropertyName("reanalysis_remaining")]
        public int ReanalysisRemaining { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        public static GroundingInspectionView Failure(string error) => new() { Error = error };
    }

    private sealed class GroundingCandidateView
    {
        [JsonPropertyName("candidate_index")]
        public int CandidateIndex { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("source")]
        public string Source { get; init; } = "";

        [JsonPropertyName("match_confidence")]
        public decimal MatchConfidence { get; init; }

        [JsonPropertyName("brand")]
        public string? Brand { get; init; }

        [JsonPropertyName("external_id")]
        public string? ExternalId { get; init; }

        [JsonPropertyName("source_url")]
        public string? SourceUrl { get; init; }
    }

    private sealed class ReanalysisToolResult
    {
        [JsonPropertyName("accepted")]
        public bool Accepted { get; init; }

        [JsonPropertyName("inspection_id")]
        public int InspectionId { get; init; } = -1;

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("estimated_grams_midpoint")]
        public decimal? EstimatedGramsMidpoint { get; init; }

        [JsonPropertyName("confidence")]
        public decimal? Confidence { get; init; }

        [JsonPropertyName("portion_confidence")]
        public decimal? PortionConfidence { get; init; }

        [JsonPropertyName("preparation_note")]
        public string? PreparationNote { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        public static ReanalysisToolResult Failure(string error) => new() { Error = error };
    }
}
