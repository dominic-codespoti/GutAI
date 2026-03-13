#pragma warning disable OPENAI001

using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Microsoft.Extensions.Logging;
using OpenAI.Assistants;

namespace GutAI.Infrastructure.Services;

public class AzureOpenAIChatService : IChatService
{
    private readonly AssistantClient _client;
    private readonly Lazy<Task<string>> _assistantIdFactory;
    private readonly ITableStore _store;
    private readonly ICorrelationEngine _correlationEngine;
    private readonly IFoodDiaryAnalysisService _diaryService;
    private readonly IFoodApiService _foodApi;
    private readonly INutritionApiService _nutritionApi;
    private readonly FodmapService _fodmapService;
    private readonly GutRiskService _gutRiskService;
    private readonly PersonalizedScoringService _scoringService;
    private readonly ILogger<AzureOpenAIChatService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AzureOpenAIChatService(
        AssistantClient client,
        Lazy<Task<string>> assistantIdFactory,
        ITableStore store,
        ICorrelationEngine correlationEngine,
        IFoodDiaryAnalysisService diaryService,
        IFoodApiService foodApi,
        INutritionApiService nutritionApi,
        FodmapService fodmapService,
        GutRiskService gutRiskService,
        PersonalizedScoringService scoringService,
        ILogger<AzureOpenAIChatService> logger)
    {
        _client = client;
        _assistantIdFactory = assistantIdFactory;
        _store = store;
        _correlationEngine = correlationEngine;
        _diaryService = diaryService;
        _foodApi = foodApi;
        _nutritionApi = nutritionApi;
        _fodmapService = fodmapService;
        _gutRiskService = gutRiskService;
        _scoringService = scoringService;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamResponseAsync(
        Guid userId, string message, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var assistantId = await _assistantIdFactory.Value;
        var user = await _store.GetUserAsync(userId, ct);
        var threadId = await GetOrCreateThreadAsync(user!, ct);

        // Add user message to thread
        await _client.CreateMessageAsync(threadId, MessageRole.User, [MessageContent.FromText(message)],
            cancellationToken: ct);

        // Build per-user instructions overlay
        var additionalInstructions = BuildAdditionalInstructions(user);

        var runOptions = new RunCreationOptions
        {
            AdditionalInstructions = additionalInstructions
        };

        // Stream the run with tool call handling
        var stream = _client.CreateRunStreamingAsync(
            threadId, assistantId, runOptions, ct);

        var toolOutputs = new List<ToolOutput>();
        string? currentRunId = null;

        while (true)
        {
            toolOutputs.Clear();
            currentRunId = null;

            await foreach (var update in stream.WithCancellation(ct))
            {
                if (update is RequiredActionUpdate actionUpdate)
                {
                    yield return new ChatStreamEvent(ToolCall: actionUpdate.FunctionName, Status: "executing");
                    var result = await ExecuteToolAsync(userId, actionUpdate.FunctionName,
                        actionUpdate.FunctionArguments, ct);
                    toolOutputs.Add(new ToolOutput(actionUpdate.ToolCallId, result));
                    currentRunId = actionUpdate.Value.Id;
                }
                else if (update is MessageContentUpdate contentUpdate)
                {
                    if (!string.IsNullOrEmpty(contentUpdate.Text))
                    {
                        yield return new ChatStreamEvent(Content: contentUpdate.Text);
                    }
                }
            }

            if (toolOutputs.Count > 0 && currentRunId is not null)
            {
                stream = _client.SubmitToolOutputsToRunStreamingAsync(threadId, currentRunId,
                    toolOutputs, ct);
            }
            else
            {
                break;
            }
        }
    }

    public async Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId, int limit = 50,
        CancellationToken ct = default)
    {
        var user = await _store.GetUserAsync(userId, ct);
        if (user?.AgentThreadId is null) return [];

        try
        {
            var result = new List<ChatHistoryMessage>();
            var messages = _client.GetMessagesAsync(user.AgentThreadId,
                new MessageCollectionOptions { Order = MessageCollectionOrder.Ascending },
                cancellationToken: ct);

            await foreach (var msg in messages.WithCancellation(ct))
            {
                var role = msg.Role == MessageRole.Assistant ? "assistant" : "user";
                foreach (var content in msg.Content)
                {
                    if (content.Text is not null)
                    {
                        result.Add(new ChatHistoryMessage(role, content.Text, msg.CreatedAt));
                    }
                }

                if (result.Count >= limit) break;
            }

            return result;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Thread {ThreadId} no longer exists, resetting", user.AgentThreadId);
            user.AgentThreadId = null;
            await _store.UpsertUserAsync(user, ct);
            return [];
        }
    }

