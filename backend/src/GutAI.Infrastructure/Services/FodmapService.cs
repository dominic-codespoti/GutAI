using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public class FodmapService : IFodmapService
{
    static bool HasTrigger(List<FodmapTriggerDto> triggers, FodmapTriggerDto info) =>
        triggers.Any(t =>
            t.Category.Equals(info.Category, StringComparison.OrdinalIgnoreCase) &&
            t.SubCategory.Equals(info.SubCategory, StringComparison.OrdinalIgnoreCase));

    static readonly ConcurrentDictionary<string, Regex> _wholeFoodRegexCache = new();
    static bool WholeFoodRegexMatch(string text, string pattern)
    {
        if (!_wholeFoodRegexCache.TryGetValue(pattern, out var regex))
        {
            regex = new Regex($@"\b{Regex.Escape(pattern)}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _wholeFoodRegexCache[pattern] = regex;
        }
        return regex.IsMatch(text);
    }

    static readonly Regex GarlicOilPattern = new(
        @"garlic\s+oil|garlic[\s-]*infused\s+(?:\w+\s+)?oil", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex GarlicWordPattern = new(@"\bgarlic\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex FirmTofuPattern = new(@"\b(?:firm|extra[\s-]firm)\s+tofu\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>Leek green tops/leaves are low-FODMAP per Monash lab testing — the fructans
    /// concentrate in the white bulb, not the green portion.</summary>
    static readonly Regex LeekGreenTopsPattern = new(
        @"\b(?:green\s+(?:tops?\s+of\s+)?leek|leek\s+(?:greens?|tops?|leaves))\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Single evidence-gathering pipeline shared by <see cref="Assess"/> and <see cref="AssessText"/>
    /// — previously each re-implemented ingredient/whole-food scanning with different matching
    /// rules and different mitigation coverage.
    /// </summary>
    static List<FodmapTriggerDto> ScanTriggers(
        string? ingredientsOrDescription, string wholeFoodScanText, decimal? sugar100g,
        List<string>? additivesTags, List<FoodAdditiveDto>? additives)
    {
        var triggers = new List<FodmapTriggerDto>();
        var lower = (ingredientsOrDescription ?? "").ToLowerInvariant();
        var combined = (lower + " " + wholeFoodScanText).Trim();
        var isLactoseFree = MatchUtils.IsLactoseFree(combined);
        var isDairyFree = MatchUtils.IsDairyFree(combined);
        var isGlutenFree = MatchUtils.IsGlutenFree(combined);

        // 1. Scan ingredients/description text against the FODMAP trigger database
        if (!string.IsNullOrWhiteSpace(lower))
        {
            foreach (var (pattern, regex, info) in IngredientTriggers)
            {
                bool matched = regex != null ? regex.IsMatch(lower) : lower.Contains(pattern);
                if (matched && !HasTrigger(triggers, info))
                {
                    if ((isLactoseFree || isDairyFree) && info.SubCategory == "Lactose")
                        continue;
                    if (isGlutenFree && info.SubCategory == "Fructan" &&
                        (pattern == "wheat" || pattern == "wheat flour" || pattern == "whole wheat" ||
                         pattern == "wheat starch" || pattern == "barley" || pattern == "rye"))
                        continue;
                    triggers.Add(info);
                }
            }
        }

        // 2. Check additive tags for FODMAP-relevant additives (sugar alcohols = polyols)
        if (additivesTags is { Count: > 0 })
        {
            foreach (var tag in additivesTags)
            {
                var norm = tag.Replace("en:", "", StringComparison.OrdinalIgnoreCase).Trim().ToUpperInvariant();
                if (FodmapAdditives.TryGetValue(norm, out var info) && !HasTrigger(triggers, info))
                    triggers.Add(info);
            }
        }

        // 3. Check linked additives by name
        if (additives is { Count: > 0 })
        {
            foreach (var add in additives)
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
        if (sugar100g > 30m &&
            (lower.Contains("fructose") || lower.Contains("fruit juice") || lower.Contains("apple juice") || lower.Contains("pear juice")))
        {
            triggers.Add(new FodmapTriggerDto
            {
                Name = "Excess Fructose (from fruit juice/fructose)",
                Category = "Monosaccharide",
                SubCategory = "Excess Fructose",
                Severity = "High",
                Explanation = "High sugar content combined with fructose sources may overwhelm absorption capacity, triggering bloating and diarrhea.",
            });
        }

        // 5. Whole-food name matching — skip generic names when real ingredients exist
        var hasRealIngredients = !string.IsNullOrWhiteSpace(ingredientsOrDescription) && ingredientsOrDescription.Contains(',');
        foreach (var (pattern, info) in WholeFood_Triggers)
        {
            if (WholeFoodRegexMatch(wholeFoodScanText, pattern) && !HasTrigger(triggers, info))
            {
                if (hasRealIngredients && GenericWholeFoodPatterns.Any(g => pattern.Contains(g, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if ((isLactoseFree || isDairyFree) && info.SubCategory == "Lactose")
                    continue;
                if (isGlutenFree && info.SubCategory == "Fructan" &&
                    (pattern == "wheat" || pattern == "wheat flour" || pattern == "whole wheat" ||
                     pattern == "wheat starch" || pattern == "barley" || pattern == "rye"))
                    continue;
                triggers.Add(info);
            }
        }

        // 6. Lactase enzyme mitigation
        if (!string.IsNullOrWhiteSpace(ingredientsOrDescription) &&
            ingredientsOrDescription.Contains("lactase", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < triggers.Count; i++)
            {
                if (triggers[i].SubCategory == "Lactose" && triggers[i].Severity != "Low")
                    triggers[i] = triggers[i] with
                    {
                        Severity = "Low",
                        Explanation = triggers[i].Explanation + " (Contains lactase enzyme — lactose impact likely reduced.)",
                    };
            }
        }

        ApplyProcessingMitigations(triggers, combined);

        return triggers;
    }

    static void ApplyProcessingMitigations(List<FodmapTriggerDto> triggers, string lower)
    {
        // Garlic-infused/garlic oil: fructans are not oil-soluble.
        if (GarlicOilPattern.IsMatch(lower))
        {
            var withoutGarlicOil = GarlicOilPattern.Replace(lower, "");
            if (!GarlicWordPattern.IsMatch(withoutGarlicOil))
                triggers.RemoveAll(t => t.Name == "Garlic (Fructan)");
        }

        // Firm/extra-firm tofu: GOS leaches out during pressing.
        if (FirmTofuPattern.IsMatch(lower))
            triggers.RemoveAll(t => t.Name == "Soybean (GOS)");

        // Leek green tops/leaves are low-FODMAP — fructans concentrate in the white bulb.
        if (LeekGreenTopsPattern.IsMatch(lower))
            triggers.RemoveAll(t => t.Name.Contains("Leek", StringComparison.OrdinalIgnoreCase));

        // Canned + rinsed legumes: downgrade rather than remove.
        if (lower.Contains("canned"))
        {
            foreach (var legume in CannedLegumeNames)
            {
                if (!lower.Contains(legume)) continue;
                for (var i = 0; i < triggers.Count; i++)
                {
                    if (triggers[i].Name.Contains(legume, StringComparison.OrdinalIgnoreCase) && triggers[i].Severity == "High")
                        triggers[i] = triggers[i] with
                        {
                            Severity = "Moderate",
                            Explanation = triggers[i].Explanation + " (Canned — GOS content is somewhat reduced versus dried/boiled.)",
                        };
                }
            }
        }
    }

    static readonly string[] CannedLegumeNames = ["chickpea", "garbanzo", "lentil"];

    static (FodmapAssessmentStatus Status, string Confidence, List<string> MissingEvidence) Resolve(
        int triggerCount, bool hasIngredients, bool hasDetailedIngredients, bool hasVerifiedIdentity,
        bool isTextCall, bool hasNonTrivialEvidence)
    {
        var confidence = isTextCall ? "Medium" : hasDetailedIngredients ? "Medium" : hasVerifiedIdentity ? "Medium" : "Low";

        if (triggerCount > 0)
            return (FodmapAssessmentStatus.PotentialTriggersDetected, confidence, []);

        if (hasIngredients || hasVerifiedIdentity || hasNonTrivialEvidence)
            return (FodmapAssessmentStatus.NoKnownTriggersDetected, confidence, []);

        var missing = new List<string>();
        if (isTextCall) missing.Add("a non-empty food description");
        else
        {
            missing.Add("an ingredient list");
            missing.Add("a verified catalog identity");
        }
        return (FodmapAssessmentStatus.InsufficientInformation, "Low", missing);
    }

    public FodmapAssessmentDto Assess(FoodProductDto product)
    {
        var triggers = ScanTriggers(product.Ingredients, product.Name.ToLowerInvariant(), product.Sugar100g,
            product.AdditivesTags, product.Additives);

        var hasIngredients = !string.IsNullOrWhiteSpace(product.Ingredients);
        var hasDetailedIngredients = hasIngredients && product.Ingredients!.Contains(',') && product.Ingredients.Length > 50;
        var hasVerifiedIdentity = (product.DataSource is "USDA" or "AUSNUT" && product.FoodKind != GutAI.Domain.Enums.FoodKind.Branded)
            || product.FoodKind == GutAI.Domain.Enums.FoodKind.WholeFood;

        var (status, confidence, missingEvidence) = Resolve(triggers.Count, hasIngredients, hasDetailedIngredients,
            hasVerifiedIdentity, isTextCall: false, hasNonTrivialEvidence: false);
        return BuildDto(triggers, status, confidence, missingEvidence);
    }

    public FodmapAssessmentDto AssessText(string foodDescription)
    {
        var triggers = ScanTriggers(foodDescription, foodDescription.ToLowerInvariant(), null, null, null);
        var hasNonTrivialEvidence = !string.IsNullOrWhiteSpace(foodDescription);

        var (status, confidence, missingEvidence) = Resolve(triggers.Count, hasIngredients: false,
            hasDetailedIngredients: false, hasVerifiedIdentity: false, isTextCall: true, hasNonTrivialEvidence);
        return BuildDto(triggers, status, confidence, missingEvidence);
    }

    static FodmapAssessmentDto BuildDto(List<FodmapTriggerDto> triggers, FodmapAssessmentStatus status,
        string confidence, List<string> missingEvidence)
    {
        var categories = triggers.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        return new FodmapAssessmentDto
        {
            Status = status.ToString(),
            IngredientScreeningScore = CalculateIngredientScreeningScore(triggers),
            Confidence = confidence,
            TriggerCount = triggers.Count,
            HighCount = triggers.Count(t => t.Severity == "High"),
            ModerateCount = triggers.Count(t => t.Severity == "Moderate"),
            LowCount = triggers.Count(t => t.Severity == "Low"),
            Categories = categories,
            Triggers = triggers.OrderByDescending(t => SeverityWeight(t.Severity)).ToList(),
            MissingEvidence = missingEvidence,
            Summary = GenerateSummary(status, triggers, categories, missingEvidence),
        };
    }

    static int CalculateIngredientScreeningScore(List<FodmapTriggerDto> triggers)
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

    static string GenerateSummary(FodmapAssessmentStatus status, List<FodmapTriggerDto> triggers,
        List<string> categories, List<string> missingEvidence)
    {
        if (status == FodmapAssessmentStatus.InsufficientInformation)
            return $"Not enough information to screen this item for FODMAP triggers (missing: {string.Join(", ", missingEvidence)}). " +
                "This does not mean it is Low FODMAP — it means the screen could not run.";

        if (triggers.Count == 0)
            return "No known FODMAP trigger names were detected in the available name/ingredient information. This is an ingredient-screening result, not a serving-size FODMAP classification; tolerance and FODMAP load depend on portion.";

        var highCount = triggers.Count(t => t.Severity == "High");

        if (highCount > 0)
        {
            var names = string.Join(", ", triggers.Where(t => t.Severity == "High").Select(t => t.Name).Take(3));
            return $"Detected {highCount} higher-concern FODMAP source(s): {names}. Categories: {string.Join(", ", categories)}. Actual FODMAP load is portion-dependent and individual tolerance varies.";
        }

        return $"Detected {triggers.Count} potential FODMAP source(s) in {string.Join(", ", categories)}. This screen cannot classify a serving without measured FODMAP quantities.";
    }

    // ─── FODMAP Trigger Database ────────────────────────────────────

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
