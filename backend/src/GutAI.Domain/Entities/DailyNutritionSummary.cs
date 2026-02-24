namespace GutAI.Domain.Entities;

public class DailyNutritionSummary
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public decimal TotalCalories { get; set; }
    public decimal TotalProteinG { get; set; }
    public decimal TotalCarbsG { get; set; }
    public decimal TotalFatG { get; set; }
    public decimal TotalFiberG { get; set; }
    public decimal TotalSugarG { get; set; }
    public decimal TotalSodiumMg { get; set; }
    public int MealCount { get; set; }
    public int CalorieGoal { get; set; }

    public User User { get; set; } = default!;
}
