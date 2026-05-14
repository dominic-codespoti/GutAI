#pragma warning disable OPENAI001

using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OpenAI.Assistants;

namespace GutAI.Infrastructure.Services;

public class AzureOpenAIChatService : IChatService
{
    private readonly AssistantClient _client;
    private readonly AssistantFactory _assistantFactory;
    private readonly ITableStore _store;
    private readonly ICorrelationEngine _correlationEngine;
    private readonly IFoodDiaryAnalysisService _diaryService;
    private readonly IFoodApiService _foodApi;
    private readonly INutritionApiService _nutritionApi;
    private readonly FodmapService _fodmapService;
    private readonly GutRiskService _gutRiskService;
    private readonly PersonalizedScoringService _scoringService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AzureOpenAIChatService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string HistoryCachePrefix = "chat_history_";

    public AzureOpenAIChatService(
        AssistantClient client,
        AssistantFactory assistantFactory,
        ITableStore store,
        ICorrelationEngine correlationEngine,
        IFoodDiaryAnalysisService diaryService,
        IFoodApiService foodApi,
        INutritionApiService nutritionApi,
        FodmapService fodmapService,
        GutRiskService gutRiskService,
        PersonalizedScoringService scoringService,
        IMemoryCache cache,
        ILogger<AzureOpenAIChatService> logger)
    {
        _client = client;
        _assistantFactory = assistantFactory;
        _store = store;
        _correlationEngine = correlationEngine;
        _diaryService = diaryService;
        _foodApi = foodApi;
        _nutritionApi = nutritionApi;
        _fodmapService = fodmapService;
        _gutRiskService = gutRiskService;
        _scoringService = scoringService;
        _cache = cache;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamResponseAsync(
        Guid userId, string message, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var assistantId = await _assistantFactory.GetAssistantIdAsync(ct);
        var user = await _store.GetUserAsync(userId, ct);
        var threadId = await GetOrCreateThreadAsync(user!, ct);
        _logger.LogDebug("Chat stream: user={UserId} thread={ThreadId} messageLen={Len}", userId, threadId, message.Length);

        // Emit thread ID so frontend can verify same thread across requests
        yield return new ChatStreamEvent(ThreadId: threadId);

        // Add user message to thread
        await _client.CreateMessageAsync(threadId, MessageRole.User, [MessageContent.FromText(message)],
            cancellationToken: ct);

        // Build per-user instructions overlay with conversation history from cache
        var historyKey = HistoryCachePrefix + userId;
        if (!_cache.TryGetValue(historyKey, out List<(string Role, string Content)>? history))
        {
            history = [];
        }
        history!.Add(("user", message));

        var additionalInstructions = BuildAdditionalInstructionsWithHistory(user, history);

        var runOptions = new RunCreationOptions
        {
            AdditionalInstructions = additionalInstructions
        };

        // Stream the run with tool call handling
        var stream = _client.CreateRunStreamingAsync(
            threadId, assistantId, runOptions, ct);

        var toolOutputs = new List<ToolOutput>();
        string? currentRunId = null;
        var assistantContent = new StringBuilder();

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
                        assistantContent.Append(contentUpdate.Text);
                        yield return new ChatStreamEvent(Content: contentUpdate.Text);
                    }
                }
            }

            if (toolOutputs.Count > 0 && currentRunId is not null)
            {
                _logger.LogDebug("Submitting {Count} tool outputs for run {RunId}", toolOutputs.Count, currentRunId);
                foreach (var output in toolOutputs)
                {
                    _logger.LogTrace("Tool output: {Id} => {Len} chars", output.ToolCallId, output.Output.Length);
                }

                stream = _client.SubmitToolOutputsToRunStreamingAsync(threadId, currentRunId,
                    toolOutputs, ct);
            }
            else
            {
                break;
            }
        }

        // Store the assistant response in cache for future requests
        if (assistantContent.Length > 0)
        {
            history.Add(("assistant", assistantContent.ToString()));
        }
        _cache.Set(historyKey, history, TimeSpan.FromHours(2));
    }

    public async Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId, int limit = 50,
        CancellationToken ct = default)
    {
        var user = await _store.GetUserAsync(userId, ct);

        // Try fetching from the cache first
        var historyKey = HistoryCachePrefix + userId;
        if (_cache.TryGetValue(historyKey, out List<(string Role, string Content)>? cachedHistory) && cachedHistory is { Count: > 0 })
        {
            return cachedHistory.TakeLast(limit).Select((m, i) => new ChatHistoryMessage(
                m.Role,
                m.Content,
                DateTimeOffset.UtcNow.AddSeconds(i)
            )).ToList();
        }

        return [];
    }

    public async Task ClearHistoryAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _store.GetUserAsync(userId, ct);
        if (user is null) return;

        // Clear cached conversation history first — most reliable way to reset context
        _cache.Remove(HistoryCachePrefix + userId);
        _logger.LogInformation("Cleared history cache for user {UserId}", userId);

        // Delete old thread if exists
        if (user.AgentThreadId is not null)
        {
            try
            {
                await _client.DeleteThreadAsync(user.AgentThreadId, ct);
                _logger.LogInformation("Deleted old thread {ThreadId} for user {UserId}", user.AgentThreadId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thread {ThreadId} on clear", user.AgentThreadId);
            }
        }

        // Create fresh thread
        var newThread = await _client.CreateThreadAsync(cancellationToken: ct);
        user.AgentThreadId = newThread.Value.Id;
        await _store.UpsertUserAsync(user, ct);
        _logger.LogInformation("Created fresh thread {ThreadId} for user {UserId}", user.AgentThreadId, userId);
    }

    private async Task<string> GetOrCreateThreadAsync(User user, CancellationToken ct)
    {
        if (user.AgentThreadId is not null)
        {
            // Trust the saved thread ID — Azure OpenAI persists threads indefinitely.
            // If the thread was somehow deleted, CreateRunStreamingAsync will 404 and
            // the error will be handled by the exception middleware.
            return user.AgentThreadId;
        }

        var thread = await _client.CreateThreadAsync(cancellationToken: ct);
        user.AgentThreadId = thread.Value.Id;
        await _store.UpsertUserAsync(user, ct);
        _logger.LogInformation("Created new thread {ThreadId} for user {UserId}", user.AgentThreadId, user.Id);
        return user.AgentThreadId;
    }

    private static string? BuildAdditionalInstructionsWithHistory(User? user, List<(string Role, string Content)> history)
    {
        var sb = new StringBuilder();

        // User profile instructions
        if (user is not null)
        {
            sb.AppendLine("## User Profile");
            if (user.Allergies.Length > 0) sb.AppendLine($"- Allergies: {string.Join(", ", user.Allergies)}");
            if (user.GutConditions.Length > 0)
                sb.AppendLine($"- Gut conditions: {string.Join(", ", user.GutConditions)}");
            if (user.DietaryPreferences.Length > 0)
                sb.AppendLine($"- Dietary preferences: {string.Join(", ", user.DietaryPreferences)}");
            sb.AppendLine(
                $"- Daily goals: {user.DailyCalorieGoal} cal, {user.DailyProteinGoalG}g protein, {user.DailyCarbGoalG}g carbs, {user.DailyFatGoalG}g fat, {user.DailyFiberGoalG}g fiber");
            if (!string.IsNullOrEmpty(user.TimezoneId)) sb.AppendLine($"- Timezone: {user.TimezoneId}");
        }

        // Include conversation history from cache
        if (history.Count > 1)
        {
            sb.AppendLine("\n## Recent Conversation History");
            var contextMessages = history.Take(history.Count - 1).TakeLast(10);
            foreach (var (role, content) in contextMessages)
            {
                var label = role == "user" ? "User" : role == "assistant" ? "Assistant" : "System";
                var text = content.Length > 500 ? content[..500] + "..." : content;
                sb.AppendLine($"**{label}**: {text}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
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
                "get_user_profile" => await ExecuteGetUserProfile(userId, ct),
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
                sugar100g = f.Sugar100g,
                sodium100g = f.Sodium100g,
                servingSize = f.ServingSize,
                servingQuantity = f.ServingQuantity,
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

        var dto = await FoodDtoHelper.BuildFoodProductDto(product, _store, ct);
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

        var dto = await FoodDtoHelper.BuildFoodProductDto(product, _store, ct);
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
                if (string.IsNullOrWhiteSpace(itemName)) continue;

                // Check for explicit serving weight from the model (e.g. 1 egg = 50g)
                var explicitServingG = item.TryGetProperty("serving_weight_g", out var swProp) && swProp.ValueKind == JsonValueKind.Number
                    ? swProp.GetDecimal() : (decimal?)null;

                // Try to resolve from food product ID first
                if (item.TryGetProperty("food_product_id", out var fpIdEl) && fpIdEl.GetString() is { } fpIdStr3
                    && Guid.TryParse(fpIdStr3, out var pid) && pid != Guid.Empty)
                {
                    var product = await _store.GetFoodProductAsync(pid, ct);
                    if (product is not null)
                    {
                        // Use explicit serving weight if provided, otherwise fall back to product's default
                        var servingG = explicitServingG ?? (product.ServingQuantity is > 0 ? product.ServingQuantity.Value : ServingEstimator.EstimateDefaultServingG(itemName ?? product.Name));
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

                // Fallback: search food database for a canonical match
                else if (!string.IsNullOrEmpty(itemName))
                {
                    var sanitized = QuerySanitizer.Sanitize(itemName);
                    _logger.LogDebug("log_meal fallback: item='{Name}' sanitized='{Sanitized}' servings={Servings}", itemName, sanitized, servings);
                    if (!string.IsNullOrEmpty(sanitized))
                    {
                        var searchResults = await _foodApi.SearchAsync(sanitized, ct);
                        var bestMatch = searchResults.FirstOrDefault();
                        if (bestMatch is not null)
                        {
                            _logger.LogDebug("log_meal fallback: matched '{MatchName}' ({Cal} cal/100g) from {Source}", bestMatch.Name, bestMatch.Calories100g, bestMatch.DataSource);

                            // Persist to store for clickable food details
                            var persistedProductId = Guid.NewGuid();
                            try
                            {
                                var fp = new FoodProduct
                                {
                                    Id = persistedProductId,
                                    Name = bestMatch.Name,
                                    Barcode = bestMatch.Barcode,
                                    Brand = bestMatch.Brand,
                                    Ingredients = bestMatch.Ingredients,
                                    ImageUrl = bestMatch.ImageUrl,
                                    NovaGroup = bestMatch.NovaGroup,
                                    NutriScore = bestMatch.NutriScore,
                                    AllergensTags = bestMatch.AllergensTags ?? [],
                                    Calories100g = bestMatch.Calories100g,
                                    Protein100g = bestMatch.Protein100g,
                                    Carbs100g = bestMatch.Carbs100g,
                                    Fat100g = bestMatch.Fat100g,
                                    Fiber100g = bestMatch.Fiber100g,
                                    Sugar100g = bestMatch.Sugar100g,
                                    Sodium100g = bestMatch.Sodium100g,
                                    ServingSize = bestMatch.ServingSize,
                                    ServingQuantity = bestMatch.ServingQuantity,
                                    DataSource = bestMatch.DataSource ?? "GutAI",
                                    ExternalId = bestMatch.ExternalId ?? bestMatch.Barcode,
                                    CachedAt = DateTime.UtcNow,
                                    CacheTtlHours = 168
                                };
                                await _store.UpsertFoodProductAsync(fp, ct);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to persist FoodProduct for '{Name}'", bestMatch.Name);
                            }

                            var servingG = explicitServingG ?? (bestMatch.ServingQuantity is > 0 ? bestMatch.ServingQuantity.Value : ServingEstimator.EstimateDefaultServingG(itemName));
                            var factor = servings * servingG / 100m;
                            mealItems.Add(new MealItem
                            {
                                Id = Guid.NewGuid(),
                                MealLogId = mealId,
                                FoodName = bestMatch.Name,
                                FoodProductId = persistedProductId,
                                Servings = servings,
                                ServingUnit = bestMatch.ServingSize ?? "serving",
                                ServingWeightG = servingG * servings,
                                Calories = (bestMatch.Calories100g ?? 0) * factor,
                                ProteinG = (bestMatch.Protein100g ?? 0) * factor,
                                CarbsG = (bestMatch.Carbs100g ?? 0) * factor,
                                FatG = (bestMatch.Fat100g ?? 0) * factor,
                                FiberG = (bestMatch.Fiber100g ?? 0) * factor,
                                SugarG = (bestMatch.Sugar100g ?? 0) * factor,
                                SodiumMg = (bestMatch.Sodium100g ?? 0) * factor,
                            });
                            originalParts.Add(itemName);
                            continue;
                        }
                    }

                    // Last resort: parse via NLP
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
            LoggedAt = ParseLoggedAt(args),
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

    private static DateTime ParseLoggedAt(JsonElement args)
    {
        if (args.TryGetProperty("logged_at", out var laProp) && laProp.GetString() is { Length: > 0 } laStr
            && DateTime.TryParse(laStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        return DateTime.UtcNow;
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

    private async Task<string> ExecuteGetUserProfile(Guid userId, CancellationToken ct)
    {
        var user = await _store.GetUserAsync(userId, ct);
        if (user is null) return "User not found.";

        return JsonSerializer.Serialize(new
        {
            user.DisplayName,
            user.Allergies,
            user.DietaryPreferences,
            user.GutConditions,
            user.TimezoneId,
            goals = new
            {
                dailyCalories = user.DailyCalorieGoal,
                dailyProteinG = user.DailyProteinGoalG,
                dailyCarbsG = user.DailyCarbGoalG,
                dailyFatG = user.DailyFatGoalG,
                dailyFiberG = user.DailyFiberGoalG
            }
        }, JsonOpts);
    }
}
