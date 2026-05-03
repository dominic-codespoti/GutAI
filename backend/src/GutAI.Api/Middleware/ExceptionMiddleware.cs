using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GutAI.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IServiceProvider serviceProvider)
    {
        _next = next;
        _logger = logger;
        _serviceProvider = serviceProvider;
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
            _serviceProvider.GetService<Microsoft.ApplicationInsights.TelemetryClient>()?.TrackException(ex);

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
                    ? "An unexpected error occurred."
                    : ex.Message,
                Instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
