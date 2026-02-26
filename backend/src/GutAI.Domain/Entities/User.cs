namespace GutAI.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int DailyCalorieGoal { get; set; } = 2000;
    public int DailyProteinGoalG { get; set; } = 50;
    public int DailyCarbGoalG { get; set; } = 250;
    public int DailyFatGoalG { get; set; } = 65;
    public int DailyFiberGoalG { get; set; } = 25;
    public string[] Allergies { get; set; } = [];
    public string[] DietaryPreferences { get; set; } = [];
    public string[] GutConditions { get; set; } = [];
    public bool OnboardingCompleted { get; set; }
    public string? TimezoneId { get; set; }

    public ICollection<MealLog> MealLogs { get; set; } = [];
    public ICollection<SymptomLog> SymptomLogs { get; set; } = [];
    public ICollection<DailyNutritionSummary> DailyNutritionSummaries { get; set; } = [];
    public ICollection<UserFoodAlert> FoodAlerts { get; set; } = [];
    public ICollection<InsightReport> InsightReports { get; set; } = [];
}