    public async Task ClearHistoryAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _store.GetUserAsync(userId, ct);
        if (user is null) return;

        // Delete old thread if exists
        if (user.AgentThreadId is not null)
        {
            try
            {
                await _client.DeleteThreadAsync(user.AgentThreadId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thread {ThreadId}", user.AgentThreadId);
            }
        }

        // Create fresh thread
        var newThread = await _client.CreateThreadAsync(cancellationToken: ct);
        user.AgentThreadId = newThread.Value.Id;
        await _store.UpsertUserAsync(user, ct);
    }

    private async Task<string> GetOrCreateThreadAsync(User user, CancellationToken ct)
    {
        if (user.AgentThreadId is not null)
        {
            try
            {
                await _client.GetThreadAsync(user.AgentThreadId, ct);
                return user.AgentThreadId;
            }
            catch (ClientResultException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Thread {ThreadId} no longer exists, creating new one", user.AgentThreadId);
            }
        }

        var thread = await _client.CreateThreadAsync(cancellationToken: ct);
        user.AgentThreadId = thread.Value.Id;
        await _store.UpsertUserAsync(user, ct);
        return thread.Value.Id;
    }

    private static string? BuildAdditionalInstructions(User? user)
    {
        if (user is null) return null;

        var sb = new StringBuilder();
        sb.AppendLine("## User Profile");
        if (user.Allergies.Length > 0) sb.AppendLine($"- Allergies: {string.Join(", ", user.Allergies)}");
        if (user.GutConditions.Length > 0)
            sb.AppendLine($"- Gut conditions: {string.Join(", ", user.GutConditions)}");
        if (user.DietaryPreferences.Length > 0)
            sb.AppendLine($"- Dietary preferences: {string.Join(", ", user.DietaryPreferences)}");
        sb.AppendLine(
            $"- Daily goals: {user.DailyCalorieGoal} cal, {user.DailyProteinGoalG}g protein, {user.DailyCarbGoalG}g carbs, {user.DailyFatGoalG}g fat, {user.DailyFiberGoalG}g fiber");
        if (!string.IsNullOrEmpty(user.TimezoneId)) sb.AppendLine($"- Timezone: {user.TimezoneId}");

        return sb.ToString();
    }

    private async Task<string> ExecuteToolAsync(Guid userId, string functionName, string functionArguments,
        CancellationToken ct)
    {
        try
        {
            var args = string.IsNullOrEmpty(functionArguments)
                ? new JsonElement()
                : JsonDocument.Parse(functionArguments).RootElement;

            return functionName switch
            {
                "search_foods" => await ExecuteSearchFoods(args, ct),
                "get_food_safety" => await ExecuteGetFoodSafety(userId, args, ct),
                "get_fodmap_assessment" => await ExecuteGetFodmap(args, ct),
                "log_meal" => await ExecuteLogMeal(userId, args, _logger.IsEnabled(LogLevel.Debug) ? functionArguments : null, ct),
                "log_symptom" => await ExecuteLogSymptom(userId, args, ct),
                "get_todays_meals" => await ExecuteGetTodaysMeals(userId, ct),
                "get_trigger_foods" => await ExecuteGetTriggerFoods(userId, args, ct),
                "get_symptom_history" => await ExecuteGetSymptomHistory(userId, args, ct),
                "get_nutrition_summary" => await ExecuteGetNutritionSummary(userId, ct),
                "get_elimination_diet_status" => await ExecuteGetEliminationDietStatus(userId, ct),
                _ => $"Unknown tool: {functionName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool execution failed: {Tool}", functionName);
            return $"Error executing {functionName}: {ex.Message}";
        }
    }

