using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace GutAI.Api.Middleware;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddGutAIRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("authenticated", httpContext =>
            {
                var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    TokensPerPeriod = 100,
                    AutoReplenishment = true,
                    QueueLimit = 10,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });

            options.AddPolicy("auth", httpContext =>
            {
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 5,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });

            options.AddPolicy("search", httpContext =>
            {
                var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 5,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });

            options.AddPolicy("chat", httpContext =>
            {
                var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter($"chat_{userId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromHours(1),
                    // QueueLimit=0: a nonzero queue on a 1-hour window leaves excess requests
                    // hanging (queued) for up to an hour instead of getting a fast 429 — see
                    // the aiExtraction policy below, where this exact behavior was caught by a test.
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });

            // Strict policy for endpoints that invoke GPT-4o (text or vision) directly per
            // request — /api/food/describe and /api/food/parse-label. These previously rode
            // the generic "search" policy (30/min = up to 1,800/hr), which is fine for cheap
            // Lucene text search but leaves the AI-cost-bearing endpoints effectively unlimited.
            options.AddPolicy("aiExtraction", httpContext =>
            {
                var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter($"ai_{userId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    // QueueLimit=0: reject immediately past the limit. A nonzero queue on a
                    // 1-hour fixed window would leave excess requests hanging for up to an
                    // hour waiting for capacity instead of getting a fast 429 (confirmed via
                    // FoodContractTests.DescribeFoodFromText_ExceedsAiExtractionLimit_*, which
                    // timed out instead of receiving 429 before this fix).
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });

            // Meal photo scan — vision-model cost per request is higher than label parsing
            // (multi-component analysis). 10/hour/user, reject fast past the limit.
            options.AddPolicy("mealScan", httpContext =>
            {
                var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter($"scan_{userId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    status = 429,
                    title = "Too Many Requests",
                    detail = "Rate limit exceeded. Please try again later.",
                    instance = context.HttpContext.Request.Path.Value
                }, ct);
            };
        });

        return services;
    }
}
