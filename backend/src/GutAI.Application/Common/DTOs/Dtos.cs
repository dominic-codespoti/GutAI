using GutAI.Domain.ValueObjects;

namespace GutAI.Application.Common.DTOs;


public record FoodProductDto
{
    public Guid Id { get; init; }
    public string? Barcode { get; init; }
    public string Name { get; init; } = default!;
    public string? Brand { get; init; }
    public string? Ingredients { get; init; }
    public string? ImageUrl { get; init; }
    public int? NovaGroup { get; init; }
    public string? NutriScore { get; init; }
    public string[] AllergensTags { get; init; } = [];
    public decimal? Calories100g { get; init; }
    public decimal? Protein100g { get; init; }
    public decimal? Carbs100g { get; init; }
    public decimal? Fat100g { get; init; }
    public decimal? Fiber100g { get; init; }
    public decimal? Sugar100g { get; init; }
    public decimal? Sodium100g { get; init; }
    public string DataSource { get; init; } = "Manual";
    public string? ExternalId { get; init; }
    public string? ServingSize { get; init; }
    public decimal? ServingQuantity { get; init; }
    public int? SafetyScore { get; init; }
    public string? SafetyRating { get; init; }
    public NutritionInfo? NutritionInfo { get; init; }
    public List<FoodAdditiveDto> Additives { get; init; } = [];
    public List<string> AdditivesTags { get; init; } = [];
    public bool IsDeleted { get; init; }
}

public record FoodAdditiveDto
{
    public int Id { get; init; }
    public string? ENumber { get; init; }
    public string Name { get; init; } = default!;
    public string Category { get; init; } = default!;
    public string CspiRating { get; init; } = default!;
    public string UsRegulatoryStatus { get; init; } = default!;
    public string EuRegulatoryStatus { get; init; } = default!;
    public string SafetyRating { get; init; } = default!;
    public string HealthConcerns { get; init; } = "";
    public string[] BannedInCountries { get; init; } = [];
    public string? Description { get; init; }
    public string[] AlternateNames { get; init; } = [];
    public decimal? EfsaAdiMgPerKgBw { get; init; }
    public DateTime? EfsaLastReviewDate { get; init; }
    public string? EpaCancerClass { get; init; }
    public int? FdaAdverseEventCount { get; init; }
    public int? FdaRecallCount { get; init; }
    public DateTime? LastUpdated { get; init; }
}

public record ParsedFoodItemDto
{
    public string Name { get; init; } = default!;
    public Guid? FoodProductId { get; init; }
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
    public decimal ServingWeightG { get; init; }
    public string? ServingSize { get; init; }
    public decimal? ServingQuantity { get; init; }
}

public record CorrelationDto
{
    public string FoodOrAdditive { get; init; } = default!;
    public string SymptomName { get; init; } = default!;
    public int Occurrences { get; init; }
    public int TotalMeals { get; init; }
    public decimal FrequencyPercent { get; init; }
    public decimal AverageSeverity { get; init; }
    public string Confidence { get; init; } = "Low";
}

public record GutRiskAssessmentDto
{
    public int GutScore { get; init; }
    public string GutRating { get; init; } = "Good";
    public int FlagCount { get; init; }
    public int HighRiskCount { get; init; }
    public int MediumRiskCount { get; init; }
    public int LowRiskCount { get; init; }
    public List<GutRiskFlagDto> Flags { get; init; } = [];
    public string Summary { get; init; } = "";
    public string Confidence { get; init; } = "High";
    public int DoseSensitiveFlagsCount { get; init; }
}

public record GutRiskFlagDto
{
    public string Source { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string RiskLevel { get; init; } = "Low";
    public string Explanation { get; init; } = "";
    public string TriggerType { get; init; } = "";
    public string FodmapClass { get; init; } = "";
    public string DoseSensitivity { get; init; } = "";
}

public record FodmapAssessmentDto
{
    public int FodmapScore { get; init; }
    public string FodmapRating { get; init; } = "Low FODMAP";
    public int TriggerCount { get; init; }
    public int HighCount { get; init; }
    public int ModerateCount { get; init; }
    public int LowCount { get; init; }
    public List<string> Categories { get; init; } = [];
    public List<FodmapTriggerDto> Triggers { get; init; } = [];
    public string Summary { get; init; } = "";
}

public record FodmapTriggerDto
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string SubCategory { get; init; } = "";
    public string Severity { get; init; } = "Low";
    public string Explanation { get; init; } = "";
}

