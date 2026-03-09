namespace GutAI.Application.Common.Interfaces;

public interface IChatService
{
    IAsyncEnumerable<ChatStreamEvent> StreamResponseAsync(Guid userId, string message, CancellationToken ct = default);
    Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId, int limit = 50, CancellationToken ct = default);
    Task ClearHistoryAsync(Guid userId, CancellationToken ct = default);
}

public record ChatStreamEvent(string? Content = null, string? ToolCall = null, string? Status = null);
public record ChatHistoryMessage(string Role, string Content, DateTimeOffset CreatedAt)
{
    /// <summary>Synthetic id for frontend FlatList keys: role + unix-ms timestamp.</summary>
    public string Id => $"{Role}-{CreatedAt.ToUnixTimeMilliseconds()}";
}
