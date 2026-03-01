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

    [McpServerTool, Description("Search the food database by name. Returns matching food products with nutrition info.")]
    public static async Task<string> SearchFoods(
        IFoodApiService foodApi,
        [Description("Food name to search for")] string query,
        CancellationToken ct)
    {
        var results = await foodApi.SearchAsync(query, ct);
        var summary = results.Take(5).Select(f => new
        {
            id = f.Id,
            name = f.Name,
            brand = f.Brand,
            calories100g = f.Calories100g,
            protein100g = f.Protein100g,
            carbs100g = f.Carbs100g,
            fat100g = f.Fat100g,
            servingSize = f.ServingSize
        });
        return JsonSerializer.Serialize(summary, JsonOpts);
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
            triggers = fodmap.Triggers.Select(t => new { t.Name, t.Category, t.Severity }),
            fodmap.Summary
        }, JsonOpts);
    }

    [McpServerTool, Description("Log a meal for the authenticated user using natural language.")]
    public static async Task<string> LogMeal(
        HttpContext httpContext,
        ITableStore store,
        INutritionApiService nutritionApi,
        [Description("Natural language description of the meal")] string description,
        [Description("Meal type: Breakfast, Lunch, Dinner, or Snack")] string mealType,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var mt = Enum.TryParse<MealType>(mealType, true, out var parsed) ? parsed : MealType.Snack;

        var items = await nutritionApi.ParseNaturalLanguageAsync(description, ct);
        if (items.Count == 0) return "Could not parse any food items.";

        var meal = new MealLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MealType = mt,
            LoggedAt = DateTime.UtcNow,
            OriginalText = description
        };

        var mealItems = items.Select(p => new MealItem
        {
            Id = Guid.NewGuid(),
            MealLogId = meal.Id,
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
        }).ToList();

        meal.TotalCalories = mealItems.Sum(i => i.Calories);
        meal.TotalProteinG = mealItems.Sum(i => i.ProteinG);
        meal.TotalCarbsG = mealItems.Sum(i => i.CarbsG);
        meal.TotalFatG = mealItems.Sum(i => i.FatG);

        await store.UpsertMealLogAsync(meal, ct);
        await store.UpsertMealItemsAsync(userId, meal.Id, mealItems, ct);

        return JsonSerializer.Serialize(new
        {
            id = meal.Id,
            mealType = meal.MealType.ToString(),
            totalCalories = meal.TotalCalories,
            items = mealItems.Select(i => new { i.FoodName, i.Calories })
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

    [McpServerTool, Description("Get all meals the authenticated user logged today.")]
    public static async Task<string> GetTodaysMeals(
        HttpContext httpContext,
        ITableStore store,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var meals = await store.GetMealLogsByDateAsync(userId, today, ct);
        foreach (var m in meals) m.Items = await store.GetMealItemsAsync(userId, m.Id, ct);

        return JsonSerializer.Serialize(meals.Select(m => new
        {
            mealType = m.MealType.ToString(),
            loggedAt = m.LoggedAt,
            totalCalories = m.TotalCalories,
            items = m.Items.Select(i => new { i.FoodName, i.Calories })
        }), JsonOpts);
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var meals = await store.GetMealLogsByDateAsync(userId, today, ct);
        foreach (var m in meals) m.Items = await store.GetMealItemsAsync(userId, m.Id, ct);

        return JsonSerializer.Serialize(new
        {
            totalCalories = meals.Sum(m => m.TotalCalories),
            totalProteinG = meals.Sum(m => m.TotalProteinG),
            totalCarbsG = meals.Sum(m => m.TotalCarbsG),
            totalFatG = meals.Sum(m => m.TotalFatG),
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

    private static Guid GetUserId(HttpContext httpContext) =>
        Guid.Parse(httpContext.User.FindFirstValue("sub")!);

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
            Ingredients = product.Ingredients,
            NovaGroup = product.NovaGroup,
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
            AdditivesTags = additiveDtos.Where(a => a.ENumber != null).Select(a => $"en:{a.ENumber!.ToLowerInvariant()}").ToList()
        };
    }
}
