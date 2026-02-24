namespace GutAI.Domain.Entities;

public class SymptomType
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Icon { get; set; } = "🩺";

    public ICollection<SymptomLog> SymptomLogs { get; set; } = [];
}
