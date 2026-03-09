using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;

public static class MealEndpoints
{
    public static RouteGroupBuilder MapMealEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateMeal);
        group.MapPost("/log-natural", LogNatural);
        group.MapGet("/", GetMealsByDate);
        group.MapGet("/recent-foods", GetRecentFoods);
        group.MapGet("/streak", GetStreak);
        group.MapGet("/{id:guid}", GetMeal);
        group.MapPut("/{id:guid}", UpdateMeal);
        group.MapDelete("/{id:guid}", DeleteMeal);
        group.MapGet("/daily-summary/{date}", GetDailySummary);
        group.MapGet("/export", ExportData);
        return group;
    }

    static Guid GetUserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);

    static async Task<IResult> CreateMeal(CreateMealRequest request, ClaimsPrincipal principal, ITableStore store, ICacheService cache)
    {
        if (request.Items.Count == 0)
            return Results.BadRequest(new { error = "A meal must have at least one item" });

        if (request.Items.Count > 50)
            return Results.BadRequest(new { error = "A meal cannot have more than 50 items" });

        if (request.Notes is not null && request.Notes.Length > 1000)
            return Results.BadRequest(new { error = "Notes must not exceed 1000 characters" });

        if (request.OriginalText is not null && request.OriginalText.Length > 2000)
            return Results.BadRequest(new { error = "Original text must not exceed 2000 characters" });

        if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.FoodName) || i.FoodName.Length > 200))
            return Results.BadRequest(new { error = "Each item must have a food name (max 200 characters)" });

        if (request.Items.Any(i => i.Servings <= 0 || i.Servings > 1000))
            return Results.BadRequest(new { error = "Servings must be between 0 and 1000" });

        if (request.Items.Any(i => i.Calories < 0 || i.ProteinG < 0 || i.CarbsG < 0 || i.FatG < 0))
            return Results.BadRequest(new { error = "Nutrition values cannot be negative" });

        if (request.Items.Any(i => i.Calories > 50000 || i.ProteinG > 5000 || i.CarbsG > 5000 || i.FatG > 5000))
            return Results.BadRequest(new { error = "Nutrition values are unrealistically high" });

        var userId = GetUserId(principal);
        var mealType = Enum.TryParse<MealType>(request.MealType, true, out var mt) ? mt : MealType.Snack;

        var meal = new MealLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MealType = mealType,
            LoggedAt = request.LoggedAt ?? DateTime.UtcNow,
            Notes = request.Notes,
            OriginalText = request.OriginalText,
            IsDeleted = false
        };

        var items = request.Items.Select(i => new MealItem
        {
            Id = Guid.NewGuid(),
            MealLogId = meal.Id,
            FoodName = i.FoodName,
            Barcode = i.Barcode,
            FoodProductId = i.FoodProductId,
            Servings = i.Servings,
            ServingUnit = i.ServingUnit,
            ServingWeightG = i.ServingWeightG,
            Calories = i.Calories,
            ProteinG = i.ProteinG,
            CarbsG = i.CarbsG,
            FatG = i.FatG,
            FiberG = i.FiberG,
            SugarG = i.SugarG,
            SodiumMg = i.SodiumMg,
            CholesterolMg = i.CholesterolMg,
            SaturatedFatG = i.SaturatedFatG,
            PotassiumMg = i.PotassiumMg
        }).ToList();

        meal.TotalCalories = items.Sum(i => i.Calories);
        meal.TotalProteinG = items.Sum(i => i.ProteinG);
        meal.TotalCarbsG = items.Sum(i => i.CarbsG);
        meal.TotalFatG = items.Sum(i => i.FatG);

        await store.UpsertMealLogAsync(meal);
        await store.UpsertMealItemsAsync(userId, meal.Id, items);

        await InvalidateUserInsightCaches(userId, cache);

        meal.Items = items;
        return Results.Created($"/api/meals/{meal.Id}", MapToDto(meal));
    }

    static async Task<IResult> LogNatural(
        NaturalLanguageMealRequest request,
        ClaimsPrincipal principal,
        INutritionApiService nutritionApi)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 2000)
            return Results.BadRequest(new { error = "Text is required and must not exceed 2000 characters" });

        var parsed = await nutritionApi.ParseNaturalLanguageAsync(request.Text);
        if (parsed.Count == 0)
            return Results.BadRequest(new { error = "Could not parse any food items from the text." });

        return Results.Ok(new
        {
            originalText = request.Text,
            mealType = request.MealType,
            parsedItems = parsed
        });
    }

    static async Task<IResult> GetMealsByDate(DateOnly? date, int? tzOffsetMinutes, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (tzOffsetMinutes.HasValue)
        {
            var offset = TimeSpan.FromMinutes(-tzOffsetMinutes.Value);
            var utcStart = targetDate.ToDateTime(TimeOnly.MinValue) - offset;
            var utcEnd = targetDate.ToDateTime(TimeOnly.MaxValue) - offset;
            var meals = await store.GetMealLogsByDateRangeAsync(
                userId,
                DateOnly.FromDateTime(utcStart),
                DateOnly.FromDateTime(utcEnd));
            meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
            foreach (var m in meals)
                m.Items = await store.GetMealItemsAsync(userId, m.Id);
            return Results.Ok(meals.OrderBy(m => m.LoggedAt).Select(MapToDto));
        }

        var allMeals = await store.GetMealLogsByDateAsync(userId, targetDate);
        foreach (var m in allMeals)
            m.Items = await store.GetMealItemsAsync(userId, m.Id);
        return Results.Ok(allMeals.OrderBy(m => m.LoggedAt).Select(MapToDto));
    }

    static async Task<IResult> GetMeal(Guid id, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var meal = await store.GetMealLogAsync(userId, id);
        if (meal is null) return Results.NotFound();

        meal.Items = await store.GetMealItemsAsync(userId, meal.Id);
        return Results.Ok(MapToDto(meal));
    }

    static async Task<IResult> UpdateMeal(Guid id, CreateMealRequest request, ClaimsPrincipal principal, ITableStore store, ICacheService cache)
    {
        var userId = GetUserId(principal);
        var meal = await store.GetMealLogAsync(userId, id);
        if (meal is null) return Results.NotFound();

        if (request.Items.Count == 0)
            return Results.BadRequest(new { error = "A meal must have at least one item" });

        if (request.Items.Count > 50)
            return Results.BadRequest(new { error = "A meal cannot have more than 50 items" });

        if (request.Notes is not null && request.Notes.Length > 1000)
            return Results.BadRequest(new { error = "Notes must not exceed 1000 characters" });

        if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.FoodName) || i.FoodName.Length > 200))
            return Results.BadRequest(new { error = "Each item must have a food name (max 200 characters)" });

        if (request.Items.Any(i => i.Servings <= 0 || i.Servings > 1000))
            return Results.BadRequest(new { error = "Servings must be between 0 and 1000" });

        if (request.Items.Any(i => i.Calories < 0 || i.ProteinG < 0 || i.CarbsG < 0 || i.FatG < 0))
            return Results.BadRequest(new { error = "Nutrition values cannot be negative" });

        if (request.Items.Any(i => i.Calories > 50000 || i.ProteinG > 5000 || i.CarbsG > 5000 || i.FatG > 5000))
            return Results.BadRequest(new { error = "Nutrition values are unrealistically high" });

        meal.MealType = Enum.TryParse<MealType>(request.MealType, true, out var mt) ? mt : meal.MealType;
        meal.Notes = request.Notes;
        if (request.LoggedAt.HasValue)
            meal.LoggedAt = request.LoggedAt.Value;

        await store.DeleteMealItemsAsync(userId, id);

        var newItems = request.Items.Select(i => new MealItem
        {
            Id = Guid.NewGuid(),
            MealLogId = meal.Id,
            FoodName = i.FoodName,
            Barcode = i.Barcode,
            FoodProductId = i.FoodProductId,
            Servings = i.Servings,
            ServingUnit = i.ServingUnit,
            ServingWeightG = i.ServingWeightG,
            Calories = i.Calories,
            ProteinG = i.ProteinG,
            CarbsG = i.CarbsG,
            FatG = i.FatG,
            FiberG = i.FiberG,
            SugarG = i.SugarG,
            SodiumMg = i.SodiumMg,
            CholesterolMg = i.CholesterolMg,
            SaturatedFatG = i.SaturatedFatG,
            PotassiumMg = i.PotassiumMg
        }).ToList();

        meal.TotalCalories = newItems.Sum(i => i.Calories);
        meal.TotalProteinG = newItems.Sum(i => i.ProteinG);
        meal.TotalCarbsG = newItems.Sum(i => i.CarbsG);
        meal.TotalFatG = newItems.Sum(i => i.FatG);

        await store.UpsertMealLogAsync(meal);
        await store.UpsertMealItemsAsync(userId, id, newItems);

        await InvalidateUserInsightCaches(userId, cache);

        meal.Items = newItems;
        return Results.Ok(MapToDto(meal));
    }

    static async Task<IResult> DeleteMeal(Guid id, ClaimsPrincipal principal, ITableStore store, ICacheService cache)
    {
        var userId = GetUserId(principal);
        var meal = await store.GetMealLogAsync(userId, id);
        if (meal is null) return Results.NotFound();

        meal.IsDeleted = true;
        await store.UpsertMealLogAsync(meal);

        await InvalidateUserInsightCaches(userId, cache);

        return Results.NoContent();
    }

    static async Task<IResult> GetDailySummary(DateOnly date, int? tzOffsetMinutes, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);

        List<MealLog> meals;
        if (tzOffsetMinutes.HasValue)
        {
            var offset = TimeSpan.FromMinutes(-tzOffsetMinutes.Value);
            var utcStart = date.ToDateTime(TimeOnly.MinValue) - offset;
            var utcEnd = date.ToDateTime(TimeOnly.MaxValue) - offset;
            meals = await store.GetMealLogsByDateRangeAsync(
                userId,
                DateOnly.FromDateTime(utcStart),
                DateOnly.FromDateTime(utcEnd));
            meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
        }
        else
        {
            meals = await store.GetMealLogsByDateAsync(userId, date);
        }
        foreach (var m in meals)
            m.Items = await store.GetMealItemsAsync(userId, m.Id);

        return Results.Ok(new DailyNutritionSummaryDto
        {
            Date = date,
            TotalCalories = meals.Sum(m => m.TotalCalories),
            TotalProteinG = meals.Sum(m => m.TotalProteinG),
            TotalCarbsG = meals.Sum(m => m.TotalCarbsG),
            TotalFatG = meals.Sum(m => m.TotalFatG),
            TotalFiberG = meals.SelectMany(m => m.Items).Sum(i => i.FiberG),
            TotalSugarG = meals.SelectMany(m => m.Items).Sum(i => i.SugarG),
            TotalSodiumMg = meals.SelectMany(m => m.Items).Sum(i => i.SodiumMg),
            MealCount = meals.Count,
            CalorieGoal = user?.DailyCalorieGoal ?? 2000
        });
    }

    static async Task<IResult> ExportData(DateOnly? from, DateOnly? to, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var meals = await store.GetMealLogsByDateRangeAsync(userId, fromDate, toDate);
        foreach (var m in meals)
            m.Items = await store.GetMealItemsAsync(userId, m.Id);

        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, fromDate, toDate);
        foreach (var s in symptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);

        var export = new
        {
            exportedAt = DateTime.UtcNow,
            from = fromDate,
            to = toDate,
            meals = meals.OrderBy(m => m.LoggedAt).Select(MapToDto),
            symptoms = symptoms.OrderBy(s => s.OccurredAt).Select(s => new
            {
                id = s.Id,
                symptomName = s.SymptomType?.Name ?? "Unknown",
                category = s.SymptomType?.Category ?? "Other",
                severity = s.Severity,
                occurredAt = s.OccurredAt,
                notes = s.Notes
            })
        };

        return Results.Ok(export);
    }

    static MealLogDto MapToDto(MealLog m) => new()
    {
        Id = m.Id,
        MealType = m.MealType.ToString(),
        LoggedAt = m.LoggedAt,
        Notes = m.Notes,
        PhotoUrl = m.PhotoUrl,
        TotalCalories = m.TotalCalories,
        TotalProteinG = m.TotalProteinG,
        TotalCarbsG = m.TotalCarbsG,
        TotalFatG = m.TotalFatG,
        OriginalText = m.OriginalText,
        Items = (m.Items ?? []).Select(i => new MealItemDto
        {
            Id = i.Id,
            FoodName = i.FoodName,
            Barcode = i.Barcode,
            Servings = i.Servings,
            ServingUnit = i.ServingUnit,
            ServingWeightG = i.ServingWeightG,
            FoodProductId = i.FoodProductId,
            Calories = i.Calories,
            ProteinG = i.ProteinG,
            CarbsG = i.CarbsG,
            FatG = i.FatG,
            FiberG = i.FiberG,
            SugarG = i.SugarG,
            SodiumMg = i.SodiumMg,
            CholesterolMg = i.CholesterolMg,
            SaturatedFatG = i.SaturatedFatG,
            PotassiumMg = i.PotassiumMg
        }).ToList()
    };

    static async Task InvalidateUserInsightCaches(Guid userId, ICacheService cache)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ranges = new[] { 7, 14, 30, 90 };
        foreach (var days in ranges)
        {
            var from = today.AddDays(-days);
            await cache.RemoveAsync($"correlations:{userId}:{from}:{today}");
            await cache.RemoveAsync($"nutrition-trends:{userId}:{from}:{today}");
            await cache.RemoveAsync($"additive-exposure:{userId}:{from}:{today}");
            await cache.RemoveAsync($"trigger-foods:{userId}:{from}:{today}");
        }
    }

    static async Task<IResult> GetRecentFoods(ClaimsPrincipal principal, ITableStore store, int? limit)
    {
        var userId = GetUserId(principal);
        var maxItems = Math.Min(limit ?? 20, 50);
        var items = await store.GetAllUserMealItemsAsync(userId, 500);

        var recentFoods = items
            .GroupBy(i => i.FoodName.ToLowerInvariant())
            .Select(g =>
            {
                var latest = g.OrderByDescending(i => i.Id).First(); // newest by ID
                return new RecentFoodDto
                {
                    FoodName = latest.FoodName,
                    FoodProductId = latest.FoodProductId,
                    Calories = latest.Calories,
                    ProteinG = latest.ProteinG,
                    CarbsG = latest.CarbsG,
                    FatG = latest.FatG,
                    FiberG = latest.FiberG,
                    SugarG = latest.SugarG,
                    SodiumMg = latest.SodiumMg,
                    ServingWeightG = latest.ServingWeightG,
                    ServingUnit = latest.ServingUnit,
                    LastLoggedAt = DateTime.UtcNow, // approximate – items don't store loggedAt
                    LogCount = g.Count()
                };
            })
            .OrderByDescending(f => f.LogCount)
            .ThenByDescending(f => f.LastLoggedAt)
            .Take(maxItems)
            .ToList();

        return Results.Ok(recentFoods);
    }

    static async Task<IResult> GetStreak(ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneInfo.Utc;
        if (user?.TimezoneId is not null)
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(user.TimezoneId); }
            catch { /* fall back to UTC */ }
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var from = today.AddDays(-90);
        var meals = await store.GetMealLogsByDateRangeAsync(userId, from, today);

        var daysWithMeals = meals
            .Select(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(m.LoggedAt, tz)))
            .Distinct()
            .OrderByDescending(d => d)
            .ToHashSet();

        // Current streak: count consecutive days backwards from today
        var currentStreak = 0;
        var checkDate = today;
        while (daysWithMeals.Contains(checkDate))
        {
            currentStreak++;
            checkDate = checkDate.AddDays(-1);
        }

        // Longest streak: find the longest run in the set
        var longestStreak = 0;
        var streak = 0;
        for (var d = from; d <= today; d = d.AddDays(1))
        {
            if (daysWithMeals.Contains(d))
            {
                streak++;
                if (streak > longestStreak) longestStreak = streak;
            }
            else
            {
                streak = 0;
            }
        }

        return Results.Ok(new StreakDto
        {
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak,
            TotalDaysLogged = daysWithMeals.Count
        });
    }
}
