using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

public static class ChatEndpoints
{
    public static RouteGroupBuilder MapChatEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/stream", StreamChat);
        group.MapGet("/history", GetHistory);
        group.MapDelete("/history", ClearHistory);
        return group;
    }

    static Guid GetUserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);

    static async Task StreamChat(HttpContext httpContext, ClaimsPrincipal principal, [FromServices] IChatService chatService)
    {
        var ct = httpContext.RequestAborted;
        using var reader = new StreamReader(httpContext.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var request = JsonSerializer.Deserialize<ChatRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (string.IsNullOrWhiteSpace(request?.Message) || request.Message.Length > 2000)
        {
            httpContext.Response.StatusCode = 400;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Message is required and must not exceed 2000 characters" }, ct);
            return;
        }

        var userId = GetUserId(principal);

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var evt in chatService.StreamResponseAsync(userId, request.Message, ct))
            {
                if (evt.Content is not null)
                {
                    var payload = JsonSerializer.Serialize(new { content = evt.Content });
                    await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
                }
                else if (evt.ToolCall is not null)
                {
                    var payload = JsonSerializer.Serialize(new { tool_call = evt.ToolCall, status = evt.Status });
                    await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
                }
                await httpContext.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }

        await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    static async Task<IResult> GetHistory(int? limit, ClaimsPrincipal principal, [FromServices] IChatService chatService, CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var messages = await chatService.GetHistoryAsync(userId, limit ?? 50, ct);
        return Results.Ok(messages.Select(m => new
        {
            m.Id,
            m.Role,
            m.Content,
            m.CreatedAt
        }));
    }

    static async Task<IResult> ClearHistory(ClaimsPrincipal principal, [FromServices] IChatService chatService, CancellationToken ct)
    {
        var userId = GetUserId(principal);
        await chatService.ClearHistoryAsync(userId, ct);
        return Results.NoContent();
    }

    record ChatRequest(string Message);
}
