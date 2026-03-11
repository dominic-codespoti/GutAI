using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public class GlycemicIndexService : IGlycemicIndexService
{
    static readonly Regex ServingSizeGramsRegex = new(@"(\d+(?:\.\d+)?)\s*g\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public GlycemicAssessmentDto Assess(FoodProductDto product)
    {
        // Skip GI assessment for very-low-carb products
        if (product.Carbs100g is null or < 5m)
        {
            return new GlycemicAssessmentDto
            {
                EstimatedGI = null,
                GiCategory = "Not Applicable",
                EstimatedGL = null,
                GlCategory = "Not Applicable",
                MatchCount = 0,
                Matches = [],
                GutImpactSummary = "Glycemic index is not meaningful for very-low-carb products (less than 5g carbohydrates per 100g).",
                Recommendations = ["This product has minimal carbohydrate content, so blood sugar impact is likely negligible."],
                Confidence = "High",
            };
        }

        var matches = new List<GlycemicMatchDto>();
        var lower = (product.Ingredients ?? "").ToLowerInvariant();
        var name = (product.Name ?? "").ToLowerInvariant();
        var hasIngredients = !string.IsNullOrWhiteSpace(product.Ingredients) && product.Ingredients.Contains(',');
        var matchedPatterns = new List<string>();

        MatchPatterns(hasIngredients ? lower : name, name, hasIngredients, matches, matchedPatterns);

        // Fallback to nutrition-based estimation
        if (matches.Count == 0)
        {
            var estimated = EstimateFromNutrition(product);
            if (estimated != null)
                matches.Add(estimated);
        }

        if (matches.Count == 0)
        {
            return new GlycemicAssessmentDto
            {
                GiCategory = "Unknown",
                GlCategory = "Unknown",
                GutImpactSummary = "Unable to estimate glycemic index for this product.",
                Confidence = "Low",
            };
        }

        // Position-weighted GI calculation
        var avgGI = ComputeWeightedGI(matches, hasIngredients);
        var carbs = product.Carbs100g ?? 0m;

        // Serving-size-aware GL calculation
        var servingGrams = ParseServingGrams(product.ServingSize);
        decimal carbsForGL;
        if (servingGrams > 0)
            carbsForGL = carbs * servingGrams / 100m;
        else
            carbsForGL = carbs; // fallback to per-100g

        var gl = Math.Round(avgGI * carbsForGL / 100m, 1);

        var giCategory = ClassifyGI(avgGI);
        var glCategory = ClassifyGL(gl);
        var confidence = matches.Any(m => m.Source == "Estimated") ? "Medium" : "High";

        var recommendations = GenerateRecommendations(avgGI, gl, product, giCategory);

        return new GlycemicAssessmentDto
        {
            EstimatedGI = avgGI,
            GiCategory = giCategory,
            EstimatedGL = gl,
            GlCategory = glCategory,
            MatchCount = matches.Count,
            Matches = matches,
            GutImpactSummary = BuildGutImpactSummary(avgGI, gl, giCategory, glCategory),
            Recommendations = recommendations,
            Confidence = confidence,
        };
    }

    public GlycemicAssessmentDto AssessText(string text)
    {
        var lower = text.ToLowerInvariant();
        var matches = new List<GlycemicMatchDto>();
        var matchedPatterns = new List<string>();

        MatchPatterns(lower, lower, false, matches, matchedPatterns);

        if (matches.Count == 0)
        {
            return new GlycemicAssessmentDto
            {
                GiCategory = "Unknown",
                GlCategory = "Unknown",
                GutImpactSummary = "Unable to estimate glycemic index from the provided text.",
                Confidence = "Low",
            };
        }

        var avgGI = ComputeWeightedGI(matches, false);
        var giCategory = ClassifyGI(avgGI);

        var recommendations = new List<string>();
        if (avgGI >= 70)
        {
            recommendations.Add("This is a high-GI food — consider pairing with protein, healthy fats, or fiber to moderate blood sugar response.");
            recommendations.Add("Look for lower-GI alternatives where possible.");
        }

        return new GlycemicAssessmentDto
        {
            EstimatedGI = avgGI,
            GiCategory = giCategory,
            EstimatedGL = null,
            GlCategory = "Unknown",
            MatchCount = matches.Count,
            Matches = matches,
            GutImpactSummary = BuildGutImpactSummary(avgGI, 0m, giCategory, "Unknown"),
            Recommendations = recommendations,
            Confidence = matches.Count > 0 ? "High" : "Low",
        };
    }

    // ─── Shared matching logic ──────────────────────────────────────────

    static void MatchPatterns(string searchText, string nameText, bool hasIngredients,
        List<GlycemicMatchDto> matches, List<string> matchedPatterns)
    {
        foreach (var entry in GlycemicData.GiDatabase)
        {
            // Use pre-compiled regex with word boundaries
            if (!entry.Regex.IsMatch(searchText))
                continue;

            // Check exclusions
            if (entry.Exclusions != null)
            {
                var excluded = false;
                foreach (var excl in entry.Exclusions)
                {
                    if (searchText.Contains(excl, StringComparison.OrdinalIgnoreCase))
                    {
                        excluded = true;
                        break;
                    }
                }
                if (excluded) continue;
            }

            // Skip if a longer pattern already covers this
            if (matchedPatterns.Any(mp => mp.Contains(entry.Pattern, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Deduplicate by food name
            if (matches.Any(m => m.Food.Equals(entry.Pattern, StringComparison.OrdinalIgnoreCase)))
                continue;

            matchedPatterns.Add(entry.Pattern);
            matches.Add(new GlycemicMatchDto
            {
                Food = entry.Pattern,
                GI = entry.GI,
                GiCategory = ClassifyGI(entry.GI),
                Source = entry.Source,
                Notes = entry.Notes,
            });
        }
    }

    static int ComputeWeightedGI(List<GlycemicMatchDto> matches, bool hasIngredients)
    {
        if (matches.Count == 1)
            return matches[0].GI;

        if (!hasIngredients)
        {
            // No ingredients list — use simple average (we can't determine position)
            return (int)Math.Round(matches.Average(m => m.GI));
        }

        // Position-weighted: 1st=60%, 2nd=25%, 3rd=10%, rest share 5%
        double[] weights = matches.Count switch
        {
            2 => [0.65, 0.35],
            3 => [0.60, 0.25, 0.15],
            _ => ComputePositionWeights(matches.Count),
        };

        var weightedSum = 0.0;
        for (var i = 0; i < matches.Count; i++)
            weightedSum += matches[i].GI * weights[i];

        return (int)Math.Round(weightedSum);
    }

    static double[] ComputePositionWeights(int count)
    {
        var weights = new double[count];
        weights[0] = 0.60;
        if (count > 1) weights[1] = 0.25;
        if (count > 2) weights[2] = 0.10;
        var remaining = 0.05;
        if (count > 3)
        {
            var share = remaining / (count - 3);
            for (var i = 3; i < count; i++)
                weights[i] = share;
        }
        return weights;
    }

    static decimal ParseServingGrams(string? servingSize)
    {
        if (string.IsNullOrWhiteSpace(servingSize)) return 0;
        var match = ServingSizeGramsRegex.Match(servingSize);
        return match.Success && decimal.TryParse(match.Groups[1].Value, out var grams) ? grams : 0;
    }

    // ─── Classification ─────────────────────────────────────────────────

    static string ClassifyGI(int gi) => gi switch
    {
        <= 55 => "Low",
        <= 69 => "Medium",
        _ => "High",
    };

    static string ClassifyGL(decimal gl) => gl switch
    {
        <= 10m => "Low",
        <= 19m => "Medium",
        _ => "High",
    };

    // ─── Recommendations ────────────────────────────────────────────────

    static List<string> GenerateRecommendations(int gi, decimal gl, FoodProductDto product, string giCategory)
    {
        var recs = new List<string>();

        if (gi >= 70)
        {
            if ((product.Protein100g ?? 0) <= 10)
                recs.Add("Consider pairing with a protein source to help moderate blood sugar response.");
            if ((product.Fat100g ?? 0) <= 5)
                recs.Add("Adding healthy fats can slow glucose absorption.");
            recs.Add("Look for lower-GI alternatives in the same category.");
        }
        else if (gi >= 56)
        {
            if ((product.Fiber100g ?? 0) <= 3)
                recs.Add("Choose fiber-rich foods alongside to further moderate blood sugar response.");
        }

        if (gl >= 20)
            recs.Add("Consider smaller portion sizes — this product has a high glycemic load per serving.");

        if ((product.Fiber100g ?? 0) > 3)
            recs.Add("Good fiber content helps moderate blood sugar response.");

        if ((product.Fiber100g ?? 0) < 1 && (product.Carbs100g ?? 0) > 30)
            recs.Add("This is a high-carb, low-fiber product — consider adding a fiber source to your meal.");

        if ((product.NovaGroup ?? 0) >= 4 && gi >= 60)
            recs.Add("Ultra-processed high-GI foods may cause sharper blood sugar spikes than whole food equivalents.");

        return recs;
    }

    // ─── Gut Impact Summary ─────────────────────────────────────────────

    static string BuildGutImpactSummary(int gi, decimal gl, string giCategory, string glCategory)
    {
        if (giCategory == "Not Applicable")
            return "Very low carbohydrate content — minimal glycemic impact expected.";

        var parts = new List<string>();

        if (gi >= 70)
            parts.Add($"This is a high-GI food (estimated GI: {gi}), which may cause rapid blood sugar spikes. In the gut, rapid glucose absorption can affect motility and may contribute to reactive hypoglycemia symptoms.");
        else if (gi >= 56)
            parts.Add($"This is a medium-GI food (estimated GI: {gi}), with moderate blood sugar impact. Generally well-tolerated from a gut perspective.");
        else
            parts.Add($"This is a low-GI food (estimated GI: {gi}), which promotes gradual glucose release. Low-GI foods tend to be gentler on the digestive system.");

        if (gl >= 20)
            parts.Add("The glycemic load is high, meaning a typical serving delivers a significant glucose dose.");

        return string.Join(" ", parts);
    }

    // ─── Nutrition-Based Estimation ─────────────────────────────────────

    static GlycemicMatchDto? EstimateFromNutrition(FoodProductDto product)
    {
        var carbs = product.Carbs100g;
        if (carbs is null or 0 or < 5) return null;

        // Whole foods with fiber tend to have much lower GI than processed foods.
        // Use a lower base for trusted whole foods to avoid overestimating GI for vegetables.
        bool isWholeFood = product.FoodKind == GutAI.Domain.Enums.FoodKind.WholeFood ||
            product.DataSource is "USDA" or "AUSNUT";
        var hasFiber = (product.Fiber100g ?? 0) > 2;
        var baseGI = carbs < 10 ? 35
            : isWholeFood && hasFiber ? 40
            : 55;
        var sugar = product.Sugar100g ?? 0;
        var fiber = product.Fiber100g ?? 0;
        var protein = product.Protein100g ?? 0;
        var fat = product.Fat100g ?? 0;
        var name = (product.Name ?? "").ToLowerInvariant();

        // Sugar ratio adjustment
        var sugarRatio = carbs > 0 ? sugar / carbs.Value : 0;
        if (sugarRatio > 0.6m) baseGI += 15;
        else if (sugarRatio > 0.3m) baseGI += 8;

        // Fiber adjustment
        if (fiber > 5) baseGI -= 6;
        else if (fiber > 2) baseGI -= 3;

        // Protein adjustment
        if (protein > 10) baseGI -= 4;
        else if (protein > 5) baseGI -= 2;

        // Fat adjustment
        if (fat > 10) baseGI -= 6;
        else if (fat > 5) baseGI -= 3;

        // Processing level awareness
        if ((product.NovaGroup ?? 0) >= 4) baseGI += 8;

        // Name-based heuristics
        if (name.Contains("whole grain") || name.Contains("whole wheat") || name.Contains("wholemeal"))
            baseGI -= 8;
        if (name.Contains("instant") || name.Contains("quick"))
            baseGI += 10;
        if (name.Contains("raw") || name.Contains("uncooked"))
            baseGI -= 5;

        baseGI = Math.Clamp(baseGI, 20, 95);

        return new GlycemicMatchDto
        {
            Food = product.Name ?? "Unknown",
            GI = baseGI,
            GiCategory = ClassifyGI(baseGI),
            Source = "Estimated",
            Notes = "No direct database match found. GI estimated from macronutrient profile and product characteristics.",
        };
    }
}
