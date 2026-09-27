using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// One model-supplied meal item (Coach <c>propose_meal</c> / MCP <c>gutai_propose_meal</c>).
/// Models supply identity and portions only — never nutrition numbers.
/// </summary>
/// <param name="Name">Food name (required when <paramref name="FoodProductId"/> is absent).</param>
/// <param name="FoodProductId">Persisted catalog product id from a search result.</param>
/// <param name="Servings">Number of servings (clamped by the resolver).</param>
/// <param name="ServingWeightG">Grams per serving; defaults to the product serving or an estimate.</param>
/// <param name="MatchConfidence">Search-time identity confidence for <paramref name="FoodProductId"/>, when known.</param>
public sealed record AgentMealItemInput(
    string? Name,
    Guid? FoodProductId,
    decimal Servings = 1m,
    decimal? ServingWeightG = null,
    decimal? MatchConfidence = null);

/// <summary>
/// Shared Coach/MCP item builder (plan §2.4): turns model-supplied items into meal-draft items
/// with a per-100 g basis. Product ids resolve from the catalog; names go through
/// <c>IFoodSearchService.ResolveAsync</c> + <c>GroundingPolicy</c>, and a resolution the policy
/// will not auto-select comes back as a <c>NeedsChoice</c> item with candidates; names with no
/// candidates and the free-text fallback go through natural-language parsing. Never writes meals.
/// </summary>
public interface IAgentMealItemResolver
{
    /// <param name="region">The user's <c>PreferredFoodRegion</c>, used by the natural-language fallback's web cascade.</param>
    Task<IReadOnlyList<MealDraftItemDto>> ResolveAsync(
        IReadOnlyList<AgentMealItemInput> items,
        string? fallbackDescription = null,
        FoodRegion region = FoodRegion.Default,
        CancellationToken ct = default);
}
