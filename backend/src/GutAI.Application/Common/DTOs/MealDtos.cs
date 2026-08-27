namespace GutAI.Application.Common.DTOs;

public record MealLogDto
{
    public Guid Id { get; init; }
    public string MealType { get; init; } = default!;
    public DateTime LoggedAt { get; init; }
    public string? Notes { get; init; }
    public string? PhotoUrl { get; init; }
    public decimal TotalCalories { get; init; }
    public decimal TotalProteinG { get; init; }
    public decimal TotalCarbsG { get; init; }
    public decimal TotalFatG { get; init; }
    public string? OriginalText { get; init; }
    public int CorrectionCount { get; init; }
    public DateTime? LastCorrectedAt { get; init; }
    public List<MealItemDto> Items { get; init; } = [];
}

public record MealItemDto
{
    public Guid Id { get; init; }
    public string FoodName { get; init; } = default!;
    public string? Barcode { get; init; }
    public decimal Servings { get; init; }
    public string ServingUnit { get; init; } = "serving";
    public decimal Calories { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbsG { get; init; }
    public decimal FatG { get; init; }
    public decimal FiberG { get; init; }
    public decimal SugarG { get; init; }
    public decimal SodiumMg { get; init; }
    public decimal? ServingWeightG { get; init; }
    public string? ServingHintUnit { get; init; }
    public string? ServingHintUnitPlural { get; init; }
    public decimal? ServingHintUnitGrams { get; init; }
    public Guid? FoodProductId { get; init; }
    public decimal CholesterolMg { get; init; }
    public decimal SaturatedFatG { get; init; }
    public decimal PotassiumMg { get; init; }
    public string? SafetyRating { get; init; }
    /// <summary>Identity confidence from the resolver at parse time, echoed back so a
    /// previously-logged item's uncertainty can still be shown on review.</summary>
    public decimal? MatchConfidence { get; init; }
    public string? NutritionProvenance { get; init; }
}

public record CreateMealRequest
{
    public string MealType { get; init; } = "Snack";
    public DateTime? LoggedAt { get; init; }
    public string? Notes { get; init; }
    public string? OriginalText { get; init; }
    public List<CreateMealItemRequest> Items { get; init; } = [];
}

public record CreateMealItemRequest
{
    public string FoodName { get; init; } = default!;
    public string? Barcode { get; init; }
    public Guid? FoodProductId { get; init; }
    public decimal Servings { get; init; } = 1.0m;
    public string ServingUnit { get; init; } = "serving";
    public decimal? ServingWeightG { get; init; }
    public string? ServingHintUnit { get; init; }
    public string? ServingHintUnitPlural { get; init; }
    public decimal? ServingHintUnitGrams { get; init; }
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
    /// <summary>Identity confidence carried over from the parsed-item preview when the
    /// user commits a natural-language-parsed meal. Null for manual entry, search/scan
    /// selection, or barcode scan — those already have a deterministic identity.</summary>
    public decimal? MatchConfidence { get; init; }
    public string? NutritionProvenance { get; init; }
}

/// <summary>Bulk import of externally tracked meals (health platforms, CSV sources).
/// One item becomes one GutAI meal with a single name-only item; nutrition arrives
/// pre-computed by the source app and is stored with Estimated provenance.</summary>
public record ImportMealsRequest
{
    /// <summary>Lowercase source slug: "health-connect", "healthkit", "myfitnesspal", …</summary>
    public string Source { get; init; } = default!;
    public List<ImportMealRequest> Items { get; init; } = [];
}

public record ImportMealRequest
{
    /// <summary>UTC or offset-aware timestamp supplied by the health platform.</summary>
    public DateTime LoggedAt { get; init; }
    /// <summary>Breakfast/Lunch/Dinner/Snack; derived from the timestamp in the user's timezone when omitted.</summary>
    public string? MealType { get; init; }
    /// <summary>Stable id in the source system; enables idempotent re-imports.</summary>
    public string? ExternalId { get; init; }
    public string? Name { get; init; }
    public decimal Servings { get; init; } = 1.0m;
    public string? Notes { get; init; }
    public decimal Calories { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbsG { get; init; }
    public decimal FatG { get; init; }
    public decimal FiberG { get; init; }
    public decimal SugarG { get; init; }
    public decimal SodiumMg { get; init; }
}

public record ImportMealsResult
{
    public int Imported { get; init; }
    public int SkippedDuplicates { get; init; }
    public int Failed { get; init; }
    public List<string> Errors { get; init; } = [];
}

public record NaturalLanguageMealRequest
{
    public string Text { get; init; } = default!;
    public string MealType { get; init; } = "Snack";
    public DateTime? LoggedAt { get; init; }
}

public record DailyNutritionSummaryDto
{
    public DateOnly Date { get; init; }
    public decimal TotalCalories { get; init; }
    public decimal TotalProteinG { get; init; }
    public decimal TotalCarbsG { get; init; }
    public decimal TotalFatG { get; init; }
    public decimal TotalFiberG { get; init; }
    public decimal TotalSugarG { get; init; }
    public decimal TotalSodiumMg { get; init; }
    public int MealCount { get; init; }
    public int CalorieGoal { get; init; }
}

public record RecentFoodDto
{
    public string FoodName { get; init; } = default!;
    public Guid? FoodProductId { get; init; }
    public decimal Calories { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbsG { get; init; }
    public decimal FatG { get; init; }
    public decimal FiberG { get; init; }
    public decimal SugarG { get; init; }
    public decimal SodiumMg { get; init; }
    public decimal? ServingWeightG { get; init; }
    public string ServingUnit { get; init; } = "serving";
    public DateTime LastLoggedAt { get; init; }
    public int LogCount { get; init; }
}

public record StreakDto
{
    public int CurrentStreak { get; init; }
    public int LongestStreak { get; init; }
    public int TotalDaysLogged { get; init; }
}
