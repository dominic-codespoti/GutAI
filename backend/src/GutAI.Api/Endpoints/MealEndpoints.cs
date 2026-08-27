using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;

public static class MealEndpoints
{
    public static RouteGroupBuilder MapMealEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateMeal);
        group.MapPost("/log-natural", LogNatural);
        group.MapPost("/import", ImportMeals);
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

        if (request.Items.Any(i => i.Calories < 0 || i.ProteinG < 0 || i.CarbsG < 0 || i.FatG < 0
            || i.FiberG < 0 || i.SugarG < 0 || i.SodiumMg < 0 || i.CholesterolMg < 0 || i.SaturatedFatG < 0 || i.PotassiumMg < 0))
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
            LoggedAt = request.LoggedAt.HasValue
                ? TimeZoneHelper.NormalizeUtc(request.LoggedAt.Value)
                : DateTime.UtcNow,
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
            ServingHintUnit = i.ServingHintUnit,
            ServingHintUnitPlural = i.ServingHintUnitPlural,
            ServingHintUnitGrams = i.ServingHintUnitGrams,
            Calories = i.Calories,
            ProteinG = i.ProteinG,
            CarbsG = i.CarbsG,
            FatG = i.FatG,
            FiberG = i.FiberG,
            SugarG = i.SugarG,
            SodiumMg = i.SodiumMg,
            CholesterolMg = i.CholesterolMg,
            SaturatedFatG = i.SaturatedFatG,
            PotassiumMg = i.PotassiumMg,
            MatchConfidence = i.MatchConfidence,
            NutritionProvenance = i.NutritionProvenance
        }).ToList();

        meal.TotalCalories = items.Sum(i => i.Calories);
        meal.TotalProteinG = items.Sum(i => i.ProteinG);
        meal.TotalCarbsG = items.Sum(i => i.CarbsG);
        meal.TotalFatG = items.Sum(i => i.FatG);

        await store.UpsertMealLogAsync(meal);
        await store.UpsertMealItemsAsync(userId, meal.Id, items);

        await InvalidateUserInsightCaches(userId, store, cache);

        meal.Items = items;
        var createSafetyRatings = await LoadSafetyRatingsAsync(items, store);
        return Results.Created($"/api/meals/{meal.Id}", MapToDto(meal, createSafetyRatings));
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

    static async Task<IResult> GetMealsByDate(
        DateOnly? date,
        int? tzOffsetMinutes,
        string? timezoneId,
        ClaimsPrincipal principal,
        ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var hasTimezone = !string.IsNullOrWhiteSpace(timezoneId)
            || !string.IsNullOrWhiteSpace(user?.TimezoneId);
        var timezone = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var targetDate = date
            ?? (hasTimezone
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone))
                : tzOffsetMinutes.HasValue
                    ? DateOnly.FromDateTime(DateTime.UtcNow.Add(TimeSpan.FromMinutes(-tzOffsetMinutes.Value)))
                    : DateOnly.FromDateTime(DateTime.UtcNow));
        var (utcStart, utcEnd) = hasTimezone
            ? TimeZoneHelper.GetUtcRangeForLocalDate(user, targetDate, timezoneId)
            : tzOffsetMinutes.HasValue
                ? TimeZoneHelper.GetUtcRangeForFixedOffset(targetDate, tzOffsetMinutes.Value)
                : TimeZoneHelper.GetUtcRangeForLocalDate(user, targetDate);

        var meals = await store.GetMealLogsByDateRangeAsync(
            userId,
            DateOnly.FromDateTime(utcStart),
            DateOnly.FromDateTime(utcEnd));
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
        foreach (var m in meals)
            m.Items = await store.GetMealItemsAsync(userId, m.Id);
        var safetyRatings = await LoadSafetyRatingsAsync(meals.SelectMany(m => m.Items ?? []).ToList(), store);
        return Results.Ok(meals.OrderBy(m => m.LoggedAt).Select(m => MapToDto(m, safetyRatings)));
    }

    static async Task<IResult> GetMeal(Guid id, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var meal = await store.GetMealLogAsync(userId, id);
        if (meal is null) return Results.NotFound();

        meal.Items = await store.GetMealItemsAsync(userId, meal.Id);
        var getSafetyRatings = await LoadSafetyRatingsAsync(meal.Items, store);
        return Results.Ok(MapToDto(meal, getSafetyRatings));
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

        if (request.Items.Any(i => i.Calories < 0 || i.ProteinG < 0 || i.CarbsG < 0 || i.FatG < 0
            || i.FiberG < 0 || i.SugarG < 0 || i.SodiumMg < 0 || i.CholesterolMg < 0 || i.SaturatedFatG < 0 || i.PotassiumMg < 0))
            return Results.BadRequest(new { error = "Nutrition values cannot be negative" });

        if (request.Items.Any(i => i.Calories > 50000 || i.ProteinG > 5000 || i.CarbsG > 5000 || i.FatG > 5000))
            return Results.BadRequest(new { error = "Nutrition values are unrealistically high" });

        meal.MealType = Enum.TryParse<MealType>(request.MealType, true, out var mt) ? mt : meal.MealType;
        meal.Notes = request.Notes;
        meal.OriginalText ??= request.OriginalText;
        meal.CorrectionCount++;
        meal.LastCorrectedAt = DateTime.UtcNow;
        if (request.LoggedAt.HasValue)
            meal.LoggedAt = TimeZoneHelper.NormalizeUtc(request.LoggedAt.Value);

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
            ServingHintUnit = i.ServingHintUnit,
            ServingHintUnitPlural = i.ServingHintUnitPlural,
            ServingHintUnitGrams = i.ServingHintUnitGrams,
            Calories = i.Calories,
            ProteinG = i.ProteinG,
            CarbsG = i.CarbsG,
            FatG = i.FatG,
            FiberG = i.FiberG,
            SugarG = i.SugarG,
            SodiumMg = i.SodiumMg,
            CholesterolMg = i.CholesterolMg,
            SaturatedFatG = i.SaturatedFatG,
            PotassiumMg = i.PotassiumMg,
            MatchConfidence = i.MatchConfidence,
            NutritionProvenance = i.NutritionProvenance
        }).ToList();

        meal.TotalCalories = newItems.Sum(i => i.Calories);
        meal.TotalProteinG = newItems.Sum(i => i.ProteinG);
        meal.TotalCarbsG = newItems.Sum(i => i.CarbsG);
        meal.TotalFatG = newItems.Sum(i => i.FatG);

        await store.UpsertMealLogAsync(meal);
        await store.UpsertMealItemsAsync(userId, id, newItems);

        await InvalidateUserInsightCaches(userId, store, cache);

        meal.Items = newItems;
        var updateSafetyRatings = await LoadSafetyRatingsAsync(newItems, store);
        return Results.Ok(MapToDto(meal, updateSafetyRatings));
    }

    static async Task<IResult> DeleteMeal(Guid id, ClaimsPrincipal principal, ITableStore store, ICacheService cache)
    {
        var userId = GetUserId(principal);
        var meal = await store.GetMealLogAsync(userId, id);
        if (meal is null) return Results.NotFound();

        meal.IsDeleted = true;
        await store.UpsertMealLogAsync(meal);

        await InvalidateUserInsightCaches(userId, store, cache);

        return Results.NoContent();
    }

    static async Task<IResult> GetDailySummary(
        DateOnly date,
        int? tzOffsetMinutes,
        string? timezoneId,
        ClaimsPrincipal principal,
        ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var hasTimezone = !string.IsNullOrWhiteSpace(timezoneId)
            || !string.IsNullOrWhiteSpace(user?.TimezoneId);
        var (utcStart, utcEnd) = hasTimezone
            ? TimeZoneHelper.GetUtcRangeForLocalDate(user, date, timezoneId)
            : tzOffsetMinutes.HasValue
                ? TimeZoneHelper.GetUtcRangeForFixedOffset(date, tzOffsetMinutes.Value)
                : TimeZoneHelper.GetUtcRangeForLocalDate(user, date);

        var meals = await store.GetMealLogsByDateRangeAsync(
            userId,
            DateOnly.FromDateTime(utcStart),
            DateOnly.FromDateTime(utcEnd));
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
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


    static async Task<IResult> ImportMeals(
        ImportMealsRequest request,
        ClaimsPrincipal principal,
        ITableStore store,
        string? timezoneId)
    {
        var userId = GetUserId(principal);
        if (timezoneId is { Length: > 100 })
            return Results.BadRequest(new { error = "Timezone ID must not exceed 100 characters" });

        var user = await store.GetUserAsync(userId);
        var timezone = TimeZoneHelper.ResolveTimeZone(user, timezoneId);

        if (string.IsNullOrWhiteSpace(request.Source) ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.Source, "^[a-z0-9-]{1,32}$"))
            return Results.BadRequest(new { error = "source must be 1-32 chars of a-z, 0-9, or '-'." });
        if (request.Items.Count == 0 || request.Items.Count > 2000)
            return Results.BadRequest(new { error = "items must contain between 1 and 2000 entries." });

        int imported = 0, skipped = 0, failed = 0;
        var errors = new List<string>();

        foreach (var item in request.Items)
        {
            if (item.LoggedAt == default || item.LoggedAt.Kind == DateTimeKind.Unspecified)
            {
                failed++;
                errors.Add($"invalid loggedAt '{item.LoggedAt:O}': timestamp must include UTC or an offset.");
                continue;
            }

            var loggedAtUtc = TimeZoneHelper.NormalizeUtc(item.LoggedAt);
            if (loggedAtUtc > DateTime.UtcNow.AddDays(1))
            {
                failed++;
                errors.Add($"invalid loggedAt '{item.LoggedAt:O}'.");
                continue;
            }

            try
            {
                // Idempotency: a known (source, externalId) pair is a re-import of the
                // same record — skip instead of duplicating.
                if (!string.IsNullOrEmpty(item.ExternalId) &&
                    await store.GetMealLogByExternalRefAsync(userId, request.Source, item.ExternalId) is not null)
                {
                    skipped++;
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(item.Name) ? "Imported meal" : item.Name.Trim();
                if (name.Length > 300) name = name[..300];

                var meal = new MealLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    MealType = ResolveMealType(item, loggedAtUtc, timezone),
                    LoggedAt = loggedAtUtc,
                    Notes = item.Notes is { Length: > MealValidation.MaxNotesLength } ? item.Notes[..MealValidation.MaxNotesLength] : item.Notes,
                    TotalCalories = MealValidation.ClampNutrient(item.Calories * item.Servings, MealValidation.MaxCalories),
                    TotalProteinG = MealValidation.ClampNutrient(item.ProteinG * item.Servings, MealValidation.MaxMacroG),
                    TotalCarbsG = MealValidation.ClampNutrient(item.CarbsG * item.Servings, MealValidation.MaxMacroG),
                    TotalFatG = MealValidation.ClampNutrient(item.FatG * item.Servings, MealValidation.MaxMacroG),
                    OriginalText = $"{request.Source} import",
                    ExternalSource = request.Source,
                    ExternalId = string.IsNullOrEmpty(item.ExternalId) ? null : item.ExternalId[..Math.Min(item.ExternalId.Length, 128)],
                };

                var mealItem = new MealItem
                {
                    Id = Guid.NewGuid(),
                    MealLogId = meal.Id,
                    FoodName = name,
                    Servings = MealValidation.ClampServings(item.Servings),
                    ServingUnit = "serving",
                    Calories = meal.TotalCalories,
                    ProteinG = meal.TotalProteinG,
                    CarbsG = meal.TotalCarbsG,
                    FatG = meal.TotalFatG,
                    FiberG = MealValidation.ClampNutrient(item.FiberG * item.Servings, MealValidation.MaxMacroG),
                    SugarG = MealValidation.ClampNutrient(item.SugarG * item.Servings, MealValidation.MaxMacroG),
                    SodiumMg = MealValidation.ClampNutrient(item.SodiumMg * item.Servings, MealValidation.MaxMacroG),
                    NutritionProvenance = "Estimated",
                };

                await store.UpsertMealLogAsync(meal);
                await store.UpsertMealItemsAsync(userId, meal.Id, [mealItem]);
                imported++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"item at '{item.LoggedAt:O}' failed: {ex.Message}");
                if (errors.Count >= 25) break;
            }
        }

        return Results.Ok(new ImportMealsResult
        {
            Imported = imported,
            SkippedDuplicates = skipped,
            Failed = failed,
            Errors = errors,
        });
    }

    static MealType ResolveMealType(
        ImportMealRequest item,
        DateTime loggedAtUtc,
        TimeZoneInfo timezone)
    {
        if (!string.IsNullOrEmpty(item.MealType) &&
            Enum.TryParse<MealType>(item.MealType, true, out var parsed))
            return parsed;

        // Local-hour heuristic for sources that don't carry a meal type.
        return TimeZoneInfo.ConvertTimeFromUtc(loggedAtUtc, timezone).Hour switch
        {
            >= 6 and < 11 => MealType.Breakfast,
            >= 11 and < 15 => MealType.Lunch,
            >= 18 and < 22 => MealType.Dinner,
            _ => MealType.Snack,
        };
    }
    /// <summary>
    /// Exports the user's selected local calendar range. The payload timestamps remain
    /// UTC instants so the export is portable across devices.
    /// </summary>
    static async Task<IResult> ExportData(
        DateOnly? from,
        DateOnly? to,
        string? timezoneId,
        ClaimsPrincipal principal,
        ITableStore store)
    {
        var userId = GetUserId(principal);
        if (timezoneId is { Length: > 100 })
            return Results.BadRequest(new { error = "Timezone ID must not exceed 100 characters" });

        var user = await store.GetUserAsync(userId);
        var timezone = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
        var fromDate = from ?? today.AddDays(-90);
        var toDate = to ?? today;
        if (fromDate > toDate)
            return Results.BadRequest(new { error = "from must not be after to" });

        var (utcStart, utcEnd) = TimeZoneHelper.GetUtcRangeForLocalDateRange(
            user, fromDate, toDate, timezoneId);
        var coarseFrom = DateOnly.FromDateTime(utcStart);
        var coarseTo = DateOnly.FromDateTime(utcEnd);

        var meals = await store.GetMealLogsByDateRangeAsync(userId, coarseFrom, coarseTo);
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
        foreach (var m in meals)
            m.Items = await store.GetMealItemsAsync(userId, m.Id);

        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, coarseFrom, coarseTo);
        symptoms = symptoms.Where(s => s.OccurredAt >= utcStart && s.OccurredAt <= utcEnd).ToList();
        foreach (var s in symptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);

        var exportSafetyRatings = await LoadSafetyRatingsAsync(meals.SelectMany(m => m.Items ?? []).ToList(), store);

        var export = new
        {
            exportedAt = DateTime.UtcNow,
            from = fromDate,
            to = toDate,
            meals = meals.OrderBy(m => m.LoggedAt).Select(m => MapToDto(m, exportSafetyRatings)),
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

    static MealLogDto MapToDto(MealLog m, IReadOnlyDictionary<Guid, string?>? safetyRatings = null) => new()
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
        CorrectionCount = m.CorrectionCount,
        LastCorrectedAt = m.LastCorrectedAt,
        Items = (m.Items ?? []).Select(i => new MealItemDto
        {
            Id = i.Id,
            FoodName = i.FoodName,
            Barcode = i.Barcode,
            Servings = i.Servings,
            ServingUnit = i.ServingUnit,
            ServingWeightG = i.ServingWeightG,
            ServingHintUnit = i.ServingHintUnit,
            ServingHintUnitPlural = i.ServingHintUnitPlural,
            ServingHintUnitGrams = i.ServingHintUnitGrams,
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
            PotassiumMg = i.PotassiumMg,
            MatchConfidence = i.MatchConfidence,
            NutritionProvenance = i.NutritionProvenance,
            SafetyRating = i.FoodProductId.HasValue && safetyRatings?.TryGetValue(i.FoodProductId.Value, out var sr) == true ? sr : null
        }).ToList()
    };

    static async Task<IReadOnlyDictionary<Guid, string?>> LoadSafetyRatingsAsync(ICollection<MealItem> items, ITableStore store)
    {
        var ids = items
            .Where(i => i.FoodProductId.HasValue)
            .Select(i => i.FoodProductId!.Value)
            .Distinct()
            .ToList();

        return ids.Count == 0
            ? new Dictionary<Guid, string?>()
            : await store.GetFoodProductSafetyRatingsAsync(ids);
    }

    static async Task InvalidateUserInsightCaches(Guid userId, ITableStore store, ICacheService cache)
    {
        var user = await store.GetUserAsync(userId);
        var timezone = TimeZoneHelper.ResolveTimeZone(user, null);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
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

    static async Task<IResult> GetStreak(
        string? timezoneId,
        ClaimsPrincipal principal,
        ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var from = today.AddDays(-90);
        var (utcStart, utcEnd) = TimeZoneHelper.GetUtcRangeForLocalDateRange(
            user, from, today, timezoneId);
        var meals = await store.GetMealLogsByDateRangeAsync(
            userId,
            DateOnly.FromDateTime(utcStart),
            DateOnly.FromDateTime(utcEnd));
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();

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
