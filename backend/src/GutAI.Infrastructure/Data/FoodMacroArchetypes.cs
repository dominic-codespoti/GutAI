using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Data;

internal readonly record struct FoodMacros(
    decimal Calories, decimal Protein, decimal Carbs, decimal Fat, decimal Sugar, bool HasMacroData)
{
    public static FoodMacros From(FoodProductDto dto) => new(
        dto.Calories100g ?? 0m, dto.Protein100g ?? 0m, dto.Carbs100g ?? 0m, dto.Fat100g ?? 0m, dto.Sugar100g ?? 0m,
        dto.Protein100g.HasValue || dto.Carbs100g.HasValue || dto.Fat100g.HasValue);
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

    private static readonly string[][] LeanProteinKeywordTokens = PrepareTerms(LeanProteinKeywords);
    private static readonly string[][] LegitimateCarbSourceKeywordTokens = PrepareTerms(LegitimateCarbSourceKeywords);

    public static bool IsLeanProteinQuery(string queryLower) =>
        IsLeanProteinQuery(FoodTextNormalizer.DepluralizeAll(FoodTextNormalizer.TokenizeForMatching(queryLower)));

    private static bool IsLeanProteinQuery(string[] queryTokenStems) =>
        ContainsAny(queryTokenStems, LeanProteinKeywordTokens)
        && !ContainsAny(queryTokenStems, LegitimateCarbSourceKeywordTokens);

    public static bool HasLegitimateCarbSource(string nameLower) =>
        HasLegitimateCarbSource(FoodTextNormalizer.DepluralizeAll(FoodTextNormalizer.TokenizeForMatching(nameLower)));

    private static bool HasLegitimateCarbSource(string[] nameTokenStems) =>
        ContainsAny(nameTokenStems, LegitimateCarbSourceKeywordTokens);

    private delegate float PlausibilityCheck(FoodMacros macros, string[] nameTokenStems);

    private readonly record struct Archetype(Func<string[], bool> Trigger, PlausibilityCheck Check);

    private static string[][] PrepareTerms(string[] terms) =>
        terms.Select(term => FoodTextNormalizer.DepluralizeAll(FoodTextNormalizer.Tokenize(term))).ToArray();

    private static bool ContainsAny(string[] tokenStems, string[][] terms)
    {
        foreach (var term in terms)
            if (ContainsPhrase(tokenStems, term))
                return true;
        return false;
    }

    private static bool ContainsPhrase(string[] tokenStems, string[] phrase)
    {
        for (var start = 0; start <= tokenStems.Length - phrase.Length; start++)
        {
            var matches = true;
            for (var i = 0; i < phrase.Length; i++)
            {
                if (tokenStems[start + i] == phrase[i])
                    continue;
                matches = false;
                break;
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static bool ExactPhrase(string[] tokenStems, string[][] terms)
    {
        foreach (var term in terms)
            if (tokenStems.Length == term.Length && ContainsPhrase(tokenStems, term))
                return true;
        return false;
    }

    private static Func<string[], bool> Exact(params string[] terms)
    {
        var normalizedTerms = PrepareTerms(terms);
        return queryTokens => ExactPhrase(queryTokens, normalizedTerms);
    }

    private static Func<string[], bool> Contains(params string[] terms)
    {
        var normalizedTerms = PrepareTerms(terms);
        return queryTokens => ContainsAny(queryTokens, normalizedTerms);
    }

    private static readonly Archetype[] Archetypes =
    [
        // Eggs: high protein, moderate fat, near-zero carbs. Chocolate "eggs" (candy) have 60g+ carbs.
        new(Exact("egg", "eggs"), (m, _) =>
            (m.Carbs > 20m ? -30f : 0f) + (m.Protein < 5m && m.Calories > 50m ? -10f : 0f)),

        // Lean meat/poultry/fish: near-zero carbs. Skip candidates that are themselves a
        // legitimate composite dish (breaded/marinated/salad/etc.) — those carry real carbs.
        new(IsLeanProteinQuery, (m, nameTokens) =>
            HasLegitimateCarbSource(nameTokens) ? 0f :
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
        if (!m.HasMacroData) return 0f;
        var macroCalories = 4m * m.Protein + 4m * m.Carbs + 9m * m.Fat;
        return m.Calories - macroCalories > ImplausibleCalorieGap ? -30f : 0f;
    }

    public static float Score(FoodProductDto dto, string queryLower) =>
        Score(dto, FoodTextNormalizer.TokenizeForMatching(queryLower),
            FoodTextNormalizer.TokenizeForMatching(dto.Name));

    public static ulong GetQueryMask(string[] queryTokens) =>
        GetQueryMaskFromStems(FoodTextNormalizer.DepluralizeAll(queryTokens));

    public static ulong GetQueryMaskFromStems(string[] queryTokenStems)
    {
        ulong mask = 0;
        for (var i = 0; i < Archetypes.Length; i++)
            if (Archetypes[i].Trigger(queryTokenStems))
                mask |= 1UL << i;
        return mask;
    }

    public static float Score(FoodProductDto dto, string[] queryTokens, string[] nameTokens) =>
        Score(dto, GetQueryMask(queryTokens), nameTokens);

    public static float Score(FoodProductDto dto, ulong queryMask, string[] nameTokens) =>
        dto.Calories100g.HasValue
            ? Score(FoodMacros.From(dto), queryMask, FoodTextNormalizer.DepluralizeAll(nameTokens))
            : 0f;

    public static float Score(FoodMacros? macros, ulong queryMask, string[] nameTokenStems)
    {
        if (!macros.HasValue)
            return 0f;

        var macroValues = macros.Value;
        float score = ScoreEnergyDensityConsistency(macroValues);
        for (var i = 0; i < Archetypes.Length; i++)
            if ((queryMask & (1UL << i)) != 0)
                score += Archetypes[i].Check(macroValues, nameTokenStems);

        return score;
    }
}
