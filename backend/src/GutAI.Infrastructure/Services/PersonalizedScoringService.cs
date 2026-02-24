using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public class PersonalizedScoringService
{
    private readonly GutRiskService _gutRisk;
    private readonly FodmapService _fodmap;

    private static readonly string[] PolyolKeywords =
        ["sorbitol", "maltitol", "xylitol", "isomalt", "mannitol", "lactitol", "erythritol"];

    public PersonalizedScoringService(GutRiskService gutRisk, FodmapService fodmap)
    {
        _gutRisk = gutRisk;
        _fodmap = fodmap;
    }

    public async Task<PersonalizedScoreDto> ScoreAsync(FoodProductDto product, Guid userId, ITableStore store)
    {
        var explanations = new List<ScoreExplanationDto>();
        var warnings = new List<string>();

        // 1. FODMAP component (30%)
        var fodmapScore = _fodmap.Assess(product).FodmapScore;
        explanations.Add(new ScoreExplanationDto
        {
            Component = "FODMAP Risk",
            Weight = 30,
            RawScore = fodmapScore,
            WeightedContribution = (int)(fodmapScore * 0.30),
            Explanation = fodmapScore >= 80
                ? "Low FODMAP content — unlikely to trigger digestive symptoms."
                : fodmapScore >= 60
                    ? "Moderate FODMAP content — may cause issues for sensitive individuals."
                    : fodmapScore >= 40
                        ? "High FODMAP content — likely to trigger bloating, gas, or discomfort."
                        : "Very high FODMAP content — strong trigger risk for IBS and FODMAP-sensitive individuals.",
        });

        // 2. Additive Risk component (15%)
        var additiveScore = _gutRisk.Assess(product).GutScore;
        explanations.Add(new ScoreExplanationDto
        {
            Component = "Additive Risk",
            Weight = 15,
            RawScore = additiveScore,
            WeightedContribution = (int)(additiveScore * 0.15),
            Explanation = additiveScore >= 80
                ? "Few or no concerning additives detected."
                : additiveScore >= 50
                    ? "Some gut-irritating additives present (emulsifiers, artificial sweeteners, etc.)."
                    : "Multiple harmful additives detected that may damage gut lining or disrupt microbiome.",
        });

        // 3. NOVA Processing component (15%)
        var novaScore = product.NovaGroup switch
        {
            1 => 100,
            2 => 75,
            3 => 50,
            4 => 30,
            _ => 60,
        };
        explanations.Add(new ScoreExplanationDto
        {
            Component = "NOVA Processing",
            Weight = 15,
            RawScore = novaScore,
            WeightedContribution = (int)(novaScore * 0.15),
            Explanation = product.NovaGroup switch
            {
                1 => "Unprocessed or minimally processed food.",
                2 => "Processed culinary ingredient.",
                3 => "Processed food — moderate level of industrial processing.",
                4 => "Ultra-processed food — associated with gut microbiome disruption and inflammation.",
                _ => "Processing level unknown; assuming moderate processing.",
            },
        });

        // 4. Fiber Content component (15%)
        var fiberScore = product.Fiber100g switch
        {
            >= 6m => 100,
            >= 3m => 75,
            >= 1m => 50,
            < 1m => 25,
            _ => 25,
        };
        explanations.Add(new ScoreExplanationDto
        {
            Component = "Fiber Content",
            Weight = 15,
            RawScore = fiberScore,
            WeightedContribution = (int)(fiberScore * 0.15),
            Explanation = product.Fiber100g switch
            {
                >= 6m => $"High fiber ({product.Fiber100g:F1}g/100g) — supports healthy gut motility and microbiome diversity.",
                >= 3m => $"Moderate fiber ({product.Fiber100g:F1}g/100g) — contributes to daily fiber goals.",
                >= 1m => $"Low fiber ({product.Fiber100g:F1}g/100g) — limited prebiotic benefit.",
                < 1m => $"Very low fiber ({product.Fiber100g:F1}g/100g) — negligible gut health benefit.",
                _ => "Fiber data not available — no fiber bonus applied.",
            },
        });

        // 5. Allergen Match component (15%)
        var user = await store.GetUserAsync(userId);
        var userAllergies = user?.Allergies ?? [];
        var allergenScore = 100;

        if (userAllergies.Length > 0 && product.AllergensTags.Length > 0)
        {
            var matchedAllergens = new List<string>();
            foreach (var allergen in product.AllergensTags)
            {
                var normalizedAllergen = allergen.Replace("en:", "").Trim();
                foreach (var userAllergy in userAllergies)
                {
                    if (normalizedAllergen.Contains(userAllergy, StringComparison.OrdinalIgnoreCase)
                        || userAllergy.Contains(normalizedAllergen, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedAllergens.Add(normalizedAllergen);
                        break;
                    }
                }
            }

            if (matchedAllergens.Count > 0)
            {
                allergenScore = 0;
                foreach (var match in matchedAllergens)
                    warnings.Add($"⚠️ Contains {match} — listed in your allergen profile.");
            }
        }

        explanations.Add(new ScoreExplanationDto
        {
            Component = "Allergen Match",
            Weight = 15,
            RawScore = allergenScore,
            WeightedContribution = (int)(allergenScore * 0.15),
            Explanation = allergenScore == 100
                ? "No allergens matching your profile detected."
                : "This product contains allergens you've flagged — avoid or use extreme caution.",
        });

        // 6. Sugar Alcohols component (10%)
        var lowerIngredients = (product.Ingredients ?? "").ToLowerInvariant();
        var polyolCount = PolyolKeywords.Count(p => lowerIngredients.Contains(p));
        var sugarAlcoholScore = polyolCount switch
        {
            0 => 100,
            1 => 60,
            2 => 30,
            _ => 10,
        };
        explanations.Add(new ScoreExplanationDto
        {
            Component = "Sugar Alcohols",
            Weight = 10,
            RawScore = sugarAlcoholScore,
            WeightedContribution = (int)(sugarAlcoholScore * 0.10),
            Explanation = polyolCount switch
            {
                0 => "No sugar alcohols detected in ingredients.",
                1 => "Contains 1 sugar alcohol — may cause mild digestive discomfort in sensitive individuals.",
                2 => "Contains 2 sugar alcohols — moderate risk of bloating and laxative effects.",
                _ => $"Contains {polyolCount} sugar alcohols — high risk of gas, bloating, and diarrhea.",
            },
        });

        // 7. Personal Trigger Penalty
        var personalPenalty = 0;
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var cutoffFrom = DateOnly.FromDateTime(cutoff);
        var cutoffTo = DateOnly.FromDateTime(DateTime.UtcNow);

        var allSymptoms = await store.GetSymptomLogsByDateRangeAsync(userId, cutoffFrom, cutoffTo);
        var symptomLogs = allSymptoms
            .Where(s => s.Severity >= 4)
            .Select(s => new { s.OccurredAt, s.RelatedMealLogId })
            .ToList();

        var triggerFoodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (symptomLogs.Count > 0)
        {
            var symptomTimes = symptomLogs.Select(s => s.OccurredAt).ToList();
            var earliest = symptomTimes.Min().AddHours(-6);

            var allMeals = await store.GetMealLogsByDateRangeAsync(userId, cutoffFrom, cutoffTo);
            foreach (var meal in allMeals)
                meal.Items = await store.GetMealItemsAsync(userId, meal.Id);

            var candidateMeals = allMeals
                .Where(m => m.LoggedAt >= earliest)
                .Select(m => new { m.LoggedAt, Items = m.Items.Select(i => i.FoodName).ToList() })
                .ToList();

            foreach (var symptom in symptomLogs)
            {
                var windowStart = symptom.OccurredAt.AddHours(-6);
                var windowEnd = symptom.OccurredAt.AddHours(-2);

                foreach (var meal in candidateMeals)
                {
                    if (meal.LoggedAt >= windowStart && meal.LoggedAt <= windowEnd)
                    {
                        foreach (var foodName in meal.Items)
                            triggerFoodNames.Add(foodName);
                    }
                }
            }

            if (triggerFoodNames.Count > 0)
            {
                var productName = product.Name.ToLowerInvariant();
                var productIngredients = lowerIngredients;
                var matchCount = 0;

                foreach (var trigger in triggerFoodNames)
                {
                    var lowerTrigger = trigger.ToLowerInvariant();
                    if ((productName?.Contains(lowerTrigger) ?? false) || (productIngredients?.Contains(lowerTrigger) ?? false)
                        || (lowerTrigger?.Contains(productName) ?? false))
                    {
                        matchCount++;
                        warnings.Add($"🔁 \"{trigger}\" appeared in meals before your recent symptoms.");
                        if (matchCount >= 5) break;
                    }
                }

                personalPenalty = Math.Min(matchCount * 5, 25);
            }
        }

        // 8. Composite Score
        var rawComposite =
            (int)(fodmapScore * 0.30
                  + additiveScore * 0.15
                  + novaScore * 0.15
                  + fiberScore * 0.15
                  + allergenScore * 0.15
                  + sugarAlcoholScore * 0.10);

        var composite = Math.Clamp(rawComposite - personalPenalty, 0, 100);

        // 9. Rating
        var rating = composite switch
        {
            >= 80 => "Excellent",
            >= 60 => "Good",
            >= 40 => "Fair",
            >= 20 => "Poor",
            _ => "Avoid",
        };

        // 10. Summary
        var summary = rating switch
        {
            "Excellent" => $"{product.Name} scores {composite}/100 — an excellent choice for your gut health with minimal risks across all categories.",
            "Good" => $"{product.Name} scores {composite}/100 — a generally good option, though some components could be better.",
            "Fair" => $"{product.Name} scores {composite}/100 — proceed with caution. There are notable concerns in one or more areas.",
            "Poor" => $"{product.Name} scores {composite}/100 — significant gut health concerns. Consider alternatives if you're sensitive.",
            _ => $"{product.Name} scores {composite}/100 — high risk across multiple gut health factors. Strongly consider a safer substitute.",
        };

        if (personalPenalty > 0)
            summary += $" Your personal history contributed a -{personalPenalty} point adjustment based on past symptom correlations.";

        return new PersonalizedScoreDto
        {
            CompositeScore = composite,
            Rating = rating,
            FodmapComponent = fodmapScore,
            AdditiveRiskComponent = additiveScore,
            NovaComponent = novaScore,
            FiberComponent = fiberScore,
            AllergenComponent = allergenScore,
            SugarAlcoholComponent = sugarAlcoholScore,
            PersonalTriggerPenalty = personalPenalty,
            Explanations = explanations,
            PersonalWarnings = warnings,
            Summary = summary,
        };
    }
}
