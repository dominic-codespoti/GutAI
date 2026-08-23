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
/// P1 vertical slice of the meal-scan pipeline: Stage A (vision decomposition via
/// structured output + deterministic semantic validation) with Stage B/C stubbed to
/// ai-source draft items. DB grounding arrives in P3, the free web cascade in P4.
/// Every scan persists a PendingReview session; nothing is logged without user
/// confirmation through the confirm endpoint.
/// </summary>
public sealed class MealScanService : IMealScanService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

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
    private readonly ILogger<MealScanService> _logger;

    public MealScanService(
        IChatClient chatClient,
        ITableStore store,
        IConfiguration config,
        ILogger<MealScanService> logger)
    {
        _chatClient = chatClient;
        _store = store;
        _config = config;
        _logger = logger;
    }

    public async Task<MealScanDraftDto> ScanMealImageAsync(Guid userId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var deployment = _config["AzureOpenAI:VisionDeployment"] ?? _config["AzureOpenAI:DeploymentName"] ?? "unknown";
        var maxComponents = _config.GetValue("MealScan:MaxComponentsPerPhoto", 12);
        using var memory = new MemoryStream();
        await imageStream.CopyToAsync(memory, ct);

        // ── Stage A: vision decomposition (typed structured output) ──
        var imageBytes = BinaryData.FromBytes(memory.ToArray(), contentType == "image/png" ? "image/png" : "image/jpeg");
        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.System, VisionSystemPrompt),
            new(ChatRole.User,
            [
                new TextContent("Identify all distinct food components in this meal photo."),
                new DataContent(imageBytes.ToArray(), imageBytes.MediaType),
            ]),
        };

        MealVisionResult vision;
        try
        {
            var response = await _chatClient.GetResponseAsync<MealVisionResult>(requestMessages, options: null, useJsonSchemaResponseFormat: true, cancellationToken: ct);
            vision = response.Result ?? throw new MealScanValidationException("Vision stage returned an empty result.");
        }
        catch (MealScanValidationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage A failed for user {UserId}: {Message}", userId, ex.Message);
            throw new MealScanValidationException("Could not analyze that photo. Try a clearer shot of the meal.");
        }

        // Deterministic semantic validation (schema shape ≠ semantic validity).
        var validated = MealVisionValidator.Validate(vision, maxComponents);

        // ── Stage B/C (P3/P5): for now, ai-source items from validated components ──
        var items = validated.Components.Select(c => new MealScanItemDto
        {
            ItemId = Guid.NewGuid(),
            Name = c.Name,
            Source = "ai",
            Grams = c.EstimatedGramsMidpoint,
            MatchConfidence = 1m,
            VisionConfidence = c.Confidence,
        }).ToList();

        var warnings = new List<string>(validated.DroppedNotes);
        if (!validated.ReferenceObjectVisible)
            warnings.Add("No reference object visible — portions are rough estimates.");
        if (vision.Components.Count > maxComponents)
            warnings.Add($"Only the first {maxComponents} detected components were kept.");

        var draft = new MealScanDraftDto
        {
            ScanSessionId = Guid.NewGuid(),
            Items = items,
            Warnings = warnings,
            ReferenceObjectVisible = validated.ReferenceObjectVisible,
            OverallConfidence = validated.OverallConfidence,
        };

        await _store.UpsertScanSessionAsync(new ScanSessionRecord
        {
            Id = draft.ScanSessionId,
            UserId = userId,
            Status = "PendingReview",
            RawVisionJson = JsonSerializer.Serialize(vision, JsonOpts),
            DraftItemsJson = JsonSerializer.Serialize(items, JsonOpts),
            Warnings = warnings,
            ReferenceObjectVisible = validated.ReferenceObjectVisible,
            OverallConfidence = validated.OverallConfidence,
            ModelDeployment = deployment,
        }, ct);

        _logger.LogInformation(
            "Meal scan {SessionId} for user {UserId}: {Components} components, overallConf={Conf:F2}, refVisible={Ref}, warnings={Warnings}",
            draft.ScanSessionId, userId, items.Count, validated.OverallConfidence, validated.ReferenceObjectVisible, warnings.Count);

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