public record SubstitutionResultDto
{
    public string ProductName { get; init; } = "";
    public int SuggestionCount { get; init; }
    public List<SubstitutionDto> Suggestions { get; init; } = [];
    public string Summary { get; init; } = "";
}

public record SubstitutionDto
{
    public string Original { get; init; } = "";
    public string Substitute { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Category { get; init; } = "";
    public string GutBenefit { get; init; } = "";
    public string Confidence { get; init; } = "Medium";
}

public record GlycemicAssessmentDto
{
    public int? EstimatedGI { get; init; }
    public string GiCategory { get; init; } = "Unknown";
    public decimal? EstimatedGL { get; init; }
    public string GlCategory { get; init; } = "Unknown";
    public int MatchCount { get; init; }
    public List<GlycemicMatchDto> Matches { get; init; } = [];
    public string GutImpactSummary { get; init; } = "";
    public List<string> Recommendations { get; init; } = [];
}

public record GlycemicMatchDto
{
    public string Food { get; init; } = "";
    public int GI { get; init; }
    public string GiCategory { get; init; } = "Unknown";
    public string Source { get; init; } = "";
    public string Notes { get; init; } = "";
}

public record PersonalizedScoreDto
{
    public int CompositeScore { get; init; }
    public string Rating { get; init; } = "";
    public int FodmapComponent { get; init; }
    public int AdditiveRiskComponent { get; init; }
    public int NovaComponent { get; init; }
    public int FiberComponent { get; init; }
    public int AllergenComponent { get; init; }
    public int SugarAlcoholComponent { get; init; }
    public int PersonalTriggerPenalty { get; init; }
    public List<ScoreExplanationDto> Explanations { get; init; } = [];
    public List<string> PersonalWarnings { get; init; } = [];
    public string Summary { get; init; } = "";
}

public record ScoreExplanationDto
{
    public string Component { get; init; } = "";
    public int Weight { get; init; }
    public int RawScore { get; init; }
    public int WeightedContribution { get; init; }
    public string Explanation { get; init; } = "";
}

public record FoodDiaryAnalysisDto
{
    public int TotalMealsAnalyzed { get; init; }
    public int TotalSymptomsAnalyzed { get; init; }
    public int PatternsFound { get; init; }
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public List<FoodSymptomPatternDto> Patterns { get; init; } = [];
    public List<TimingInsightDto> TimingInsights { get; init; } = [];
    public List<string> Recommendations { get; init; } = [];
    public string Summary { get; init; } = "";
}

public record FoodSymptomPatternDto
{
    public string FoodName { get; init; } = "";
    public string SymptomName { get; init; } = "";
    public int Occurrences { get; init; }
    public decimal AverageSeverity { get; init; }
    public decimal AverageOnsetHours { get; init; }
    public string Confidence { get; init; } = "Low";
    public string Explanation { get; init; } = "";
}

public record TimingInsightDto
{
    public string Insight { get; init; } = "";
    public string Category { get; init; } = "";
    public int SupportingDataPoints { get; init; }
}

public record EliminationDietStatusDto
{
    public string Phase { get; init; } = "Not Started";
    public List<string> FoodsToEliminate { get; init; } = [];
    public List<string> FoodsToReintroduce { get; init; } = [];
    public List<string> SafeFoods { get; init; } = [];
    public List<ReintroductionResultDto> ReintroductionResults { get; init; } = [];
    public List<string> Recommendations { get; init; } = [];
    public string Summary { get; init; } = "";
}

public record ReintroductionResultDto
{
    public string FoodName { get; init; } = "";
    public string Result { get; init; } = "";
    public decimal AverageSeverity { get; init; }
    public int TestCount { get; init; }
}

public record CreateFoodProductRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Barcode { get; init; }
    public string? NovaGroup { get; init; }
    public string? Brand { get; init; }
    public string? Ingredients { get; init; }
    public string? ServingSize { get; init; }
    public NutritionInfo? NutritionInfo { get; init; }
    public List<int> AdditiveIds { get; init; } = [];
}

public record UpdateFoodProductRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Barcode { get; init; }
    public string? NovaGroup { get; init; }
    public string? Brand { get; init; }
    public string? Ingredients { get; init; }
    public string? ServingSize { get; init; }
    public NutritionInfo? NutritionInfo { get; init; }
    public List<int> AdditiveIds { get; init; } = [];
}

public record AddFoodAlertRequest
{
    public int AdditiveId { get; init; }
}
