namespace GutAI.Application.Common;

public class PagedResult<T>
{
    public List<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasNextPage => Page * PageSize < TotalCount;
}

public record PaginationParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
