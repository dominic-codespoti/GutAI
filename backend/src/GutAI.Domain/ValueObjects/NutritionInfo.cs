namespace GutAI.Domain.ValueObjects;

public record NutritionInfo
{
    public decimal Calories { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbsG { get; init; }
    public decimal FatG { get; init; }
    public decimal FiberG { get; init; }
    public decimal SugarG { get; init; }
    public decimal SodiumMg { get; init; }
    public decimal CholesterolMg { get; init; }
    public decimal SaturatedFatG { get; init; }
    public decimal PotassiumMg { get; init; }
}
