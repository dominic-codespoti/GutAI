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

        // FODMAP screening component (35%)
        var fodmapAssessment = _fodmap.Assess(product);
        // Additive-only component (20%). GutRisk also contains FODMAP, nutrient, and NOVA
        // signals; using its composite score here would count those dimensions twice.
        var gutRiskAssessment = _gutRisk.Assess(product);


        // Insufficient evidence must not score like a confirmed-clean result — treat it as
        // neutral rather than letting "we don't know" masquerade as "screened, nothing found".
        var fodmapScore = fodmapAssessment.Status == nameof(FodmapAssessmentStatus.InsufficientInformation)
            ? 50
            : fodmapAssessment.IngredientScreeningScore;
        explanations.Add(new ScoreExplanationDto
        {
            Component = "FODMAP Risk",
            Weight = 35,
            RawScore = fodmapScore,
            WeightedContribution = (int)(fodmapScore * 0.35),
            Explanation = fodmapAssessment.Status == nameof(FodmapAssessmentStatus.InsufficientInformation)
                ? "Not enough ingredient or product data to screen for FODMAP sources — this component is neutral, not a confirmed low-FODMAP result."
                : fodmapScore >= 80
                    ? "No or few configured FODMAP source names detected; actual load depends on portion."
                    : fodmapScore >= 60
                        ? "Some potential FODMAP sources detected; portion and individual tolerance matter."
                        : fodmapScore >= 40
                            ? "Several potential FODMAP sources detected."
                            : "Multiple higher-concern FODMAP sources detected; this remains an ingredient screen, not a measured serving classification.",
        });

        var additiveFlags = gutRiskAssessment.Flags
            .Where(flag => flag.TriggerType == "Additive")
            .ToList();
        var additiveScore = Math.Clamp(100 - additiveFlags.Sum(flag => flag.RiskLevel switch
        {
            "High" => 20,
            "Medium" => 10,
            "Low" => 5,
            _ => 0,
        }), 0, 100);
        explanations.Add(new ScoreExplanationDto
        {
            Component = "Additive Risk",
            Weight = 20,
            RawScore = additiveScore,
            WeightedContribution = (int)(additiveScore * 0.20),
            Explanation = additiveFlags.Count == 0
                ? "No configured additive concern signals detected in the available data."
                : $"Detected {additiveFlags.Count} configured additive concern signal(s); effects depend on dose and individual response.",
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
                4 => "Ultra-processed food — some research has linked ultra-processing to changes in gut microbiome composition.",
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
        var allergenDataAvailable = product.AllergensTags.Length > 0;
        var hasAllergenMatch = false;

        if (userAllergies.Length > 0 && allergenDataAvailable)
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
                hasAllergenMatch = true;
                foreach (var match in matchedAllergens)
                    warnings.Add($"Contains {match}, which is listed in your allergen profile.");
            }
        }
        else if (userAllergies.Length > 0)
        {
            warnings.Add("Allergen data is unavailable for this product; absence of a warning does not establish safety.");
        }

        explanations.Add(new ScoreExplanationDto
        {
            Component = "Allergen Match",
            Weight = 15,
            RawScore = allergenScore,
            WeightedContribution = (int)(allergenScore * 0.15),
            Explanation = userAllergies.Length > 0 && !allergenDataAvailable
                ? "Allergen data unavailable — this component is neutral and cannot establish safety."
                : hasAllergenMatch
                    ? "This product matches an allergen in your profile."
                    : "No profile allergen match was detected in the available allergen data.",
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
            Weight = 0,
            RawScore = sugarAlcoholScore,
            WeightedContribution = 0,
            Explanation = polyolCount switch
            {
                0 => "No sugar alcohols detected in ingredients.",
                1 => "Contains 1 sugar alcohol — may cause mild digestive discomfort in sensitive individuals.",
                2 => "Contains 2 sugar alcohols — some people experience bloating or digestive changes with multiple sugar alcohols.",
                _ => $"Contains {polyolCount} sugar alcohols — multiple sugar alcohols may increase the chance of digestive discomfort.",
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

        // Trigger identity: prefer FoodProductId (exact, zero false positives) and fall back to
        // normalized product-NAME containment (not ingredients — matching raw ingredient text
        // against a single common trigger word like "milk" or "egg" used to flag nearly every
        // packaged product that mentions it as an ingredient).
        var triggerFoods = new Dictionary<string, (Guid? FoodProductId, string FoodName, int SymptomAssociations)>(
            StringComparer.OrdinalIgnoreCase);

        if (symptomLogs.Count > 0)
        {
            var symptomTimes = symptomLogs.Select(s => s.OccurredAt).ToList();
            var earliest = symptomTimes.Min().AddHours(-FoodSymptomMatching.MaxOnsetHours);

            var allMeals = await store.GetMealLogsByDateRangeAsync(userId, cutoffFrom, cutoffTo);
            foreach (var meal in allMeals)
                meal.Items = await store.GetMealItemsAsync(userId, meal.Id);

            var candidateMeals = allMeals
                .Where(m => m.LoggedAt >= earliest)
                .Select(m => new { m.LoggedAt, Items = m.Items.Select(i => (i.FoodProductId, i.FoodName)).ToList() })
                .ToList();

            foreach (var symptom in symptomLogs)
            {
                var seenForSymptom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var meal in candidateMeals)
                {
                    if (!FoodSymptomMatching.IsWithinOnsetWindow(meal.LoggedAt, symptom.OccurredAt))
                        continue;

                    foreach (var (foodProductId, foodName) in meal.Items)
                    {
                        var key = foodProductId.HasValue
                            ? $"id:{foodProductId.Value}"
                            : $"name:{FoodSymptomMatching.NormalizeForGrouping(foodName)}";
                        if (!seenForSymptom.Add(key))
                            continue;

                        if (triggerFoods.TryGetValue(key, out var existing))
                            triggerFoods[key] = (existing.FoodProductId, existing.FoodName, existing.SymptomAssociations + 1);
                        else
                            triggerFoods[key] = (foodProductId, foodName, 1);
                    }
                }
            }

            if (triggerFoods.Count > 0)
            {
                var normalizedProductName = FoodSymptomMatching.NormalizeForGrouping(product.Name);
                var matchCount = 0;

                foreach (var (_, (triggerProductId, triggerName, symptomAssociations)) in triggerFoods)
                {
                    var isMatch = (triggerProductId.HasValue && triggerProductId.Value == product.Id)
                        || IsNormalizedNameMatch(triggerName, normalizedProductName);

                    if (isMatch)
                    {
                        matchCount += symptomAssociations;
                        warnings.Add($"\"{triggerName}\" appeared before {symptomAssociations} recent symptom event(s). This is an association, not proof of causation.");
                    }
                }

                personalPenalty = Math.Min(matchCount * 5, 25);
            }
        }

        // 8. Composite Score
        var rawComposite =
            (int)(fodmapScore * 0.35
                  + additiveScore * 0.20
                  + novaScore * 0.15
                  + fiberScore * 0.15
                  + allergenScore * 0.15);

        var composite = Math.Clamp(rawComposite - personalPenalty, 0, 100);
        if (hasAllergenMatch)
            composite = Math.Min(composite, 19);

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
            "Excellent" => $"{product.Name} scores {composite}/100 with few concerns detected in the available data.",
            "Good" => $"{product.Name} scores {composite}/100; some screened components could be better.",
            "Fair" => $"{product.Name} scores {composite}/100; several screened factors may be relevant.",
            "Poor" => $"{product.Name} scores {composite}/100; consider the component details and your own tolerance.",
            _ when hasAllergenMatch => $"{product.Name} matches an allergen in your profile. Do not rely on this score as medical clearance.",
            _ => $"{product.Name} scores {composite}/100; multiple screened factors scored low.",
        };

        if (personalPenalty > 0)
            summary += $" Personal history contributed a -{personalPenalty} point adjustment from repeated temporal associations; this does not establish causation.";

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

    /// <summary>
    /// Fallback trigger match when no FoodProductId is available on either side: compares
    /// normalized (case/punctuation/plural-insensitive) NAMES only, so a generic logged food
    /// like "Pizza" still flags a specific product like "Pizza Margherita", without matching
    /// against the product's full ingredient list (the source of the false-positive problem
    /// this replaced — a trigger named "milk" would otherwise flag almost any packaged food).
    /// </summary>
    private static bool IsNormalizedNameMatch(string triggerName, string normalizedProductName)
    {
        if (string.IsNullOrWhiteSpace(normalizedProductName))
            return false;

        var normalizedTrigger = FoodSymptomMatching.NormalizeForGrouping(triggerName);
        if (string.IsNullOrWhiteSpace(normalizedTrigger))
            return false;

        return normalizedProductName.Contains(normalizedTrigger, StringComparison.Ordinal)
            || normalizedTrigger.Contains(normalizedProductName, StringComparison.Ordinal);
    }
}
