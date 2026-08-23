using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class MealScanEndpoints
{
    public static RouteGroupBuilder MapMealScanEndpoints(this RouteGroupBuilder group)
    {
        // NOTE: mapped under /api/meals/scan via Program.cs with the "mealScan" limiter.
        group.MapPost("/image", ScanImage).DisableAntiforgery();
        group.MapGet("/{id:guid}", GetDraft);
        group.MapDelete("/{id:guid}", Discard);
        group.MapPut("/{id:guid}/confirm", Confirm);
        return group;
    }

    static Guid GetUserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);

    /// <summary>POST /api/meals/scan/image — multipart photo → draft meal items for review.</summary>
    private static async Task<IResult> ScanImage(
        HttpRequest request, ClaimsPrincipal principal, ITableStore store,
        IMealScanService scanService, ILogger<Program> logger)
    {
        var uid = Guid.Parse(principal.FindFirstValue("sub")!);
        try
        {
            if (!request.HasFormContentType || !request.Form.Files.Any())
                return Results.BadRequest(new { error = "No image provided." });

            var file = request.Form.Files.GetFile("file") ?? request.Form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No image provided." });

            const long MaxUploadBytes = 20_000_000;
            if (file.Length > MaxUploadBytes)
                return Results.BadRequest(new { error = "Image is too large. Please use a photo under 20MB." });

            logger.LogInformation("Meal scan started for user {UserId}, original size {Size} bytes.", uid, file.Length);

            // Reuse the label-flow preprocessor: EXIF strip, edge cap 2000px, JPEG q80 —
            // cuts vision tokens ~40% and removes GPS metadata before upload to the model.
            using var originalStream = file.OpenReadStream();
            using var preprocessed = await GutAI.Api.Imaging.NutritionLabelImagePreprocessor.PreprocessAsync(originalStream);

            var draft = await scanService.ScanMealImageAsync(uid, preprocessed.Stream, preprocessed.ContentType);
            return Results.Ok(draft);
        }
        catch (MealScanValidationException ex)
        {
            logger.LogWarning("Meal scan rejected for user {UserId}: {Reason}", uid, ex.Message);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Meal scan failed unexpectedly for user {UserId}.", uid);
            return Results.Problem("An error occurred while analyzing your meal photo. Please try again.", statusCode: 500);
        }
    }

    /// <summary>GET /api/meals/scan/{id} — re-fetch a pending draft.</summary>
    private static async Task<IResult> GetDraft(Guid id, ClaimsPrincipal principal, IMealScanService scanService)
    {
        var uid = Guid.Parse(principal.FindFirstValue("sub")!);
        var draft = await scanService.GetDraftAsync(uid, id);
        return draft is null ? Results.NotFound() : Results.Ok(draft);
    }

    /// <summary>DELETE /api/meals/scan/{id} — discard without logging.</summary>
    private static async Task<IResult> Discard(Guid id, ClaimsPrincipal principal, IMealScanService scanService)
    {
        var uid = Guid.Parse(principal.FindFirstValue("sub")!);
        await scanService.DiscardAsync(uid, id);
        return Results.NoContent();
    }

    /// <summary>
    /// PUT /api/meals/scan/{id}/confirm — log the (user-edited) draft as a real meal.
    /// P1 minimal implementation: items arrive already carrying grams + computed macros
    /// from the draft; totals are recomputed server-side. The frontend review sheet
    /// lands in P6.
    /// </summary>
    private static async Task<IResult> Confirm(
        Guid id, HttpRequest request, ClaimsPrincipal principal, ITableStore store,
        ILogger<Program> logger)
    {
        var uid = Guid.Parse(principal.FindFirstValue("sub")!);
        var session = await store.GetScanSessionAsync(uid, id);
        if (session is null || session.Status != "PendingReview")
            return Results.NotFound();

        ConfirmRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<ConfirmRequest>();
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid request body." });
        }
        if (body?.Items is null || body.Items.Count == 0 || body.Items.Count > 50)
            return Results.BadRequest(new { error = "A meal must have between 1 and 50 items." });

        var user = await store.GetUserAsync(uid);
        var loggedAt = body.LoggedAt ?? DateTimeOffset.UtcNow;

        var mealId = Guid.NewGuid();
        var drafts = new Dictionary<Guid, MealScanItemDto>();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<MealScanItemDto>>(session.DraftItemsJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            foreach (var d in parsed ?? []) drafts[d.ItemId] = d;
        }
        catch (JsonException) { }

        var items = new List<MealItem>();
        foreach (var i in body.Items)
        {
            if (string.IsNullOrWhiteSpace(i.Name)) continue;
            if (i.Grams <= 0 || i.Grams > 5000m) continue;

            // Prefer the stored draft row (server-authoritative macros + provenance);
            // fall back to the client echo only when no draft row exists.
            MealScanItemDto src = i.ItemId != Guid.Empty && drafts.TryGetValue(i.ItemId, out var d)
                ? d with { Grams = i.Grams }
                : new MealScanItemDto
                {
                    ItemId = Guid.NewGuid(), Name = i.Name, Source = i.Source,
                    SourceUrl = i.SourceUrl, Grams = i.Grams, FoodProductId = i.FoodProductId,
                    Calories = i.Calories, ProteinG = i.ProteinG, CarbsG = i.CarbsG,
                    FatG = i.FatG, FiberG = i.FiberG, SugarG = i.SugarG, SodiumMg = i.SodiumMg,
                    MatchConfidence = i.MatchConfidence, VisionConfidence = i.VisionConfidence,
                };

            items.Add(new MealItem
            {
                Id = Guid.NewGuid(),
                MealLogId = mealId,
                FoodName = src.Name.Trim(),
                FoodProductId = src.FoodProductId,
                Servings = 1,
                ServingUnit = "g",
                ServingWeightG = src.Grams,
                Calories = Math.Round(src.Calories ?? 0),
                ProteinG = src.ProteinG ?? 0,
                CarbsG = src.CarbsG ?? 0,
                FatG = src.FatG ?? 0,
                FiberG = src.FiberG ?? 0,
                SugarG = src.SugarG ?? 0,
                SodiumMg = src.SodiumMg ?? 0,
                MatchConfidence = src.MatchConfidence,
                NutritionProvenance = src.Source == "ai" ? "Estimated" : "Sourced",
            });
        }

        if (items.Count == 0)
            return Results.BadRequest(new { error = "No valid items to log." });

        var meal = new MealLog
        {
            Id = mealId,
            UserId = uid,
            MealType = ParseMealType(body.MealType),
            LoggedAt = loggedAt.UtcDateTime,
            OriginalText = $"photo scan {session.Id}",
            TotalCalories = items.Sum(x => x.Calories),
            TotalProteinG = items.Sum(x => x.ProteinG),
            TotalCarbsG = items.Sum(x => x.CarbsG),
            TotalFatG = items.Sum(x => x.FatG),
        };

        await store.UpsertMealLogAsync(meal);
        await store.UpsertMealItemsAsync(uid, meal.Id, items);

        await store.UpsertScanSessionAsync(session with { Status = "Confirmed" });

        logger.LogInformation("Meal scan {SessionId} confirmed by user {UserId}: {Items} items, {Calories} kcal.",
            id, uid, items.Count, meal.TotalCalories);
        return Results.Ok(new { mealId = meal.Id });
    }

    private sealed record ConfirmRequest(string? MealType, DateTimeOffset? LoggedAt, List<ConfirmItem> Items);
    private sealed record ConfirmItem(Guid ItemId, string Name, decimal Grams, Guid? FoodProductId,
        string Source, string? SourceUrl, decimal MatchConfidence, decimal VisionConfidence,
        decimal? Calories, decimal? ProteinG, decimal? CarbsG, decimal? FatG,
        decimal? FiberG, decimal? SugarG, decimal? SodiumMg);

    private static MealType ParseMealType(string? s) =>
        Enum.TryParse<MealType>(s, ignoreCase: true, out var mt) ? mt : MealType.Snack;
}
