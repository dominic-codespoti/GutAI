using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace GutAI.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Response already started, cannot write error response");
                return;
            }

            context.Response.StatusCode = ex switch
            {
                ArgumentException or FormatException => (int)HttpStatusCode.BadRequest,
                UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
                KeyNotFoundException => (int)HttpStatusCode.NotFound,
                OperationCanceledException => 499, // client closed request
                _ => (int)HttpStatusCode.InternalServerError
            };

            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = context.Response.StatusCode,
                Title = ex switch
                {
                    ArgumentException => "Bad Request",
                    FormatException => "Bad Request",
                    UnauthorizedAccessException => "Unauthorized",
                    KeyNotFoundException => "Not Found",
                    OperationCanceledException => "Request Cancelled",
                    _ => "Internal Server Error"
                },
                Detail = context.Response.StatusCode == (int)HttpStatusCode.InternalServerError
                    ? "An unexpected error occurred. Please try again later."
                    : ex.Message,
                Instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
