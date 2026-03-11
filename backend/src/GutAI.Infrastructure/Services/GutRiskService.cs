using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using static GutAI.Infrastructure.Services.GutRiskData;

namespace GutAI.Infrastructure.Services;

public class GutRiskService : IGutRiskService
{
    static readonly Regex ENumberRegex =
        new(@"\b(?:INS\s*|E\s*-?\s*)(\d{3,4}[A-Za-z]?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    static string NormalizeTag(string raw)
    {
        var cleaned = raw.Replace("en:", "", StringComparison.OrdinalIgnoreCase).Trim();
        var m = ENumberRegex.Match(cleaned);
        if (m.Success)
            return "E" + m.Groups[1].Value.ToUpperInvariant();
        return NormalizeKey(cleaned);
    }

    static string NormalizeKey(string s)
        => WhitespaceRegex.Replace((s ?? "").Trim().ToUpperInvariant(), " ");

    static string FlagKey(GutRiskFlagDto flag)
        => NormalizeKey(!string.IsNullOrWhiteSpace(flag.Code) ? flag.Code : flag.Name);

    static bool HasFlag(List<GutRiskFlagDto> flags, string key)
        => flags.Any(f => FlagKey(f) == NormalizeKey(key));

    static (TriggerType trigger, FodmapClass fodmap, DoseSensitivity dose) Classify(string category, string riskLevel)
    {
        var (trigger, fodmap) = CategoryMap.GetValueOrDefault(category, (TriggerType.Additive, FodmapClass.None));

        var dose = category switch
        {
            "Sugar Alcohol" => riskLevel == "Low" ? DoseSensitivity.Low : DoseSensitivity.High,
            "GOS Source" => riskLevel == "High" ? DoseSensitivity.High : DoseSensitivity.Medium,
            "Polyol Source" or "High-FODMAP Ingredient" or "Prebiotic Fiber (FODMAP)" or "Sweetener"
                => DoseSensitivity.Medium,
            "Dairy/Lactose" or "Fructose Source" => DoseSensitivity.High,
            _ => DoseSensitivity.Low,
        };

        return (trigger, fodmap, dose);
    }

    static GutRiskFlagDto MakeFlag(string source, string code, string name, string category, string riskLevel, string explanation)
    {
        var (trigger, fodmap, dose) = Classify(category, riskLevel);
        return new GutRiskFlagDto
        {
            Source = source,
            Code = code,
            Name = name,
            Category = category,
            RiskLevel = riskLevel,
            Explanation = explanation,
            TriggerType = trigger.ToString(),
            FodmapClass = fodmap == FodmapClass.None ? "" : fodmap.ToString(),
            DoseSensitivity = dose.ToString(),
        };
    }

    public GutRiskAssessmentDto Assess(FoodProductDto product)
    {
        var flags = new List<GutRiskFlagDto>();

        // 1. Score additives from OpenFoodFacts tags
        if (product.AdditivesTags is { Count: > 0 })
        {
            foreach (var tag in product.AdditivesTags)
            {
                var normalized = NormalizeTag(tag);
                if (GutHarmfulAdditives.TryGetValue(normalized, out var info) && !HasFlag(flags, info.ENumber))
                    flags.Add(MakeFlag("Additive", info.ENumber, info.Name, info.Category, info.RiskLevel, info.Explanation));
            }
        }

        // 2. Score from linked FoodAdditiveDto objects (seeded DB additives)
        if (product.Additives is { Count: > 0 })
        {
            foreach (var add in product.Additives)
            {
                var eNum = NormalizeTag(add.ENumber ?? "");
                if (!string.IsNullOrEmpty(eNum) && GutHarmfulAdditives.TryGetValue(eNum, out var knownInfo))
                {
                    if (HasFlag(flags, knownInfo.ENumber))
                        continue; // already flagged from tags
                    flags.Add(MakeFlag("Additive", knownInfo.ENumber, knownInfo.Name, knownInfo.Category, knownInfo.RiskLevel, knownInfo.Explanation));
                    continue;
                }

                var lowerName = add.Name.ToLowerInvariant();
                if (SugarAlcoholNames.Any(sa => lowerName.Contains(sa)))
                {
                    var flag = MakeFlag("Additive", add.ENumber ?? "", add.Name, "Sugar Alcohol", "High",
                        "Sugar alcohols are high-FODMAP and can cause bloating, gas, and diarrhea in sensitive individuals.");
                    if (!HasFlag(flags, !string.IsNullOrWhiteSpace(flag.Code) ? flag.Code : flag.Name))
                        flags.Add(flag);
                }
                else if (ArtificialSweetenerNames.Any(sw => lowerName.Contains(sw)))
                {
                    var flag = MakeFlag("Additive", add.ENumber ?? "", add.Name, "Artificial Sweetener", "Medium",
                        "May disrupt gut microbiome composition and glucose response.");
                    if (!HasFlag(flags, !string.IsNullOrWhiteSpace(flag.Code) ? flag.Code : flag.Name))
                        flags.Add(flag);
                }
            }
        }

        // 3. Scan ingredients text for gut-concerning items not captured as additive tags
        if (!string.IsNullOrWhiteSpace(product.Ingredients))
        {
            var lowerIngredients = product.Ingredients.ToLowerInvariant();
            var ingredientsCombined = lowerIngredients + " " + (product.Name ?? "").ToLowerInvariant();
            var isLactoseFreeIngr = MatchUtils.IsLactoseFree(ingredientsCombined);
            var isDairyFreeIngr = MatchUtils.IsDairyFree(ingredientsCombined);
            foreach (var (pattern, regex, info) in IngredientPatterns)
            {
                bool matched = regex != null
                    ? regex.IsMatch(lowerIngredients)
                    : lowerIngredients.Contains(pattern);
                var key = !string.IsNullOrEmpty(info.ENumber) ? info.ENumber : info.Name;
                if (matched && !HasFlag(flags, key))
                {
                    if ((isLactoseFreeIngr || isDairyFreeIngr) &&
                        (info.Category == "Dairy/Lactose"))
                        continue;
                    flags.Add(MakeFlag("Ingredient", info.ENumber, info.Name, info.Category, info.RiskLevel, info.Explanation));
                }
            }
        }

        // 3b. Whole food name matching — for products with no ingredients list (USDA whole foods)
        if (string.IsNullOrWhiteSpace(product.Ingredients) && !string.IsNullOrWhiteSpace(product.Name))
        {
            var lowerName = product.Name.ToLowerInvariant();
            var isLactoseFree = MatchUtils.IsLactoseFree(lowerName);
            var isDairyFree = MatchUtils.IsDairyFree(lowerName);
            foreach (var (pattern, regex, info) in WholeFoodRiskPatterns)
            {
                bool matched = regex != null ? regex.IsMatch(lowerName) : lowerName.Contains(pattern);
                if (matched && !HasFlag(flags, info.Name))
                {
                    if ((isLactoseFree || isDairyFree) && info.Category == "Dairy/Lactose")
                        continue;
                    flags.Add(MakeFlag("WholeFoodName", info.ENumber, info.Name, info.Category, info.RiskLevel, info.Explanation));
                }
            }
        }

        // 4. Flag high NOVA group
        if (product.NovaGroup >= 4)
            flags.Add(MakeFlag("Processing", $"NOVA-{product.NovaGroup}", "Ultra-Processed Food", "Processing Level", "Medium",
                "Ultra-processed foods (NOVA 4) have been studied for potential links to changes in gut microbiome diversity and digestive comfort."));

        // 5. Flag high sodium (>600mg per 100g)
        if (product.Sodium100g > 0.6m)
            flags.Add(MakeFlag("Nutrient", "HIGH-NA", "High Sodium", "Nutrient Concern", "Low",
                $"Contains {product.Sodium100g * 1000:0}mg sodium per 100g. Some research has explored links between sodium intake and gut function."));

        // 6. Flag very high sugar (>25g per 100g)
        if (product.Sugar100g > 25m)
            flags.Add(MakeFlag("Nutrient", "HIGH-SUGAR", "High Sugar Content", "Nutrient Concern", "Low",
                $"Contains {product.Sugar100g:0}g sugar per 100g. High sugar intake has been associated with changes in gut bacteria composition."));

        // 7. Stacking penalties
        var emulsifierCount = flags.Count(f => f.Category is "Emulsifier" or "Emulsifier/Thickener");
        if (emulsifierCount >= 2 && !HasFlag(flags, "STACK-EMUL"))
            flags.Add(MakeFlag("Combination", "STACK-EMUL", "Multiple Emulsifiers", "Stacking Penalty", "Low",
                $"Contains {emulsifierCount} emulsifiers. Combined exposure may have greater impact on gut mucus layer than individual additives."));

        var thickenerCount = flags.Count(f => f.Category is "Thickener" or "Emulsifier/Thickener");
        if (thickenerCount >= 2 && !HasFlag(flags, "STACK-HYDROCOL"))
            flags.Add(MakeFlag("Combination", "STACK-HYDROCOL", "Multiple Hydrocolloids", "Stacking Penalty", "Low",
                $"Contains {thickenerCount} thickeners/hydrocolloids. Combined fermentable load may increase gas and bloating."));

        var polyolCount = flags.Count(f => f.Category == "Sugar Alcohol");
        if (polyolCount >= 2 && !HasFlag(flags, "STACK-POLYOL"))
            flags.Add(MakeFlag("Combination", "STACK-POLYOL", "Multiple Sugar Alcohols", "Stacking Penalty", "Medium",
                $"Contains {polyolCount} sugar alcohols. Combined polyol load significantly increases risk of osmotic diarrhea and bloating."));

        var hasNova4 = flags.Any(f => (f.Code ?? "").StartsWith("NOVA", StringComparison.OrdinalIgnoreCase));
        var hasEmulsifier = emulsifierCount > 0;
        if (hasNova4 && hasEmulsifier && !HasFlag(flags, "STACK-NOVA-EMUL"))
            flags.Add(MakeFlag("Combination", "STACK-NOVA-EMUL", "Ultra-Processed + Emulsifier", "Stacking Penalty", "Low",
                "Ultra-processed food with emulsifiers — combination associated with greater microbiome disruption than either factor alone."));

        // 8. FODMAP class stacking penalties
        var fructanCount = flags.Count(f => f.FodmapClass == "Fructans");
        if (fructanCount >= 2 && !HasFlag(flags, "STACK-FRUCTAN"))
            flags.Add(MakeFlag("Combination", "STACK-FRUCTAN", "Multiple Fructan Sources", "Stacking Penalty", "Low",
                $"Contains {fructanCount} fructan sources. Combined fructan load increases risk of bloating and gas."));

        var gosCount = flags.Count(f => f.FodmapClass == "GOS");
        if (gosCount >= 2 && !HasFlag(flags, "STACK-GOS"))
            flags.Add(MakeFlag("Combination", "STACK-GOS", "Multiple GOS Sources", "Stacking Penalty", "Low",
                $"Contains {gosCount} GOS sources. Combined galacto-oligosaccharide load increases risk of bloating and gas."));

        var fodmapClasses = flags.Where(f => f.TriggerType == "Fodmap" && f.FodmapClass != "")
            .Select(f => f.FodmapClass).Distinct().Count();
        if (fodmapClasses >= 3 && !HasFlag(flags, "STACK-FODMAP-MIX"))
            flags.Add(MakeFlag("Combination", "STACK-FODMAP-MIX", "Multiple FODMAP Classes", "Stacking Penalty", "Medium",
                $"Triggers across {fodmapClasses} FODMAP classes. Combined load from different FODMAP types significantly increases symptom risk."));

        // 9. Amplifiers: concentrate/powder/isolate boosts
        if (!string.IsNullOrWhiteSpace(product.Ingredients))
        {
            var lowerIng = product.Ingredients.ToLowerInvariant();
            bool hasAmplifier = lowerIng.Contains("powder") || lowerIng.Contains("concentrate") ||
                lowerIng.Contains("extract") || lowerIng.Contains("syrup") ||
                lowerIng.Contains("isolate");
            if (hasAmplifier)
            {
                bool hasTargetedAmplifier =
                    lowerIng.Contains("inulin") || lowerIng.Contains("chicory") || lowerIng.Contains("oligofructose") ||
                    lowerIng.Contains("garlic") || lowerIng.Contains("onion") || lowerIng.Contains("fruit concentrate") ||
                    lowerIng.Contains("whey") || lowerIng.Contains("soy protein isolate") ||
                    lowerIng.Contains("fructose") || lowerIng.Contains("fructan");
                var fodmapHighDose = flags.Any(f => f.TriggerType == "Fodmap" && f.DoseSensitivity is "Medium" or "High");
                if (hasTargetedAmplifier && fodmapHighDose && !HasFlag(flags, "AMP-DOSE"))
                    flags.Add(MakeFlag("Combination", "AMP-DOSE", "Concentrated FODMAP Source", "Stacking Penalty", "Low",
                        "Contains FODMAP triggers in concentrated form (powder, concentrate, isolate). Dose is likely higher per serving than whole-food equivalents."));
            }
        }

        // 10. Lactase enzyme mitigation
        if (!string.IsNullOrWhiteSpace(product.Ingredients) &&
            product.Ingredients.Contains("lactase", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < flags.Count; i++)
            {
                if (flags[i].Category == "Dairy/Lactose" && flags[i].RiskLevel != "Low")
                {
                    flags[i] = MakeFlag(flags[i].Source, flags[i].Code, flags[i].Name, flags[i].Category, "Low",
                        flags[i].Explanation + " (Contains lactase enzyme — lactose impact likely reduced.)");
                }
            }
        }

        var score = CalculateGutScore(flags, product);
        var rating = score switch
        {
            >= 80 => "Good",
            >= 60 => "Fair",
            >= 40 => "Poor",
            _ => "Bad",
        };

        var doseSensitiveCount = flags.Count(f => f.DoseSensitivity is "Medium" or "High");
        var confidence = ComputeConfidence(product, flags);

        return new GutRiskAssessmentDto
        {
            GutScore = score,
            GutRating = rating,
            FlagCount = flags.Count,
            HighRiskCount = flags.Count(f => f.RiskLevel == "High"),
            MediumRiskCount = flags.Count(f => f.RiskLevel == "Medium"),
            LowRiskCount = flags.Count(f => f.RiskLevel == "Low"),
            Flags = flags.OrderByDescending(f => RiskLevelWeight(f.RiskLevel)).ToList(),
            Summary = GenerateSummary(flags, score, rating),
            Confidence = confidence,
            DoseSensitiveFlagsCount = doseSensitiveCount,
        };
    }

    public GutRiskAssessmentDto AssessText(string foodDescription)
    {
        var lower = foodDescription.ToLowerInvariant();
        var flags = new List<GutRiskFlagDto>();

        // Scan ingredient patterns
        foreach (var (pattern, regex, info) in IngredientPatterns)
        {
            bool matched = regex != null ? regex.IsMatch(lower) : lower.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            if (!matched) continue;
            var flag = MakeFlag("text", info.ENumber, info.Name, info.Category, info.RiskLevel, info.Explanation);
            if (!HasFlag(flags, !string.IsNullOrWhiteSpace(flag.Code) ? flag.Code : flag.Name))
                flags.Add(flag);
        }

        // Whole food patterns
        foreach (var (pattern, regex, info) in WholeFoodRiskPatterns)
        {
            bool matched = regex != null ? regex.IsMatch(lower) : lower.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            if (!matched) continue;
            var flag = MakeFlag("text", info.ENumber, info.Name, info.Category, info.RiskLevel, info.Explanation);
            if (!HasFlag(flags, !string.IsNullOrWhiteSpace(flag.Code) ? flag.Code : flag.Name))
                flags.Add(flag);
        }

        var score = CalculateGutScore(flags, null);
        var rating = score switch
        {
            >= 80 => "Good",
            >= 60 => "Fair",
            >= 40 => "Poor",
            _ => "Bad",
        };

        return new GutRiskAssessmentDto
        {
            GutScore = score,
            GutRating = rating,
            FlagCount = flags.Count,
            HighRiskCount = flags.Count(f => f.RiskLevel == "High"),
            MediumRiskCount = flags.Count(f => f.RiskLevel == "Medium"),
            LowRiskCount = flags.Count(f => f.RiskLevel == "Low"),
            Flags = flags.OrderByDescending(f => f.RiskLevel == "High" ? 3 : f.RiskLevel == "Medium" ? 2 : 1).ToList(),
            Summary = GenerateSummary(flags, score, rating),
            Confidence = "Medium",
            DoseSensitiveFlagsCount = flags.Count(f => f.DoseSensitivity is "Medium" or "High"),
        };
    }

    static int CalculateGutScore(List<GutRiskFlagDto> flags, FoodProductDto? product)
    {
        var score = 100;

        foreach (var flag in flags)
        {
            int basePenalty = flag.RiskLevel switch
            {
                "High" => 20,
                "Medium" => 10,
                "Low" => flag.Category == "Stacking Penalty" ? 2 : 5,
                _ => 0,
            };

            double multiplier = flag.TriggerType switch
            {
                "Fodmap" => 1.0,
                "Processing" => 0.8,
                "Nutrient" => 0.8,
                _ => 1.0,
            };

            score -= (int)Math.Round(basePenalty * multiplier);
        }

        score = Math.Clamp(score, 0, 100);

        // Bonus for high fiber (only when there are risks to offset)
        if (product?.Fiber100g > 5m && score < 100)
        {
            var bonus = 5;
            var hasFodmapFiber = flags.Any(f =>
                f.Category == "Prebiotic Fiber (FODMAP)" ||
                f.Name.Contains("Inulin", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("Chicory", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("FOS", StringComparison.OrdinalIgnoreCase));
            var medHighFodmapCount = flags.Count(f => f.TriggerType == "Fodmap" && f.RiskLevel is "Medium" or "High");
            var onlyOneMedium = medHighFodmapCount == 1 && flags.All(f => f.TriggerType != "Fodmap" || f.RiskLevel != "High");
            if (medHighFodmapCount >= 2)
                bonus = 0;
            else if (onlyOneMedium)
                bonus = 2;
            else if (hasFodmapFiber && medHighFodmapCount <= 1)
                bonus = 2;
            score = Math.Clamp(score + bonus, 0, 100);
        }

        return score;
    }

    static int RiskLevelWeight(string level) => level switch
    {
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0,
    };

    static string GenerateSummary(List<GutRiskFlagDto> flags, int score, string rating)
    {
        if (flags.Count == 0)
            return "No gut-concerning additives or ingredients detected. This product appears gut-friendly.";

        var highCount = flags.Count(f => f.RiskLevel == "High");
        var categories = flags.Select(f => f.Category).Distinct().ToList();

        if (highCount > 0)
            return $"Contains {highCount} ingredient(s) of higher concern including {string.Join(", ", flags.Where(f => f.RiskLevel == "High").Select(f => f.Name).Take(3))}. Individuals with digestive sensitivities may want to explore alternatives.";

        return $"Contains {flags.Count} item(s) of note in categories: {string.Join(", ", categories.Take(3))}. Individuals with digestive sensitivities may want to be mindful.";
    }

    static string ComputeConfidence(FoodProductDto product, List<GutRiskFlagDto> flags)
    {
        var hasIngredients = !string.IsNullOrWhiteSpace(product.Ingredients);
        var hasAdditives = product.AdditivesTags is { Count: > 0 } || product.Additives is { Count: > 0 };
        var hasDetailedIngredients = hasIngredients && product.Ingredients!.Contains(',') && product.Ingredients.Length > 50;

        if (!hasIngredients && !hasAdditives)
            return "Low";
        if (!hasDetailedIngredients)
            return "Medium";
        return "High";
    }
}
