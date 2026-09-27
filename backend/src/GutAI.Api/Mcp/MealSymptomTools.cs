using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GutAI.Api.Mcp;

[McpServerToolType]
public class MealSymptomTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string CommitNote = "Nothing is logged yet. The user can confirm this draft in the GutAI app, or call gutai_commit_meal after they confirm.";

    private readonly ITableStore _store;
    private readonly ICorrelationEngine _correlationEngine;
    private readonly IFoodDiaryAnalysisService _diaryService;
    private readonly IMealDraftService _draftService;
    private readonly IAgentMealItemResolver _itemResolver;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MealSymptomTools> _logger;

    public MealSymptomTools(
        ITableStore store,
        ICorrelationEngine correlationEngine,
        IFoodDiaryAnalysisService diaryService,
        IMealDraftService draftService,
        IAgentMealItemResolver itemResolver,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<MealSymptomTools> logger)
    {
        _store = store;
        _correlationEngine = correlationEngine;
        _diaryService = diaryService;
        _draftService = draftService;
        _itemResolver = itemResolver;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [McpServerTool(Name = "gutai_propose_meal")]
    [Authorize]
    [Description("Propose a meal for human review; this does not log anything. Call gutai_search_foods first and copy food_product_id and the matchConfidence value from its results into items.match_confidence. Supply either the JSON items array or a description. The user must review the saved draft in the GutAI app or in this chat before gutai_commit_meal; wait for the enforced review delay (20 seconds by default) before committing.")]
    public async Task<string> ProposeMeal(
        ClaimsPrincipal? user,
        [Description("Meal type: Breakfast, Lunch, Dinner, or Snack (required)")] string mealType,
        [Description("JSON array of items: [{\"food_product_id\":\"GUID\",\"name\":\"food name\",\"servings\":1,\"serving_weight_g\":100,\"match_confidence\":0.95}]. Copy food_product_id and matchConfidence exactly from gutai_search_foods results into food_product_id and match_confidence.")] string? items = null,
        [Description("Optional natural-language description when an items array is impractical.")] string? description = null,
        [Description("Optional ISO-8601 date/time for the proposed meal.")] string? loggedAt = null,
        CancellationToken ct = default)
    {
        McpAccess.EnsureWrite(user!);
        try
        {
            var userId = GetUserId(user!);
            if (!Enum.TryParse<GutAI.Domain.Enums.MealType>(mealType, true, out var parsedMealType)
                || !Enum.IsDefined(parsedMealType))
                throw new McpException($"Invalid meal type '{mealType}'. Must be one of: Breakfast, Lunch, Dinner, Snack.");

            DateTimeOffset? timestamp = null;
            if (loggedAt is not null)
            {
                if (!DateTimeOffset.TryParse(loggedAt, out var parsedTimestamp))
                    throw new McpException("loggedAt must be a valid ISO-8601 date/time.");
                timestamp = parsedTimestamp;
            }

            var inputs = string.IsNullOrWhiteSpace(items) ? new List<AgentMealItemInput>() : ParseMealItems(items);
            if (inputs.Count > 50)
                throw new McpException("A meal can contain no more than 50 items.");
            if (inputs.Count == 0 && string.IsNullOrWhiteSpace(description))
                throw new McpException("Provide at least one item or a meal description.");
            var profile = await _store.GetUserAsync(userId);
            var resolved = await _itemResolver.ResolveAsync(
                inputs,
                description,
                region: profile?.PreferredFoodRegion ?? GutAI.Domain.Enums.FoodRegion.Default,
                ct: ct);
            if (resolved.Count == 0)
                throw new McpException("Could not resolve any food items from the provided input.");
            if (resolved.Count > 50)
                throw new McpException("A meal can contain no more than 50 resolved items.");

            var draft = await _draftService.CreateAsync(userId, new MealDraftCreateRequest
            {
                Origin = MealDraftOrigins.Mcp,
                MealType = parsedMealType.ToString(),
                LoggedAt = timestamp,
                Items = resolved,
                Warnings = [],
                OverallConfidence = resolved.Average(i => i.MatchConfidence)
            }, ct);

            return JsonSerializer.Serialize(new
            {
                draft_id = draft.DraftId,
                meal_type = draft.MealType,
                items = draft.Items.Select(i => new
                {
                    item_id = i.ItemId,
                    name = i.Name,
                    grams = i.Grams,
                    calories = i.Calories,
                    provenance = i.NutritionProvenance,
                    needs_choice = i.NeedsChoice
                }),
                totals = new
                {
                    calories = draft.Totals.Calories,
                    protein_g = draft.Totals.ProteinG,
                    carbs_g = draft.Totals.CarbsG,
                    fat_g = draft.Totals.FatG,
                    items_without_nutrition = draft.Totals.ItemsWithoutNutrition
                },
                note = CommitNote
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (JsonException)
        {
            throw new McpException("items must be a valid JSON array of food items.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProposeMeal failed");
            throw new McpException("Could not propose that meal. Please try again.");
        }
    }

    [McpServerTool(Name = "gutai_commit_meal")]
    [Authorize]
    [Description("Commit an existing MCP-origin meal draft only after the user has reviewed it in the GutAI app or in this chat. A minimum delay (20 seconds by default) is enforced after proposal, so never call this in the same response as gutai_propose_meal.")]
    public async Task<string> CommitMeal(
        ClaimsPrincipal? user,
        [Description("Draft ID returned by gutai_propose_meal (required)")] string draftId,
        CancellationToken ct = default)
    {
        McpAccess.EnsureWrite(user!);
        try
        {
            if (!Guid.TryParse(draftId, out var id))
                throw new McpException("draftId must be a valid draft GUID.");
            var result = await _draftService.CommitAsync(
                GetUserId(user!), id, null,
                new MealDraftCommitGuard(
                    RequiredOrigin: MealDraftOrigins.Mcp,
                    CreatedBefore: _timeProvider.GetUtcNow()
                        - TimeSpan.FromSeconds(_configuration.GetValue("Mcp:MinCommitDelaySeconds", 20))), ct);
            return JsonSerializer.Serialize(new
            {
                meal_id = result.MealId,
                total_calories = result.TotalCalories,
                total_protein_g = result.TotalProteinG,
                total_carbs_g = result.TotalCarbsG,
                total_fat_g = result.TotalFatG,
                item_count = result.ItemCount,
                items_without_nutrition = result.ItemsWithoutNutrition
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (MealDraftException ex) when (ex.Code == MealDraftErrorCode.SameTurnCommit)
        {
            throw new McpException(
                "This meal draft was just proposed. The user must first review it in the GutAI app or in this chat, then call gutai_commit_meal again. Nothing was logged.");
        }
        catch (MealDraftException ex)
        {
            throw new McpException($"Could not commit meal draft ({ex.Code}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CommitMeal failed");
            throw new McpException("Could not commit that meal. Please try again.");
        }
    }

    private static List<AgentMealItemInput> ParseMealItems(string json)
    {
        var entries = JsonSerializer.Deserialize<List<ProposedMealItem>>(json, JsonOpts)
            ?? throw new JsonException("Items cannot be null.");
        return entries.Select(item =>
        {
            if (item is null)
                throw new JsonException("Items cannot contain null.");
            if (item.FoodProductId is not null && !Guid.TryParse(item.FoodProductId, out _))
                throw new JsonException("food_product_id must be a valid GUID.");
            if (string.IsNullOrWhiteSpace(item.Name) && item.FoodProductId is null)
                throw new JsonException("Each item requires a name or food_product_id.");
            if (item.ServingWeightG is < 0m)
                throw new JsonException("serving_weight_g cannot be negative.");
            if (item.MatchConfidence is < 0m or > 1m)
                throw new JsonException("match_confidence must be between 0 and 1.");
            return new AgentMealItemInput(
                item.Name,
                item.FoodProductId is null ? null : Guid.Parse(item.FoodProductId),
                item.Servings ?? 1m,
                item.ServingWeightG,
                item.MatchConfidence);
        }).ToList();
    }

    private sealed class ProposedMealItem
    {
        [JsonPropertyName("food_product_id")]
        public string? FoodProductId { get; init; }
        [JsonPropertyName("name")]
        public string? Name { get; init; }
        [JsonPropertyName("servings")]
        public decimal? Servings { get; init; }
        [JsonPropertyName("serving_weight_g")]
        public decimal? ServingWeightG { get; init; }
        [JsonPropertyName("match_confidence")]
        public decimal? MatchConfidence { get; init; }
    }

    [McpServerTool(Name = "gutai_log_symptom")]
    [Authorize]
    [Description("Record a symptom the user is experiencing. Severity must be 1 (mild) to 10 (severe). Common symptom names include: Bloating, Nausea, Gas, Headache, Fatigue, Stomach Pain, Diarrhea, Constipation, Heartburn, Cramps.")]
    public async Task<string> LogSymptom(
        ClaimsPrincipal? user,
        [Description("Name of the symptom, e.g. 'Bloating', 'Nausea', 'Gas', 'Headache', 'Fatigue', 'Stomach Pain'")] string symptomName,
        [Description("Severity from 1 (mild) to 10 (severe). Required.")] int severity,
        [Description("Optional notes about the symptom — e.g. timing, triggers, duration.")] string? notes = null,
        CancellationToken ct = default)
    {
        try
        {
            McpAccess.EnsureWrite(user!);
            var userId = GetUserId(user!);
            var types = await _store.GetAllSymptomTypesAsync(ct);
            var type = types.FirstOrDefault(t => t.Name.Equals(symptomName, StringComparison.OrdinalIgnoreCase));
            if (type is null)
                throw new McpException($"Unknown symptom: {symptomName}. Available: {string.Join(", ", types.Select(t => t.Name))}");

            var symptom = new SymptomLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SymptomTypeId = type.Id,
                Severity = Math.Clamp(severity, 1, 10),
                OccurredAt = DateTime.UtcNow,
                Notes = notes is { Length: > MealValidation.MaxNotesLength } ? notes[..MealValidation.MaxNotesLength] : notes
            };
            await _store.UpsertSymptomLogAsync(symptom, ct);
            return JsonSerializer.Serialize(new { id = symptom.Id, symptom = type.Name, severity = symptom.Severity }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LogSymptom failed");
            throw new McpException("Could not log that symptom. Please try again.");
        }
    }

    [McpServerTool(Name = "gutai_get_todays_meals", ReadOnly = true)]
    [Authorize]
    [Description("Get all meals the user logged today with per-item and per-meal nutrition info. 'Today' is determined by the user's timezone. Use this to answer questions about what the user has eaten today.")]
    public async Task<string> GetTodaysMeals(
        ClaimsPrincipal? user,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(user!);
            var appUser = await _store.GetUserAsync(userId, ct);
            var (rangeStart, rangeEnd) = TimeZoneHelper.GetUserTodayUtcRange(appUser);

            var meals = await _store.GetMealLogsByDateRangeAsync(userId,
                DateOnly.FromDateTime(rangeStart), DateOnly.FromDateTime(rangeEnd), ct);
            meals = meals.Where(m => m.LoggedAt >= rangeStart && m.LoggedAt <= rangeEnd).ToList();
            foreach (var m in meals) m.Items = await _store.GetMealItemsAsync(userId, m.Id, ct);

            var summary = meals.Select(m => new
            {
                mealType = m.MealType.ToString(),
                loggedAt = m.LoggedAt,
                totalCalories = m.TotalCalories,
                totalProteinG = m.TotalProteinG,
                totalCarbsG = m.TotalCarbsG,
                totalFatG = m.TotalFatG,
                items = m.Items.Select(i => new { i.FoodName, i.Calories, i.ProteinG, i.CarbsG, i.FatG, i.FiberG })
            });
            return JsonSerializer.Serialize(summary, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetTodaysMeals failed");
            throw new McpException("Could not get today's meals. Please try again.");
        }
    }

    [McpServerTool(Name = "gutai_get_nutrition_summary", ReadOnly = true)]
    [Authorize]
    [Description("Get today's nutrition totals (calories, protein, carbs, fat, fiber) compared against the user's daily goals. 'Today' is determined by the user's timezone. Use this before making dietary recommendations to understand what the user has already consumed today.")]
    public async Task<string> GetNutritionSummary(
        ClaimsPrincipal? user,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(user!);
            var appUser = await _store.GetUserAsync(userId, ct);
            var (rangeStart, rangeEnd) = TimeZoneHelper.GetUserTodayUtcRange(appUser);

            var meals = await _store.GetMealLogsByDateRangeAsync(userId,
                DateOnly.FromDateTime(rangeStart), DateOnly.FromDateTime(rangeEnd), ct);
            meals = meals.Where(m => m.LoggedAt >= rangeStart && m.LoggedAt <= rangeEnd).ToList();
            foreach (var m in meals) m.Items = await _store.GetMealItemsAsync(userId, m.Id, ct);

            return JsonSerializer.Serialize(new
            {
                totalCalories = meals.Sum(m => m.TotalCalories),
                totalProteinG = meals.Sum(m => m.TotalProteinG),
                totalCarbsG = meals.Sum(m => m.TotalCarbsG),
                totalFatG = meals.Sum(m => m.TotalFatG),
                totalFiberG = meals.SelectMany(m => m.Items).Sum(i => i.FiberG),
                mealCount = meals.Count,
                goals = new
                {
                    calories = appUser?.DailyCalorieGoal ?? 2000,
                    proteinG = appUser?.DailyProteinGoalG ?? 50,
                    carbsG = appUser?.DailyCarbGoalG ?? 250,
                    fatG = appUser?.DailyFatGoalG ?? 65,
                    fiberG = appUser?.DailyFiberGoalG ?? 25
                }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetNutritionSummary failed");
            throw new McpException("Could not get the nutrition summary. Please try again.");
        }
    }

    [McpServerTool(Name = "gutai_get_trigger_foods", ReadOnly = true)]
    [Authorize]
    [Description("Get the user's trigger foods — foods most associated with their symptoms based on statistical correlation analysis. Only returns correlations that occurred 2+ times with average severity of 4+. Uses the user's timezone for date range calculation.")]
    public async Task<string> GetTriggerFoods(
        ClaimsPrincipal? user,
        [Description("Number of days to look back for correlation data. Default 30.")] int? days = null,
        CancellationToken ct = default)
    {
        try
        {
            var userId = GetUserId(user!);
            var appUser = await _store.GetUserAsync(userId, ct);
            var timezone = TimeZoneHelper.ResolveTimeZone(appUser, null);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
            var from = today.AddDays(-(days ?? 30));
            var to = today;
            var correlations = await _correlationEngine.ComputeCorrelationsAsync(
                userId, from, to, ct, appUser?.TimezoneId);

            var triggers = correlations
                .Where(c => c.Occurrences >= 2 && c.AverageSeverity >= 4)
                .GroupBy(c => c.FoodOrAdditive)
                .Select(g => new
                {
                    food = g.Key,
                    symptoms = g.Select(c => c.SymptomName).Distinct().ToList(),
                    totalOccurrences = g.Sum(c => c.Occurrences),
                    avgSeverity = g.Average(c => (double)c.AverageSeverity)
                })
                .OrderByDescending(t => t.avgSeverity).Take(10);
            return JsonSerializer.Serialize(triggers, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetTriggerFoods failed");
            throw new McpException("Could not get trigger foods. Please try again.");
        }
    }
    [McpServerTool(Name = "gutai_get_symptom_history", ReadOnly = true)]
    [Authorize]
    [Description("Get the user's recent symptom logs. Returns up to 20 of the most recent entries with symptom name, severity, timestamp, and notes. Uses the user's timezone for date range.")]
    public async Task<string> GetSymptomHistory(
        ClaimsPrincipal? user,
        [Description("Number of days to look back. Default 7.")] int? days = null,
        CancellationToken ct = default)
    {
        try
        {
            var userId = GetUserId(user!);
            var appUser = await _store.GetUserAsync(userId, ct);
            var timezone = TimeZoneHelper.ResolveTimeZone(appUser, null);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
            var from = today.AddDays(-(days ?? 7));
            var to = today;
            var (utcStart, utcEnd) = TimeZoneHelper.GetUtcRangeForLocalDateRange(
                appUser, from, to, appUser?.TimezoneId);
            var symptoms = await _store.GetSymptomLogsByDateRangeAsync(
                userId,
                DateOnly.FromDateTime(utcStart),
                DateOnly.FromDateTime(utcEnd),
                ct);
            symptoms = symptoms.Where(s => s.OccurredAt >= utcStart && s.OccurredAt <= utcEnd).ToList();
            foreach (var s in symptoms)
                s.SymptomType = await _store.GetSymptomTypeAsync(s.SymptomTypeId, ct) ?? new SymptomType { Name = "Unknown" };

            return JsonSerializer.Serialize(symptoms.OrderByDescending(s => s.OccurredAt).Take(20).Select(s => new
            {
                symptom = s.SymptomType.Name,
                s.Severity,
                s.OccurredAt,
                s.Notes
            }), JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetSymptomHistory failed");
            throw new McpException("Could not get symptom history. Please try again.");
        }
    }

    [McpServerTool(Name = "gutai_get_elimination_diet_status", ReadOnly = true)]
    [Authorize]
    [Description("Get the user's current elimination diet phase, foods to eliminate, safe foods, reintroduction results, and recommendations. Use this when the user asks about their elimination diet progress or what foods are safe during their current phase.")]
    public async Task<string> GetEliminationDietStatus(
        ClaimsPrincipal? user,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(user!);
            var appUser = await _store.GetUserAsync(userId, ct);
            var result = await _diaryService.GetEliminationStatusAsync(userId, _store, appUser?.TimezoneId);
            return JsonSerializer.Serialize(new
            {
                result.Phase,
                result.FoodsToEliminate,
                result.SafeFoods,
                result.Recommendations,
                result.Summary
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetEliminationDietStatus failed");
            throw new McpException("Could not get elimination diet status. Please try again.");
        }
    }

    private static Guid GetUserId(ClaimsPrincipal? user) =>
        Guid.Parse(user!.FindFirstValue("sub")!);
}
