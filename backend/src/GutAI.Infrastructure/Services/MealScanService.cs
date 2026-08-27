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

    private static readonly ChatRole DeveloperRole = new("developer");

    /// <summary>
    /// Builds per-request options for the configured reasoning deployment.
    /// Reasoning models reject custom sampling parameters such as temperature;
    /// leaving Temperature null omits the field from the request.
    /// </summary>
    private ChatOptions BuildModelOptions()
        => MealScanReasoningOptions.Create(_config["AzureOpenAI:VisionReasoningEffort"]);

    /// <summary>
    /// Version tag for the Stage-A prompt + schema contract. Bump whenever the prompt,
    /// schema, transport or model deployment changes — the golden-image gate keys its
    /// cache and regression reports on this value. Do NOT edit an existing version in place.
    /// </summary>
    public const string VisionPromptVersion = "2026-08-26.v11-serving-hint";

    private const string VisionDeveloperInstructions = """
        You are a food identification assistant. Analyze the meal photo and list every
        distinct food component visible.

        Rules:
        - Composite Dish Test: log ONE item for foods that are normally eaten mixed or
          tossed together as a single dish — even if a component (like sauce) is plated as
          a pool on top for presentation rather than already stirred through. This covers
          (not limited to): pizza, sandwiches/burgers/wraps, burritos/tacos, lasagna/
          casseroles/pot pies, pasta with sauce ('spaghetti with tomato sauce', NOT
          spaghetti + sauce + butter — even when the sauce is plated as a pool on top),
          rice/noodle bowls with sauce ('katsu curry rice bowl', NOT rice + curry + katsu as
          three items), tossed/chopped salads once dressed or mixed together ('caesar salad',
          'taco salad', NOT lettuce + tomato + cucumber + dressing as separate items), soups,
          stews, curries, chilis, and smoothies/blended drinks.
        - Spreads/Toppings On Bread Stay Separate: a spread or topping on a base bread,
          cracker, or bagel (avocado, butter, jam, peanut butter, cream cheese) is its OWN
          item, separate from the bread — even though it is spread directly on top. These are
          each independently significant for nutrition tracking (e.g. 'toast' + 'avocado', NOT
          'avocado toast' as one item).
        - Distinct Plate Components: sides and toppings that are merely placed on top or
          served alongside without being mixed/tossed through stay separate items (e.g.
          berries placed on top of oatmeal, a side of miso soup or cabbage next to a rice
          bowl, a fried egg next to hash browns, dressing served in its own ramekin).
        - Canonical Naming: Name the food itself without shape, cut, or serving descriptors:
          * Use 'sausage' (NEVER 'sausage pieces', 'sliced sausage', 'sausage chunks').
          * Use 'pineapple' (NEVER 'pineapple chunks', 'pineapple pieces').
          * Use 'toasted bread' or 'toast' (NEVER 'toast bread slice').
          * Use 'avocado' or 'avocado spread' (NEVER 'spread on toast').
          * Never claim a specific species/cut you can't verify from color/texture (use
            'fish fillet' NEVER 'salmon' unless the flesh is clearly salmon-pink; use
            'chicken' NEVER a specific cut you can't see).
          * For a well-known named combo dish, use its common name, not an ingredient list
            (a ham-and-pineapple pizza is 'Hawaiian pizza', NOT 'ham and pineapple pizza');
            when the dish's identity includes a specific protein/main ingredient you can see,
            keep it in the name (a breaded pork cutlet in curry is 'pork katsu curry', NOT
            'katsu curry' alone — the protein matters for a food diary).
        - Never use disjunctions ('or', '/') in component name — choose the single dominant visible identity.
        - Portion calibration anchors (reference points, not exact answers — adjust for the
          visible portion relative to the plate and any reference objects): a large egg ≈50g,
          a slice of bread/toast ≈30-35g, a standard pizza slice ≈100-150g, a chicken
          breast/steak portion ≈150-200g, a cup of cooked rice or pasta ≈180-200g, a
          tablespoon of a spread/dip/sauce ≈15g, a cup of leafy greens ≈30-50g, a medium
          piece of fruit ≈120-180g, a berry/small-fruit garnish serving ≈30g.
        - Estimate grams per component using visual references (plate ≈26cm, cutlery,
          hands) when present; describe them in scale_notes. Without references, widen the
          low/high range and lower portion_confidence.
        - Account for cooking method in preparation_note (oil absorbed, breading, sauces).
        - estimated_grams_midpoint must lie within [estimated_grams_low, estimated_grams_high].
        - confidence reflects identity certainty only.
        - portion_confidence reflects certainty in the gram range only.
        - For every component, propose ONE familiar household unit for its visible form
          (examples: large egg, slice, cup cooked, tablespoon, medium fruit,
          palm-sized portion). Provide singular and plural labels and the approximate
          gram weight for ONE unit. The hint must be consistent with the gram midpoint.
          Leave serving_hint_unit and serving_hint_unit_plural empty and
          serving_hint_unit_grams as 0 only when no familiar unit is meaningful.
        - serving hints are display guidance only; never output calories or nutrition values.
        - is_garnish is true for low-mass garnishes or seasonings under 5g (sprinkled pepper, herbs, etc.).
        - search_queries must contain up to three short, generic retrieval descriptions
          for this component. Include preparation when useful. Never include a brand,
          hidden ingredient, unsupported species, or nutrition claim.
        - Never output calories or nutrition values — component identity and portion only.
        """;



    private const string CandidateChoiceDeveloperInstructions = """
        You are selecting a food catalog candidate for one visible component.
        Choose ONLY one candidate index from the supplied list, or abstain with null.
        Never invent a candidate, ingredient, brand, species, or nutrition value.
        Prefer generic foods when the image does not prove a brand or species.
        Abstain when the candidates are visually indistinguishable, when candidates are
        packaged snacks but fresh food is observed, or when the image cannot establish the requested specificity.
        Return candidate_index, confidence (0..1), and a short reason.
        """;

    private readonly IChatClient _chatClient;
    private readonly ITableStore _store;
    private readonly IConfiguration _config;
    private readonly ComponentGroundingEngine _grounding;
    private readonly MealScanAgentReviewService _agentReview;
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
        _agentReview = new MealScanAgentReviewService(_chatClient, _grounding, config, logger);
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
            new(DeveloperRole, VisionDeveloperInstructions),
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
                    messages, options: BuildModelOptions(), useJsonSchemaResponseFormat: true, cancellationToken: ct);
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
    // Stage B2 — direct candidate choice, then bounded Agent Framework review
    // ──────────────────────────────────────────────────────────────

    private bool IsCandidateSelectionEligible(GroundedItem grounded)
        => _config.GetValue("MealScan:EnableCandidateDisambiguation", true)
           && string.Equals(grounded.Attempt.ResolutionStatus, "ambiguous", StringComparison.OrdinalIgnoreCase)
           && grounded.ResolvedProduct is null
           && grounded.CandidateProducts.Count >= 2;

    private async Task<GroundedItem> TryVisionCandidateSelectionAsync(
        GroundedItem grounded,
        byte[] imageBytes,
        string contentType,
        CancellationToken ct)
    {
        if (!IsCandidateSelectionEligible(grounded))
            return grounded;

        var direct = await TryDirectCandidateSelectionAsync(grounded, imageBytes, contentType, ct);
        if (!ReferenceEquals(direct, grounded))
            return direct;

        if (!_config.GetValue("MealScan:EnableAgentGroundingReview", false))
        {
            _logger.LogInformation(
                "Direct B2 abstained for '{Component}'; agent review disabled.",
                grounded.Original.Name);
            return grounded;
        }

        return await _agentReview.ReviewAsync(grounded, imageBytes, contentType, ct);
    }

    private async Task<GroundedItem> TryDirectCandidateSelectionAsync(
        GroundedItem grounded,
        byte[] imageBytes,
        string contentType,
        CancellationToken ct)
    {
        var candidates = grounded.CandidateProducts
            .Select((product, index) =>
                $"{index}: {product.Name} | source={product.DataSource} | " +
                $"brand={product.Brand ?? "generic"}")
            .ToArray();

        var prompt = $"""
            Visible component: {grounded.Original.Name}
            Preparation note: {grounded.Original.PreparationNote}

            Candidate products:
            {string.Join(Environment.NewLine, candidates)}

            Select a candidate only when the image supports that candidate's
            specificity. Generic observations must remain generic. If the image
            cannot distinguish candidates, return candidate_index=null.
            """;

        try
        {
            var messages = new List<ChatMessage>
            {
                new(DeveloperRole, CandidateChoiceDeveloperInstructions),
                new(ChatRole.User,
                [
                    new TextContent(prompt),
                    new DataContent(imageBytes, contentType == "image/png" ? "image/png" : "image/jpeg"),
                ]),
            };

            var response = await _chatClient.GetResponseAsync<MealScanCandidateChoice>(
                messages,
                options: BuildModelOptions(),
                useJsonSchemaResponseFormat: true,
                cancellationToken: ct);

            var choice = response.Result;
            var minConfidence = _config.GetValue("MealScan:MinCandidateSelectionConfidence", 0.85m);
            var selectedIndex = MealScanCandidateSelector.SelectIndex(
                choice,
                grounded.CandidateProducts.Count,
                minConfidence);
            if (selectedIndex is not { } index)
                return grounded;

            var selected = grounded.CandidateProducts[index];
            var attempt = grounded.Attempt with
            {
                ResolutionStatus = "vision_selected",
                AutoSelected = true,
                SelectedFoodProductId = selected.Id,
                CanonicalName = selected.Name,
                MatchConfidence = selected.MatchConfidence,
                Method = "vision_candidate_selection",
            };

            _logger.LogInformation(
                "Direct B2 selected candidate {Candidate} for '{Component}' with confidence {Confidence:F2}.",
                selected.Name, grounded.Original.Name, choice?.Confidence ?? 0);

            return grounded with
            {
                ResolvedProduct = selected,
                Attempt = attempt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direct B2 candidate selection failed for '{Component}'.", grounded.Original.Name);
            return grounded;
        }
    }

    private async Task<GroundedItem> EnsureResolvedProductPersistedAsync(
        GroundedItem grounded,
        CancellationToken ct)
    {
        if (grounded.ResolvedProduct is not { } product || product.Id != Guid.Empty)
            return grounded;

        var id = await FoodProductPersistence.ResolveOrPersistAsync(product, _store, ct);
        var persisted = product with { Id = id };
        var attempt = grounded.Attempt with
        {
            SelectedFoodProductId = id,
            CanonicalName = persisted.Name,
        };

        return grounded with
        {
            ResolvedProduct = persisted,
            Attempt = attempt,
        };
    }

    public async Task<MealScanDraftDto> ScanMealImageAsync(Guid userId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var deployment = _config["AzureOpenAI:VisionDeployment"] ?? _config["AzureOpenAI:DeploymentName"] ?? "unknown";

        using var imageBuffer = new MemoryStream();
        await imageStream.CopyToAsync(imageBuffer, ct);
        var imageBytes = imageBuffer.ToArray();
        using var decompositionStream = new MemoryStream(imageBytes, writable: false);
        var decomposition = await DecomposeAsync(decompositionStream, contentType, ct);
        var maxComponents = _config.GetValue("MealScan:MaxComponentsPerPhoto", 12);

        // ── Stage B: ground components concurrently, then review/persist in stable order ──
        // Resolver/provider calls are I/O-bound and independent; candidate selection stays
        // sequential because it consumes the shared per-scan vision budget.
        var groundedComponents = await GroundComponentsAsync(decomposition.Components, ct);

        var groundedItems = new List<MealScanItemDto>();
        var needsDisambiguation = 0;
        var maxCandidateSelections = _config.GetValue("MealScan:MaxCandidateDisambiguationsPerScan", 4);
        var candidateSelectionAttempts = 0;
        foreach (var (component, grounded) in decomposition.Components.Zip(groundedComponents, (component, grounded) => (component, grounded)))
        {
            var resolved = grounded
                ?? throw new InvalidOperationException($"Component '{component.Name}' did not produce a grounding result.");
            if (IsCandidateSelectionEligible(resolved) && candidateSelectionAttempts < maxCandidateSelections)
            {
                candidateSelectionAttempts++;
                resolved = await TryVisionCandidateSelectionAsync(resolved, imageBytes, contentType, ct);
            }
            resolved = await EnsureResolvedProductPersistedAsync(resolved, ct);

            groundedItems.Add(resolved.ToItem());
            if (!resolved.Attempt.AutoSelected) needsDisambiguation++;
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
                // Web-sourced items are outside the health-signal safety boundary: clear any
                // catalog identity and FODMAP/gut signals so scraped nutrition can never
                // inherit or imply them (AGENTS.md guardrail 8 — Health Signal Isolation).
                FoodProductId = null,
                FodmapStatus = null,
                FodmapTriggers = null,
                GutRating = null,
                Source = "web",
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
    internal async Task<GroundedItem[]> GroundComponentsAsync(
        IReadOnlyList<ScannedComponent> components,
        CancellationToken ct)
    {
        if (components.Count == 0) return [];

        var maxConcurrency = Math.Clamp(
            _config.GetValue("MealScan:MaxConcurrentGrounding", 4),
            1,
            components.Count);
        var grounded = new GroundedItem?[components.Count];
        await Parallel.ForAsync(
            0,
            components.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = ct,
            },
            async (index, token) =>
                grounded[index] = await _grounding.GroundAsync(components[index], token));

        return grounded.Cast<GroundedItem>().ToArray();
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
