using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.AspNetCore.RateLimiting;

public static class MealSuggestionEndpoints
{
    public static RouteGroupBuilder MapMealSuggestionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/status", (HttpContext context, IConfiguration configuration) =>
                Results.Ok(new MealSuggestionStatusDto
                {
                    Enabled = configuration.GetValue<bool>("Features:MealSuggestions")
                        && context.RequestServices.GetService<IMealSuggestionService>() is not null
                }))
            .RequireRateLimiting("authenticated");

        group.MapPost("/", async Task<IResult> (
                MealSuggestionRequest? request,
                string? timezoneId,
                HttpContext context,
                IConfiguration configuration,
                CancellationToken ct) =>
            {
                if (!configuration.GetValue<bool>("Features:MealSuggestions"))
                    return Results.NotFound(new { error = "Meal suggestions are not enabled." });

                var service = context.RequestServices.GetService<IMealSuggestionService>();
                if (service is null)
                    return Results.Json(new { error = "Meal suggestions are not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable);

                if (request is null
                    || string.IsNullOrWhiteSpace(request.MealType)
                    || !new[] { "Breakfast", "Lunch", "Dinner", "Snack" }.Contains(request.MealType, StringComparer.OrdinalIgnoreCase)
                    || request.Preferences is { Length: > 200 })
                    return Results.BadRequest(new { error = "A valid mealType and preferences of at most 200 characters are required." });

                try
                {
                    return Results.Ok(await service.SuggestAsync(
                        Guid.Parse(context.User.FindFirstValue("sub")!), request, timezoneId, ct));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("aiExtraction");

        return group;
    }
}
