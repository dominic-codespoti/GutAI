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
}
