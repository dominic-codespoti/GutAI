using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// Compact per-user coach state injected each turn as a delimited <c>&lt;session_state&gt;</c>
/// user-data block. Keeps catalog ids resolved in earlier turns (history is text-only) so the
/// present → confirm workflow never re-searches or falls back to a name match. Cleared with
/// chat history.
/// </summary>
public sealed record CoachSessionState
{
    /// <summary>Most recent catalog foods returned by coach searches (bounded, newest last).</summary>
    public List<CoachResolvedFood> ResolvedFoods { get; init; } = [];

    /// <summary>Coach-origin meal drafts awaiting confirmation.</summary>
    public List<Guid> OpenDraftIds { get; init; } = [];

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A persisted catalog food the coach has seen, with its search-time identity confidence.</summary>
public sealed record CoachResolvedFood(
    string Name,
    Guid FoodProductId,
    NutritionPer100gDto? Per100g,
    decimal MatchConfidence);
