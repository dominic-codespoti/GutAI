namespace GutAI.Application.Common.Interfaces;

public interface IChatService
{
    IAsyncEnumerable<ChatStreamEvent> StreamResponseAsync(Guid userId, string message, CancellationToken ct = default, string? timezoneId = null);
    Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId, int limit = 50, CancellationToken ct = default);
    Task ClearHistoryAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Streaming chat event. ToolResult carries a completed tool name with an
/// optional compact SummaryJson (typed by tool — see CoachChatService.
/// BuildToolSummary); the SSE shape is { tool_result, summary }.
/// </summary>
public record ChatStreamEvent(string? Content = null, string? ToolCall = null, string? Status = null, string? ThreadId = null, string? Error = null, string? ToolResult = null, string? SummaryJson = null);
public record ChatHistoryMessage(string Role, string Content, DateTimeOffset CreatedAt)
{
    private readonly string? _sourceId;

    public ChatHistoryMessage(string role, string content, DateTimeOffset createdAt, string? sourceId)
        : this(role, content, createdAt)
    {
        _sourceId = sourceId;
    }

    public string Id => _sourceId ?? $"{Role}-{CreatedAt.ToUnixTimeMilliseconds()}";
}
