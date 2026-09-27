using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// Grounded meal suggestions (plan Phase 7): a server-built candidate pool, a model that picks
/// pool items and grams only, and deterministic validation (budget fit, FODMAP screen, user
/// exclusions). Each valid suggestion is persisted as a <c>suggestion</c>-origin meal draft.
/// </summary>
public interface IMealSuggestionService
{
    /// <exception cref="ArgumentException">Unknown meal type or preferences over 200 characters.</exception>
    Task<MealSuggestionResultDto> SuggestAsync(
        Guid userId,
        MealSuggestionRequest request,
        string? timezoneId = null,
        CancellationToken ct = default);
}
