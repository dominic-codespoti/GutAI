namespace GutAI.Api.Middleware;

/// <summary>
/// Endpoint filter that requires a valid X-Admin-Key header for food CRUD operations.
/// The expected key is read from configuration: AdminKey (or ADMIN_KEY env var).
/// </summary>
public class AdminKeyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = config["AdminKey"];

        if (string.IsNullOrEmpty(expectedKey))
        {
            // If no admin key is configured, block all admin operations
            return Results.StatusCode(503);
        }

        var providedKey = context.HttpContext.Request.Headers["X-Admin-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey) || !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            return Results.Json(new { error = "Forbidden — admin key required" }, statusCode: 403);
        }

        return await next(context);
    }
}
