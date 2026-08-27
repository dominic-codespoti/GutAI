using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// Generic candidate compatibility signals shared by meal-scan retrieval.
/// This scores source quality, preparation/state compatibility, data completeness,
/// and unsupported specificity without naming individual foods.
/// </summary>
internal static class FoodCandidateCompatibilityScorer
{
    private static readonly string[] PreparationTerms =
    [
        "raw", "fresh", "cooked", "boiled", "fried", "scrambled", "grilled",
        "roasted", "baked", "steamed", "sauteed", "sautéed", "toasted", "breaded",
        "smoked", "pre-cooked", "precooked", "fully cooked",
    ];

    private static readonly string[] ProductFormTerms =
    [
        "snack", "mix", "patty", "patties", "bites", "bar", "pastry", "drink",
        "supplement", "powder", "sauce", "gravy",
    ];

    public static float Score(ScannedComponent observation, FoodProductDto candidate)
    {
        var score = (float)(candidate.MatchConfidence * 100m);
        var observationText = string.Join(
            ' ',
            new[] { observation.Name, observation.PreparationNote }
                .Concat(observation.SearchQueries));
        var observationLower = observationText.ToLowerInvariant();
        var candidateLower = candidate.Name.ToLowerInvariant();
        var queryTokens = FoodTextNormalizer.Tokenize(observationLower)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateTokens = FoodTextNormalizer.Tokenize(candidateLower);

        if (candidate.FoodKind == FoodKind.WholeFood)
            score += 8f;
        else if (candidate.FoodKind == FoodKind.Unknown && string.IsNullOrWhiteSpace(candidate.Brand))
            score += 3f;

        var brandMentioned = !string.IsNullOrWhiteSpace(candidate.Brand)
            && observationLower.Contains(candidate.Brand, StringComparison.OrdinalIgnoreCase);
        if (!brandMentioned && !string.IsNullOrWhiteSpace(candidate.Brand))
            score -= 8f;

        foreach (var preparation in PreparationTerms)
        {
            var observed = observationLower.Contains(preparation, StringComparison.OrdinalIgnoreCase);
            var candidateHas = candidateLower.Contains(preparation, StringComparison.OrdinalIgnoreCase);
            if (!observed || !candidateHas)
                continue;

            score += 5f;
        }

        var observedRaw = observationLower.Contains("raw", StringComparison.OrdinalIgnoreCase)
            || observationLower.Contains("fresh", StringComparison.OrdinalIgnoreCase);
        var observedCooked = PreparationTerms
            .Where(t => t is not "raw" and not "fresh")
            .Any(t => observationLower.Contains(t, StringComparison.OrdinalIgnoreCase));
        var candidateRaw = candidateLower.Contains("raw", StringComparison.OrdinalIgnoreCase)
            || candidateLower.Contains("fresh", StringComparison.OrdinalIgnoreCase);
        var candidateCooked = PreparationTerms
            .Where(t => t is not "raw" and not "fresh")
            .Any(t => candidateLower.Contains(t, StringComparison.OrdinalIgnoreCase));

        if (observedCooked && candidateRaw)
            score -= 18f;
        if (observedRaw && candidateCooked)
            score -= 12f;

        if (!ProductFormTerms.Any(term => observationLower.Contains(term, StringComparison.OrdinalIgnoreCase))
            && ProductFormTerms.Any(candidateLower.Contains))
            score -= 6f;

        var excessTokens = candidateTokens.Count(token => !queryTokens.Contains(token));
        if (candidateTokens.Length >= queryTokens.Count + 4)
            score -= 12f;
        else if (excessTokens >= 3)
            score -= 6f;

        if (queryTokens.Count <= 4 && candidate.Ingredients?.Count(c => c == ',') >= 5)
            score -= 8f;

        return score;
    }
}
