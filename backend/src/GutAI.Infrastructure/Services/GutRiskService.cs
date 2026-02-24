using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

public class GutRiskService
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

    enum TriggerType { Fodmap, Additive, Processing, Nutrient, Combination }
    enum FodmapClass { None, Fructans, GOS, Lactose, ExcessFructose, Polyols }
    enum DoseSensitivity { Low, Medium, High }

    static readonly Dictionary<string, (TriggerType trigger, FodmapClass fodmap)> CategoryMap = new()
    {
        ["Sugar Alcohol"] = (TriggerType.Fodmap, FodmapClass.Polyols),
        ["Polyol Source"] = (TriggerType.Fodmap, FodmapClass.Polyols),
        ["High-FODMAP Ingredient"] = (TriggerType.Fodmap, FodmapClass.Fructans),
        ["Prebiotic Fiber (FODMAP)"] = (TriggerType.Fodmap, FodmapClass.Fructans),
        ["Dairy/Lactose"] = (TriggerType.Fodmap, FodmapClass.Lactose),
        ["Fructose Source"] = (TriggerType.Fodmap, FodmapClass.ExcessFructose),
        ["Sweetener"] = (TriggerType.Fodmap, FodmapClass.ExcessFructose),
        ["GOS Source"] = (TriggerType.Fodmap, FodmapClass.GOS),
        ["Emulsifier"] = (TriggerType.Additive, FodmapClass.None),
        ["Emulsifier/Thickener"] = (TriggerType.Additive, FodmapClass.None),
        ["Thickener"] = (TriggerType.Additive, FodmapClass.None),
        ["Preservative"] = (TriggerType.Additive, FodmapClass.None),
        ["Preservative/Sulfite"] = (TriggerType.Additive, FodmapClass.None),
        ["Artificial Sweetener"] = (TriggerType.Additive, FodmapClass.None),
        ["Artificial Colorant"] = (TriggerType.Additive, FodmapClass.None),
        ["Colorant"] = (TriggerType.Additive, FodmapClass.None),
        ["Colorant/Whitener"] = (TriggerType.Additive, FodmapClass.None),
        ["Antioxidant"] = (TriggerType.Additive, FodmapClass.None),
        ["Acidity Regulator"] = (TriggerType.Additive, FodmapClass.None),
        ["Flavor Enhancer"] = (TriggerType.Additive, FodmapClass.None),
        ["Anti-caking Agent"] = (TriggerType.Additive, FodmapClass.None),
        ["Emulsifier/Stabilizer"] = (TriggerType.Additive, FodmapClass.None),
        ["Stabilizer"] = (TriggerType.Additive, FodmapClass.None),
        ["Bulking Agent"] = (TriggerType.Additive, FodmapClass.None),
        ["Processing Level"] = (TriggerType.Processing, FodmapClass.None),
        ["Nutrient Concern"] = (TriggerType.Nutrient, FodmapClass.None),
        ["Stacking Penalty"] = (TriggerType.Combination, FodmapClass.None),
        ["Stimulant/Motility"] = (TriggerType.Additive, FodmapClass.None),
        ["Spicy/Irritant"] = (TriggerType.Additive, FodmapClass.None),
        ["Hidden FODMAP Risk"] = (TriggerType.Fodmap, FodmapClass.Fructans),
    };

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
                    flags.Add(MakeFlag("Additive", add.ENumber ?? "", add.Name, "Sugar Alcohol", "High",
                        "Sugar alcohols are high-FODMAP and can cause bloating, gas, and diarrhea in sensitive individuals."));
                else if (ArtificialSweetenerNames.Any(sw => lowerName.Contains(sw)))
                    flags.Add(MakeFlag("Additive", add.ENumber ?? "", add.Name, "Artificial Sweetener", "Medium",
                        "May disrupt gut microbiome composition and glucose response."));
            }
        }

        // 3. Scan ingredients text for gut-concerning items not captured as additive tags
        if (!string.IsNullOrWhiteSpace(product.Ingredients))
        {
            var lowerIngredients = product.Ingredients.ToLowerInvariant();
            foreach (var (pattern, regex, info) in IngredientPatterns)
            {
                bool matched = regex != null
                    ? regex.IsMatch(lowerIngredients)
                    : lowerIngredients.Contains(pattern);
                var key = !string.IsNullOrEmpty(info.ENumber) ? info.ENumber : info.Name;
                if (matched && !HasFlag(flags, key))
                    flags.Add(MakeFlag("Ingredient", info.ENumber, info.Name, info.Category, info.RiskLevel, info.Explanation));
            }
        }

        // 4. Flag high NOVA group
        if (product.NovaGroup >= 4)
            flags.Add(MakeFlag("Processing", $"NOVA-{product.NovaGroup}", "Ultra-Processed Food", "Processing Level", "Medium",
                "Ultra-processed foods (NOVA 4) are associated with increased gut inflammation, altered microbiome diversity, and higher risk of IBS symptoms."));

        // 5. Flag high sodium (>600mg per 100g)
        if (product.Sodium100g > 0.6m)
            flags.Add(MakeFlag("Nutrient", "HIGH-NA", "High Sodium", "Nutrient Concern", "Low",
                $"Contains {product.Sodium100g * 1000:0}mg sodium per 100g. High sodium intake can affect gut barrier function and microbiome composition."));

        // 6. Flag very high sugar (>25g per 100g)
        if (product.Sugar100g > 25m)
            flags.Add(MakeFlag("Nutrient", "HIGH-SUGAR", "High Sugar Content", "Nutrient Concern", "Low",
                $"Contains {product.Sugar100g:0}g sugar per 100g. Excess sugar feeds harmful gut bacteria and can worsen IBS symptoms."));

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
        var confidence = ComputeConfidence(flags);

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

    static int CalculateGutScore(List<GutRiskFlagDto> flags, FoodProductDto product)
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
                "Fodmap" => 1.2,
                "Processing" => 0.8,
                "Nutrient" => 0.8,
                _ => 1.0,
            };

            score -= (int)Math.Round(basePenalty * multiplier);
        }

        score = Math.Clamp(score, 0, 100);

        // Bonus for high fiber (only when there are risks to offset)
        if (product.Fiber100g > 5m && score < 100)
        {
            var bonus = 5;
            var hasFodmapFiber = flags.Any(f =>
                f.Category == "Prebiotic Fiber (FODMAP)" ||
                f.Name.Contains("Inulin", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("Chicory", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains("FOS", StringComparison.OrdinalIgnoreCase));
            var hasMedHighFodmap = flags.Any(f => f.TriggerType == "Fodmap" && f.RiskLevel is "Medium" or "High");
            if (hasMedHighFodmap)
                bonus = 0;
            else if (hasFodmapFiber)
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
            return $"Contains {highCount} high-risk gut irritant(s) including {string.Join(", ", flags.Where(f => f.RiskLevel == "High").Select(f => f.Name).Take(3))}. Consider alternatives if you have IBS, IBD, or gut sensitivity.";

        return $"Contains {flags.Count} item(s) of concern in categories: {string.Join(", ", categories.Take(3))}. Monitor for symptoms if you have gut sensitivity.";
    }

    static string ComputeConfidence(List<GutRiskFlagDto> flags)
    {
        var fodmapCount = flags.Count(f => f.TriggerType == "Fodmap");
        var highDoseCount = flags.Count(f => f.DoseSensitivity == "High");
        var broadTermCount = flags.Count(f =>
            f.TriggerType == "Fodmap" &&
            (f.Name.Equals("Wheat", StringComparison.OrdinalIgnoreCase) ||
             f.Name.Equals("Onion", StringComparison.OrdinalIgnoreCase) ||
             f.Name.Equals("Garlic", StringComparison.OrdinalIgnoreCase)));

        if (fodmapCount >= 3 || highDoseCount >= 3 || broadTermCount >= 2)
            return "Low";
        if (fodmapCount >= 2 || highDoseCount >= 2 || broadTermCount >= 1)
            return "Medium";
        return "High";
    }

    // ─── Gut-Harmful Additives Database ─────────────────────────────────────
    // Based on published research linking these additives to gut inflammation,
    // microbiome disruption, intestinal permeability, or IBS/IBD exacerbation.

    static readonly Dictionary<string, AdditiveInfo> GutHarmfulAdditives = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Emulsifiers (damage mucus layer, increase intestinal permeability) ──
        ["E433"] = new("E433", "Polysorbate 80", "Emulsifier", "High",
            "Directly damages the gut mucus layer, promotes intestinal inflammation, and is linked to increased risk of Crohn's disease and metabolic syndrome in animal studies."),
        ["E466"] = new("E466", "Carboxymethyl Cellulose (CMC)", "Emulsifier", "High",
            "Erodes the protective gut mucus barrier, promotes bacterial overgrowth in the mucus layer, and triggers low-grade intestinal inflammation."),
        ["E471"] = new("E471", "Mono- and Diglycerides", "Emulsifier", "Medium",
            "Common emulsifier that may affect gut barrier integrity at high intake levels."),
        ["E472E"] = new("E472E", "DATEM", "Emulsifier", "Medium",
            "Diacetyl tartaric acid ester used in bread — may affect gut microbiome composition."),
        ["E435"] = new("E435", "Polysorbate 60", "Emulsifier", "Medium",
            "Similar mechanism to Polysorbate 80 but less studied. May damage gut mucus layer."),
        ["E436"] = new("E436", "Polysorbate 65", "Emulsifier", "Medium",
            "Polysorbate family emulsifier with potential gut mucus disruption."),
        ["E407"] = new("E407", "Carrageenan", "Emulsifier/Thickener", "High",
            "Triggers intestinal inflammation even in small amounts. Widely used in research to induce gut inflammation in animal models. Linked to ulcerative colitis flares."),
        ["E407A"] = new("E407A", "Processed Eucheuma Seaweed (PES)", "Emulsifier/Thickener", "High",
            "Semi-refined carrageenan with similar inflammatory properties to E407."),
        ["E415"] = new("E415", "Xanthan Gum", "Thickener", "Low",
            "Generally well-tolerated but can cause bloating and gas in high amounts, especially in people with SIBO."),
        ["E410"] = new("E410", "Locust Bean Gum", "Thickener", "Low",
            "Generally well-tolerated; may cause mild bloating in sensitive individuals at high doses."),
        ["E412"] = new("E412", "Guar Gum", "Thickener", "Low",
            "Fermentable fiber that can cause gas and bloating, especially in large amounts or with SIBO."),
        ["E417"] = new("E417", "Tara Gum", "Thickener", "Low",
            "Similar to guar gum; may cause mild GI discomfort in sensitive individuals."),
        ["E418"] = new("E418", "Gellan Gum", "Thickener", "Low",
            "Generally well-tolerated but may cause bloating at high intake in sensitive individuals."),
        ["E440"] = new("E440", "Pectin", "Thickener", "Low",
            "Naturally occurring in fruit; fermentable fiber that may cause gas in large supplemental doses."),

        // ── Alginates & Additional Hydrocolloids ──
        ["E401"] = new("E401", "Sodium Alginate", "Thickener", "Low",
            "Seaweed-derived hydrocolloid; generally well-tolerated but fermentable — may cause gas and bloating in sensitive individuals."),
        ["E402"] = new("E402", "Potassium Alginate", "Thickener", "Low",
            "Alginate salt; similar gut profile to sodium alginate."),
        ["E403"] = new("E403", "Ammonium Alginate", "Thickener", "Low",
            "Alginate salt; similar gut profile to sodium alginate."),
        ["E404"] = new("E404", "Calcium Alginate", "Thickener", "Low",
            "Alginate salt; similar gut profile to sodium alginate."),
        ["E405"] = new("E405", "Propylene Glycol Alginate", "Thickener", "Low",
            "Modified alginate ester; generally well-tolerated at typical food levels."),
        ["E413"] = new("E413", "Tragacanth", "Thickener", "Low",
            "Natural gum; may cause bloating and gas in sensitive individuals due to fermentable fiber content."),
        ["E414"] = new("E414", "Gum Arabic", "Thickener", "Low",
            "Highly fermentable soluble fiber; generally well-tolerated but may cause gas at high doses."),

        // ── Sugar Alcohols (high FODMAP — osmotic diarrhea, bloating, gas) ──
        ["E420"] = new("E420", "Sorbitol", "Sugar Alcohol", "High",
            "High-FODMAP polyol. Poorly absorbed, draws water into the intestine causing bloating, cramps, and osmotic diarrhea. Major IBS trigger."),
        ["E421"] = new("E421", "Mannitol", "Sugar Alcohol", "High",
            "High-FODMAP polyol. Causes significant bloating and diarrhea in IBS patients even in small amounts (>0.5g)."),
        ["E953"] = new("E953", "Isomalt", "Sugar Alcohol", "High",
            "High-FODMAP. Causes dose-dependent GI distress — bloating, flatulence, and diarrhea."),
        ["E965"] = new("E965", "Maltitol", "Sugar Alcohol", "High",
            "High-FODMAP. One of the worst sugar alcohols for gut symptoms. Causes severe bloating and diarrhea in sensitive individuals."),
        ["E967"] = new("E967", "Xylitol", "Sugar Alcohol", "High",
            "FODMAP polyol that causes bloating, gas, and diarrhea. Dose-dependent — symptoms common above 10g."),
        ["E968"] = new("E968", "Erythritol", "Sugar Alcohol", "Low",
            "Best-tolerated sugar alcohol (90% absorbed in small intestine), but can still cause nausea and bloating at high doses."),
        ["E966"] = new("E966", "Lactitol", "Sugar Alcohol", "High",
            "Poorly absorbed polyol that causes dose-dependent bloating and osmotic diarrhea."),

        // ── Artificial Sweeteners (microbiome disruption) ──
        ["E951"] = new("E951", "Aspartame", "Artificial Sweetener", "Medium",
            "Some evidence suggests aspartame may alter gut microbiome composition; clinical significance in humans is still under investigation."),
        ["E950"] = new("E950", "Acesulfame K", "Artificial Sweetener", "Medium",
            "Animal studies show altered gut microbiome, including reduced Bifidobacterium; human data are limited."),
        ["E955"] = new("E955", "Sucralose", "Artificial Sweetener", "Medium",
            "Animal studies report reduced beneficial gut bacteria at high doses; human studies show mixed results at typical intake levels."),
        ["E954"] = new("E954", "Saccharin", "Artificial Sweetener", "Medium",
            "Shown in some studies to alter gut microbiome and glucose metabolism; effects may vary between individuals."),
        ["E962"] = new("E962", "Aspartame-Acesulfame Salt", "Artificial Sweetener", "Medium",
            "Combination sweetener with combined microbiome-disrupting effects of both components."),

        // ── Preservatives (antimicrobial = kills good bacteria too) ──
        ["E211"] = new("E211", "Sodium Benzoate", "Preservative", "Medium",
            "Antimicrobial preservative that can also reduce beneficial gut bacteria. May worsen histamine intolerance symptoms."),
        ["E210"] = new("E210", "Benzoic Acid", "Preservative", "Medium",
            "Antimicrobial preservative; parent compound of sodium benzoate with similar gut flora effects."),
        ["E200"] = new("E200", "Sorbic Acid", "Preservative", "Low",
            "Antimicrobial preservative that may modestly affect gut flora at high intake levels."),
        ["E202"] = new("E202", "Potassium Sorbate", "Preservative", "Low",
            "Generally safe but has antimicrobial properties that may modestly affect gut flora at high intake."),
        ["E220"] = new("E220", "Sulfur Dioxide", "Preservative/Sulfite", "Medium",
            "Sulfite that can trigger GI symptoms in sensitive individuals, especially those with sulfite sensitivity or asthma."),
        ["E221"] = new("E221", "Sodium Sulfite", "Preservative/Sulfite", "Medium",
            "Sulfite preservative — can cause abdominal pain, diarrhea, and nausea in sulfite-sensitive individuals."),
        ["E223"] = new("E223", "Sodium Metabisulfite", "Preservative/Sulfite", "Medium",
            "Sulfite preservative used in dried fruits and wine. Common trigger for GI symptoms in sensitive people."),
        ["E250"] = new("E250", "Sodium Nitrite", "Preservative", "Medium",
            "Used in processed meats. Associated with increased intestinal inflammation and altered gut microbiome. May form carcinogenic nitrosamines."),
        ["E252"] = new("E252", "Potassium Nitrate", "Preservative", "Medium",
            "Converts to nitrite in the body. Similar gut inflammation concerns as E250."),

        // ── Colorants (some linked to gut inflammation) ──
        ["E102"] = new("E102", "Tartrazine", "Artificial Colorant", "Low",
            "Azo dye that may increase intestinal permeability and trigger symptoms in sensitive individuals."),
        ["E110"] = new("E110", "Sunset Yellow", "Artificial Colorant", "Low",
            "Azo dye associated with increased intestinal permeability in some studies."),
        ["E129"] = new("E129", "Allura Red AC", "Artificial Colorant", "Medium",
            "Common food dye shown to promote intestinal inflammation and increase susceptibility to colitis in animal models."),
        ["E171"] = new("E171", "Titanium Dioxide", "Colorant/Whitener", "High",
            "Nanoparticles accumulate in gut tissue, disrupt microbiome, and promote intestinal inflammation. Banned in the EU since 2022."),

        // ── Phosphate Additives (gut & kidney stress) ──
        ["E338"] = new("E338", "Phosphoric Acid", "Acidity Regulator", "Low",
            "Common in soft drinks. May affect gut microbiome and calcium absorption at high intake levels."),
        ["E339"] = new("E339", "Sodium Phosphates", "Emulsifier/Stabilizer", "Low",
            "Inorganic phosphate — high intake linked to gut inflammation and cardiovascular risk."),
        ["E341"] = new("E341", "Calcium Phosphates", "Stabilizer", "Low",
            "Inorganic phosphate additive with similar concerns to E339 at high intake."),
        ["E450"] = new("E450", "Diphosphates", "Emulsifier/Stabilizer", "Low",
            "Phosphate additive; excessive inorganic phosphate intake is linked to gut and vascular inflammation."),
        ["E451"] = new("E451", "Triphosphates", "Emulsifier/Stabilizer", "Low",
            "Phosphate additive with similar concerns to E450 regarding cumulative phosphate load."),
        ["E452"] = new("E452", "Polyphosphates", "Emulsifier/Stabilizer", "Low",
            "Phosphate additive; contributes to total inorganic phosphate burden."),

        // ── Acidity Regulators ──
        ["E330"] = new("E330", "Citric Acid (manufactured)", "Acidity Regulator", "Low",
            "Industrially produced citric acid (from Aspergillus niger) may trigger symptoms in a small subset of sensitive individuals."),
        ["E296"] = new("E296", "Malic Acid", "Acidity Regulator", "Low",
            "Generally well-tolerated; may contribute to GI discomfort at very high doses."),
        ["E260"] = new("E260", "Acetic Acid", "Acidity Regulator", "Low",
            "Generally well-tolerated; found naturally in vinegar."),
        ["E262"] = new("E262", "Sodium Acetate", "Acidity Regulator", "Low",
            "Generally well-tolerated acidity regulator."),
        ["E270"] = new("E270", "Lactic Acid", "Acidity Regulator", "Low",
            "Generally well-tolerated; occurs naturally in fermented foods."),

        // ── Antioxidants ──
        ["E319"] = new("E319", "TBHQ", "Antioxidant", "Medium",
            "Tertiary butylhydroquinone — some animal studies suggest immune and gut microbiome effects at high doses."),
        ["E320"] = new("E320", "BHA", "Antioxidant", "Medium",
            "Butylated hydroxyanisole — possible endocrine disruptor; some evidence of gut microbiome effects."),
        ["E321"] = new("E321", "BHT", "Antioxidant", "Low",
            "Butylated hydroxytoluene — generally considered safe at typical food levels; limited gut-specific concerns."),

        // ── Other Gut Irritants ──
        ["E621"] = new("E621", "Monosodium Glutamate (MSG)", "Flavor Enhancer", "Low",
            "Generally safe but may cause GI symptoms in a small subset of sensitive individuals (\"Chinese Restaurant Syndrome\")."),
        ["E551"] = new("E551", "Silicon Dioxide", "Anti-caking Agent", "Low",
            "Nanoparticle form may affect gut barrier function; standard forms are generally considered safe."),

        // ── Lecithins ──
        ["E322"] = new("E322", "Lecithins", "Emulsifier", "Low",
            "Generally well-tolerated; soy-derived lecithin may be a concern for soy-sensitive individuals."),

        // ── Missing Artificial Sweeteners ──
        ["E952"] = new("E952", "Cyclamate", "Artificial Sweetener", "Medium",
            "Artificial sweetener banned in the US. Some evidence of gut microbiome disruption."),
        ["E961"] = new("E961", "Neotame", "Artificial Sweetener", "Medium",
            "Potent artificial sweetener. Limited data on gut effects; structural similarity to aspartame suggests potential microbiome impact."),
        ["E969"] = new("E969", "Advantame", "Artificial Sweetener", "Medium",
            "Ultra-potent artificial sweetener. Limited human data on gut microbiome effects."),

        // ── Missing Additives ──
        ["E422"] = new("E422", "Glycerol", "Bulking Agent", "Low",
            "Osmotic laxative effect at high doses. May draw water into the intestine causing loose stools."),
        ["E476"] = new("E476", "PGPR", "Emulsifier", "Low",
            "Polyglycerol polyricinoleate — common in chocolate. May affect gut barrier at high intake levels."),
        ["E491"] = new("E491", "Sorbitan Monostearate", "Emulsifier", "Low",
            "Sorbitan ester emulsifier. May affect gut barrier integrity at high intake levels."),
        ["E492"] = new("E492", "Sorbitan Tristearate", "Emulsifier", "Low",
            "Sorbitan ester emulsifier used in confectionery. May affect gut barrier integrity."),
        ["E493"] = new("E493", "Sorbitan Monolaurate", "Emulsifier", "Low",
            "Sorbitan ester emulsifier. May affect gut barrier integrity at high intake levels."),
        ["E494"] = new("E494", "Sorbitan Monooleate", "Emulsifier", "Low",
            "Sorbitan ester emulsifier. May affect gut barrier integrity at high intake levels."),
        ["E495"] = new("E495", "Sorbitan Monopalmitate", "Emulsifier", "Low",
            "Sorbitan ester emulsifier. May affect gut barrier integrity at high intake levels."),
        ["E481"] = new("E481", "Sodium Stearoyl Lactylate", "Emulsifier", "Low",
            "Stearoyl lactylate bread emulsifier. May affect gut barrier at high intake."),
        ["E482"] = new("E482", "Calcium Stearoyl Lactylate", "Emulsifier", "Low",
            "Stearoyl lactylate bread emulsifier. May affect gut barrier at high intake."),
        ["E1442"] = new("E1442", "Hydroxypropyl Distarch Phosphate", "Thickener", "Low",
            "Modified starch. May resist digestion and undergo colonic fermentation, causing gas in sensitive individuals."),
        ["E331"] = new("E331", "Sodium Citrates", "Acidity Regulator", "Low",
            "Generally well-tolerated acidity regulator. May contribute to GI discomfort at very high doses."),
        ["E334"] = new("E334", "Tartaric Acid", "Acidity Regulator", "Low",
            "Naturally occurring acid. Generally well-tolerated; may cause GI discomfort at high doses."),
        ["E224"] = new("E224", "Potassium Metabisulfite", "Preservative/Sulfite", "Medium",
            "Sulfite preservative. Can trigger GI symptoms in sulfite-sensitive individuals."),
    };

    static readonly string[] SugarAlcoholNames =
    [
        "sorbitol", "mannitol", "xylitol", "maltitol", "isomalt", "erythritol",
        "lactitol", "hydrogenated starch hydrolysate", "polyol",
    ];

    static readonly string[] ArtificialSweetenerNames =
    [
        "aspartame", "sucralose", "saccharin", "acesulfame", "neotame",
        "advantame", "cyclamate",
    ];

    static readonly (string pattern, Regex? regex, AdditiveInfo info)[] IngredientPatterns =
    [
        ("carrageenan", null, new("E407", "Carrageenan", "Emulsifier/Thickener", "High",
            "Triggers intestinal inflammation. Used in research to induce gut inflammation.")),
        ("polysorbate 80", null, new("E433", "Polysorbate 80", "Emulsifier", "High",
            "Damages gut mucus layer and promotes intestinal inflammation.")),
        ("polysorbate 60", null, new("E435", "Polysorbate 60", "Emulsifier", "Medium",
            "May damage gut mucus layer.")),
        ("cellulose gum", null, new("E466", "Carboxymethyl Cellulose", "Emulsifier", "High",
            "Erodes protective gut mucus barrier.")),
        ("carboxymethyl cellulose", null, new("E466", "Carboxymethyl Cellulose", "Emulsifier", "High",
            "Erodes protective gut mucus barrier.")),
        ("locust bean gum", null, new("E410", "Locust Bean Gum", "Thickener", "Low",
            "Generally well-tolerated; may cause mild bloating in sensitive individuals.")),
        ("guar gum", null, new("E412", "Guar Gum", "Thickener", "Low",
            "Fermentable fiber that can cause gas and bloating, especially in large amounts.")),
        ("tara gum", null, new("E417", "Tara Gum", "Thickener", "Low",
            "Similar to guar gum; may cause mild GI discomfort in sensitive individuals.")),
        ("gellan gum", null, new("E418", "Gellan Gum", "Thickener", "Low",
            "Generally well-tolerated but may cause bloating at high intake.")),
        ("xanthan gum", null, new("E415", "Xanthan Gum", "Thickener", "Low",
            "Fermentable polysaccharide; may cause gas and bloating in sensitive individuals.")),
        ("pectin", null, new("E440", "Pectin", "Thickener", "Low",
            "Naturally occurring in fruit; may cause gas in large supplemental doses.")),
        ("sodium benzoate", null, new("E211", "Sodium Benzoate", "Preservative", "Medium",
            "Antimicrobial that can reduce beneficial gut bacteria.")),
        ("benzoic acid", null, new("E210", "Benzoic Acid", "Preservative", "Medium",
            "Antimicrobial preservative with gut flora effects.")),
        ("sorbic acid", null, new("E200", "Sorbic Acid", "Preservative", "Low",
            "Antimicrobial preservative that may modestly affect gut flora.")),
        ("titanium dioxide", null, new("E171", "Titanium Dioxide", "Colorant", "High",
            "Nanoparticles disrupt microbiome and promote gut inflammation.")),
        ("tbhq", new Regex(@"\btbhq\b", RegexOptions.Compiled), new("E319", "TBHQ", "Antioxidant", "Medium",
            "Some animal studies suggest immune and gut microbiome effects.")),
        ("bha", new Regex(@"\bbha\b", RegexOptions.Compiled), new("E320", "BHA", "Antioxidant", "Medium",
            "Butylated hydroxyanisole — possible endocrine disruptor with gut effects.")),
        ("bht", new Regex(@"\bbht\b", RegexOptions.Compiled), new("E321", "BHT", "Antioxidant", "Low",
            "Butylated hydroxytoluene — limited gut-specific concerns at typical levels.")),
        ("polydextrose", null, new("", "Polydextrose", "Bulking Agent", "Low",
            "Soluble fiber that can cause gas and bloating at high doses (>50g/day).")),
        ("sorbitol", null, new("E420", "Sorbitol", "Sugar Alcohol", "High",
            "High-FODMAP polyol causing bloating, cramps, and diarrhea.")),
        ("mannitol", null, new("E421", "Mannitol", "Sugar Alcohol", "High",
            "High-FODMAP polyol causing significant GI distress.")),
        ("maltitol", null, new("E965", "Maltitol", "Sugar Alcohol", "High",
            "High-FODMAP. Causes severe bloating and diarrhea.")),
        ("xylitol", null, new("E967", "Xylitol", "Sugar Alcohol", "High",
            "FODMAP polyol causing bloating and gas.")),
        ("isomalt", null, new("E953", "Isomalt", "Sugar Alcohol", "High",
            "High-FODMAP causing dose-dependent GI distress.")),
        ("lactitol", null, new("E966", "Lactitol", "Sugar Alcohol", "High",
            "Poorly absorbed polyol causing bloating and osmotic diarrhea.")),
                ("erythritol", null, new("E968", "Erythritol", "Sugar Alcohol", "Low",
                    "Better tolerated polyol but may cause GI symptoms at high doses.")),
        ("sugar alcohol", null, new("", "Sugar Alcohol", "Sugar Alcohol", "High",
            "Sugar alcohols are high-FODMAP and can cause bloating, gas, and diarrhea in sensitive individuals.")),
        ("sucralose", null, new("E955", "Sucralose", "Artificial Sweetener", "Medium",
            "Animal studies report reduced beneficial gut bacteria at high doses.")),
        ("aspartame", null, new("E951", "Aspartame", "Artificial Sweetener", "Medium",
            "Some evidence suggests altered gut microbiome; human significance under investigation.")),
        ("acesulfame", null, new("E950", "Acesulfame K", "Artificial Sweetener", "Medium",
            "Animal studies show reduced beneficial Bifidobacterium; human data limited.")),
        ("saccharin", null, new("E954", "Saccharin", "Artificial Sweetener", "Medium",
            "Some studies show altered gut microbiome and glucose metabolism.")),
        ("sodium nitrite", null, new("E250", "Sodium Nitrite", "Preservative", "Medium",
            "Associated with increased intestinal inflammation.")),
        ("allura red", null, new("E129", "Allura Red AC", "Artificial Colorant", "Medium",
            "Promotes intestinal inflammation and colitis susceptibility.")),
        ("red 40", null, new("E129", "Allura Red AC", "Artificial Colorant", "Medium",
            "Promotes intestinal inflammation and colitis susceptibility.")),
        ("soy lecithin", null, new("E322", "Lecithins", "Emulsifier", "Low",
            "Generally well-tolerated; soy-derived lecithin may be a concern for soy-sensitive individuals.")),
        ("lecithin", null, new("E322", "Lecithins", "Emulsifier", "Low",
            "Generally well-tolerated; soy-derived lecithin may be a concern for soy-sensitive individuals.")),
        ("onion powder", null, new("", "Onion Powder", "High-FODMAP Ingredient", "Medium",
            "Concentrated source of fructans (high-FODMAP). Common IBS trigger even in small amounts.")),
        ("garlic powder", null, new("", "Garlic Powder", "High-FODMAP Ingredient", "Medium",
            "Concentrated source of fructans (high-FODMAP). One of the most common IBS triggers.")),
        ("onion", new Regex(@"\bonion\b", RegexOptions.Compiled), new("", "Onion", "High-FODMAP Ingredient", "Medium",
            "Contains fructans (high-FODMAP). Common trigger for bloating and gas in IBS patients.")),
        ("garlic", new Regex(@"\bgarlic\b", RegexOptions.Compiled), new("", "Garlic", "High-FODMAP Ingredient", "Medium",
            "Contains fructans (high-FODMAP). Common trigger for bloating and gas in IBS patients.")),
        ("wheat starch", null, new("", "Wheat Starch", "High-FODMAP Ingredient", "Low",
            "Contains fructans. May trigger symptoms in fructan-sensitive individuals.")),
        ("wheat protein", null, new("", "Wheat Protein", "High-FODMAP Ingredient", "Low",
            "Contains gluten and fructans. May trigger symptoms in sensitive individuals.")),
        ("wheat flour", null, new("", "Wheat Flour", "High-FODMAP Ingredient", "Low",
            "Contains fructans. May trigger symptoms in fructan-sensitive individuals; gluten is a separate concern.")),
        ("wheat fiber", null, new("", "Wheat Fiber", "High-FODMAP Ingredient", "Low",
            "Contains fructans. May trigger symptoms in fructan-sensitive individuals.")),
        ("wheat", new Regex(@"\bwheat\b", RegexOptions.Compiled), new("", "Wheat", "High-FODMAP Ingredient", "Low",
            "Contains fructans. May trigger symptoms in fructan-sensitive individuals; gluten is a separate concern.")),

        // ── Prebiotic Fiber / FODMAP Triggers ──
        ("inulin", null, new("", "Inulin", "Prebiotic Fiber (FODMAP)", "Medium",
            "Fructan fiber — prebiotic but high-FODMAP. Can cause significant bloating and gas in IBS patients.")),
        ("chicory root", null, new("", "Chicory Root Fiber", "Prebiotic Fiber (FODMAP)", "Medium",
            "Rich source of inulin (fructan). Beneficial for microbiome but high-FODMAP — triggers bloating in sensitive individuals.")),
        ("fructooligosaccharide", null, new("", "FOS", "Prebiotic Fiber (FODMAP)", "Medium",
            "Fructan-type fiber — prebiotic but high-FODMAP. Common cause of gas and bloating.")),
        ("oligofructose", null, new("", "Oligofructose", "Prebiotic Fiber (FODMAP)", "Medium",
            "Short-chain fructan. High-FODMAP trigger for IBS symptoms.")),

        // ── Sweetener / Fructose Triggers ──
        ("high fructose corn syrup", null, new("", "High Fructose Corn Syrup", "Sweetener", "Medium",
            "Excess fructose can overwhelm absorption capacity, causing bloating, gas, and diarrhea — especially in fructose malabsorbers.")),
        ("agave", null, new("", "Agave Syrup", "Sweetener", "Medium",
            "Very high in fructose (up to 90%). Can trigger symptoms in people with fructose malabsorption.")),

        // ── Dairy / Lactose Triggers ──
        ("whey powder", null, new("", "Whey Powder", "Dairy/Lactose", "Medium",
            "Concentrated source of lactose. Common trigger for lactose-intolerant individuals.")),
        ("whey", new Regex(@"\bwhey\b", RegexOptions.Compiled), new("", "Whey", "Dairy/Lactose", "Medium",
            "Contains lactose. May trigger bloating, gas, and diarrhea in lactose-intolerant individuals.")),
        ("skim milk powder", null, new("", "Skim Milk Powder", "Dairy/Lactose", "Medium",
            "Concentrated source of lactose. Common trigger for lactose-intolerant individuals.")),
        ("milk powder", null, new("", "Milk Powder", "Dairy/Lactose", "Medium",
            "Concentrated source of lactose. Common trigger for lactose-intolerant individuals.")),
        ("milk solids", null, new("", "Milk Solids", "Dairy/Lactose", "Medium",
            "Contains lactose and milk proteins. Common trigger for lactose-intolerant individuals.")),
        ("cream powder", null, new("", "Cream Powder", "Dairy/Lactose", "Medium",
            "Contains lactose. May trigger symptoms in lactose-intolerant individuals.")),
        ("buttermilk", null, new("", "Buttermilk", "Dairy/Lactose", "Medium",
            "Contains lactose. May trigger GI symptoms in lactose-intolerant individuals.")),
        ("lactose", null, new("", "Lactose", "Dairy/Lactose", "Medium",
            "Milk sugar poorly digested by ~68% of the global population. Causes bloating, cramps, and diarrhea in lactose-intolerant individuals.")),
        ("skim milk", null, new("", "Skim Milk", "Dairy/Lactose", "Medium",
            "Contains lactose. May trigger GI symptoms in lactose-intolerant individuals.")),

        // ── Fruit Concentrate Fructose Triggers ──
        ("apple juice concentrate", null, new("", "Apple Juice Concentrate", "Fructose Source", "Medium",
            "Concentrated source of excess fructose. Can overwhelm fructose absorption and trigger bloating and diarrhea.")),
        ("pear juice concentrate", null, new("", "Pear Juice Concentrate", "Fructose Source", "Medium",
            "Concentrated source of excess fructose and sorbitol. Double FODMAP trigger for sensitive individuals.")),
        ("apple concentrate", null, new("", "Apple Concentrate", "Fructose Source", "Medium",
            "Concentrated source of excess fructose. Can trigger bloating and diarrhea in fructose malabsorbers.")),
        ("pear concentrate", null, new("", "Pear Concentrate", "Fructose Source", "Medium",
            "Concentrated source of excess fructose and sorbitol. Double FODMAP trigger for sensitive individuals.")),
        ("fruit juice concentrate", null, new("", "Fruit Juice Concentrate", "Fructose Source", "Medium",
            "Concentrated fructose source. May trigger symptoms in people with fructose malabsorption.")),
        ("fruit concentrate", null, new("", "Fruit Concentrate", "Fructose Source", "Medium",
            "Concentrated fructose source. May trigger symptoms in people with fructose malabsorption.")),

        // ── Additional Hydrocolloid Ingredient Patterns ──
        ("tragacanth", null, new("E413", "Tragacanth", "Thickener", "Low",
            "Natural gum; may cause bloating and gas in sensitive individuals.")),
        ("gum arabic", null, new("E414", "Gum Arabic", "Thickener", "Low",
            "Highly fermentable soluble fiber; may cause gas at high doses.")),
        ("sodium alginate", null, new("E401", "Sodium Alginate", "Thickener", "Low",
            "Seaweed-derived hydrocolloid; may cause gas and bloating in sensitive individuals.")),
        ("alginate", null, new("E401", "Sodium Alginate", "Thickener", "Low",
            "Seaweed-derived hydrocolloid; may cause gas and bloating in sensitive individuals.")),

        // ── GOS (Galacto-oligosaccharides) — Legumes & Soy ──
        ("chickpea flour", null, new("", "Chickpea Flour", "GOS Source", "High",
            "Concentrated source of galacto-oligosaccharides (GOS). High-FODMAP — triggers bloating and gas in IBS patients.")),
        ("chickpea", new Regex(@"\bchickpeas?\b", RegexOptions.Compiled), new("", "Chickpea", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP trigger for bloating and gas.")),
        ("lentil", new Regex(@"\blentils?\b", RegexOptions.Compiled), new("", "Lentil", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP trigger for bloating and gas.")),
        ("kidney bean", null, new("", "Kidney Bean", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP trigger for bloating and gas.")),
        ("black bean", null, new("", "Black Bean", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP trigger for bloating and gas.")),
        ("navy bean", null, new("", "Navy Bean", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP trigger for bloating and gas.")),
        ("soy protein isolate", null, new("", "Soy Protein Isolate", "GOS Source", "High",
            "Concentrated soy source high in GOS. Dense FODMAP load per serving.")),
        ("soy flour", null, new("", "Soy Flour", "GOS Source", "High",
            "Concentrated soy source high in GOS. Dense FODMAP load per serving.")),
        ("textured vegetable protein", null, new("", "Textured Vegetable Protein", "GOS Source", "High",
            "Usually soy-derived, concentrated GOS source. Dense FODMAP load per serving.")),
        ("tvp", new Regex(@"\btvp\b", RegexOptions.Compiled), new("", "Textured Vegetable Protein", "GOS Source", "High",
            "Usually soy-derived, concentrated GOS source. Dense FODMAP load per serving.")),
        ("soybean", null, new("", "Soybean", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). May trigger bloating in GOS-sensitive individuals.")),

        // ── Polyols — Natural Sources (Fruits, Vegetables, Mushrooms) ──
        ("mushroom", new Regex(@"\bmushrooms?\b", RegexOptions.Compiled), new("", "Mushroom", "Polyol Source", "Medium",
            "Contains mannitol (polyol). High-FODMAP — common trigger for bloating and gas.")),
        ("prune", new Regex(@"\bprunes?\b", RegexOptions.Compiled), new("", "Prune", "Polyol Source", "High",
            "High in sorbitol (polyol). Concentrated FODMAP source — strongly associated with GI distress.")),
        ("plum", new Regex(@"\bplums?\b", RegexOptions.Compiled), new("", "Plum", "Polyol Source", "Medium",
            "Contains sorbitol (polyol). May trigger symptoms in polyol-sensitive individuals.")),
        ("cherry", new Regex(@"\bcherr(y|ies)\b", RegexOptions.Compiled), new("", "Cherry", "Polyol Source", "Medium",
            "Contains sorbitol (polyol). May trigger symptoms in polyol-sensitive individuals.")),
        ("apricot", new Regex(@"\bapricots?\b", RegexOptions.Compiled), new("", "Apricot", "Polyol Source", "Medium",
            "Contains sorbitol and polyols. May trigger symptoms in sensitive individuals.")),
        ("peach", new Regex(@"\bpeach(es)?\b", RegexOptions.Compiled), new("", "Peach", "Polyol Source", "Medium",
            "Contains sorbitol (polyol). May trigger symptoms in polyol-sensitive individuals.")),
        ("cauliflower", new Regex(@"\bcauliflowers?\b", RegexOptions.Compiled), new("", "Cauliflower", "Polyol Source", "Low",
            "Contains mannitol (polyol). May trigger symptoms in sensitive individuals at larger servings.")),
        ("avocado", new Regex(@"\bavocados?\b", RegexOptions.Compiled), new("", "Avocado", "Polyol Source", "Low",
            "Contains sorbitol (polyol). Dose-dependent — small amounts often tolerated.")),

        // ── Excess Fructose — Expanded ──
        ("honey", new Regex(@"\bhoney\b", RegexOptions.Compiled), new("", "Honey", "Fructose Source", "Medium",
            "High in excess fructose. Can overwhelm absorption capacity and trigger bloating and diarrhea.")),
        ("mango", new Regex(@"\bmango\b", RegexOptions.Compiled), new("", "Mango", "Fructose Source", "Low",
            "Contains excess fructose. May trigger symptoms in fructose-sensitive individuals at higher servings.")),
        ("watermelon", new Regex(@"\bwatermelon\b", RegexOptions.Compiled), new("", "Watermelon", "Fructose Source", "Low",
            "Contains excess fructose and mannitol. Dual FODMAP trigger.")),
        ("apple juice", null, new("", "Apple Juice", "Fructose Source", "Medium",
            "High in excess fructose even without concentration. Common trigger for fructose malabsorbers.")),
        ("fruit juice", null, new("", "Fruit Juice", "Fructose Source", "Low",
            "May contain excess fructose depending on fruit type. Monitor for symptoms.")),

        // ── Fructans — Expanded ──
        ("onion salt", null, new("", "Onion Salt", "High-FODMAP Ingredient", "High",
            "Concentrated source of fructans (high-FODMAP). Common IBS trigger.")),
        ("garlic salt", null, new("", "Garlic Salt", "High-FODMAP Ingredient", "High",
            "Concentrated source of fructans (high-FODMAP). Common IBS trigger.")),
        ("shallot", new Regex(@"\bshallots?\b", RegexOptions.Compiled), new("", "Shallot", "High-FODMAP Ingredient", "Medium",
            "Contains fructans. Related to onion — common IBS trigger.")),
        ("leek", new Regex(@"\bleeks?\b", RegexOptions.Compiled), new("", "Leek", "High-FODMAP Ingredient", "Medium",
            "Contains fructans. Common trigger for bloating and gas in IBS patients.")),
        ("wheat bran", null, new("", "Wheat Bran", "High-FODMAP Ingredient", "Low",
            "Contains fructans. May trigger symptoms in fructan-sensitive individuals.")),

        // ── Dairy / Lactose — Expanded ──
        ("milk solids non-fat", null, new("", "Milk Solids Non-Fat", "Dairy/Lactose", "Medium",
            "Concentrated source of lactose. Common trigger for lactose-intolerant individuals.")),
        ("dry milk", null, new("", "Dry Milk", "Dairy/Lactose", "Medium",
            "Concentrated source of lactose. Common trigger for lactose-intolerant individuals.")),
        ("whey solids", null, new("", "Whey Solids", "Dairy/Lactose", "Medium",
            "Concentrated source of lactose. Common trigger for lactose-intolerant individuals.")),

        // ── Barley & Rye — Fructan Sources ──
        ("barley", new Regex(@"\bbarley\b", RegexOptions.Compiled), new("", "Barley", "High-FODMAP Ingredient", "Low",
            "Contains fructans. May trigger symptoms in fructan-sensitive individuals.")),
        ("rye", new Regex(@"\brye\b", RegexOptions.Compiled), new("", "Rye", "High-FODMAP Ingredient", "Low",
            "Contains fructans. May trigger symptoms in fructan-sensitive individuals.")),

        // ── Cashew & Pistachio — GOS Sources ──
        ("cashew", new Regex(@"\bcashews?\b", RegexOptions.Compiled), new("", "Cashew", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP — common in snack and protein bars.")),
        ("pistachio", new Regex(@"\bpistachios?\b", RegexOptions.Compiled), new("", "Pistachio", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP — common in snack and protein bars.")),

        // ── Fructose Ingredient & Syrup Patterns ──
        ("crystalline fructose", null, new("", "Crystalline Fructose", "Fructose Source", "Medium",
            "Pure fructose. Can overwhelm absorption capacity and trigger bloating and diarrhea in fructose malabsorbers.")),
        ("maltitol syrup", null, new("", "Maltitol Syrup", "Sugar Alcohol", "High",
            "Liquid form of maltitol (polyol). High-FODMAP — causes severe bloating and diarrhea.")),
        ("sorbitol syrup", null, new("", "Sorbitol Syrup", "Sugar Alcohol", "High",
            "Liquid form of sorbitol (polyol). High-FODMAP — causes bloating, cramps, and osmotic diarrhea.")),
        ("fructose", new Regex(@"\bfructose\b", RegexOptions.Compiled), new("", "Fructose", "Fructose Source", "Low",
            "Excess fructose can overwhelm absorption capacity. May trigger symptoms in fructose malabsorbers.")),

        // ── Hidden Onion/Garlic — Low Confidence Flags ──
        ("natural flavors", null, new("", "Natural Flavors", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("natural flavour", null, new("", "Natural Flavours", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("natural flavouring", null, new("", "Natural Flavouring", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("vegetable powder", null, new("", "Vegetable Powder", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("seasoning", new Regex(@"\bseasoning\b", RegexOptions.Compiled), new("", "Seasoning", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("bouillon", new Regex(@"\bbouillon\b", RegexOptions.Compiled), new("", "Bouillon", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("stock", new Regex(@"\bstock\b", RegexOptions.Compiled), new("", "Stock", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("flavouring", new Regex(@"\bflavouring\b", RegexOptions.Compiled), new("", "Flavouring", "Hidden FODMAP Risk", "Low",
            "Often contains onion/garlic; not always disclosed. Common hidden FODMAP source.")),
        ("spices", new Regex(@"\bspices\b", RegexOptions.Compiled), new("", "Spices", "Hidden FODMAP Risk", "Low",
            "May contain undisclosed onion/garlic powder. Common hidden FODMAP source.")),

        // ── Remaining FODMAP Gaps — Fructan Extracts ──
        ("onion extract", null, new("", "Onion Extract", "High-FODMAP Ingredient", "Medium",
            "Concentrated source of fructans (high-FODMAP). Common IBS trigger.")),
        ("garlic extract", null, new("", "Garlic Extract", "High-FODMAP Ingredient", "Medium",
            "Concentrated source of fructans (high-FODMAP). Common IBS trigger.")),
        ("garlic flavor", null, new("", "Garlic Flavor", "High-FODMAP Ingredient", "Medium",
            "Likely contains fructans. Common IBS trigger.")),
        ("onion flavor", null, new("", "Onion Flavor", "High-FODMAP Ingredient", "Medium",
            "Likely contains fructans. Common IBS trigger.")),
        ("garlic flavour", null, new("", "Garlic Flavour", "High-FODMAP Ingredient", "Medium",
            "Likely contains fructans. Common IBS trigger.")),
        ("onion flavour", null, new("", "Onion Flavour", "High-FODMAP Ingredient", "Medium",
            "Likely contains fructans. Common IBS trigger.")),

        // ── Remaining FODMAP Gaps — GOS ──
        ("hummus", new Regex(@"\bhummus\b", RegexOptions.Compiled), new("", "Hummus", "GOS Source", "Low",
            "Chickpea-based — contains galacto-oligosaccharides (GOS). High dose sensitivity.")),
        ("baked beans", null, new("", "Baked Beans", "GOS Source", "Medium",
            "Contains galacto-oligosaccharides (GOS). High-FODMAP trigger for bloating and gas.")),
        ("pea protein", null, new("", "Pea Protein", "GOS Source", "Low",
            "May contain residual GOS from peas. High dose sensitivity — concentrated forms increase risk.")),

        // ── Remaining FODMAP Gaps — Excess Fructose (Whole Fruit) ──
        ("pear", new Regex(@"\bpear\b", RegexOptions.Compiled), new("", "Pear", "Fructose Source", "Low",
            "Contains excess fructose and sorbitol. Dual FODMAP trigger in sensitive individuals.")),
        ("apple", new Regex(@"\bapple\b", RegexOptions.Compiled), new("", "Apple", "Fructose Source", "Low",
            "Contains excess fructose. May trigger symptoms in fructose-sensitive individuals.")),
        ("fruit puree", null, new("", "Fruit Puree", "Fructose Source", "Low",
            "Concentrated fructose source. May trigger symptoms in fructose malabsorbers.")),
        ("fruit paste", null, new("", "Fruit Paste", "Fructose Source", "Low",
            "Concentrated fructose source. May trigger symptoms in fructose malabsorbers.")),
        ("date paste", null, new("", "Date Paste", "Fructose Source", "Medium",
            "Concentrated fructose source. Dates are high in excess fructose.")),

        // ── Remaining FODMAP Gaps — Dairy (Casein) ──
        ("whey concentrate", null, new("", "Whey Concentrate", "Dairy/Lactose", "Medium",
            "Contains lactose. May trigger symptoms in lactose-intolerant individuals.")),
        ("caseinate", null, new("", "Caseinate", "Dairy/Lactose", "Low",
            "Dairy protein derivative. Low in lactose but may trigger symptoms in dairy-protein-sensitive individuals.")),
        ("casein", new Regex(@"\bcasein\b", RegexOptions.Compiled), new("", "Casein", "Dairy/Lactose", "Low",
            "Dairy protein — low in lactose but may trigger symptoms in dairy-protein-sensitive individuals.")),

        // ── Non-FODMAP IBS Triggers — Stimulant/Motility ──
        ("caffeine", new Regex(@"\bcaffeine\b", RegexOptions.Compiled), new("", "Caffeine", "Stimulant/Motility", "Low",
            "Stimulates gut motility and increases gastric acid. May worsen diarrhea-predominant IBS.")),
        ("coffee", new Regex(@"\bcoffee\b", RegexOptions.Compiled), new("", "Coffee", "Stimulant/Motility", "Low",
            "Stimulates gut motility and increases gastric acid. May worsen diarrhea-predominant IBS.")),
        ("guarana", new Regex(@"\bguarana\b", RegexOptions.Compiled), new("", "Guarana", "Stimulant/Motility", "Medium",
            "Natural caffeine source. Stimulates gut motility — may worsen IBS symptoms.")),
        ("green tea extract", null, new("", "Green Tea Extract", "Stimulant/Motility", "Low",
            "Contains caffeine. May stimulate gut motility in sensitive individuals.")),

        // ── Non-FODMAP IBS Triggers — Spicy/Irritant ──
        ("chilli", new Regex(@"\bchillis?\b", RegexOptions.Compiled), new("", "Chilli", "Spicy/Irritant", "Low",
            "Contains capsaicin which can irritate the gut lining and accelerate transit. Common IBS trigger.")),
        ("chili", new Regex(@"\bchilis?\b", RegexOptions.Compiled), new("", "Chili", "Spicy/Irritant", "Low",
            "Contains capsaicin which can irritate the gut lining and accelerate transit. Common IBS trigger.")),
        ("capsicum", new Regex(@"\bcapsicum\b", RegexOptions.Compiled), new("", "Capsicum", "Spicy/Irritant", "Low",
            "Contains capsaicin. May irritate the gut lining in sensitive individuals.")),
        ("cayenne", new Regex(@"\bcayenne\b", RegexOptions.Compiled), new("", "Cayenne", "Spicy/Irritant", "Medium",
            "High capsaicin content. Can irritate gut lining and accelerate transit — common IBS trigger.")),
        ("hot pepper", null, new("", "Hot Pepper", "Spicy/Irritant", "Low",
            "Contains capsaicin which can irritate the gut lining and accelerate transit.")),
    ];

    record AdditiveInfo(string ENumber, string Name, string Category, string RiskLevel, string Explanation);
}
