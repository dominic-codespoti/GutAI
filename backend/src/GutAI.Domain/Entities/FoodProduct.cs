using GutAI.Domain.Enums;
using GutAI.Domain.ValueObjects;

namespace GutAI.Domain.Entities;

public class FoodProduct
{
    public Guid Id { get; set; }
    public string? Barcode { get; set; }
    public string Name { get; set; } = default!;
    public string? Brand { get; set; }
    // Removed IngredientsText, use Ingredients
    public string? Ingredients { get; set; }
    public string? ImageUrl { get; set; }
    public int? NovaGroup { get; set; }
    public string? NutriScore { get; set; }
    public string[] AllergensTags { get; set; } = [];
    public decimal? Calories100g { get; set; }
    public decimal? Protein100g { get; set; }
    public decimal? Carbs100g { get; set; }
    public decimal? Fat100g { get; set; }
    public decimal? Fiber100g { get; set; }
    public decimal? Sugar100g { get; set; }
    public decimal? Sodium100g { get; set; }
    public string? ServingSize { get; set; }
    public decimal? ServingQuantity { get; set; }
    public string DataSource { get; set; } = "Manual";
    public string? SourceUrl { get; set; }
    public string? ExternalId { get; set; }
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public int CacheTtlHours { get; set; } = 24;
    public int? SafetyScore { get; set; }
    public SafetyRating? SafetyRating { get; set; }
    public NutritionInfo? NutritionInfo { get; set; }
    public List<int> FoodProductAdditiveIds { get; set; } = new();
    public bool IsDeleted { get; set; }

    public ICollection<FoodProductAdditive> FoodProductAdditives { get; set; } = [];

    public bool IsCacheExpired() => DateTime.UtcNow > CachedAt.AddHours(CacheTtlHours);
}
