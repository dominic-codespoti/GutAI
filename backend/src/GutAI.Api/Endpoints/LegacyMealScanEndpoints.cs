using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

// Legacy app ≤ 1.0.10 compatibility: remove when AppRequests shows no /api/meals/scan/{id} traffic.
public static class LegacyMealScanEndpoints
{
    public static RouteGroupBuilder MapLegacyMealScanRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", Get);
        group.MapDelete("/{id:guid}", Discard);
        group.MapPut("/{id:guid}/confirm", Confirm);
        return group;
    }

    // Legacy app ≤ 1.0.10 compatibility: preserve the old response casing from configured HTTP JSON options.
    public static IResult WithScanSessionId(MealDraftDto draft, JsonSerializerOptions options)
    {
        var properties = JsonSerializer.SerializeToElement(draft, options)
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        properties["scanSessionId"] = JsonSerializer.SerializeToElement(draft.DraftId, options);
        return Results.Json(properties, options);
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);

    // Legacy app ≤ 1.0.10 compatibility: only photo drafts represented scan sessions to the old client.
    private static async Task<IResult> Get(
        Guid id, ClaimsPrincipal principal, IMealDraftService drafts,
        Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
        CancellationToken ct)
    {
        var draft = await drafts.GetAsync(UserId(principal), id, ct);
        return draft is null || draft.Origin != MealDraftOrigins.Photo
            ? Results.NotFound()
            : WithScanSessionId(draft, jsonOptions.Value.SerializerOptions);
    }

    // Legacy app ≤ 1.0.10 compatibility: deleting an already-finished scan remains idempotent (204).
    private static async Task<IResult> Discard(Guid id, ClaimsPrincipal principal, IMealDraftService drafts, CancellationToken ct)
    {
        try
        {
            await drafts.DiscardAsync(UserId(principal), id, ct);
            return Results.NoContent();
        }
        catch (MealDraftException ex) when (ex.Code == MealDraftErrorCode.NotPending)
        {
            return Results.NoContent();
        }
        catch (MealDraftException ex)
        {
            return MapError(ex);
        }
    }

    // Legacy app ≤ 1.0.10 compatibility: translate the former scan confirmation contract to draft commits.
    private static async Task<IResult> Confirm(
        Guid id, LegacyMealScanConfirmRequest? request, ClaimsPrincipal principal,
        IMealDraftService drafts, CancellationToken ct)
    {
        if (request?.Items is not { Count: >= 1 and <= 50 } oldItems)
            return Results.BadRequest(new { error = "A meal must contain 1 to 50 items" });

        var draft = await drafts.GetAsync(UserId(principal), id, ct);
        if (draft is null || draft.Origin != MealDraftOrigins.Photo)
            return Results.NotFound();

        var draftItems = draft.Items.ToDictionary(item => item.ItemId);
        var commitItems = new List<MealDraftCommitItem>(oldItems.Count);
        foreach (var oldItem in oldItems)
        {
            if (!draftItems.TryGetValue(oldItem.ItemId, out var item))
                continue;

            var commitItem = new MealDraftCommitItem { ItemId = item.ItemId, Grams = oldItem.Grams };
            if (oldItem.FoodProductId is { } selectedProductId)
            {
                if (selectedProductId == item.FoodProductId)
                {
                    // Retain the product already selected by the draft.
                }
                else
                {
                    var candidate = item.Grounding?.Candidates.FirstOrDefault(c => c.FoodProductId == selectedProductId);
                    commitItem = candidate is null
                        ? commitItem with { ReplacementFoodProductId = selectedProductId }
                        : candidate.CandidateKey is { Length: > 0 } key
                            ? commitItem with { SelectedCandidateKey = key }
                            : commitItem with { ReplacementFoodProductId = selectedProductId };
                }
            }
            else if (item.FoodProductId is null && (item.NeedsChoice || item.Per100g is null))
            {
                // A null choice was an explicit no-match selection in the legacy review UI.
                commitItem = commitItem with { LogWithoutCalories = true };
            }
            commitItems.Add(commitItem);
        }

        var mealType = Enum.TryParse< GutAI.Domain.Enums.MealType>(request.MealType, true, out var parsedType)
                       && Enum.IsDefined(parsedType)
            ? parsedType.ToString()
            : GutAI.Domain.Enums.MealType.Snack.ToString();
        try
        {
            var result = await drafts.CommitAsync(UserId(principal), id, new MealDraftCommitRequest
            {
                MealType = mealType,
                LoggedAt = request.LoggedAt,
                Items = commitItems
            }, ct: ct);
            return Results.Ok(new { mealId = result.MealId });
        }
        catch (MealDraftUnresolvedItemsException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message, itemIds = ex.ItemIds });
        }
        catch (MealDraftException ex)
        {
            return MapError(ex);
        }
    }

    private static IResult MapError(MealDraftException ex) => ex.Code switch
    {
        MealDraftErrorCode.NotFound or MealDraftErrorCode.NotPending => Results.NotFound(new { error = ex.Message }),
        MealDraftErrorCode.OriginMismatch or MealDraftErrorCode.SameTurnCommit => Results.Conflict(new { error = ex.Message }),
        MealDraftErrorCode.Validation => Results.BadRequest(new { error = ex.Message }),
        MealDraftErrorCode.UnresolvedItems => Results.UnprocessableEntity(new { error = ex.Message }),
        _ => Results.BadRequest(new { error = ex.Message })
    };
}

// Legacy app ≤ 1.0.10 compatibility: payload uses fields from MealScanConfirmRequest, not draft DTOs.
public sealed class LegacyMealScanConfirmRequest
{
    public string? MealType { get; init; }
    public DateTimeOffset? LoggedAt { get; init; }
    public List<LegacyMealScanConfirmItem>? Items { get; init; }
}

public sealed class LegacyMealScanConfirmItem
{
    public Guid ItemId { get; init; }
    public string? Name { get; init; }
    public decimal Grams { get; init; }
    public Guid? FoodProductId { get; init; }
    public string? Source { get; init; }
    public string? SourceUrl { get; init; }
    public decimal MatchConfidence { get; init; }
    public decimal? VisionConfidence { get; init; }
    public decimal? Calories { get; init; }
    public decimal? ProteinG { get; init; }
    public decimal? CarbsG { get; init; }
    public decimal? FatG { get; init; }
    public decimal? FiberG { get; init; }
    public decimal? SugarG { get; init; }
    public decimal? SodiumMg { get; init; }
}
