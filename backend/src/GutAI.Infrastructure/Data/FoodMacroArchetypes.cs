using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Data;

internal readonly record struct FoodMacros(decimal Calories, decimal Protein, decimal Carbs, decimal Fat, decimal Sugar)
{
    public static FoodMacros From(FoodProductDto dto) => new(
        dto.Calories100g ?? 0m, dto.Protein100g ?? 0m, dto.Carbs100g ?? 0m, dto.Fat100g ?? 0m, dto.Sugar100g ?? 0m);
}

/// <summary>
/// Generalized nutrition-plausibility model: dispatches to a small table of named
/// macro-expectation archetypes instead of ~13 sequential <c>if (queryLower is "x")</c>
/// branches. Also the single source of truth for lean-protein detection, shared with
/// <c>NaturalLanguageFallbackService</c>'s hard pre-filter — previously two independently
/// tuned thresholds (5g hard filter vs 40g soft ranking signal) disagreed on the same
/// question; now there is exactly one number.
/// </summary>
internal static class FoodMacroArchetypes
{
    public static readonly string[] LeanProteinKeywords =
        ["chicken", "turkey", "beef", "pork", "fish", "salmon", "tuna", "shrimp", "steak", "breast"];

    public static readonly string[] LegitimateCarbSourceKeywords =
        [
            "breaded", "tender", "nugget", "sausage", "patty", "teriyaki", "bbq", "barbecue",
            "marinated", "glazed", "battered", "stuffed", "casserole", "salad", "sandwich",
            "wrap", "taco", "curry", "stir fry", "stir-fry", "fried rice", "gravy", "sauce",
        ];

    /// <summary>Plain lean meat/poultry/fish carries near-zero carbohydrate. Crowd-sourced
    /// catalogs occasionally have mislabeled or malformed entries for generic terms (e.g. a
    /// "grilled chicken breast" product with implausible carbs) — this is the threshold that
    /// separates a real match from one of those.</summary>
    public const decimal LeanProteinMaxCarbsG = 5m;

    public static bool IsLeanProteinQuery(string queryLower) =>
        LeanProteinKeywords.Any(queryLower.Contains) && !LegitimateCarbSourceKeywords.Any(queryLower.Contains);

    public static bool HasLegitimateCarbSource(string nameLower) =>
        LegitimateCarbSourceKeywords.Any(nameLower.Contains);

    private delegate float PlausibilityCheck(FoodMacros macros, string nameLower);

    private readonly record struct Archetype(Func<string, bool> Trigger, PlausibilityCheck Check);

    private static Func<string, bool> Exact(params string[] terms) => q => terms.Contains(q);
    private static Func<string, bool> Contains(params string[] terms) => q => terms.Any(q.Contains);

