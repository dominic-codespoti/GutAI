namespace GutAI.Application.Common.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
// Grounded meal suggestions (plan Phase 7). The model picks pool items and grams
// only; every number here is computed server-side by NutritionCalculator.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Calories and macros for goals, consumption, remaining budget or a meal target.</summary>
public sealed record NutritionTargetsDto
{
    public decimal Calories { get; init; }
    public decimal ProteinG { get; init; }
    public decimal CarbsG { get; init; }
    public decimal FatG { get; init; }
    public decimal FiberG { get; init; }
}

/// <summary>
/// Today's nutrition budget in the user's timezone: goals minus committed diary totals.
/// </summary>
public sealed record NutritionBudgetDto
{
    /// <summary>The user's local calendar date the budget covers.</summary>
    public required DateOnly Date { get; init; }

    public required NutritionTargetsDto Goals { get; init; }
    public required NutritionTargetsDto Consumed { get; init; }

    /// <summary>Goals minus consumed, clamped at zero per nutrient.</summary>
    public required NutritionTargetsDto Remaining { get; init; }

    public int MealCount { get; init; }

    /// <summary>Logged items without nutrition — consumed totals are lower bounds when non-zero.</summary>
    public int ItemsWithoutNutrition { get; init; }

    /// <summary>Meal type the <see cref="MealTarget"/> was split for, when requested.</summary>
    public string? MealType { get; init; }

    /// <summary>Share of the remaining budget for <see cref="MealType"/> (configured meal shares).</summary>
    public NutritionTargetsDto? MealTarget { get; init; }
}

public sealed record MealSuggestionRequest
{
    /// <summary>Breakfast | Lunch | Dinner | Snack.</summary>
    public required string MealType { get; init; }

    /// <summary>Optional free-text preference (≤ 200 chars), passed to the model as delimited user content.</summary>
    public string? Preferences { get; init; }
}

/// <summary>One validated suggestion, persisted as a <c>suggestion</c>-origin meal draft.</summary>
public sealed record MealSuggestionDto
{
    public required string Title { get; init; }
    public required string Rationale { get; init; }

    /// <summary>Pending draft with server-computed items and totals; commit via the drafts API.</summary>
    public required MealDraftDto Draft { get; init; }
}

public sealed record MealSuggestionResultDto
{
    public required NutritionBudgetDto Budget { get; init; }
    public required IReadOnlyList<MealSuggestionDto> Suggestions { get; init; }
    public required string PromptVersion { get; init; }

    /// <summary>Model suggestions dropped by deterministic validation after repair.</summary>
    public int RejectedCount { get; init; }
}

/// <summary>Whether grounded meal suggestions are enabled (<c>Features:MealSuggestions</c>).</summary>
public sealed record MealSuggestionStatusDto
{
    public required bool Enabled { get; init; }
}
