using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public class FodmapService : IFodmapService
{
    // Deduplicate on canonical trigger Name: synonym patterns intentionally share a Name
    // ("wheat flour"/"whole wheat"/"wheat" → "Wheat (Fructan)"), so they still collapse,
    // while DISTINCT foods in the same class (onion vs garlic vs wheat) each remain visible
    // and counted — consistent with GutRisk's per-source stacking philosophy. The previous
    // Category+SubCategory key silently hid every fructan/lactose/polyol source after the
    // first, understating TriggerCount, HighCount and the screening score.
    static bool HasTrigger(List<FodmapTriggerDto> triggers, FodmapTriggerDto info) =>
        triggers.Any(t => t.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase));

    static readonly ConcurrentDictionary<string, Regex> _wholeFoodRegexCache = new();
    static bool WholeFoodRegexMatch(string text, string pattern)
    {
        // Plural-tolerant like IngredientPatternMatch: singular-authored entries
        // ("pistachio") must catch plural product names ("Roasted Pistachios").
        // Patterns already ending in 's' (blackberries, snow peas) stay exact;
        // leading \b keeps false positives like pita/pepitas dead.
        var regex = _wholeFoodRegexCache.GetOrAdd(pattern, static p =>
        {
            var sfx = p.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? "" : "s?";
            return new Regex($@"\b{Regex.Escape(p)}{sfx}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        });
        return regex.IsMatch(text);
    }

    static readonly ConcurrentDictionary<string, Regex> _ingredientTokenRegexCache = new();

    /// <summary>Ingredient-text matching. Patterns without an explicit regex keep substring
    /// semantics ONLY when multi-word (phrases are self-delimiting); single tokens get a
    /// plural-tolerant word-boundary regex so "breaded"/"spelted"/"ciders" cannot
    /// false-positive on "bread"/"spelt"/"cider".</summary>
    static bool IngredientPatternMatch(string lower, string pattern, Regex? regex)
    {
        if (regex != null) return regex.IsMatch(lower);
        if (pattern.Contains(' ')) return lower.Contains(pattern);
        var re = _ingredientTokenRegexCache.GetOrAdd(pattern, static p =>
            new Regex($@"\b{Regex.Escape(p)}s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase));
        return re.IsMatch(lower);
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
                bool matched = IngredientPatternMatch(lower, pattern, regex);
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

        // 4. Check for high sugar (potential excess fructose). Skipped when any excess-fructose
        //    trigger was already found (honey, apple juice, …) — the heuristic adds no
        //    information then and previously double-penalized the score for one substance.
        if (sugar100g > 30m &&
            (lower.Contains("fructose") || lower.Contains("fruit juice") || lower.Contains("apple juice") || lower.Contains("pear juice")) &&
            !triggers.Any(t => t.SubCategory == "Excess Fructose"))
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

        // Firm/extra-firm tofu: GOS leaches out during pressing. Only suppresses the plain
        // whole-soybean trigger — an independent soy ingredient (flour/protein/isolate/oil)
        // keeps its own evidence.
        if (FirmTofuPattern.IsMatch(lower))
        {
            var withoutTofu = FirmTofuPattern.Replace(lower, "");
            if (!IndependentSoyFormPattern.IsMatch(withoutTofu))
                triggers.RemoveAll(t => t.Name == "Soybean (GOS)");
        }

        // Leek green tops/leaves are low-FODMAP — fructans concentrate in the white bulb.
        if (LeekGreenTopsPattern.IsMatch(lower))
            triggers.RemoveAll(t => t.Name.Contains("Leek", StringComparison.OrdinalIgnoreCase));

        // Canned/tinned + rinsed legumes: downgrade rather than remove.
        if (CannedLegumePattern.IsMatch(lower))
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


    static readonly Regex CannedLegumePattern = new(@"\b(?:canned|tinned)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Soy forms whose GOS/fructan content is independent of tofu processing.</summary>
    static readonly Regex IndependentSoyFormPattern = new(
        @"\bsoy(?:bean)?\s+(?:flour|protein|isolate|oil)|\bsoya\b|\bsoy\s+lecithin\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly string[] CannedLegumeNames =
    [
        "chickpea", "garbanzo", "lentil", "kidney bean", "black bean", "navy bean",
        "pinto bean", "lima bean", "cannellini", "fava bean", "broad bean", "baked bean",
    ];

    static (FodmapAssessmentStatus Status, string Confidence, List<string> MissingEvidence) Resolve(
        int triggerCount, bool hasIngredients, bool hasDetailedIngredients, bool hasVerifiedIdentity,
        bool isTextCall, bool hasNonTrivialEvidence, int textWordCount = 0)
    {
        // Free-text confidence is graded: a one-word description ("pizza") screens against
        // far less evidence than a composed dish description, so it must not claim Medium.
        var confidence = isTextCall
            ? textWordCount >= 3 ? "Medium" : "Low"
            : hasDetailedIngredients || hasVerifiedIdentity ? "Medium" : "Low";

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
        var textWordCount = foodDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        var (status, confidence, missingEvidence) = Resolve(triggers.Count, hasIngredients: false,
            hasDetailedIngredients: false, hasVerifiedIdentity: false, isTextCall: true, hasNonTrivialEvidence,
            textWordCount);
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

        // Breadth = distinct FODMAP families (Monash classes), not raw subcategory
        // strings. Dual-class entries ("Excess Fructose + Sorbitol") previously counted
        // once as "Excess", under-stating cumulative load for mixed-class foods.
        var distinctCategories = triggers
            .SelectMany(t => (t.SubCategory ?? t.Category).Split('+'))
            .Select(ChemistryFamily)
            .Where(f => f.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .Count;
        if (distinctCategories >= 3)
            multiplier *= Math.Pow(0.92, distinctCategories - 2);

        return Math.Clamp((int)Math.Round(100 * multiplier), 0, 100);
    }

    static readonly string[] PolyolMarkers =
        ["Sorbitol", "Mannitol", "Maltitol", "Xylitol", "Isomalt", "Lactitol", "Erythritol", "Polyol"];

    /// <summary>Buckets a sub-category fragment into one of the five Monash FODMAP
    /// families — Sorbitol and Mannitol are different molecules but the same burden
    /// class, and the stacking bonus models classes, not molecules.</summary>
    static string ChemistryFamily(string fragment)
    {
        var f = fragment.Trim();
        if (f.Length == 0) return "";
        if (f.Contains("Fructan", StringComparison.OrdinalIgnoreCase)) return "Fructan";
        if (f.Contains("GOS", StringComparison.OrdinalIgnoreCase)) return "GOS";
        if (f.Contains("Lactose", StringComparison.OrdinalIgnoreCase)) return "Lactose";
        if (f.Contains("Fructose", StringComparison.OrdinalIgnoreCase)) return "Excess Fructose";
        if (PolyolMarkers.Any(m => f.Contains(m, StringComparison.OrdinalIgnoreCase))) return "Polyol";
        return f;
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
