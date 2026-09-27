using Microsoft.AspNetCore.Mvc.ModelBinding;

using Microsoft.AspNetCore.Mvc;

using GutAI.Infrastructure.Services;
using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

public static class MealDraftEndpoints
{
    public static RouteGroupBuilder MapMealDraftEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListPending);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Update);
        group.MapPut("/{id:guid}/commit", Commit);
        group.MapDelete("/{id:guid}", Discard);
        return group;
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);

    private static async Task<IResult> ListPending(ClaimsPrincipal principal, IMealDraftService service, CancellationToken ct) =>
        Results.Ok(await service.ListPendingAsync(UserId(principal), ct));

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal principal, IMealDraftService service, CancellationToken ct)
    {
        var result = await service.GetAsync(UserId(principal), id, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> Update(Guid id, MealDraftUpdateRequest request, ClaimsPrincipal principal, IMealDraftService service, CancellationToken ct)
    {
        try { return Results.Ok(await service.UpdateAsync(UserId(principal), id, request, ct)); }
        catch (MealDraftException ex) { return MapError(ex); }
    }

    private static async Task<IResult> Commit(Guid id, ClaimsPrincipal principal, IMealDraftService service, CancellationToken ct, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MealDraftCommitRequest? request = null)
    {
        try { return Results.Ok(await service.CommitAsync(UserId(principal), id, request, ct: ct)); }
        catch (MealDraftUnresolvedItemsException ex) { return Results.UnprocessableEntity(new { error = ex.Message, itemIds = ex.ItemIds }); }
        catch (MealDraftException ex) { return MapError(ex); }
    }

    private static async Task<IResult> Discard(Guid id, ClaimsPrincipal principal, IMealDraftService service, CancellationToken ct)
    {
        try
        {
            await service.DiscardAsync(UserId(principal), id, ct);
            return Results.NoContent();
        }
        catch (MealDraftException ex) { return MapError(ex); }
    }

    private static IResult MapError(MealDraftException ex) => ex.Code switch
    {
        MealDraftErrorCode.NotFound => Results.NotFound(new { error = ex.Message }),
        MealDraftErrorCode.NotPending or MealDraftErrorCode.OriginMismatch or MealDraftErrorCode.SameTurnCommit => Results.Conflict(new { error = ex.Message }),
        MealDraftErrorCode.Validation => Results.BadRequest(new { error = ex.Message }),
        MealDraftErrorCode.UnresolvedItems => Results.UnprocessableEntity(new { error = ex.Message }),
        _ => Results.BadRequest(new { error = ex.Message })
    };
}
