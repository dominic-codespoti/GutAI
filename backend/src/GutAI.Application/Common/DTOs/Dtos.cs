using GutAI.Domain.Enums;
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
    public decimal? SodiumMg100g { get; init; }
    public FoodKind FoodKind { get; init; } = FoodKind.Unknown;
    public string DataSource { get; init; } = "Manual";
    public string? SourceUrl { get; init; }
    public string? ExternalId { get; init; }
    public string? SourceVersion { get; init; }
    public string? LicenseType { get; init; }
    public string? Attribution { get; init; }
    public DateTime? RetrievedAt { get; init; }
    public string? ServingSize { get; init; }
    public decimal? ServingQuantity { get; init; }
    public decimal MatchConfidence { get; init; }
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
    public string[] EvidenceSources { get; init; } = [];
    public string? EpaCancerClass { get; init; }
    public int? FdaAdverseEventCount { get; init; }
    public int? FdaRecallCount { get; init; }
    public DateTime? LastUpdated { get; init; }
}

/// <summary>Where a parsed item's nutrition numbers actually came from — distinct from
/// <see cref="ParsedFoodItemDto.MatchConfidence"/> (identity confidence), so a low-confidence
/// name match and a fabricated generic estimate are never conflated into one signal.</summary>
public enum NutritionProvenance
{
    /// <summary>Nutrition came from a resolved catalog product (USDA/OpenFoodFacts/embedded DB).</summary>
    Sourced,
    /// <summary>No catalog match — nutrition is a keyword-based generic estimate, not measured.</summary>
    Estimated,
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
    /// <summary>Identity confidence — how sure the resolver is that <see cref="Name"/> names the
    /// right food. 0 when no catalog match exists (<see cref="NutritionProvenance.Estimated"/>).</summary>
    public decimal MatchConfidence { get; init; }
    /// <summary>Portion confidence — how sure the estimated <see cref="ServingWeightG"/> is,
    /// independent of whether the food's identity itself is confident. Tiers: explicit weight
    /// unit (deterministic conversion) > explicit volume unit > count unit backed by real
    /// product serving data > default-serving guess.</summary>
    public decimal PortionConfidence { get; init; }
    public string NutritionProvenance { get; init; } = "";
    public string ResolutionStatus { get; init; } = "";
}

/// <summary>
/// The single food-identity resolution decision. Every consumer that auto-selects a food
/// (NLP meal parsing, barcode-driven flows) must use this instead of re-ranking or
/// re-scoring an already-ranked candidate list. <c>Unresolved</c> means no candidate had
/// any meaningful lexical/alias/brand overlap with the query — callers must not silently
/// substitute an unrelated "best quality" candidate in that case.
/// </summary>
public enum FoodResolutionStatus
{
    /// <summary>Top candidate's name (or its depluralized stem) is a literal match for the query.</summary>
    Exact,
    /// <summary>Top candidate has a decisive lead over the runner-up — safe to auto-select.</summary>
    Probable,
    /// <summary>Multiple candidates are close enough that auto-selection risks picking the wrong one.</summary>
    Ambiguous,
    /// <summary>No candidate had meaningful overlap with the query.</summary>
    Unresolved,
}

public record FoodResolutionDto
{
    public string OriginalQuery { get; init; } = "";
    public FoodResolutionStatus Status { get; init; } = FoodResolutionStatus.Unresolved;
    public FoodProductDto? Selected { get; init; }
    public decimal MatchConfidence { get; init; }
    public IReadOnlyList<FoodProductDto> Alternatives { get; init; } = [];
}

public record CorrelationDto
{
    public string FoodOrAdditive { get; init; } = default!;
    public string SymptomName { get; init; } = default!;
    public int Occurrences { get; init; }
    public int TotalMeals { get; init; }
    public decimal FrequencyPercent { get; init; }
    /// <summary>Symptom rate on meals WITHOUT this food/additive, for comparison — the same
    /// occurrence count means very different things depending on this baseline.</summary>
    public decimal BaselineFrequencyPercent { get; init; }
    public decimal AverageSeverity { get; init; }
    public string Confidence { get; init; } = "Low";
    /// <summary>"UserLinked" when the majority of evidence came from symptoms the user
    /// explicitly tied to a specific meal; "InferredOnsetWindow" when it's derived from the
    /// 1-6h onset window instead.</summary>
    public string AttributionMethod { get; init; } = "InferredOnsetWindow";
    public List<string> Limitations { get; init; } = [];
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

/// <summary>
/// The FODMAP screening decision. Ingredient/name-based string matching can never establish
/// a measured serving-level FODMAP classification — this status distinguishes "we screened
/// and found nothing" from "we don't have enough information to screen at all", which the
/// previous design conflated into a single misleading "Low FODMAP" result for both cases.
/// </summary>
public enum FodmapAssessmentStatus
{
    /// <summary>At least one recognized FODMAP trigger name was detected.</summary>
    PotentialTriggersDetected,
    /// <summary>Screened against the trigger database with adequate evidence; nothing matched.</summary>
    NoKnownTriggersDetected,
    /// <summary>No ingredient list and no verified product identity — the screen could not run.</summary>
    InsufficientInformation,
}

public record FodmapAssessmentDto
{
    public string Status { get; init; } = nameof(FodmapAssessmentStatus.InsufficientInformation);
    /// <summary>Internal 0-100 ingredient-screening signal (same computation as before, renamed
    /// from the misleading "FodmapScore"). Not a serving-level FODMAP measurement — used as one
    /// input to <see cref="PersonalizedScoreDto"/>'s composite, not shown as a standalone rating.</summary>
    public int IngredientScreeningScore { get; init; }
    public string Confidence { get; init; } = "Low";
    public int TriggerCount { get; init; }
    public int HighCount { get; init; }
    public int ModerateCount { get; init; }
    public int LowCount { get; init; }
    public List<string> Categories { get; init; } = [];
    public List<FodmapTriggerDto> Triggers { get; init; } = [];
    /// <summary>What evidence was missing when <see cref="Status"/> is <c>InsufficientInformation</c>.</summary>
    public List<string> MissingEvidence { get; init; } = [];
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
    public string Confidence { get; init; } = "Unknown";
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
    public int ExposureMeals { get; init; }
    public decimal AssociationRatePercent { get; init; }
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
    public string? FoodKind { get; init; }
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
    public string? FoodKind { get; init; }
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
