using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.ExternalApis;

public class CompositeNutritionService : INutritionApiService
{
    private readonly NaturalLanguageFallbackService _fallback;

    public CompositeNutritionService(NaturalLanguageFallbackService fallback)
    {
        _fallback = fallback;
    }

    public Task<List<ParsedFoodItemDto>> ParseNaturalLanguageAsync(string text, CancellationToken ct = default)
        => _fallback.ParseAsync(text, ct);
}
