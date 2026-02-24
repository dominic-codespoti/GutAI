using System.Net;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class CompositeNutritionService : INutritionApiService
{
    private readonly EdamamFoodClient _edamam;
    private readonly CalorieNinjasClient _calorieNinjas;
    private readonly NaturalLanguageFallbackService _fallback;
    private readonly ILogger<CompositeNutritionService> _logger;

    public CompositeNutritionService(
        EdamamFoodClient edamam,
        CalorieNinjasClient calorieNinjas,
        NaturalLanguageFallbackService fallback,
        ILogger<CompositeNutritionService> logger)
    {
        _edamam = edamam;
        _calorieNinjas = calorieNinjas;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<List<ParsedFoodItemDto>> ParseNaturalLanguageAsync(string text, CancellationToken ct = default)
    {
        // 1. Try Edamam first (if configured)
        if (_edamam.IsConfigured)
        {
            try
            {
                var results = await _edamam.ParseNaturalLanguageAsync(text, ct);
                if (results.Count > 0)
                    return results;

                _logger.LogInformation("Edamam returned empty results for '{Text}', trying next source", text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Edamam failed for '{Text}', trying next source", text);
            }
        }

        // 2. Try CalorieNinjas
        try
        {
            var results = await _calorieNinjas.ParseNaturalLanguageAsync(text, ct);
            if (results.Count > 0)
                return results;

            _logger.LogInformation("CalorieNinjas returned empty results for '{Text}', falling back", text);
        }
        catch (CalorieNinjasApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("CalorieNinjas returned {StatusCode}, falling back to food database search", ex.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CalorieNinjas failed unexpectedly for '{Text}', falling back", text);
        }

        // 3. Free fallback (NaturalLanguageFallbackService using OFF + USDA)
        return await _fallback.ParseAsync(text, ct);
    }
}

public class CalorieNinjasApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public CalorieNinjasApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