    private async Task<string> ExecuteSearchFoods(JsonElement args, CancellationToken ct)
    {
        var query = QuerySanitizer.Sanitize(args.GetProperty("query").GetString()!);
        var results = await _foodApi.SearchAsync(query, ct);
        var summary = results.Take(10).Select((f, i) => new
        {
            index = i + 1,
            id = f.Id,
            name = f.Name,
            brand = f.Brand,
            dataSource = f.DataSource,
            calories100g = f.Calories100g,
            protein100g = f.Protein100g,
            carbs100g = f.Carbs100g,
            fat100g = f.Fat100g,
            fiber100g = f.Fiber100g,
            servingSize = f.ServingSize,
            matchConfidence = f.MatchConfidence,
            ingredients = f.Ingredients?.Length > 120 ? f.Ingredients[..120] + "..." : f.Ingredients
        });
        return JsonSerializer.Serialize(new { results = summary }, JsonOpts);
    }

    private async Task<string> ExecuteGetFoodSafety(Guid userId, JsonElement args, CancellationToken ct)
    {
        var id = Guid.Parse(args.GetProperty("food_product_id").GetString()!);
        var product = await _store.GetFoodProductAsync(id, ct);
        if (product is null) return "Food product not found.";

        var dto = await BuildFoodProductDto(product, ct);
        var fodmap = _fodmapService.Assess(dto);
        var gutRisk = _gutRiskService.Assess(dto);
        var score = await _scoringService.ScoreAsync(dto, userId, _store);

        return JsonSerializer.Serialize(new
        {
            product = new { product.Name, product.Brand, product.Ingredients },
            fodmap = new { fodmap.FodmapScore, fodmap.FodmapRating, fodmap.Summary },
            gutRisk = new { gutRisk.GutScore, gutRisk.GutRating, gutRisk.Summary },
            personalizedScore = new { score.CompositeScore, score.Rating, score.Summary }
        }, JsonOpts);
    }

    private async Task<string> ExecuteGetFodmap(JsonElement args, CancellationToken ct)
    {
        var id = Guid.Parse(args.GetProperty("food_product_id").GetString()!);
        var product = await _store.GetFoodProductAsync(id, ct);
        if (product is null) return "Food product not found.";

        var dto = await BuildFoodProductDto(product, ct);
        var fodmap = _fodmapService.Assess(dto);
        return JsonSerializer.Serialize(new
        {
            fodmap.FodmapScore,
            fodmap.FodmapRating,
            fodmap.TriggerCount,
            triggers = fodmap.Triggers.Select(t => new { t.Name, t.Category, t.Severity, t.Explanation }),
            fodmap.Summary
        }, JsonOpts);
    }

