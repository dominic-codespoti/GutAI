namespace GutAI.Application.Common.DTOs;

public record SymptomLogDto
{
    public Guid Id { get; init; }
    public int SymptomTypeId { get; init; }
    public string SymptomName { get; init; } = default!;
    public string Category { get; init; } = default!;
    public string Icon { get; init; } = "🩺";
    public int Severity { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid? RelatedMealLogId { get; init; }
    public string? Notes { get; init; }
    public TimeSpan? Duration { get; init; }
}

public record CreateSymptomRequest
{
    public int SymptomTypeId { get; init; }
    public int Severity { get; init; }
    public DateTime? OccurredAt { get; init; }
    public Guid? RelatedMealLogId { get; init; }
    public string? Notes { get; init; }
    public TimeSpan? Duration { get; init; }
}

public record SymptomTypeDto
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string Category { get; init; } = default!;
    public string Icon { get; init; } = "🩺";
}
