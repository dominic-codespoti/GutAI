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
}
