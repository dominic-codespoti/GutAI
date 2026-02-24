using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;

public static class InsightEndpoints
{
    public static RouteGroupBuilder MapInsightEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/correlations", GetCorrelations);
        group.MapGet("/nutrition-trends", GetNutritionTrends);
        group.MapGet("/additive-exposure", GetAdditiveExposure);
        group.MapGet("/trigger-foods", GetTriggerFoods);
        group.MapGet("/food-diary-analysis", GetFoodDiaryAnalysis);
        group.MapGet("/elimination-diet/status", GetEliminationDietStatus);
        return group;
    }

    static Guid GetUserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);

    static async Task<IResult> GetCorrelations(DateOnly? from, DateOnly? to, ClaimsPrincipal principal, ITableStore store, ICorrelationEngine correlationEngine)
    {
        var userId = GetUserId(principal);
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await correlationEngine.ComputeCorrelationsAsync(userId, fromDate, toDate);
        return Results.Ok(result);
    }

    static async Task<IResult> GetNutritionTrends(DateOnly? from, DateOnly? to, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var meals = await store.GetMealLogsByDateRangeAsync(userId, fromDate, toDate);
        foreach (var m in meals)
            m.Items = await store.GetMealItemsAsync(userId, m.Id);
        var grouped = meals.GroupBy(m => m.LoggedAt.Date)
            .Select(g => new
            {
                date = DateOnly.FromDateTime(g.Key),
                calories = g.Sum(m => m.TotalCalories),
                protein = g.Sum(m => m.TotalProteinG),
                carbs = g.Sum(m => m.TotalCarbsG),
                fat = g.Sum(m => m.TotalFatG),
                fiber = g.Sum(m => m.Items.Sum(i => i.FiberG)),
                sugar = g.Sum(m => m.Items.Sum(i => i.SugarG)),
                sodium = g.Sum(m => m.Items.Sum(i => i.SodiumMg)),
                mealCount = g.Count()
            });
        return Results.Ok(grouped.OrderBy(x => x.date));
    }

    static async Task<IResult> GetAdditiveExposure(DateOnly? from, DateOnly? to, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var meals = await store.GetMealLogsByDateRangeAsync(userId, fromDate, toDate);
        var allAdditives = await store.GetAllFoodAdditivesAsync();
        var exposure = new Dictionary<int, int>();
        foreach (var m in meals)
        {
            var items = await store.GetMealItemsAsync(userId, m.Id);
            foreach (var item in items)
            {
                if (item.FoodProductId.HasValue)
                {
                    var product = await store.GetFoodProductAsync(item.FoodProductId.Value);
                    if (product?.FoodProductAdditiveIds != null)
                    {
                        foreach (var additiveId in product.FoodProductAdditiveIds)
                            exposure[additiveId] = exposure.GetValueOrDefault(additiveId) + 1;
                    }
                }
            }
        }
        var result = exposure.Select(kvp =>
        {
            var a = allAdditives.FirstOrDefault(x => x.Id == kvp.Key);
            return new
            {
                additive = a?.Name ?? "Unknown",
                cspiRating = a?.CspiRating.ToString() ?? "Unknown",
                count = kvp.Value
            };
        });
        return Results.Ok(result.OrderByDescending(x => x.count));
    }

    static async Task<IResult> GetTriggerFoods(DateOnly? from, DateOnly? to, ClaimsPrincipal principal, ITableStore store, ICorrelationEngine correlationEngine)
    {
        var userId = GetUserId(principal);
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var correlations = await correlationEngine.ComputeCorrelationsAsync(userId, fromDate, toDate);
        var triggers = correlations
            .Where(c => c.Occurrences >= 2 && c.AverageSeverity >= 4)
            .GroupBy(c => c.FoodOrAdditive)
            .Select(g => new
            {
                food = g.Key,
                symptoms = g.Select(c => c.SymptomName).Distinct().ToList(),
                totalOccurrences = g.Sum(c => c.Occurrences),
                avgSeverity = g.Average(c => (double)c.AverageSeverity),
                worstConfidence = g.Max(c => c.Confidence)
            })
            .OrderByDescending(t => t.avgSeverity);
        return Results.Ok(triggers);
    }

    static async Task<IResult> GetFoodDiaryAnalysis(DateOnly? from, DateOnly? to, ClaimsPrincipal principal, ITableStore store, IFoodDiaryAnalysisService analysisService)
    {
        var userId = GetUserId(principal);
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await analysisService.AnalyzeAsync(userId, fromDate, toDate, store);
        return Results.Ok(result);
    }

    static async Task<IResult> GetEliminationDietStatus(ClaimsPrincipal principal, ITableStore store, IFoodDiaryAnalysisService analysisService)
    {
        var userId = GetUserId(principal);
        var result = await analysisService.GetEliminationStatusAsync(userId, store);
        return Results.Ok(result);
    }
}