    private static readonly Archetype[] Archetypes =
    [
        // Eggs: high protein, moderate fat, near-zero carbs. Chocolate "eggs" (candy) have 60g+ carbs.
        new(Exact("egg", "eggs"), (m, _) =>
            (m.Carbs > 20m ? -30f : 0f) + (m.Protein < 5m && m.Calories > 50m ? -10f : 0f)),

        // Lean meat/poultry/fish: near-zero carbs. Skip candidates that are themselves a
        // legitimate composite dish (breaded/marinated/salad/etc.) — those carry real carbs.
        new(IsLeanProteinQuery, (m, nameLower) =>
            HasLegitimateCarbSource(nameLower) ? 0f :
            (m.Carbs > LeanProteinMaxCarbsG ? -15f : 0f) + (m.Protein < 5m && m.Calories > 50m ? -10f : 0f)),

        // Oils/fats/lard: nearly 100% fat.
        new(Contains("oil", "butter", "lard"), (m, _) => m.Fat < 20m && m.Calories > 100m ? -15f : 0f),

        // Leafy greens/low-cal vegetables: very low calories.
        new(Contains("lettuce", "spinach", "kale", "celery", "cucumber"), (m, _) => m.Calories > 100m ? -15f : 0f),

        // Beverages: low fat.
        new(Contains("juice", "water", "tea", "coffee"), (m, _) => m.Fat > 20m ? -10f : 0f),

        // Aromatics/herbs: very low cal, low fat, low protein whole foods.
        // Branded "Garlic" is sometimes sausage (high fat/protein); real garlic isn't.
        new(Exact("garlic", "onion", "ginger", "basil", "oregano", "thyme", "rosemary", "cilantro", "parsley", "mint", "dill"),
            (m, _) => (m.Fat > 15m ? -20f : 0f) + (m.Protein > 20m ? -15f : 0f)),

        // Oats/oatmeal: low sugar. Branded "Oatmeal" granola bars run high sugar.
        new(Exact("oats", "oatmeal", "porridge"), (m, _) => m.Sugar > 15m ? -20f : 0f),

        // Rice: very low fat/sugar.
        new(Exact("rice"), (m, _) => (m.Sugar > 10m ? -15f : 0f) + (m.Fat > 10m ? -15f : 0f)),

        // Nuts: high fat. Candied/flavored versions run high carbs+sugar together.
        new(Exact("almonds", "walnuts", "cashews", "pecans", "pistachios", "peanuts", "hazelnuts", "macadamia"),
            (m, _) => (m.Carbs > 35m && m.Sugar > 15m ? -15f : 0f) + (m.Fat < 15m ? -15f : 0f)),

        // Yogurt: plain yogurt is low sugar; flavored/branded products can spike it.
        new(Exact("yogurt", "yoghurt"), (m, _) => m.Sugar > 25m ? -15f : 0f),

        // Chocolate/cocoa as an ingredient: real cocoa is low sugar; candy "Chocolate" isn't.
        new(Exact("chocolate", "cocoa"), (m, _) => m.Sugar > 40m ? -20f : 0f),

        // Raw fruit: low calorie, low fat.
        new(Exact("apple", "banana", "orange", "strawberry", "strawberries", "blueberry", "blueberries",
                   "grape", "grapes", "mango", "pineapple", "peach", "pear", "watermelon", "cherry", "cherries"),
            (m, _) => (m.Calories > 150m ? -15f : 0f) + (m.Fat > 10m ? -15f : 0f)),
    ];

    /// <summary>Calories wildly exceeding what the macros could plausibly produce (Atwater:
    /// 4 kcal/g protein or carbs, 9 kcal/g fat) indicates corrupted source data — most often
    /// a kJ figure entered into the kcal field upstream in a crowd-sourced/branded catalog
    /// (e.g. a real "1580 kJ" oatmeal product surfacing as "1580 kcal/100g", ~4x too high).
    /// Runs for every candidate regardless of food type, unlike the archetypes above.
    ///
    /// Uses an absolute gap rather than a ratio so alcoholic beverages aren't flagged: their
    /// ~7 kcal/g alcohol content isn't tracked in <see cref="FoodMacros"/> and legitimately
    /// produces a large ratio (e.g. wine: ~85 kcal vs ~11 macro-kcal) but a small absolute gap
    /// that stays well under this threshold.</summary>
    private const decimal ImplausibleCalorieGap = 400m;

    private static float ScoreEnergyDensityConsistency(FoodMacros m)
    {
        var macroCalories = 4m * m.Protein + 4m * m.Carbs + 9m * m.Fat;
        return m.Calories - macroCalories > ImplausibleCalorieGap ? -30f : 0f;
    }

    public static float Score(FoodProductDto dto, string queryLower)
    {
        if (!dto.Calories100g.HasValue) return 0f;

        var macros = FoodMacros.From(dto);
        var nameLower = dto.Name.ToLowerInvariant();

        float score = ScoreEnergyDensityConsistency(macros);
        foreach (var archetype in Archetypes)
            if (archetype.Trigger(queryLower))
                score += archetype.Check(macros, nameLower);

        return score;
    }
}
