namespace GutAI.Domain.Entities;

public class SymptomLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int SymptomTypeId { get; set; }
    public int Severity { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Guid? RelatedMealLogId { get; set; }
    public string? Notes { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User User { get; set; } = default!;
    public SymptomType SymptomType { get; set; } = default!;
    public MealLog? RelatedMealLog { get; set; }
}
