using System.Text.Json.Serialization;

namespace GutAI.Infrastructure.Services;

public sealed record DescribedDish
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("serving")]
    public DescribedDishServing Serving { get; init; } = new();
    [JsonPropertyName("components")]
    public List<DescribedDishComponent> Components { get; init; } = [];
    [JsonPropertyName("confidence")]
    public decimal Confidence { get; init; }
}

public sealed record DescribedDishServing
{
    [JsonPropertyName("unit")]
    public string Unit { get; init; } = "g";
    [JsonPropertyName("grams")]
    public decimal Grams { get; init; }
}

public sealed record DescribedDishComponent
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("grams")]
    public decimal Grams { get; init; }
    [JsonPropertyName("search_queries")]
    public List<string> SearchQueries { get; init; } = [];
    [JsonPropertyName("fallback_per_100g")]
    public DescribedDishNutritionEstimate? FallbackPer100G { get; init; }
}

public sealed record DescribedDishNutritionEstimate
{
    [JsonPropertyName("kcal")]
    public decimal Kcal { get; init; }
    [JsonPropertyName("protein_g")]
    public decimal ProteinG { get; init; }
    [JsonPropertyName("carbs_g")]
    public decimal CarbsG { get; init; }
    [JsonPropertyName("fat_g")]
    public decimal FatG { get; init; }
    [JsonPropertyName("fiber_g")]
    public decimal? FiberG { get; init; }
}

/// <summary>Model-owned nutrition-label extraction fields; server provenance is deliberately absent.</summary>
public sealed record NutritionLabelExtraction
{
    public string Name { get; init; } = "";
    public string? BrandName { get; init; }
    public decimal ServingSize { get; init; }
    public string ServingSizeUnit { get; init; } = "g";
    public decimal Calories { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbG { get; init; }
    public decimal FatG { get; init; }
    public decimal? FiberG { get; init; }
    public decimal? SugarG { get; init; }
    public decimal? SodiumMg { get; init; }
    public decimal? SaturatedFatG { get; init; }
    public decimal? TransFatG { get; init; }
    public decimal? CholesterolMg { get; init; }
    public decimal? PotassiumMg { get; init; }
    public decimal? CalciumMg { get; init; }
    public decimal? IronMg { get; init; }
    public decimal? MagnesiumMg { get; init; }
    public decimal? ZincMg { get; init; }
    public decimal? VitaminA_IU { get; init; }
    public decimal? VitaminC_Mg { get; init; }
    public decimal? VitaminD_Mcg { get; init; }
    public decimal? VitaminB12_Mcg { get; init; }
    public decimal? Omega3G { get; init; }
    public decimal? CaffeineMg { get; init; }
    public string? Ingredients { get; init; }
    public string? Barcode { get; init; }
    public decimal? ExtractionConfidence { get; init; }
}
