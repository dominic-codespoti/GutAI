namespace GutAI.Domain.ValueObjects;

public record ServingSize
{
    public decimal Amount { get; init; } = 1.0m;
    public string Unit { get; init; } = "serving";
    public decimal? WeightInGrams { get; init; }
}
