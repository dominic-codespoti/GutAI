using GutAI.Domain.Enums;

namespace GutAI.Domain.Entities;

public class MealLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public MealType MealType { get; set; }
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public decimal TotalCalories { get; set; }
    public decimal TotalProteinG { get; set; }
    public decimal TotalCarbsG { get; set; }
    public decimal TotalFatG { get; set; }
    public string? OriginalText { get; set; }
    public int CorrectionCount { get; set; }
    public DateTime? LastCorrectedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Origin of externally imported meals ("health-connect", "healthkit", "myfitnesspal", …).
    /// Null for meals logged natively in GutAI. Pair with ExternalId it enables idempotent
    /// re-imports; health-platform sync filters its own writes by this field.
    /// </summary>
    public string? ExternalSource { get; set; }
    public string? ExternalId { get; set; }

    public User User { get; set; } = default!;
    public ICollection<MealItem> Items { get; set; } = [];
    public ICollection<SymptomLog> AssociatedSymptoms { get; set; } = [];
}
