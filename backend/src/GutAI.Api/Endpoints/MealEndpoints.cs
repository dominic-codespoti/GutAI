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
        var validationError = ValidateMealRequest(request);
        if (validationError is not null)
            return validationError;

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

        var (items, itemError) = await BuildMealItemsAsync(request.Items, meal.Id, store);
        if (itemError is not null)
            return itemError;

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
        ITableStore store,
        INutritionApiService nutritionApi,
        IMealDraftService draftService)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 2000)
            return Results.BadRequest(new { error = "Text is required and must not exceed 2000 characters" });
        MealType? mealType = null;
        if (request.MealType is not null)
        {
            if (!MealValidation.TryParseMealType(request.MealType, out var parsedMealType))
                return Results.BadRequest(new { error = $"Meal type must be {MealValidation.MealTypeNames}" });
            mealType = parsedMealType;
        }
        var user = await store.GetUserAsync(GetUserId(principal));
        var parsed = await nutritionApi.ParseNaturalLanguageAsync(
            request.Text,
            region: user?.PreferredFoodRegion ?? FoodRegion.Default);
        if (parsed.Count == 0)
            return Results.BadRequest(new { error = "Could not parse any food items from the text." });

        var draftItems = parsed.Select(item => new MealDraftItemDto
        {
            ItemId = Guid.NewGuid(),
            Name = item.Name,
            FoodProductId = item.FoodProductId,
            Source = item.NutritionProvenance switch
            {
                nameof(NutritionProvenance.Sourced) => "db",
                nameof(NutritionProvenance.Web) => "web",
                _ => "estimate",
            },
            Grams = item.ServingWeightG,
            Per100g = item.Per100g,
            NutritionProvenance = item.NutritionProvenance,
            MatchConfidence = item.MatchConfidence,
            PortionConfidence = item.PortionConfidence,
            NeedsChoice = item.NeedsChoice,
            Grounding = item.Grounding,
            PortionMethod = "nlp_estimate",
            Calories = item.Calories,
            ProteinG = item.ProteinG,
            CarbsG = item.CarbsG,
            FatG = item.FatG,
            FiberG = item.FiberG,
            SugarG = item.SugarG,
            SodiumMg = item.SodiumMg,
        }).ToList();
        var draft = await draftService.CreateAsync(GetUserId(principal), new MealDraftCreateRequest
        {
            Origin = MealDraftOrigins.Nlp,
            MealType = mealType?.ToString(),
            LoggedAt = request.LoggedAt.HasValue
                ? new DateTimeOffset(TimeZoneHelper.NormalizeUtc(request.LoggedAt.Value))
                : null,
            Items = draftItems,
            Warnings = [],
            ReferenceObjectVisible = false,
            OverallConfidence = parsed.Min(item => item.MatchConfidence),
        });
        var parsedWithDraftIds = parsed.Select((item, index) => item with { DraftItemId = draftItems[index].ItemId }).ToList();

        return Results.Ok(new
        {
            originalText = request.Text,
            mealType = request.MealType,
            parsedItems = parsedWithDraftIds,
            draftId = draft.DraftId
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

        var validationError = ValidateMealRequest(request);
        if (validationError is not null)
            return validationError;
        var (newItems, itemError) = await BuildMealItemsAsync(request.Items, id, store);
        if (itemError is not null)
            return itemError;

        meal.MealType = Enum.TryParse<MealType>(request.MealType, true, out var mt) ? mt : meal.MealType;
        meal.Notes = request.Notes;
        meal.OriginalText ??= request.OriginalText;
        meal.CorrectionCount++;
        meal.LastCorrectedAt = DateTime.UtcNow;
        if (request.LoggedAt.HasValue)
            meal.LoggedAt = TimeZoneHelper.NormalizeUtc(request.LoggedAt.Value);

        await store.DeleteMealItemsAsync(userId, id);
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

        var dayItems = meals.SelectMany(m => m.Items ?? []).ToList();
        return Results.Ok(new DailyNutritionSummaryDto
        {
            Date = date,
            TotalCalories = meals.Sum(m => m.TotalCalories),
            TotalProteinG = meals.Sum(m => m.TotalProteinG),
            TotalCarbsG = meals.Sum(m => m.TotalCarbsG),
            TotalFatG = meals.Sum(m => m.TotalFatG),
            TotalFiberG = dayItems.Sum(i => i.FiberG),
            TotalSugarG = dayItems.Sum(i => i.SugarG),
            TotalSodiumMg = dayItems.Sum(i => i.SodiumMg),
            MealCount = meals.Count,
            CalorieGoal = user?.DailyCalorieGoal ?? 2000,
            ItemsWithoutNutrition = dayItems.Count(i => i.NutritionProvenance == nameof(NutritionProvenance.Unknown))
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

    static IResult? ValidateMealRequest(CreateMealRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
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
        if (request.Items.Any(i => i.ServingWeightG.HasValue
            && (i.ServingWeightG.Value <= 0m || i.ServingWeightG.Value > 5000m)))
            return Results.BadRequest(new { error = "servingWeightG must be greater than 0 and no more than 5000 g" });
        return null;
    }

    static async Task<(List<MealItem> Items, IResult? Error)> BuildMealItemsAsync(
        IReadOnlyList<CreateMealItemRequest> requested,
        Guid mealId,
        ITableStore store)
    {
        var items = new List<MealItem>(requested.Count);
        foreach (var input in requested)
        {
            var servingWeightG = input.ServingWeightG;
            var calories = input.Calories;
            var protein = input.ProteinG;
            var carbs = input.CarbsG;
            var fat = input.FatG;
            var fiber = input.FiberG;
            var sugar = input.SugarG;
            var sodium = input.SodiumMg;
            var provenance = input.NutritionProvenance;

            if (input.FoodProductId is { } productId)
            {
                var product = await store.GetFoodProductAsync(productId);
                if (product is null)
                    return ([], Results.UnprocessableEntity(new { error = $"Food product '{productId}' was not found" }));
                var basis = NutritionCalculator.BasisFrom(product);
                var grams = servingWeightG is > 0
                    ? servingWeightG.Value
                    : product.ServingQuantity is > 0
                        ? input.Servings * product.ServingQuantity.Value
                        : (decimal?)null;
                if (grams is null && input.Calories > 0m && basis?.CaloriesKcal is > 0m)
                {
                    // Legacy app ≤ 1.0.10 sends null servingWeightG for favourites and copied items;
                    // inferring the portion from its calories keeps the saved value equal to what that app showed.
                    grams = Math.Round(input.Calories * 100m / basis.CaloriesKcal, 1, MidpointRounding.AwayFromZero);
                }
                if (grams is null)
                    return ([], Results.UnprocessableEntity(new { error = "servingWeightG is required for catalog items" }));
                if (grams is <= 0m || grams > 5000m)
                    return ([], Results.BadRequest(new { error = "servingWeightG must be greater than 0 and no more than 5000 g" }));
                if (basis is null)
                    return ([], Results.UnprocessableEntity(new { error = "Catalog item has no nutrition basis" }));

                var amounts = NutritionCalculator.Compute(basis, grams.Value);
                servingWeightG = grams;
                calories = amounts.Calories;
                protein = amounts.ProteinG;
                carbs = amounts.CarbsG;
                fat = amounts.FatG;
                fiber = amounts.FiberG ?? 0m;
                sugar = amounts.SugarG ?? 0m;
                sodium = amounts.SodiumMg ?? 0m;
                // The per-100 g basis intentionally omits cholesterol, saturated fat, and potassium;
                // preserve these client-supplied values only after applying the normal storage clamps.
                provenance = nameof(NutritionProvenance.Sourced);
            }
            else
            {
                if (string.IsNullOrEmpty(provenance))
                    provenance = nameof(NutritionProvenance.UserEntered);
                else if (provenance == nameof(NutritionProvenance.Sourced))
                    provenance = nameof(NutritionProvenance.Estimated);
                else
                {
                    if (!NutritionProvenanceRules.TryParse(provenance, out var parsedProvenance))
                        return ([], Results.BadRequest(new { error = $"Unknown nutrition provenance '{provenance}'" }));
                    if (parsedProvenance == NutritionProvenance.Unknown
                        && (calories != 0m || protein != 0m || carbs != 0m || fat != 0m
                            || fiber != 0m || sugar != 0m || sodium != 0m
                            || input.CholesterolMg != 0m || input.SaturatedFatG != 0m || input.PotassiumMg != 0m))
                        return ([], Results.BadRequest(new { error = "Items with Unknown nutrition provenance cannot include nutrition values" }));
                }

            }

            var amountsToCheck = new NutritionAmountsDto
            {
                Calories = calories,
                ProteinG = protein,
                CarbsG = carbs,
                FatG = fat,
                FiberG = fiber,
                SugarG = sugar,
                SodiumMg = sodium,
            };
            if (NutritionSanity.CheckPortion(amountsToCheck, servingWeightG, input.FoodName).HasHardViolation)
                return ([], Results.UnprocessableEntity(new { error = $"Nutrition values for '{input.FoodName}' are implausible" }));

            items.Add(new MealItem
            {
                Id = Guid.NewGuid(),
                MealLogId = mealId,
                FoodName = input.FoodName,
                Barcode = input.Barcode,
                FoodProductId = input.FoodProductId,
                Servings = input.Servings,
                ServingUnit = input.ServingUnit,
                ServingWeightG = servingWeightG,
                ServingHintUnit = input.ServingHintUnit,
                ServingHintUnitPlural = input.ServingHintUnitPlural,
                ServingHintUnitGrams = input.ServingHintUnitGrams,
                Calories = MealValidation.ClampNutrient(calories, MealValidation.MaxCalories),
                ProteinG = MealValidation.ClampNutrient(protein, MealValidation.MaxMacroG),
                CarbsG = MealValidation.ClampNutrient(carbs, MealValidation.MaxMacroG),
                FatG = MealValidation.ClampNutrient(fat, MealValidation.MaxMacroG),
                FiberG = MealValidation.ClampNutrient(fiber, MealValidation.MaxMacroG),
                SugarG = MealValidation.ClampNutrient(sugar, MealValidation.MaxMacroG),
                SodiumMg = MealValidation.ClampNutrient(sodium, MealValidation.MaxMacroG),
                CholesterolMg = MealValidation.ClampNutrient(input.CholesterolMg, MealValidation.MaxMacroG),
                SaturatedFatG = MealValidation.ClampNutrient(input.SaturatedFatG, MealValidation.MaxMacroG),
                PotassiumMg = MealValidation.ClampNutrient(input.PotassiumMg, MealValidation.MaxMacroG),
                MatchConfidence = input.MatchConfidence,
                NutritionProvenance = provenance,
            });
        }
        return (items, null);
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
