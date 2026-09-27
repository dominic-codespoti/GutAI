using System.Security.Claims;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class MealScanEndpoints
{
    public static RouteGroupBuilder MapMealScanEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/image", ScanImage).DisableAntiforgery();
        return group;
    }

    /// <summary>POST /api/meals/scan/image — multipart photo and optional context note.</summary>
    private static async Task<IResult> ScanImage(
        HttpRequest request, ClaimsPrincipal principal,
        IMealScanService scanService, ILogger<Program> logger)
    {
        var uid = Guid.Parse(principal.FindFirstValue("sub")!);
        try
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "No image provided." });
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No image provided." });

            const long MaxUploadBytes = 20_000_000;
            if (file.Length > MaxUploadBytes)
                return Results.BadRequest(new { error = "Image is too large. Please use a photo under 20MB." });
            var note = form["note"].FirstOrDefault()?.Trim();
            if (note?.Length > 200)
                return Results.BadRequest(new { error = "Note must be 200 characters or fewer." });

            logger.LogInformation("Meal scan started for user {UserId}, original size {Size} bytes.", uid, file.Length);
            using var originalStream = file.OpenReadStream();
            using var preprocessed = await GutAI.Api.Imaging.MealPhotoPreprocessor.PreprocessAsync(originalStream);
            var draft = await scanService.ScanMealImageAsync(uid, preprocessed.Stream, preprocessed.ContentType, note, request.HttpContext.RequestAborted);
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
}
