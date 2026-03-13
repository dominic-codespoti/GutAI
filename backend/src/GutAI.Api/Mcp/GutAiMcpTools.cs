using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using ModelContextProtocol.Server;

namespace GutAI.Api.Mcp;

[McpServerToolType]
public class GutAiMcpTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool, Description("Search the food database by name. Returns up to 10 matching food products with IDs, nutrition, brand, data source, and match confidence.")]
    public static async Task<string> SearchFoods(
        IFoodApiService foodApi,
        [Description("Food name to search for")] string query,
        CancellationToken ct)
    {
        var sanitized = QuerySanitizer.Sanitize(query);
        var results = await foodApi.SearchAsync(sanitized, ct);
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

    [McpServerTool, Description("Get a FODMAP assessment for a food product.")]
    public static async Task<string> GetFodmapAssessment(
        ITableStore store,
        FodmapService fodmapService,
        [Description("The food product ID (GUID)")] string foodProductId,
        CancellationToken ct)
    {
        var id = Guid.Parse(foodProductId);
        var product = await store.GetFoodProductAsync(id, ct);
        if (product is null) return "Food product not found.";

        var dto = await BuildDto(product, store, ct);
        var fodmap = fodmapService.Assess(dto);
        return JsonSerializer.Serialize(new
        {
            fodmap.FodmapScore,
            fodmap.FodmapRating,
            fodmap.TriggerCount,
            triggers = fodmap.Triggers.Select(t => new { t.Name, t.Category, t.Severity, t.Explanation }),
            fodmap.Summary
        }, JsonOpts);
    }

    [McpServerTool, Description("Get a safety report for a food product including FODMAP assessment, gut risk, additive analysis, and personalized score.")]
    public static async Task<string> GetFoodSafety(
        HttpContext httpContext,
        ITableStore store,
        FodmapService fodmapService,
        GutRiskService gutRiskService,
        PersonalizedScoringService scoringService,
        [Description("The food product ID (GUID)")] string foodProductId,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var id = Guid.Parse(foodProductId);
        var product = await store.GetFoodProductAsync(id, ct);
        if (product is null) return "Food product not found.";

        var dto = await BuildDto(product, store, ct);
        var fodmap = fodmapService.Assess(dto);
        var gutRisk = gutRiskService.Assess(dto);
        var score = await scoringService.ScoreAsync(dto, userId, store);

        return JsonSerializer.Serialize(new
        {
            product = new { product.Name, product.Brand, product.Ingredients },
            fodmap = new { fodmap.FodmapScore, fodmap.FodmapRating, fodmap.Summary },
            gutRisk = new { gutRisk.GutScore, gutRisk.GutRating, gutRisk.Summary },
            personalizedScore = new { score.CompositeScore, score.Rating, score.Summary }
        }, JsonOpts);
    }

    [McpServerTool, Description("Log a meal for the authenticated user. Supports structured items with food_product_id for accurate nutrition, or natural language fallback.")]
    public static async Task<string> LogMeal(
        HttpContext httpContext,
        ITableStore store,
        INutritionApiService nutritionApi,
        [Description("Meal type: Breakfast, Lunch, Dinner, or Snack")] string mealType,
        [Description("JSON array of items: [{\"food_product_id\":\"GUID\",\"name\":\"food name\",\"servings\":1}]. Use food_product_id from SearchFoods results when available.")] string? items,
        [Description("Fallback: natural language description of the meal. Only use when items is not provided.")] string? description,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var mt = Enum.TryParse<MealType>(mealType, true, out var parsed) ? parsed : MealType.Snack;
        var mealId = Guid.NewGuid();
        var mealItems = new List<MealItem>();
        var originalParts = new List<string>();

        // Structured items path (preferred — gives accurate nutrition from food DB)
        if (!string.IsNullOrEmpty(items))
        {
            using var doc = JsonDocument.Parse(items);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var servings = item.TryGetProperty("servings", out var sv) && sv.ValueKind == JsonValueKind.Number
                    ? sv.GetDecimal() : 1m;
                var itemName = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;

                if (item.TryGetProperty("food_product_id", out var fpId) && fpId.GetString() is { } fpIdStr
                    && Guid.TryParse(fpIdStr, out var productId))
                {
                    var product = await store.GetFoodProductAsync(productId, ct);
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

                // Fallback: parse item name via nutrition API
                if (!string.IsNullOrEmpty(itemName))
                {
                    var parsedItems = await nutritionApi.ParseNaturalLanguageAsync(itemName, ct);
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

        // Legacy free-text fallback
        if (mealItems.Count == 0 && !string.IsNullOrEmpty(description))
        {
            var parsedItems = await nutritionApi.ParseNaturalLanguageAsync(description, ct);
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
            MealType = mt,
            LoggedAt = DateTime.UtcNow,
            OriginalText = string.Join(", ", originalParts),
            TotalCalories = mealItems.Sum(i => i.Calories),
            TotalProteinG = mealItems.Sum(i => i.ProteinG),
            TotalCarbsG = mealItems.Sum(i => i.CarbsG),
            TotalFatG = mealItems.Sum(i => i.FatG)
        };

        await store.UpsertMealLogAsync(meal, ct);
        await store.UpsertMealItemsAsync(userId, meal.Id, mealItems, ct);

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

    [McpServerTool, Description("Record a symptom for the authenticated user.")]
    public static async Task<string> LogSymptom(
        HttpContext httpContext,
        ITableStore store,
        [Description("Symptom name, e.g. Bloating, Nausea, Gas")] string symptomName,
        [Description("Severity from 1 (mild) to 10 (severe)")] int severity,
        [Description("Optional notes")] string? notes,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var types = await store.GetAllSymptomTypesAsync(ct);
        var type = types.FirstOrDefault(t => t.Name.Equals(symptomName, StringComparison.OrdinalIgnoreCase));
        if (type is null)
            return $"Unknown symptom: {symptomName}. Available: {string.Join(", ", types.Select(t => t.Name))}";

        var symptom = new SymptomLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SymptomTypeId = type.Id,
            Severity = Math.Clamp(severity, 1, 10),
            OccurredAt = DateTime.UtcNow,
            Notes = notes
        };
        await store.UpsertSymptomLogAsync(symptom, ct);
        return JsonSerializer.Serialize(new { id = symptom.Id, symptom = type.Name, severity = symptom.Severity }, JsonOpts);
    }

    [McpServerTool, Description("Get all meals the authenticated user logged today with items and nutrition info.")]
    public static async Task<string> GetTodaysMeals(
        HttpContext httpContext,
        ITableStore store,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var user = await store.GetUserAsync(userId, ct);
        var (rangeStart, rangeEnd) = GetUserTodayUtcRange(user);

        var meals = await store.GetMealLogsByDateRangeAsync(userId,
            DateOnly.FromDateTime(rangeStart), DateOnly.FromDateTime(rangeEnd), ct);
        meals = meals.Where(m => m.LoggedAt >= rangeStart && m.LoggedAt <= rangeEnd).ToList();
        foreach (var m in meals) m.Items = await store.GetMealItemsAsync(userId, m.Id, ct);

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

    [McpServerTool, Description("Get the authenticated user's trigger foods from correlation analysis.")]
    public static async Task<string> GetTriggerFoods(
        HttpContext httpContext,
        ICorrelationEngine correlationEngine,
        [Description("Days to look back, default 30")] int? days,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-(days ?? 30)));
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var correlations = await correlationEngine.ComputeCorrelationsAsync(userId, from, to, ct);

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

    [McpServerTool, Description("Get the authenticated user's recent symptom history.")]
    public static async Task<string> GetSymptomHistory(
        HttpContext httpContext,
        ITableStore store,
        [Description("Days to look back, default 7")] int? days,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-(days ?? 7)));
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, from, to, ct);
        foreach (var s in symptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId, ct) ?? new SymptomType { Name = "Unknown" };

        return JsonSerializer.Serialize(symptoms.OrderByDescending(s => s.OccurredAt).Take(20).Select(s => new
        {
            symptom = s.SymptomType.Name,
            s.Severity,
            s.OccurredAt,
            s.Notes
        }), JsonOpts);
    }

    [McpServerTool, Description("Get today's nutrition summary vs the authenticated user's goals.")]
    public static async Task<string> GetNutritionSummary(
        HttpContext httpContext,
        ITableStore store,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var user = await store.GetUserAsync(userId, ct);
        var (rangeStart, rangeEnd) = GetUserTodayUtcRange(user);

        var meals = await store.GetMealLogsByDateRangeAsync(userId,
            DateOnly.FromDateTime(rangeStart), DateOnly.FromDateTime(rangeEnd), ct);
        meals = meals.Where(m => m.LoggedAt >= rangeStart && m.LoggedAt <= rangeEnd).ToList();
        foreach (var m in meals) m.Items = await store.GetMealItemsAsync(userId, m.Id, ct);

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

    [McpServerTool, Description("Get the authenticated user's elimination diet status.")]
    public static async Task<string> GetEliminationDietStatus(
        HttpContext httpContext,
        ITableStore store,
        IFoodDiaryAnalysisService diaryService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var result = await diaryService.GetEliminationStatusAsync(userId, store);
        return JsonSerializer.Serialize(new
        {
            result.Phase,
            result.FoodsToEliminate,
            result.SafeFoods,
            result.Recommendations,
            result.Summary
        }, JsonOpts);
    }

    [McpServerTool, Description("Get the authenticated user's profile including allergies, gut conditions, dietary preferences, and daily nutrition goals.")]
    public static async Task<string> GetUserProfile(
        HttpContext httpContext,
        ITableStore store,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var user = await store.GetUserAsync(userId, ct);
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

    // ── Helpers ──────────────────────────────────────────────

    private static Guid GetUserId(HttpContext httpContext) =>
        Guid.Parse(httpContext.User.FindFirstValue("sub")!);

    /// <summary>
    /// Returns the UTC start/end of "today" in the user's local timezone.
    /// Falls back to UTC if the user has no timezone configured.
    /// </summary>
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
        var localToday = nowInUserTz.Date;
        var localTomorrow = localToday.AddDays(1);

        var utcStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday, DateTimeKind.Unspecified), tz);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTomorrow, DateTimeKind.Unspecified), tz)
            .AddTicks(-1);

        return (utcStart, utcEnd);
    }

    private static async Task<FoodProductDto> BuildDto(FoodProduct product, ITableStore store, CancellationToken ct)
    {
        var additiveIds = product.FoodProductAdditiveIds ?? [];
        var allAdditives = await store.GetAllFoodAdditivesAsync(ct);
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