    private async Task<string> ExecuteLogMeal(Guid userId, JsonElement args, string? rawArgs, CancellationToken ct)
    {
        if (rawArgs is not null) _logger.LogDebug("log_meal called with: {Args}", rawArgs);
        var mealTypeStr = args.TryGetProperty("meal_type", out var mtProp) ? mtProp.GetString() ?? "Snack" : "Snack";
        var mealType = Enum.TryParse<MealType>(mealTypeStr, true, out var mt) ? mt : MealType.Snack;

        var mealItems = new List<MealItem>();
        var mealId = Guid.NewGuid();
        var originalParts = new List<string>();

        // New path: structured items array with optional food_product_ids
        if (args.TryGetProperty("items", out var itemsArr) && itemsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsArr.EnumerateArray())
            {
                var servings = item.TryGetProperty("servings", out var sv) && sv.ValueKind == JsonValueKind.Number
                    ? sv.GetDecimal() : 1m;
                var itemName = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;

                // Try to resolve from food product ID first
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
                            Calories = (product.Calories100g ?? 0) * factor,
                            ProteinG = (product.Protein100g ?? 0) * factor,
                            CarbsG = (product.Carbs100g ?? 0) * factor,
                            FatG = (product.Fat100g ?? 0) * factor,
                            FiberG = (product.Fiber100g ?? 0) * factor,
                            SugarG = (product.Sugar100g ?? 0) * factor,
                            SodiumMg = (product.Sodium100g ?? 0) * factor,
                        });
                        originalParts.Add(itemName ?? product.Name);
                        continue;
                    }
                }

                // Fallback: parse the item name via nutrition API
                if (!string.IsNullOrEmpty(itemName))
                {
                    var parsed = await _nutritionApi.ParseNaturalLanguageAsync(itemName, ct);
                    foreach (var p in parsed)
                    {
                        mealItems.Add(new MealItem
                        {
                            Id = Guid.NewGuid(),
                            MealLogId = mealId,
                            FoodName = p.Name,
                            Servings = servings * (p.ServingQuantity ?? 1m),
                            ServingUnit = "serving",
                            ServingWeightG = p.ServingWeightG * servings,
                            Calories = p.Calories * servings,
                            ProteinG = p.ProteinG * servings,
                            CarbsG = p.CarbsG * servings,
                            FatG = p.FatG * servings,
                            FiberG = p.FiberG * servings,
                            SugarG = p.SugarG * servings,
                            SodiumMg = p.SodiumMg * servings,
                            CholesterolMg = p.CholesterolMg * servings,
                            SaturatedFatG = p.SaturatedFatG * servings,
                            PotassiumMg = p.PotassiumMg * servings,
                        });
                        originalParts.Add(p.Name);
                    }
                }
            }
        }

        // Legacy fallback: free-text description
        if (mealItems.Count == 0 && args.TryGetProperty("description", out var descProp)
            && descProp.GetString() is { Length: > 0 } description)
        {
            var parsed = await _nutritionApi.ParseNaturalLanguageAsync(description, ct);
            foreach (var p in parsed)
            {
                mealItems.Add(new MealItem
                {
                    Id = Guid.NewGuid(),
                    MealLogId = mealId,
                    FoodName = p.Name,
                    Servings = p.ServingQuantity ?? 1m,
                    ServingUnit = "serving",
                    ServingWeightG = p.ServingWeightG,
                    Calories = p.Calories,
                    ProteinG = p.ProteinG,
                    CarbsG = p.CarbsG,
                    FatG = p.FatG,
                    FiberG = p.FiberG,
                    SugarG = p.SugarG,
                    SodiumMg = p.SodiumMg,
                    CholesterolMg = p.CholesterolMg,
                    SaturatedFatG = p.SaturatedFatG,
                    PotassiumMg = p.PotassiumMg
                });
                originalParts.Add(p.Name);
            }
        }

        if (mealItems.Count == 0)
            return "Could not resolve any food items from the provided input.";

        var meal = new MealLog
        {
            Id = mealId,
            UserId = userId,
            MealType = mealType,
            LoggedAt = DateTime.UtcNow,
            OriginalText = string.Join(", ", originalParts),
            TotalCalories = mealItems.Sum(i => i.Calories),
            TotalProteinG = mealItems.Sum(i => i.ProteinG),
            TotalCarbsG = mealItems.Sum(i => i.CarbsG),
            TotalFatG = mealItems.Sum(i => i.FatG)
        };

        await _store.UpsertMealLogAsync(meal, ct);
        await _store.UpsertMealItemsAsync(userId, meal.Id, mealItems, ct);

        return JsonSerializer.Serialize(new
        {
            id = meal.Id,
            mealType = meal.MealType.ToString(),
            totalCalories = meal.TotalCalories,
            totalProteinG = meal.TotalProteinG,
            totalCarbsG = meal.TotalCarbsG,
            totalFatG = meal.TotalFatG,
            totalFiberG = mealItems.Sum(i => i.FiberG),
            items = mealItems.Select(i => new { i.FoodName, i.Calories, i.ProteinG, i.CarbsG, i.FatG, i.FiberG })
        }, JsonOpts);
    }

    private async Task<string> ExecuteLogSymptom(Guid userId, JsonElement args, CancellationToken ct)
    {
        var symptomName = args.GetProperty("symptom_name").GetString()!;
        var severity = args.GetProperty("severity").GetInt32();
        var notes = args.TryGetProperty("notes", out var n) ? n.GetString() : null;

        var types = await _store.GetAllSymptomTypesAsync(ct);
        var type = types.FirstOrDefault(t =>
            t.Name.Equals(symptomName, StringComparison.OrdinalIgnoreCase));
        if (type is null)
            return
                $"Unknown symptom type: {symptomName}. Available: {string.Join(", ", types.Select(t => t.Name))}";

        var symptom = new SymptomLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SymptomTypeId = type.Id,
            Severity = Math.Clamp(severity, 1, 10),
            OccurredAt = DateTime.UtcNow,
            Notes = notes
        };

        await _store.UpsertSymptomLogAsync(symptom, ct);
        return JsonSerializer.Serialize(new { id = symptom.Id, symptom = type.Name, severity = symptom.Severity },
            JsonOpts);
    }

    private async Task<string> ExecuteGetTodaysMeals(Guid userId, CancellationToken ct)
    {
        var user = await _store.GetUserAsync(userId, ct);
        var (rangeStart, rangeEnd) = GetUserTodayUtcRange(user);
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

    private async Task<string> ExecuteGetTriggerFoods(Guid userId, JsonElement args, CancellationToken ct)
    {
        var days = args.ValueKind != JsonValueKind.Undefined && args.TryGetProperty("days", out var d)
            ? d.GetInt32()
            : 30;
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
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
            .OrderByDescending(t => t.avgSeverity)
            .Take(10);
        return JsonSerializer.Serialize(triggers, JsonOpts);
    }

    private async Task<string> ExecuteGetSymptomHistory(Guid userId, JsonElement args, CancellationToken ct)
    {
        var days = args.ValueKind != JsonValueKind.Undefined && args.TryGetProperty("days", out var d)
            ? d.GetInt32()
            : 7;
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var symptoms = await _store.GetSymptomLogsByDateRangeAsync(userId, from, to, ct);

        foreach (var s in symptoms)
            s.SymptomType = await _store.GetSymptomTypeAsync(s.SymptomTypeId, ct) ??
                            new SymptomType { Name = "Unknown" };

        var summary = symptoms.OrderByDescending(s => s.OccurredAt).Take(20).Select(s => new
        {
            symptom = s.SymptomType.Name,
            s.Severity,
            s.OccurredAt,
            s.Notes
        });
        return JsonSerializer.Serialize(summary, JsonOpts);
    }

    private async Task<string> ExecuteGetNutritionSummary(Guid userId, CancellationToken ct)
    {
        var user = await _store.GetUserAsync(userId, ct);
        var (rangeStart, rangeEnd) = GetUserTodayUtcRange(user);
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

    private async Task<string> ExecuteGetEliminationDietStatus(Guid userId, CancellationToken ct)
    {
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

    private static (DateTime UtcStart, DateTime UtcEnd) GetUserTodayUtcRange(User? user)
    {
        TimeZoneInfo tz;
        try
        {
            tz = !string.IsNullOrEmpty(user?.TimezoneId)
                ? TimeZoneInfo.FindSystemTimeZoneById(user.TimezoneId)
                : TimeZoneInfo.Utc;
        }
        catch (TimeZoneNotFoundException)
        {
            tz = TimeZoneInfo.Utc;
        }

        var nowInUserTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var localToday = nowInUserTz.Date; // midnight local
        var localTomorrow = localToday.AddDays(1);

        // Convert local day boundaries back to UTC
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday, DateTimeKind.Unspecified), tz);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTomorrow, DateTimeKind.Unspecified), tz)
            .AddTicks(-1);

        return (utcStart, utcEnd);
    }

    private async Task<FoodProductDto> BuildFoodProductDto(FoodProduct product, CancellationToken ct)
    {
        var additiveIds = product.FoodProductAdditiveIds ?? [];
        var allAdditives = await _store.GetAllFoodAdditivesAsync(ct);
        var additiveDtos = additiveIds.Select(aid =>
        {
            var a = allAdditives.FirstOrDefault(x => x.Id == aid);
            return new FoodAdditiveDto
            {
                Id = a?.Id ?? aid,
                Name = a?.Name ?? "Unknown",
                CspiRating = a?.CspiRating.ToString() ?? "Unknown",
                UsRegulatoryStatus = a?.UsRegulatoryStatus.ToString() ?? "Unknown",
                EuRegulatoryStatus = a?.EuRegulatoryStatus.ToString() ?? "Unknown",
                SafetyRating = a?.SafetyRating.ToString() ?? "Unknown",
                Category = a?.Category ?? "Unknown",
                ENumber = a?.ENumber,
                HealthConcerns = a?.HealthConcerns ?? ""
            };
        }).ToList();

        return new FoodProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Brand = product.Brand,
            Barcode = product.Barcode,
            Ingredients = product.Ingredients,
            NovaGroup = product.NovaGroup,
            NutriScore = product.NutriScore,
            AllergensTags = product.AllergensTags ?? [],
            Calories100g = product.Calories100g,
            Protein100g = product.Protein100g,
            Carbs100g = product.Carbs100g,
            Fat100g = product.Fat100g,
            Fiber100g = product.Fiber100g,
            Sugar100g = product.Sugar100g,
            Sodium100g = product.Sodium100g,
            ServingSize = product.ServingSize,
            Additives = additiveDtos,
            AdditivesTags = additiveDtos.Where(a => a.ENumber != null)
                .Select(a => $"en:{a.ENumber!.ToLowerInvariant()}").ToList()
        };
    }
}
