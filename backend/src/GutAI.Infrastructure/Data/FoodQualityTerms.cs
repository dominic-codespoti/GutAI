namespace GutAI.Infrastructure.Data;

internal enum QueryMatch { Exact, Contains }

/// <summary>One (or several alternative) surface forms that add/subtract a single weight
/// when present (or, if <see cref="InvertPresence"/>, when absent) in the candidate name.
/// Multiple <see cref="Terms"/> are OR'd for presence — use <see cref="RequireAll"/> for AND.</summary>
internal readonly record struct ModifierEffect(
    string[] Terms, float Weight, bool UnlessQueried = false, bool InvertPresence = false, bool RequireAll = false)
{
    public ModifierEffect(string term, float weight, bool UnlessQueried = false, bool InvertPresence = false)
        : this([term], weight, UnlessQueried, InvertPresence) { }

    public float Apply(string nameLower, string queryLower)
    {
        var present = RequireAll ? Terms.All(nameLower.Contains) : Terms.Any(nameLower.Contains);
        if (present == InvertPresence) return 0f;
        if (UnlessQueried && Terms.Any(queryLower.Contains)) return 0f;
        return Weight;
    }
}

/// <summary>A named food-specific preference rule: when the query matches one of
/// <see cref="Triggers"/>, apply every effect that fires. Generalizes what was previously
/// ~15 separate hand-written <c>if (queryLower is "x")</c> branches (one per food) into one
/// declarative table + one dispatch loop — extending it means adding a row, not a branch.</summary>
internal readonly record struct FoodModifierRule(
    string[] Triggers, QueryMatch Match, ModifierEffect[] Effects, string? ExcludeIfQueryContains = null)
{
    public bool Triggered(string queryLower) =>
        (Match == QueryMatch.Exact ? Triggers.Contains(queryLower) : Triggers.Any(queryLower.Contains))
        && (ExcludeIfQueryContains is null || !queryLower.Contains(ExcludeIfQueryContains));
}

internal static class FoodQualityTerms
{
    // ────────────────────────────────────────────────────────────────
    // Unconditional (query-independent) penalty terms — applied at quality-scoring time.
    // ────────────────────────────────────────────────────────────────

    public static readonly string[] HardPenaltyTerms =
    [
        "frozen", "canned", "dehydrated", "powder", "mix",
        "mixture", "substitute", "imitation", "meatless", "baby food", "infant", "formula",
        "alaska native", "industrial", "fast food",
        "ns as to", "usda commodity", "as purchased", "not further specified",
        "nfs", "ready-to-eat", "ready-to-heat", "glucose reduced", "stabilized",
        "prepared", "cooked", "instant", "fortified",
        "nuggets", "nugget", "breaded", "patties", "patty", "stick", "sticks",
        "cereals ready-to-eat", "includes foods for usda", "food distribution program",
        "mechanically deboned", "mechanically separated", "by-products", "manufacturing",
        "glucose", "liquid from",
    ];

    public static readonly string[] SoftPenaltyTerms =
    [
        "navajo", "hopi", "southwest", "shoshone", "apache",
        "pasteurized", "restaurant", "commercial", "institutional",
        "from concentrate", "hohoysi", "laborador", "tundra",
    ];

    // ────────────────────────────────────────────────────────────────
    // Generic (food-agnostic) preference terms — apply for any short/simple query.
    // ────────────────────────────────────────────────────────────────

    public static readonly string[] RawFreshTerms = ["raw", "fresh"];
    public static readonly string[] PlainTerms = ["whole", "plain", "white", "regular"];
    public static readonly string[] ProcessedTerms =
        ["juice", "concentrate", "dried", "dehydrated", "pickled", "sauce", "paste", "spread", "flavored", "frozen", "canned", "powder"];

