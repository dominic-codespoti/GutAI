using GutAI.Domain.Enums;

namespace GutAI.Domain.Entities;

public class InsightReport
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public ReportType ReportType { get; set; }
    public string CorrelationsJson { get; set; } = "[]";
    public string SummaryText { get; set; } = "";
    public string AdditiveExposureJson { get; set; } = "{}";
    public string TopTriggersJson { get; set; } = "[]";

    public User User { get; set; } = default!;
}
