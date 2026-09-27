namespace GutAI.Application.Common.DTOs;

/// <summary>
/// Per-100 g nutrition basis. Every persisted item that has a basis (catalog product,
/// web result, draft basis) is computed server-side from this via
/// <c>NutritionCalculator</c> — never from client-sent absolute numbers (AGENTS.md N1).
/// </summary>
public sealed record NutritionPer100gDto
{
    public required decimal CaloriesKcal { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbsG { get; init; }
    public decimal FatG { get; init; }
    public decimal? FiberG { get; init; }
    public decimal? SugarG { get; init; }
    public decimal? SodiumMg { get; init; }
}

/// <summary>Absolute nutrition for one portion, produced by <c>NutritionCalculator</c>.</summary>
public sealed record NutritionAmountsDto
{
    public decimal Calories { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbsG { get; init; }
    public decimal FatG { get; init; }
    public decimal? FiberG { get; init; }
    public decimal? SugarG { get; init; }
    public decimal? SodiumMg { get; init; }
}