    // If the query itself names a cooking method, the generic "prefer raw/plain form"
    // bonus below must not fire — that default only makes sense for bare noun queries.
    // A "fried egg" query should rank a fried candidate above a raw one, not the reverse.
    public static readonly HashSet<string> PreparationMethodTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "fried", "cooked", "grilled", "baked", "roasted", "boiled", "steamed",
        "broiled", "poached", "toasted", "sauteed", "sautéed", "scrambled", "smoked",
    };

    public static readonly HashSet<string> SpiceTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "cinnamon", "pepper", "nutmeg", "cloves", "cumin", "paprika", "turmeric",
        "oregano", "basil", "thyme", "rosemary", "parsley",
    };

    // ────────────────────────────────────────────────────────────────
    // Universal conditional penalties: bad in ANY context unless the query itself asks
    // for that form. Consolidates what were 5 separately-implemented penalty passes.
    // ────────────────────────────────────────────────────────────────

    public readonly record struct ConditionalPenalty(string[] Terms, float Penalty, int MaxQueryTokens = int.MaxValue, string? UnlessContains = null);

    public static readonly ConditionalPenalty[] ConditionalPenalties =
    [
        new(["meatless", "imitation", "substitute", "analog"], -40f),
        new(["liver", "giblets", "heart", "gizzard", "tongue", "kidney", "brain", "tripe", "sweetbreads"], -30f),
        new(["mechanically deboned", "mechanically separated"], -40f, UnlessContains: "mechanically"),
        new(["juice", "oil", "butter", "buns", "frosted", "products", "liquid", "nectar", "concentrate", "roll", "sliced", "deli"], -15f),
        // "corned" is excluded here — the dedicated Corn modifier rule below owns that
        // signal with a stronger, more targeted penalty; stacking both double-counted it.
        new(["cured", "salt pork", "smoked"], -15f, MaxQueryTokens: 2),
        new(["(hopi)", "(navajo)", "(apache)", "(alaska native)", "hohoysi", "shoshone", "tundra", "laborador"], -25f, MaxQueryTokens: 1),
    ];

    public static float ScoreConditionalPenalties(string nameLower, string queryLower, int queryTokenCount)
    {
        float score = 0f;
        foreach (var rule in ConditionalPenalties)
        {
            if (queryTokenCount > rule.MaxQueryTokens) continue;
            foreach (var term in rule.Terms)
            {
                if (nameLower.Contains(term) && !queryLower.Contains(rule.UnlessContains ?? term))
                {
                    score += rule.Penalty;
                    break;
                }
            }
        }
        return score;
    }

    // ────────────────────────────────────────────────────────────────
    // Food-specific modifier rules — replaces the old per-food special-case branches.
    // ────────────────────────────────────────────────────────────────

    public static readonly FoodModifierRule[] ModifierRules =
    [
        new(["egg", "eggs", "milk"], QueryMatch.Exact,
        [
            new("whole", 15f),
            new("white", -10f, UnlessQueried: true),
            new("yolk", -10f, UnlessQueried: true),
            new("buttermilk", -20f, UnlessQueried: true),
            new("dry", -15f, UnlessQueried: true),
        ]),
        new(["yogurt", "yoghurt"], QueryMatch.Exact,
        [
            new("plain", 15f),
            new("strawberry", -10f),
            new("blueberry", -10f),
            new("vanilla", -10f),
        ]),
        new(["bacon"], QueryMatch.Exact, [new("turkey", -25f, UnlessQueried: true)]),
        new([.. SpiceTerms], QueryMatch.Exact,
        [
            new(["spices,", "ground"], 25f),
            new(["buns", "bread", "pastry", "danish", "frosted"], -30f),
        ]),
        new(["coffee"], QueryMatch.Exact,
        [
            new(["soymilk", "soy milk"], -30f),
            new(["beverages", "coffee"], 10f, RequireAll: true),
        ]),
        new(["tea"], QueryMatch.Exact, [new(["beverages", "tea"], 10f, RequireAll: true)]),
        new(["coconut milk"], QueryMatch.Exact, [new("coconut", -20f, InvertPresence: true)]),
        new(["lime", "lemon", "orange", "grapefruit"], QueryMatch.Exact, [new("juice", -45f, UnlessQueried: true)]),
        new(["bean"], QueryMatch.Contains, [new("liquid", -30f)]),
        new(["crab", "crabs"], QueryMatch.Exact, [new("crabapple", -60f), new("crustacean", 15f)]),
        new(["mustard"], QueryMatch.Exact, [new(["greens", "spinach"], -25f), new(["prepared", "yellow"], 10f)]),
        new(["bread"], QueryMatch.Exact, [new("pan dulce", -15f)]),
        new(["corn"], QueryMatch.Contains, [new("corned", -60f)], ExcludeIfQueryContains: "corned"),
    ];

    public static float ScoreModifierRules(string nameLower, string queryLower)
    {
        float score = 0f;
        foreach (var rule in ModifierRules)
        {
            if (!rule.Triggered(queryLower)) continue;
            foreach (var effect in rule.Effects)
                score += effect.Apply(nameLower, queryLower);
        }

        // Raw citrus preference is an AND-of-(presence, absence) combination that doesn't
        // fit the OR/AND-of-terms shape above — kept as one small documented exception
        // rather than complicating the general rule schema for a single case.
        if (queryLower is "lime" or "lemon" or "orange" or "grapefruit" && nameLower.Contains("raw") && !nameLower.Contains("juice"))
            score += 15f;

        return score;
    }
}
