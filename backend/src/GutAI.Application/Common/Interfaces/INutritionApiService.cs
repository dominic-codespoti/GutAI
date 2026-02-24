using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface INutritionApiService
{
    Task<List<ParsedFoodItemDto>> ParseNaturalLanguageAsync(string text, CancellationToken ct = default);
}
