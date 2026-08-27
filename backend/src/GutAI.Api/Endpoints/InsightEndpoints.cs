using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;

public static class InsightEndpoints
{
    public static RouteGroupBuilder MapInsightEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/correlations", GetCorrelations);
        group.MapGet("/nutrition-trends", GetNutritionTrends);
        group.MapGet("/nutrition-by-meal-type", GetNutritionByMealType);
        group.MapGet("/additive-exposure", GetAdditiveExposure);
        group.MapGet("/trigger-foods", GetTriggerFoods);
        group.MapGet("/food-diary-analysis", GetFoodDiaryAnalysis);
        group.MapGet("/elimination-diet/status", GetEliminationDietStatus);
        return group;
    }

    static Guid GetUserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);

    static async Task<IResult> GetCorrelations(DateOnly? from, DateOnly? to, string? timezoneId, ClaimsPrincipal principal, ITableStore store, ICorrelationEngine correlationEngine)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var fromDate = from ?? todayLocal.AddDays(-30);
        var toDate = to ?? todayLocal;
        var result = await correlationEngine.ComputeCorrelationsAsync(userId, fromDate, toDate, default, timezoneId);
        return Results.Ok(result);
    }

    static async Task<IResult> GetNutritionTrends(
        DateOnly? from,
        DateOnly? to,
        int? tzOffsetMinutes,
        string? timezoneId,
        ClaimsPrincipal principal,
        ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var hasTimezone = !string.IsNullOrWhiteSpace(timezoneId)
            || !string.IsNullOrWhiteSpace(user?.TimezoneId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        if (!hasTimezone && tzOffsetMinutes.HasValue)
        {
            tz = TimeZoneInfo.CreateCustomTimeZone(
                "request-offset",
                TimeSpan.FromMinutes(-tzOffsetMinutes.Value),
                "request-offset",
                "request-offset");
        }
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var fromDate = from ?? todayLocal.AddDays(-30);
        var toDate = to ?? todayLocal;

        var (utcStart, utcEnd) = hasTimezone
            ? TimeZoneHelper.GetUtcRangeForLocalDateRange(user, fromDate, toDate, timezoneId)
            : tzOffsetMinutes.HasValue
                ? TimeZoneHelper.GetUtcRangeForFixedOffset(fromDate, toDate, tzOffsetMinutes.Value)
                : TimeZoneHelper.GetUtcRangeForLocalDateRange(user, fromDate, toDate);
        var meals = await store.GetMealLogsByDateRangeAsync(
            userId,
            DateOnly.FromDateTime(utcStart),
            DateOnly.FromDateTime(utcEnd));
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
        foreach (var m in meals)
            m.Items = await store.GetMealItemsAsync(userId, m.Id);

        var grouped = meals.GroupBy(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(m.LoggedAt, tz)))
            .Select(g => new
            {
                date = g.Key,
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

    static async Task<IResult> GetNutritionByMealType(DateOnly? from, DateOnly? to, string? timezoneId, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var fromDate = from ?? todayLocal.AddDays(-30);
        var toDate = to ?? todayLocal;

        var (utcStart, utcEnd) = TimeZoneHelper.GetUtcRangeForLocalDateRange(user, fromDate, toDate, timezoneId);
        var meals = await store.GetMealLogsByDateRangeAsync(userId, DateOnly.FromDateTime(utcStart), DateOnly.FromDateTime(utcEnd));
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
        var grouped = meals.GroupBy(m => m.MealType)
            .Select(g => new
            {
                mealType = g.Key.ToString(),
                totalCalories = g.Sum(m => m.TotalCalories),
                totalProteinG = g.Sum(m => m.TotalProteinG),
                totalCarbsG = g.Sum(m => m.TotalCarbsG),
                totalFatG = g.Sum(m => m.TotalFatG),
                mealCount = g.Count()
            })
            .OrderBy(x => x.mealType switch
            {
                "Breakfast" => 0,
                "Lunch" => 1,
                "Dinner" => 2,
                "Snack" => 3,
                _ => 4
            });
        return Results.Ok(grouped);
    }

    static async Task<IResult> GetAdditiveExposure(DateOnly? from, DateOnly? to, string? timezoneId, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var fromDate = from ?? todayLocal.AddDays(-30);
        var toDate = to ?? todayLocal;

        var (utcStart, utcEnd) = TimeZoneHelper.GetUtcRangeForLocalDateRange(user, fromDate, toDate, timezoneId);
        var meals = await store.GetMealLogsByDateRangeAsync(userId, DateOnly.FromDateTime(utcStart), DateOnly.FromDateTime(utcEnd));
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
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

    static async Task<IResult> GetTriggerFoods(DateOnly? from, DateOnly? to, string? timezoneId, ClaimsPrincipal principal, ITableStore store, ICorrelationEngine correlationEngine)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var fromDate = from ?? todayLocal.AddDays(-30);
        var toDate = to ?? todayLocal;
        var correlations = await correlationEngine.ComputeCorrelationsAsync(userId, fromDate, toDate, default, timezoneId);
        var triggers = correlations
            .Where(c => c.Occurrences >= 2 && c.AverageSeverity >= 4)
            .GroupBy(c => c.FoodOrAdditive)
            .Select(g => new
            {
                food = g.Key,
                symptoms = g.Select(c => c.SymptomName).Distinct().ToList(),
                totalOccurrences = g.Sum(c => c.Occurrences),
                avgSeverity = g.Average(c => (double)c.AverageSeverity),
                // Confidence is a string ("Low"/"Medium"/"High") — string.Max would pick
                // "Medium" over "High" (lexicographic: 'M' > 'L' > 'H'). Rank ordinally instead
                // so the strongest evidence in the group is reported correctly.
                worstConfidence = g.OrderByDescending(c => ConfidenceRank(c.Confidence)).First().Confidence
            })
            .OrderByDescending(t => t.avgSeverity);
        return Results.Ok(triggers);
    }

    static int ConfidenceRank(string confidence) => confidence switch
    {
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0
    };

    static async Task<IResult> GetFoodDiaryAnalysis(DateOnly? from, DateOnly? to, string? timezoneId, ClaimsPrincipal principal, ITableStore store, IFoodDiaryAnalysisService analysisService)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var fromDate = from ?? todayLocal.AddDays(-30);
        var toDate = to ?? todayLocal;
        var result = await analysisService.AnalyzeAsync(userId, fromDate, toDate, store, timezoneId);
        return Results.Ok(result);
    }

    static async Task<IResult> GetEliminationDietStatus(string? timezoneId, ClaimsPrincipal principal, ITableStore store, IFoodDiaryAnalysisService analysisService)
    {
        var userId = GetUserId(principal);
        var result = await analysisService.GetEliminationStatusAsync(userId, store, timezoneId);
        return Results.Ok(result);
    }
}
