using GutAI.Domain.Enums;

namespace GutAI.Application.Common.Helpers;

public static class MealValidation
{
    public const decimal MaxServings = 1000m;
    public const decimal MaxCalories = 50000m;
    public const decimal MaxMacroG = 5000m;
    public const int MaxNotesLength = 1000;

    /// <summary>Clamps servings to (0, MaxServings]. Values &lt;= 0 become 1.</summary>
    public static decimal ClampServings(decimal servings)
        => servings <= 0 || servings > MaxServings ? Math.Clamp(servings <= 0 ? 1m : servings, 0.01m, MaxServings) : servings;

    /// <summary>Clamps a nutrition value into [0, MaxCalories] (for calories) or [0, MaxMacroG] (for macros in grams).</summary>
    public static decimal ClampNutrient(decimal value, decimal max) => Math.Clamp(value, 0m, max);

    /// <summary>Accepted meal type names, for validation errors and agent tool descriptions.</summary>
    public const string MealTypeNames = "Breakfast, Lunch, Dinner, or Snack";

    /// <summary>
    /// Parses a meal type by name, case-insensitively and ignoring surrounding whitespace. Numeric
    /// strings and unknown names such as "Meal" or "Beverage" are rejected.
    /// </summary>
    public static bool TryParseMealType(string? value, out MealType mealType)
    {
        mealType = default;
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var candidate in Enum.GetValues<MealType>())
        {
            if (!candidate.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            mealType = candidate;
            return true;
        }
        return false;
    }
}
