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
    /// <summary>Optional model-supplied household unit retained only for display.</summary>
    public string? ServingHintUnit { get; set; }
    public string? ServingHintUnitPlural { get; set; }
    public decimal? ServingHintUnitGrams { get; set; }
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
    /// <summary>Identity confidence from the resolver at parse time, null when the item was
    /// created without NLP resolution (manual entry, barcode scan). Only meaningful alongside
    /// <see cref="NutritionProvenance"/> — a low value on a <c>Sourced</c> item is a weak
    /// catalog match; on an <c>Estimated</c> item it is inherent, not a data-quality signal.</summary>
    public decimal? MatchConfidence { get; set; }
    /// <summary>Where this item's nutrition numbers came from — "Sourced" (a resolved catalog
    /// product) or "Estimated" (keyword-based generic guess, not measured). Null when created
    /// without NLP resolution.</summary>
    public string? NutritionProvenance { get; set; }

    public MealLog MealLog { get; set; } = default!;
    public FoodProduct? FoodProduct { get; set; }
}
