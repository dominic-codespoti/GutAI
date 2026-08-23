using System.Text;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// AI meal photo scan pipeline.
/// Stage A (IMealVisionStage): vision decomposition via typed structured output +
/// deterministic semantic validation, with a single corrective retry on unusable
/// output. Stage B/C currently stubbed to ai-source items (P3/P5).
/// Every scan persists a PendingReview session; nothing is logged without user
/// confirmation through the confirm endpoint.
/// </summary>
public sealed class MealScanService : IMealScanService, IMealVisionStage
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Version tag for the Stage-A prompt + schema contract. Bump whenever the prompt,
    /// schema or model deployment changes — the golden-image gate keys its cache and
    /// regression reports on this value. Do NOT edit an existing version in place.
    /// </summary>
    public const string VisionPromptVersion = "2026-08-23.v1";

    private const string VisionSystemPrompt = """
        You are a food identification assistant. Analyze the meal photo and list every
        distinct food component visible.

        Rules:
        - Separate components (rice, chicken, salad dressing) — never merge into one dish
          unless truly inseparable (e.g. casserole).
        - Estimate grams per component using visual references (plate ≈26cm, cutlery,
          hands) when present; describe them in scale_notes. Without references, widen the
          low/high range and lower confidence.
        - Account for cooking method in preparation_note (oil absorbed, breading, sauces).
        - estimated_grams_midpoint must lie within [estimated_grams_low, estimated_grams_high].
        - confidence reflects BOTH identity certainty AND portion certainty.
        - Never output calories or nutrition values — component identity and portion only.
        """;

    private readonly IChatClient _chatClient;
    private readonly ITableStore _store;
    private readonly IConfiguration _config;
    private readonly ComponentGroundingEngine _grounding;
    private readonly IWebNutritionLookup _webLookup;
    private readonly IFodmapService fodmapService;
    private readonly IGutRiskService gutRiskService;
    private readonly ILogger<MealScanService> _logger;

    public MealScanService(
        IChatClient chatClient,
        ITableStore store,
        IConfiguration config,
        IFoodSearchService foodSearch,
        IWebNutritionLookup webLookup,
        IFodmapService fodmapService,
        IGutRiskService gutRiskService,
        ILogger<MealScanService> logger)
    {
        _chatClient = chatClient;
        _store = store;
        _config = config;
        _grounding = new ComponentGroundingEngine(foodSearch);
        _webLookup = webLookup;
        this.fodmapService = fodmapService;
        this.gutRiskService = gutRiskService;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────
    // Stage A — vision decomposition
    // ──────────────────────────────────────────────────────────────

    public async Task<VisionDecomposition> DecomposeAsync(Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var maxComponents = _config.GetValue("MealScan:MaxComponentsPerPhoto", 12);
        using var memory = new MemoryStream();
        await imageStream.CopyToAsync(memory, ct);
        var imageData = BinaryData.FromBytes(memory.ToArray(), contentType == "image/png" ? "image/png" : "image/jpeg");

        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.System, VisionSystemPrompt),
            new(ChatRole.User,
            [
                new TextContent("Identify all distinct food components in this meal photo."),
                new DataContent(imageData.ToArray(), imageData.MediaType),
            ]),
        };

        // One corrective retry: structured output can still fail (empty result,
        // unparseable, all-invalid). Attempt 2 tells the model what went wrong.
        string? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            List<ChatMessage> messages = requestMessages;
            if (attempt == 2 && lastError is not null)
            {
                messages = [.. requestMessages];
                messages.Add(new ChatMessage(ChatRole.User,
                    $"Your previous response could not be used: {lastError}. Respond again following the schema exactly."));
            }

            int? inputTokens = null, outputTokens = null;
            MealVisionResult? vision;
            try
            {
                var response = await _chatClient.GetResponseAsync<MealVisionResult>(
                    messages, options: null, useJsonSchemaResponseFormat: true, cancellationToken: ct);
                vision = response.Result;
                inputTokens = (int?)response.Usage?.InputTokenCount;
                outputTokens = (int?)response.Usage?.OutputTokenCount;
            }
            catch (Exception ex) when (ex is not MealScanValidationException)
            {
                lastError = "response was not parseable";
                _logger.LogWarning(ex, "Stage A attempt {Attempt} failed to parse.", attempt);
                continue;
            }

            if (vision is null)
            {
                lastError = "result was empty";
                continue;
            }

            try
            {
                var validated = MealVisionValidator.Validate(vision, maxComponents);
                LogUsage(inputTokens, outputTokens, validated.Components.Count, attempt);

                return new VisionDecomposition(
                    validated.Components,
                    validated.ReferenceObjectVisible,
                    validated.ScaleNotes,
                    validated.OverallConfidence,
                    validated.DroppedNotes,
                    JsonSerializer.Serialize(vision, JsonOpts),
                    VisionPromptVersion,
                    inputTokens,
                    outputTokens);
            }
            catch (MealScanValidationException ex)
            {
                lastError = ex.Message;
                _logger.LogWarning("Stage A attempt {Attempt} failed validation: {Reason}", attempt, ex.Message);
            }
        }

        throw new MealScanValidationException("Could not analyze that photo. Try a clearer shot of the meal.");
    }

    private void LogUsage(int? inTok, int? outTok, int components, int attempt)
        => _logger.LogInformation(
            "Stage A ok (attempt {Attempt}): {Components} components, tokens in={In}/out={Out}, prompt={PromptVersion}",
            attempt, components, inTok, outTok, VisionPromptVersion);

    // ──────────────────────────────────────────────────────────────
    // Full pipeline (Stage B/C stubbed until P3/P5)
    // ──────────────────────────────────────────────────────────────

    public async Task<MealScanDraftDto> ScanMealImageAsync(Guid userId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var deployment = _config["AzureOpenAI:VisionDeployment"] ?? _config["AzureOpenAI:DeploymentName"] ?? "unknown";

        var decomposition = await DecomposeAsync(imageStream, contentType, ct);
        var maxComponents = _config.GetValue("MealScan:MaxComponentsPerPhoto", 12);

        // ── Stage B: ground each component through the shared food resolver ──
        var groundedItems = new List<MealScanItemDto>();
        var needsDisambiguation = 0;
        foreach (var component in decomposition.Components)
        {
            var grounded = await _grounding.GroundAsync(component, ct);
            groundedItems.Add(grounded.ToItem());
            if (!grounded.Attempt.AutoSelected) needsDisambiguation++;
        }

        // ── P5: FODMAP + gut-risk signals for grounded items (fail-soft) ──
        await MealScanHealthSignals.EnrichAllAsync(
            groundedItems.Where(i => i.FoodProductId is not null),
            _store, fodmapService, gutRiskService, ct);

        // ── Stage B3: free web cascade for items the DB couldn't ground (flag-gated) ──
        var maxWebQueries = _config.GetValue("MealScan:MaxWebQueriesPerScan", 2);
        var webUsed = 0;
        foreach (var item in groundedItems.Where(i => i.Source == "ai"))
        {
            if (webUsed >= maxWebQueries) break;
            WebNutritionResult? web = null;
            try { web = await _webLookup.LookupAsync(item.Name, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Web lookup failed for '{Item}'.", item.Name); }
            if (web is null) continue;

            webUsed++;
            var factor = item.Grams / 100m;
            groundedItems.Remove(item);
            groundedItems.Add(item with
            {
                CanonicalName = web.SourceName,
                SourceUrl = web.SourceUrl,
                Calories = decimal.Round(web.CaloriesKcal * factor),
                ProteinG = decimal.Round(web.ProteinG * factor, 1),
                CarbsG = decimal.Round(web.CarbsG * factor, 1),
                FatG = decimal.Round(web.FatG * factor, 1),
                FiberG = web.FiberG is null ? null : decimal.Round(web.FiberG.Value * factor, 1),
                SugarG = web.SugarG is null ? null : decimal.Round(web.SugarG.Value * factor, 1),
                SodiumMg = web.SodiumMg is null ? null : decimal.Round(web.SodiumMg.Value * factor),
                MatchConfidence = 0.6m,
                Grounding = new GroundingAttemptDto
                {
                    Query = item.Name,
                    ResolutionStatus = "resolved_web",
                    AutoSelected = false,          // still shown with a review chip in the UI
                    SelectedFoodProductId = null,
                    CanonicalName = web.SourceName,
                    Candidates = [new GroundingCandidateDto(web.SourceName, null, "web", 0.6m)],
                    MatchConfidence = 0.6m,
                    Method = "web_cascade",
                },
            });
        }

        var warnings = new List<string>(decomposition.DroppedNotes);
        if (!decomposition.ReferenceObjectVisible)
            warnings.Add("No reference object visible — portions are rough estimates.");
        if (needsDisambiguation > 0)
            warnings.Add($"{needsDisambiguation} item(s) need a quick check — confirm the right match before saving.");

        var draft = new MealScanDraftDto
        {
            ScanSessionId = Guid.NewGuid(),
            Items = groundedItems,
            Warnings = warnings,
            ReferenceObjectVisible = decomposition.ReferenceObjectVisible,
            OverallConfidence = decomposition.OverallConfidence,
        };

        await _store.UpsertScanSessionAsync(new ScanSessionRecord
        {
            Id = draft.ScanSessionId,
            UserId = userId,
            Status = "PendingReview",
            RawVisionJson = JsonSerializer.Serialize(decomposition, JsonOpts),
            DraftItemsJson = JsonSerializer.Serialize(groundedItems, JsonOpts),
            Warnings = warnings,
            ReferenceObjectVisible = decomposition.ReferenceObjectVisible,
            OverallConfidence = decomposition.OverallConfidence,
            ModelDeployment = $"{deployment}/{VisionPromptVersion}",
        }, ct);

        _logger.LogInformation(
            "Meal scan {SessionId} for user {UserId}: {Components} components, {AutoSelected} auto-grounded, {NeedsReview} need review, overallConf={Conf:F2}",
            draft.ScanSessionId, userId, groundedItems.Count, groundedItems.Count - needsDisambiguation, needsDisambiguation, decomposition.OverallConfidence);

        return draft;
    }

    public async Task<MealScanDraftDto?> GetDraftAsync(Guid userId, Guid scanSessionId, CancellationToken ct = default)
    {
        var session = await _store.GetScanSessionAsync(userId, scanSessionId, ct);
        if (session is null || session.Status != "PendingReview") return null;

        var items = JsonSerializer.Deserialize<List<MealScanItemDto>>(session.DraftItemsJson, JsonOpts) ?? [];
        return new MealScanDraftDto
        {
            ScanSessionId = session.Id,
            Items = items,
            Warnings = session.Warnings,
            ReferenceObjectVisible = session.ReferenceObjectVisible,
            OverallConfidence = session.OverallConfidence,
        };
    }

    public Task DiscardAsync(Guid userId, Guid scanSessionId, CancellationToken ct = default)
        => _store.DeleteScanSessionAsync(userId, scanSessionId, ct);
}
