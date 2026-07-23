using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
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

    private readonly ITableStore _store;
    private readonly INutritionApiService _nutritionApi;
    private readonly ICorrelationEngine _correlationEngine;
    private readonly IFoodDiaryAnalysisService _diaryService;
    private readonly ILogger<MealSymptomTools> _logger;

    public MealSymptomTools(
        ITableStore store,
        INutritionApiService nutritionApi,
        ICorrelationEngine correlationEngine,
        IFoodDiaryAnalysisService diaryService,
        ILogger<MealSymptomTools> logger)
    {
        _store = store;
        _nutritionApi = nutritionApi;
        _correlationEngine = correlationEngine;
        _diaryService = diaryService;
        _logger = logger;
    }

    [McpServerTool(Name = "gutai_log_meal")]
    [Description("Log a meal with one or more food items. For each item, first call gutai_search_foods to find its food_product_id, then include that ID here for accurate nutrition data. Items without a food_product_id will fall back to natural language nutrition estimation (less accurate). Use the description field as a last resort when items array is impractical.")]
    public async Task<string> LogMeal(
        HttpContext httpContext,
        [Description("Meal type: Breakfast, Lunch, Dinner, or Snack (required)")] string mealType,
        [Description("JSON array of items: [{\"food_product_id\":\"GUID\",\"name\":\"food name\",\"servings\":1}]. Strongly prefer including food_product_id from gutai_search_foods results for each item.")] string? items,
        [Description("Fallback: natural language description of the meal. Only use when items array cannot capture the meal (e.g. 'a bowl of chicken soup and a glass of water').")] string? description,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
            if (!Enum.TryParse<MealType>(mealType, true, out var mt))
                throw new McpException($"Invalid meal type '{mealType}'. Must be one of: Breakfast, Lunch, Dinner, Snack.");
            var mealId = Guid.NewGuid();
            var mealItems = new List<MealItem>();
            var originalParts = new List<string>();

            if (!string.IsNullOrEmpty(items))
            {
                using var doc = JsonDocument.Parse(items);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var servings = MealValidation.ClampServings(item.TryGetProperty("servings", out var sv) && sv.ValueKind == JsonValueKind.Number
                        ? sv.GetDecimal() : 1m);
                    var itemName = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;

                    if (item.TryGetProperty("food_product_id", out var fpId) && fpId.GetString() is { } fpIdStr
                        && Guid.TryParse(fpIdStr, out var productId))
                    {
                        var product = await _store.GetFoodProductAsync(productId, ct);
                        if (product is not null)
                        {
                            var servingG = product.ServingQuantity is > 0 ? product.ServingQuantity.Value : 100m;
                            var factor = servings * servingG / 100m;
                            mealItems.Add(new MealItem
                            {
                                Id = Guid.NewGuid(),
                                MealLogId = mealId,
                                FoodName = itemName ?? product.Name,
                                FoodProductId = product.Id,
                                Servings = servings,
                                ServingUnit = product.ServingSize ?? "serving",
                                ServingWeightG = servingG * servings,
                                Calories = MealValidation.ClampNutrient((product.Calories100g ?? 0) * factor, MealValidation.MaxCalories),
                                ProteinG = MealValidation.ClampNutrient((product.Protein100g ?? 0) * factor, MealValidation.MaxMacroG),
                                CarbsG = MealValidation.ClampNutrient((product.Carbs100g ?? 0) * factor, MealValidation.MaxMacroG),
                                FatG = MealValidation.ClampNutrient((product.Fat100g ?? 0) * factor, MealValidation.MaxMacroG),
                                FiberG = (product.Fiber100g ?? 0) * factor,
                                SugarG = (product.Sugar100g ?? 0) * factor,
                                SodiumMg = (product.SodiumMg100g ?? 0) * factor,
                            });
                            originalParts.Add(itemName ?? product.Name);
                            continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(itemName))
                    {
                        var parsedItems = await _nutritionApi.ParseNaturalLanguageAsync(itemName, ct);
                        foreach (var p in parsedItems)
                        {
                            mealItems.Add(new MealItem
                            {
                                Id = Guid.NewGuid(),
                                MealLogId = mealId,
                                FoodName = p.Name,
                                Servings = servings * (p.ServingQuantity ?? 1m),
                                ServingUnit = "serving",
                                ServingWeightG = p.ServingWeightG * servings,
                                Calories = MealValidation.ClampNutrient(p.Calories * servings, MealValidation.MaxCalories),
                                ProteinG = MealValidation.ClampNutrient(p.ProteinG * servings, MealValidation.MaxMacroG),
                                CarbsG = MealValidation.ClampNutrient(p.CarbsG * servings, MealValidation.MaxMacroG),
                                FatG = MealValidation.ClampNutrient(p.FatG * servings, MealValidation.MaxMacroG),
                                FiberG = p.FiberG * servings,
                                SugarG = p.SugarG * servings,
                                SodiumMg = p.SodiumMg * servings,
                                CholesterolMg = p.CholesterolMg * servings,
                                SaturatedFatG = p.SaturatedFatG * servings,
                                PotassiumMg = p.PotassiumMg * servings,
                                MatchConfidence = p.MatchConfidence,
                                NutritionProvenance = p.NutritionProvenance.ToString(),
                            });
                            originalParts.Add(p.Name);
                        }
                    }
                }
            }

            if (mealItems.Count == 0 && !string.IsNullOrEmpty(description))
            {
                var parsedItems = await _nutritionApi.ParseNaturalLanguageAsync(description, ct);
                foreach (var p in parsedItems)
                {
                    mealItems.Add(new MealItem
                    {
                        Id = Guid.NewGuid(),
                        MealLogId = mealId,
                        FoodName = p.Name,
                        Servings = p.ServingQuantity ?? 1m,
                        ServingUnit = "serving",
                        ServingWeightG = p.ServingWeightG,
                        Calories = MealValidation.ClampNutrient(p.Calories, MealValidation.MaxCalories),
                        ProteinG = MealValidation.ClampNutrient(p.ProteinG, MealValidation.MaxMacroG),
                        CarbsG = MealValidation.ClampNutrient(p.CarbsG, MealValidation.MaxMacroG),
                        FatG = MealValidation.ClampNutrient(p.FatG, MealValidation.MaxMacroG),
                        FiberG = p.FiberG,
                        SugarG = p.SugarG,
                        SodiumMg = p.SodiumMg,
                        CholesterolMg = p.CholesterolMg,
                        SaturatedFatG = p.SaturatedFatG,
                        PotassiumMg = p.PotassiumMg,
                        MatchConfidence = p.MatchConfidence,
                        NutritionProvenance = p.NutritionProvenance.ToString(),
                    });
                    originalParts.Add(p.Name);
                }
            }

            if (mealItems.Count == 0)
                throw new McpException("Could not resolve any food items from the provided input.");

            var meal = new MealLog
            {
                Id = mealId,
                UserId = userId,
                MealType = mt,
                LoggedAt = DateTime.UtcNow,
                OriginalText = string.Join(", ", originalParts),
                TotalCalories = mealItems.Sum(i => i.Calories),
                TotalProteinG = mealItems.Sum(i => i.ProteinG),
                TotalCarbsG = mealItems.Sum(i => i.CarbsG),
                TotalFatG = mealItems.Sum(i => i.FatG)
            };

            await _store.UpsertMealLogAsync(meal, ct);
            await _store.UpsertMealItemsAsync(userId, meal.Id, mealItems, ct);

            // Surface identity/nutrition uncertainty per item so the calling model can flag
            // low-confidence or estimated entries to the user instead of presenting every
            // item as equally trustworthy — auto-logging still happens (no confirmation gate
            // exists in this tool-call flow), but the caller now has the evidence to act on.
            var lowConfidenceItems = mealItems
                .Where(i => i.NutritionProvenance == "Estimated" || i.MatchConfidence is < 0.6m)
                .Select(i => i.FoodName)
                .ToList();

            return JsonSerializer.Serialize(new
            {
                id = meal.Id,
                mealType = meal.MealType.ToString(),
                totalCalories = meal.TotalCalories,
                totalProteinG = meal.TotalProteinG,
                totalCarbsG = meal.TotalCarbsG,
                totalFatG = meal.TotalFatG,
                totalFiberG = mealItems.Sum(i => i.FiberG),
                items = mealItems.Select(i => new
                {
                    i.FoodName,
                    i.Calories,
                    i.ProteinG,
                    i.CarbsG,
                    i.FatG,
                    i.FiberG,
                    matchConfidence = i.MatchConfidence,
                    nutritionProvenance = i.NutritionProvenance
                }),
                lowConfidenceItems,
                lowConfidenceNote = lowConfidenceItems.Count > 0
                    ? "Some items used an estimated or low-confidence nutrition match — consider mentioning this to the user and offering to correct them."
                    : null
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LogMeal failed");
            throw new McpException("Could not log that meal. Please try again.");
        }
    }

    [McpServerTool(Name = "gutai_log_symptom")]
    [Description("Record a symptom the user is experiencing. Severity must be 1 (mild) to 10 (severe). Common symptom names include: Bloating, Nausea, Gas, Headache, Fatigue, Stomach Pain, Diarrhea, Constipation, Heartburn, Cramps.")]
    public async Task<string> LogSymptom(
        HttpContext httpContext,
        [Description("Name of the symptom, e.g. 'Bloating', 'Nausea', 'Gas', 'Headache', 'Fatigue', 'Stomach Pain'")] string symptomName,
        [Description("Severity from 1 (mild) to 10 (severe). Required.")] int severity,
        [Description("Optional notes about the symptom — e.g. timing, triggers, duration.")] string? notes,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
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
    [Description("Get all meals the user logged today with per-item and per-meal nutrition info. 'Today' is determined by the user's timezone. Use this to answer questions about what the user has eaten today.")]
    public async Task<string> GetTodaysMeals(
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
            var user = await _store.GetUserAsync(userId, ct);
            var (rangeStart, rangeEnd) = TimeZoneHelper.GetUserTodayUtcRange(user);

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
    [Description("Get today's nutrition totals (calories, protein, carbs, fat, fiber) compared against the user's daily goals. 'Today' is determined by the user's timezone. Use this before making dietary recommendations to understand what the user has already consumed today.")]
    public async Task<string> GetNutritionSummary(
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
            var user = await _store.GetUserAsync(userId, ct);
            var (rangeStart, rangeEnd) = TimeZoneHelper.GetUserTodayUtcRange(user);

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
                    calories = user?.DailyCalorieGoal ?? 2000,
                    proteinG = user?.DailyProteinGoalG ?? 50,
                    carbsG = user?.DailyCarbGoalG ?? 250,
                    fatG = user?.DailyFatGoalG ?? 65,
                    fiberG = user?.DailyFiberGoalG ?? 25
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
    [Description("Get the user's trigger foods — foods most associated with their symptoms based on statistical correlation analysis. Only returns correlations that occurred 2+ times with average severity of 4+. Uses the user's timezone for date range calculation.")]
    public async Task<string> GetTriggerFoods(
        HttpContext httpContext,
        [Description("Number of days to look back for correlation data. Default 30.")] int? days,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
            var user = await _store.GetUserAsync(userId, ct);
            var (_, utcEnd) = TimeZoneHelper.GetUserTodayUtcRange(user);
            var from = DateOnly.FromDateTime(utcEnd.AddDays(-(days ?? 30)));
            var to = DateOnly.FromDateTime(utcEnd);
            var correlations = await _correlationEngine.ComputeCorrelationsAsync(userId, from, to, ct);

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
    [Description("Get the user's recent symptom logs. Returns up to 20 of the most recent entries with symptom name, severity, timestamp, and notes. Uses the user's timezone for date range.")]
    public async Task<string> GetSymptomHistory(
        HttpContext httpContext,
        [Description("Number of days to look back. Default 7.")] int? days,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
            var user = await _store.GetUserAsync(userId, ct);
            var (_, utcEnd) = TimeZoneHelper.GetUserTodayUtcRange(user);
            var from = DateOnly.FromDateTime(utcEnd.AddDays(-(days ?? 7)));
            var to = DateOnly.FromDateTime(utcEnd);
            var symptoms = await _store.GetSymptomLogsByDateRangeAsync(userId, from, to, ct);
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
    [Description("Get the user's current elimination diet phase, foods to eliminate, safe foods, reintroduction results, and recommendations. Use this when the user asks about their elimination diet progress or what foods are safe during their current phase.")]
    public async Task<string> GetEliminationDietStatus(
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
            var result = await _diaryService.GetEliminationStatusAsync(userId, _store);
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

    private static Guid GetUserId(HttpContext httpContext) =>
        Guid.Parse(httpContext.User.FindFirstValue("sub")!);
}
