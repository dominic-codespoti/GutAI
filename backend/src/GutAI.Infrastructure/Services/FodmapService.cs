using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public class FodmapService : IFodmapService
{
    static bool HasTrigger(List<FodmapTriggerDto> triggers, FodmapTriggerDto info)
    {
        // Deduplicate by SubCategory — e.g. "Sorbitol" from ingredients and "Sorbitol (E420)" from additive tags
        // are the same FODMAP trigger. Also match by exact name.
        return triggers.Any(t =>
            t.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(t.SubCategory) && t.SubCategory.Equals(info.SubCategory, StringComparison.OrdinalIgnoreCase) && t.Category.Equals(info.Category, StringComparison.OrdinalIgnoreCase)));
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _wholeFoodRegexCache = new();
    static bool WholeFoodRegexMatch(string text, string pattern)
    {
        var regex = _wholeFoodRegexCache.GetOrAdd(pattern, p =>
            new Regex(@"\b" + Regex.Escape(p) + @"\b", RegexOptions.Compiled | RegexOptions.IgnoreCase));
        return regex.IsMatch(text);
    }

    public FodmapAssessmentDto Assess(FoodProductDto product)
    {
        var triggers = new List<FodmapTriggerDto>();

        // 1. Scan ingredients text against FODMAP trigger database
        if (!string.IsNullOrWhiteSpace(product.Ingredients))
        {
            var lower = product.Ingredients.ToLowerInvariant();
            var combined = lower + " " + (product.Name ?? "").ToLowerInvariant();
            var isLactoseFree = MatchUtils.IsLactoseFree(combined);
            var isDairyFree = MatchUtils.IsDairyFree(combined);
            var isGlutenFree = MatchUtils.IsGlutenFree(combined);

            foreach (var (pattern, regex, info) in IngredientTriggers)
            {
                bool matched = regex != null ? regex.IsMatch(lower) : lower.Contains(pattern);
                if (matched && !HasTrigger(triggers, info))
                {
                    if ((isLactoseFree || isDairyFree) && info.SubCategory == "Lactose")
                        continue;
                    if (isGlutenFree && (info.SubCategory == "Fructan") &&
                        (pattern == "wheat" || pattern == "wheat flour" || pattern == "whole wheat" ||
                         pattern == "wheat starch" || pattern == "barley" || pattern == "rye"))
                        continue;
                    triggers.Add(info);
                }
            }
        }

        // 2. Check additive tags for FODMAP-relevant additives (sugar alcohols = polyols)
        if (product.AdditivesTags is { Count: > 0 })
        {
            foreach (var tag in product.AdditivesTags)
            {
                var norm = tag.Replace("en:", "", StringComparison.OrdinalIgnoreCase).Trim().ToUpperInvariant();
                if (FodmapAdditives.TryGetValue(norm, out var info) && !HasTrigger(triggers, info))
                    triggers.Add(info);
            }
        }

        // 3. Check linked additives by name
        if (product.Additives is { Count: > 0 })
        {
            foreach (var add in product.Additives)
            {
                var lowerName = add.Name.ToLowerInvariant();
                foreach (var (pattern, info) in AdditiveNameTriggers)
                {
                    if (lowerName.Contains(pattern) && !HasTrigger(triggers, info))
                    {
                        triggers.Add(info);
                        break;
                    }
                }
            }
        }

        // 4. Check for high sugar (potential excess fructose)
        if (product.Sugar100g > 30m)
        {
            var lower = (product.Ingredients ?? "").ToLowerInvariant();
            if (lower.Contains("fructose") || lower.Contains("fruit juice") || lower.Contains("apple juice") || lower.Contains("pear juice"))
                triggers.Add(new FodmapTriggerDto
                {
                    Name = "Excess Fructose (from fruit juice/fructose)",
                    Category = "Monosaccharide",
                    SubCategory = "Excess Fructose",
                    Severity = "High",
                    Explanation = "High sugar content combined with fructose sources may overwhelm absorption capacity, triggering bloating and diarrhea.",
                });
        }

        // 5. Score by product name (whole food matching) — skip generic names when real ingredients exist
        var productName = product.Name.ToLowerInvariant();
        var hasRealIngredients = !string.IsNullOrWhiteSpace(product.Ingredients) && product.Ingredients.Contains(',');
        foreach (var (pattern, info) in WholeFood_Triggers)
        {
            if (WholeFoodRegexMatch(productName, pattern) && !HasTrigger(triggers, info))
            {
                if (hasRealIngredients && GenericWholeFoodPatterns.Any(g => pattern.Contains(g, StringComparison.OrdinalIgnoreCase)))
                    continue;
                triggers.Add(info);
            }
        }

        // 6. Lactase enzyme mitigation — if "lactase" in ingredients, downgrade lactose triggers
        if (!string.IsNullOrWhiteSpace(product.Ingredients) &&
            product.Ingredients.Contains("lactase", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < triggers.Count; i++)
            {
                if (triggers[i].SubCategory == "Lactose" && triggers[i].Severity != "Low")
                {
                    triggers[i] = new FodmapTriggerDto
                    {
                        Name = triggers[i].Name,
                        Category = triggers[i].Category,
                        SubCategory = triggers[i].SubCategory,
                        Severity = "Low",
                        Explanation = triggers[i].Explanation + " (Contains lactase enzyme — lactose impact likely reduced.)",
                    };
                }
            }
        }

        var score = CalculateFodmapScore(triggers);
        var rating = score switch
        {
            >= 75 => "Low FODMAP",
            >= 60 => "Moderate FODMAP",
            >= 30 => "High FODMAP",
            _ => "Very High FODMAP",
        };

        var categories = triggers.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();

        var confidence = ComputeFodmapConfidence(product, triggers);

        return new FodmapAssessmentDto
        {
            FodmapScore = score,
            FodmapRating = rating,
            TriggerCount = triggers.Count,
            HighCount = triggers.Count(t => t.Severity == "High"),
            ModerateCount = triggers.Count(t => t.Severity == "Moderate"),
            LowCount = triggers.Count(t => t.Severity == "Low"),
            Categories = categories,
            Triggers = triggers.OrderByDescending(t => SeverityWeight(t.Severity)).ToList(),
            Summary = GenerateSummary(triggers, rating, categories),
            Confidence = confidence,
        };
    }

    public FodmapAssessmentDto AssessText(string foodDescription)
    {
        var lower = foodDescription.ToLowerInvariant();
        var triggers = new List<FodmapTriggerDto>();

        foreach (var (pattern, regex, info) in IngredientTriggers)
        {
            bool matched = regex != null ? regex.IsMatch(lower) : lower.Contains(pattern);
            if (matched && !HasTrigger(triggers, info))
                triggers.Add(info);
        }

        foreach (var (pattern, info) in WholeFood_Triggers)
        {
            if (lower.Contains(pattern) && !HasTrigger(triggers, info))
                triggers.Add(info);
        }

        var score = CalculateFodmapScore(triggers);
        var rating = score switch
        {
            >= 75 => "Low FODMAP",
            >= 60 => "Moderate FODMAP",
            >= 30 => "High FODMAP",
            _ => "Very High FODMAP",
        };

        var categories = triggers.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();

        var confidence = "Medium"; // Text-only assessment always medium confidence

        return new FodmapAssessmentDto
        {
            FodmapScore = score,
            FodmapRating = rating,
            TriggerCount = triggers.Count,
            HighCount = triggers.Count(t => t.Severity == "High"),
            ModerateCount = triggers.Count(t => t.Severity == "Moderate"),
            LowCount = triggers.Count(t => t.Severity == "Low"),
            Categories = categories,
            Triggers = triggers.OrderByDescending(t => SeverityWeight(t.Severity)).ToList(),
            Summary = GenerateSummary(triggers, rating, categories),
            Confidence = confidence,
        };
    }

    static int CalculateFodmapScore(List<FodmapTriggerDto> triggers)
    {
        if (triggers.Count == 0) return 100;

        var multiplier = 1.0;
        foreach (var t in triggers)
        {
            multiplier *= t.Severity switch
            {
                "High" => 0.40,
                "Moderate" => 0.85,
                "Low" => 0.95,
                _ => 1.0,
            };
        }

        // Category stacking penalty — if 3+ distinct FODMAP categories, apply extra penalty
        var distinctCategories = triggers.Select(t => t.SubCategory?.Split('+', ' ').FirstOrDefault() ?? t.Category)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctCategories >= 3)
            multiplier *= Math.Pow(0.92, distinctCategories - 2);

        return Math.Clamp((int)Math.Round(100 * multiplier), 0, 100);
    }

    static int SeverityWeight(string s) => s switch
    {
        "High" => 3,
        "Moderate" => 2,
        "Low" => 1,
        _ => 0,
    };

    static string GenerateSummary(List<FodmapTriggerDto> triggers, string rating, List<string> categories)
    {
        if (triggers.Count == 0)
            return "No known FODMAP triggers detected. This product appears suitable for a low-FODMAP diet.";

        var highCount = triggers.Count(t => t.Severity == "High");

        if (highCount > 0)
        {
            var names = string.Join(", ", triggers.Where(t => t.Severity == "High").Select(t => t.Name).Take(3));
            return $"Contains {highCount} high-FODMAP trigger(s): {names}. FODMAP categories affected: {string.Join(", ", categories)}. Often reduced during a FODMAP elimination phase.";
        }

        return $"Contains {triggers.Count} FODMAP concern(s) in {string.Join(", ", categories)}. May be better tolerated in smaller portions — personal experience can vary.";
    }

    static string ComputeFodmapConfidence(FoodProductDto product, List<FodmapTriggerDto> triggers)
    {
        var hasIngredients = !string.IsNullOrWhiteSpace(product.Ingredients);
        var hasDetailedIngredients = hasIngredients && product.Ingredients!.Contains(',') && product.Ingredients.Length > 50;

        if (!hasIngredients)
        {
            // Trusted whole foods (USDA/AUSNUT) — the name IS the ingredient; no hidden ambiguity.
            bool isTrustedWholeFood = product.DataSource is "USDA" or "AUSNUT" ||
                product.FoodKind == GutAI.Domain.Enums.FoodKind.WholeFood;
            return isTrustedWholeFood ? "Medium" : "Low";
        }
        if (!hasDetailedIngredients)
            return "Medium";
        return "High";
    }


    // ─── FODMAP Trigger Database ────────────────────────────────────
    // Data sourced from FodmapData.cs — single source of truth.

    static readonly (string pattern, Regex? regex, FodmapTriggerDto info)[] IngredientTriggers =
        FodmapData.IngredientTriggers.Select(e => (e.Pattern, e.Regex, e.Trigger)).ToArray();

    static readonly (string pattern, FodmapTriggerDto info)[] WholeFood_Triggers =
        FodmapData.WholeFoodTriggers.Select(e => (e.Pattern, e.Trigger)).ToArray();

    static readonly Dictionary<string, FodmapTriggerDto> FodmapAdditives =
        new(FodmapData.Additives, StringComparer.OrdinalIgnoreCase);

    static readonly (string pattern, FodmapTriggerDto info)[] AdditiveNameTriggers =
        FodmapData.AdditiveNameTriggers.Select(e => (e.Pattern, e.Trigger)).ToArray();

    static readonly HashSet<string> GenericWholeFoodPatterns =
        new(FodmapData.GenericWholeFoodPatterns, StringComparer.OrdinalIgnoreCase);
}
