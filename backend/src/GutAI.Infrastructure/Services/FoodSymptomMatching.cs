using GutAI.Infrastructure.Data;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Shared foundation for food-symptom timing and identity matching, used by both
/// <see cref="CorrelationEngine"/> (Insights → Correlations) and
/// <see cref="FoodDiaryAnalysisService"/> (Food Diary / elimination diet).
///
/// These two features independently re-implemented onset-window filtering and
/// food-name grouping with different parameters (1–6h vs 1–8h windows, exact
/// case-sensitive FoodName vs case-insensitive), which let the same underlying
/// meal/symptom data produce contradictory answers depending on which screen
/// the user was looking at. This class is the single source of truth for both,
/// so future changes to the onset window or matching rules only need to happen
/// once.
/// </summary>
internal static class FoodSymptomMatching
{
    /// <summary>Symptoms occurring fewer than this many hours after a meal are not attributed to it.</summary>
    public const int MinOnsetHours = 1;

    /// <summary>Symptoms occurring more than this many hours after a meal are not attributed to it.</summary>
    public const int MaxOnsetHours = 6;

    /// <summary>True if a symptom falls within the onset-attribution window for a meal.</summary>
    public static bool IsWithinOnsetWindow(DateTime mealLoggedAt, DateTime symptomOccurredAt)
    {
        var hours = (symptomOccurredAt - mealLoggedAt).TotalHours;
        return hours >= MinOnsetHours && hours <= MaxOnsetHours;
    }

    /// <summary>
    /// Normalizes a user-typed food name into a case/punctuation/plural-insensitive
    /// grouping key so "Chicken Breast", "chicken breasts", and "CHICKEN BREAST!"
    /// are tracked as the same food instead of silently fragmenting into separate,
    /// under-powered correlation buckets (each needing its own occurrence threshold
    /// to surface).
    /// </summary>
    public static string NormalizeForGrouping(string foodName) => FoodTextNormalizer.NormalizeFoodName(foodName);
}
