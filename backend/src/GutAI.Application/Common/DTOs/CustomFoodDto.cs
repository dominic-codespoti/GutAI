namespace GutAI.Application.Common.DTOs;

/// <summary>
/// Data Transfer Object for custom food entries created by users.
/// Contains comprehensive nutritional information extracted from food labels.
/// </summary>
public class CustomFoodDto
{
    // Basic Information
    public string Name { get; set; } = default!;
    public string? BrandName { get; set; }
    public decimal ServingSize { get; set; }
    public string ServingSizeUnit { get; set; } = "g";
    
    // Macronutrients (Required)
    public decimal Calories { get; set; }
    public decimal ProteinG { get; set; }
    public decimal CarbG { get; set; }
    public decimal FatG { get; set; }
    
    // Basic Micronutrients
    public decimal? FiberG { get; set; }
    public decimal? SugarG { get; set; }
    public decimal? SodiumMg { get; set; }
    
    // Extended Macronutrients
    /// <summary>
    /// Saturated fat content in grams
    /// </summary>
    public decimal? SaturatedFatG { get; set; }
    
    /// <summary>
    /// Trans fat content in grams
    /// </summary>
    public decimal? TransFatG { get; set; }
    
    /// <summary>
    /// Cholesterol content in milligrams
    /// </summary>
    public decimal? CholesterolMg { get; set; }
    
    /// <summary>
    /// Potassium content in milligrams
    /// </summary>
    public decimal? PotassiumMg { get; set; }
    
    // Essential Minerals
    /// <summary>
    /// Calcium content in milligrams
    /// </summary>
    public decimal? CalciumMg { get; set; }
    
    /// <summary>
    /// Iron content in milligrams
    /// </summary>
    public decimal? IronMg { get; set; }
    
    /// <summary>
    /// Magnesium content in milligrams
    /// </summary>
    public decimal? MagnesiumMg { get; set; }
    
    /// <summary>
    /// Zinc content in milligrams
    /// </summary>
    public decimal? ZincMg { get; set; }
    
    // Vitamins
    /// <summary>
    /// Vitamin A content in International Units (IU)
    /// </summary>
    public decimal? VitaminA_IU { get; set; }
    
    /// <summary>
    /// Vitamin C (Ascorbic Acid) content in milligrams
    /// </summary>
    public decimal? VitaminC_Mg { get; set; }
    
    /// <summary>
    /// Vitamin D content in micrograms (mcg)
    /// </summary>
    public decimal? VitaminD_Mcg { get; set; }
    
    /// <summary>
    /// Vitamin B12 (Cobalamin) content in micrograms (mcg)
    /// </summary>
    public decimal? VitaminB12_Mcg { get; set; }
    
    // Special Nutrients
    /// <summary>
    /// Omega-3 fatty acids (ALA, EPA, DHA combined) in grams
    /// </summary>
    public decimal? Omega3G { get; set; }
    
    /// <summary>
    /// Caffeine content in milligrams
    /// </summary>
    public decimal? CaffeineMg { get; set; }
    
    // Metadata
    public string? Ingredients { get; set; }
    
    /// <summary>
    /// Barcode/UPC if available from label
    /// </summary>
    public string? Barcode { get; set; }
    
    /// <summary>
    /// Confidence score from AI extraction (0-1)
    /// </summary>
    public decimal? ExtractionConfidence { get; set; }

    /// <summary>
    /// <see cref="NutritionProvenance"/> name for these numbers: <c>Sourced</c> when every
    /// described component was grounded to the catalog, <c>ModelEstimated</c> when any
    /// component fell back to a sanity-checked model estimate. Null for label/manual foods.
    /// </summary>
    public string? NutritionProvenance { get; set; }

    /// <summary>Per-component breakdown for text-described foods (null for labels/manual).</summary>
    public List<DescribedFoodComponentDto>? DescribedComponents { get; set; }
}

/// <summary>
/// One component of a text-described food, grounded via <c>GroundingPolicy</c> and computed
/// by <c>NutritionCalculator</c> for the grams in one serving (plan §6.5).
/// </summary>
public sealed record DescribedFoodComponentDto
{
    public required string Name { get; init; }

    /// <summary>Grams of this component in one serving of the described food.</summary>
    public required decimal Grams { get; init; }

    public Guid? FoodProductId { get; init; }
    public string? CanonicalName { get; init; }

    /// <summary>"usda" | "off" | "au" | "db" for catalog matches, "ai" for a model estimate.</summary>
    public required string Source { get; init; }

    /// <summary><see cref="NutritionProvenance"/> name: <c>Sourced</c> or <c>ModelEstimated</c>.</summary>
    public required string NutritionProvenance { get; init; }

    public decimal MatchConfidence { get; init; }
    public decimal? Calories { get; init; }
    public decimal? ProteinG { get; init; }
    public decimal? CarbsG { get; init; }
    public decimal? FatG { get; init; }
}

public record DescribeCustomFoodRequest
{
    public string Text { get; init; } = default!;
}
