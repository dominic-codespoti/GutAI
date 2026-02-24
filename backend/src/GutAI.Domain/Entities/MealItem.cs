namespace GutAI.Domain.Entities;

public class MealItem
{
    public Guid Id { get; set; }
    public Guid MealLogId { get; set; }
    public Guid? FoodProductId { get; set; }
    public string FoodName { get; set; } = default!;
    public string? Barcode { get; set; }
    public decimal Servings { get; set; } = 1.0m;
    public string ServingUnit { get; set; } = "serving";
    public decimal? ServingWeightG { get; set; }
    public decimal Calories { get; set; }
    public decimal ProteinG { get; set; }
    public decimal CarbsG { get; set; }
    public decimal FatG { get; set; }
    public decimal FiberG { get; set; }
    public decimal SugarG { get; set; }
    public decimal SodiumMg { get; set; }
    public decimal CholesterolMg { get; set; }
    public decimal SaturatedFatG { get; set; }
    public decimal PotassiumMg { get; set; }

    public MealLog MealLog { get; set; } = default!;
    public FoodProduct? FoodProduct { get; set; }
}
