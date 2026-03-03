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
    public Guid? FoodProductId { get; init; }
    public decimal CholesterolMg { get; init; }
    public decimal SaturatedFatG { get; init; }
    public decimal PotassiumMg { get; init; }
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
