using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;

namespace GutAI.Application.Common.Interfaces;

public interface INutritionApiService
{
    /// <param name="region">The user's <c>PreferredFoodRegion</c>; biases the web cascade and its cache.</param>
    Task<List<ParsedFoodItemDto>> ParseNaturalLanguageAsync(
        string text,
        FoodRegion region = FoodRegion.Default,
        CancellationToken ct = default);
}
