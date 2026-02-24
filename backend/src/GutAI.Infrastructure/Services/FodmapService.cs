using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

public class FodmapService
{
    static bool HasTrigger(List<FodmapTriggerDto> triggers, FodmapTriggerDto info)
    {
        // Deduplicate by SubCategory — e.g. "Sorbitol" from ingredients and "Sorbitol (E420)" from additive tags
        // are the same FODMAP trigger. Also match by exact name.
        return triggers.Any(t =>
            t.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(t.SubCategory) && t.SubCategory.Equals(info.SubCategory, StringComparison.OrdinalIgnoreCase) && t.Category.Equals(info.Category, StringComparison.OrdinalIgnoreCase)));
    }

    public FodmapAssessmentDto Assess(FoodProductDto product)
    {
        var triggers = new List<FodmapTriggerDto>();

        // 1. Scan ingredients text against FODMAP trigger database
        if (!string.IsNullOrWhiteSpace(product.Ingredients))
        {
            var lower = product.Ingredients.ToLowerInvariant();
            foreach (var (pattern, info) in IngredientTriggers)
            {
                if (lower.Contains(pattern) && !HasTrigger(triggers, info))
                    triggers.Add(info);
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
            if (productName.Contains(pattern) && !HasTrigger(triggers, info))
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
            >= 80 => "Low FODMAP",
            >= 60 => "Moderate FODMAP",
            >= 40 => "High FODMAP",
            _ => "Very High FODMAP",
        };

        var categories = triggers.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();

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
        };
    }

    public FodmapAssessmentDto AssessText(string foodDescription)
    {
        var lower = foodDescription.ToLowerInvariant();
        var triggers = new List<FodmapTriggerDto>();

        foreach (var (pattern, info) in IngredientTriggers)
        {
            if (lower.Contains(pattern) && !HasTrigger(triggers, info))
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
            >= 80 => "Low FODMAP",
            >= 60 => "Moderate FODMAP",
            >= 40 => "High FODMAP",
            _ => "Very High FODMAP",
        };

        var categories = triggers.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();

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
        };
    }

    static int CalculateFodmapScore(List<FodmapTriggerDto> triggers)
    {
        var score = 100;
        foreach (var t in triggers)
        {
            score -= t.Severity switch
            {
                "High" => 25,
                "Moderate" => 12,
                "Low" => 5,
                _ => 0,
            };
        }
        return Math.Clamp(score, 0, 100);
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
            return $"Contains {highCount} high-FODMAP trigger(s): {names}. FODMAP categories affected: {string.Join(", ", categories)}. Avoid during elimination phase.";
        }

        return $"Contains {triggers.Count} FODMAP concern(s) in {string.Join(", ", categories)}. May be tolerable in small portions — monitor symptoms.";
    }

    // ─── FODMAP Trigger Database ────────────────────────────────────────────
    // Based on Monash University FODMAP research, King's College London data,
    // and published peer-reviewed studies on fermentable carbohydrates.
    //
    // FODMAP = Fermentable Oligosaccharides, Disaccharides, Monosaccharides And Polyols
    //
    // Categories:
    //   Oligosaccharide/Fructan  — wheat, garlic, onion, inulin, FOS
    //   Oligosaccharide/GOS     — legumes, chickpeas, lentils
    //   Disaccharide/Lactose    — milk, soft cheese, yogurt, cream
    //   Monosaccharide/Fructose — apple, pear, honey, mango, HFCS, agave
    //   Polyol/Sorbitol         — apple, pear, stone fruits, E420
    //   Polyol/Mannitol         — mushroom, cauliflower, E421

    static readonly (string pattern, FodmapTriggerDto info)[] IngredientTriggers =
    [
        // ── Oligosaccharides — Fructans ──────────────────────────────────
        ("wheat flour", new() { Name = "Wheat (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Wheat contains fructans — a key FODMAP trigger. Major source of bloating, gas, and abdominal pain in IBS. Significant amounts are poorly absorbed." }),
        ("wheat starch", new() { Name = "Wheat Starch", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Wheat starch retains some fructans though less than wheat flour. May be tolerable in small amounts." }),
        ("whole wheat", new() { Name = "Whole Wheat (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Whole wheat contains even more fructans than refined wheat flour." }),
        ("wheat", new() { Name = "Wheat (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Wheat contains fructans — a key FODMAP trigger. Major source of bloating, gas, and abdominal pain in IBS." }),
        (" rye", new() { Name = "Rye (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Rye is high in fructans. One of the highest FODMAP grains." }),
        ("barley", new() { Name = "Barley (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Barley contains significant fructans. Avoid during FODMAP elimination phase." }),
        ("onion", new() { Name = "Onion (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Onions are one of the highest dietary sources of fructans. Even small amounts trigger symptoms in many IBS patients." }),
        ("garlic", new() { Name = "Garlic (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Garlic is extremely high in fructans. One of the most common IBS triggers. Garlic-infused oil (fructans not oil-soluble) may be tolerated." }),
        ("shallot", new() { Name = "Shallot (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Shallots are high in fructans, similar to onions." }),
        ("leek", new() { Name = "Leek (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Leeks contain fructans mainly in the white part. Green tops are lower FODMAP." }),
        ("artichoke", new() { Name = "Artichoke (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Artichokes are very high in fructans and inulin-type fructans." }),
        ("asparagus", new() { Name = "Asparagus (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Asparagus contains moderate fructans. Small portions (≤5 spears) may be tolerated." }),
        ("beetroot", new() { Name = "Beetroot (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Beetroot contains moderate levels of fructans and GOS." }),
        ("brussels sprout", new() { Name = "Brussels Sprouts (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Brussels sprouts contain moderate fructans. May be tolerable in portions ≤2 sprouts." }),
        ("inulin", new() { Name = "Inulin (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Inulin is a fructan fiber added to many 'high fiber' processed foods. Major FODMAP trigger even in small doses." }),
        ("chicory root", new() { Name = "Chicory Root Fiber (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Chicory root is the primary industrial source of inulin. Extremely high in fructans." }),
        ("chicory fibre", new() { Name = "Chicory Root Fiber (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Chicory root is the primary industrial source of inulin. Extremely high in fructans." }),
        ("fructooligosaccharide", new() { Name = "FOS (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "FOS (fructooligosaccharides) are short-chain fructans. Rapidly fermented, major gas and bloating trigger." }),
        ("oligofructose", new() { Name = "Oligofructose (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Oligofructose is a type of FOS — highly fermentable fructan." }),
        ("fennel", new() { Name = "Fennel (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Fennel bulb contains moderate fructans. Fennel seeds/tea are generally low FODMAP." }),
        ("pistachio", new() { Name = "Pistachio (Fructan+GOS)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Pistachios contain both fructans and GOS. High FODMAP even in small portions." }),
        ("cashew", new() { Name = "Cashew (Fructan+GOS)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Cashews are high in fructans and GOS. One of the highest FODMAP nuts." }),

        // ── Oligosaccharides — GOS (Galacto-oligosaccharides) ────────────
        ("chickpea", new() { Name = "Chickpea (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Chickpeas/garbanzo beans are very high in GOS. Canned and rinsed may reduce GOS slightly." }),
        ("lentil", new() { Name = "Lentil (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Lentils are high in GOS. Canned lentils have slightly lower GOS than dried." }),
        ("kidney bean", new() { Name = "Kidney Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Kidney beans contain very high GOS levels." }),
        ("black bean", new() { Name = "Black Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Black beans are high in GOS." }),
        ("baked bean", new() { Name = "Baked Beans (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Baked beans (navy/haricot beans) are high in GOS." }),
        ("soybean", new() { Name = "Soybean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Soybeans contain high GOS. Firm tofu (GOS leached out in processing) is low FODMAP; soy milk from whole beans is high." }),
        ("soy milk", new() { Name = "Soy Milk from Beans (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Soy milk made from whole soybeans is high in GOS. Soy milk from soy protein isolate is low FODMAP." }),
        ("hummus", new() { Name = "Hummus (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Moderate",
            Explanation = "Hummus is made from chickpeas (high GOS) but small portions (≤2 tbsp) may be tolerated." }),
        ("lima bean", new() { Name = "Lima Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Lima beans are high in GOS." }),
        ("split pea", new() { Name = "Split Pea (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Split peas are high in GOS." }),
        ("navy bean", new() { Name = "Navy Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Navy beans (haricot) are high in GOS." }),
        ("pinto bean", new() { Name = "Pinto Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Pinto beans are high in GOS." }),

        // ── Disaccharides — Lactose ──────────────────────────────────────
        ("whole milk", new() { Name = "Whole Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Whole milk contains ~5g lactose per 100ml. Major trigger for lactose-intolerant individuals." }),
        ("skim milk", new() { Name = "Skim Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Skim milk contains similar lactose levels to whole milk." }),
        ("low fat milk", new() { Name = "Low Fat Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Low fat milk contains similar lactose levels to whole milk." }),
        ("fat free milk", new() { Name = "Fat Free Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Fat free milk contains similar lactose levels to whole milk." }),
        ("reduced fat milk", new() { Name = "Reduced Fat Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Reduced fat milk contains similar lactose levels to whole milk." }),
        ("milk powder", new() { Name = "Milk Powder (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Milk powder is concentrated lactose. Small amounts in processed foods add up." }),
        ("whey powder", new() { Name = "Whey Powder (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Whey powder contains variable lactose. Whey protein isolate is very low lactose; whey concentrate retains more." }),
        ("whey concentrate", new() { Name = "Whey Concentrate (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Whey protein concentrate retains 10-50% lactose depending on grade." }),
        ("cream cheese", new() { Name = "Cream Cheese (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Cream cheese contains moderate lactose. Small portions (≤2 tbsp) may be tolerated." }),
        ("ricotta", new() { Name = "Ricotta (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Ricotta is a fresh cheese with high lactose content." }),
        ("cottage cheese", new() { Name = "Cottage Cheese (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Cottage cheese retains significant lactose." }),
        ("ice cream", new() { Name = "Ice Cream (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Ice cream is high in lactose from milk and cream." }),
        ("custard", new() { Name = "Custard (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Custard is made with milk — high lactose content." }),
        ("condensed milk", new() { Name = "Condensed Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Condensed milk is concentrated milk — very high in lactose." }),
        ("evaporated milk", new() { Name = "Evaporated Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Evaporated milk is concentrated milk with high lactose." }),
        ("buttermilk", new() { Name = "Buttermilk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Buttermilk has moderate lactose — fermentation reduces some but not all." }),
        ("milk", new() { Name = "Milk (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Milk contains ~5g lactose per 100ml. Major trigger for lactose-intolerant individuals." }),
        ("lactose", new() { Name = "Lactose", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Pure lactose added as ingredient. Direct FODMAP trigger for lactose-intolerant individuals." }),

        // ── Monosaccharides — Excess Fructose ────────────────────────────
        ("high fructose corn syrup", new() { Name = "HFCS (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "HFCS has excess fructose over glucose, overwhelming fructose absorption. Major trigger for fructose malabsorption." }),
        ("agave", new() { Name = "Agave (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Agave syrup/nectar contains up to 90% fructose — extremely high excess fructose." }),
        ("honey", new() { Name = "Honey (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Honey has ~40% fructose vs ~30% glucose — significant excess fructose. Major IBS trigger." }),
        ("apple juice", new() { Name = "Apple Juice (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Apple juice contains high excess fructose plus sorbitol. Double FODMAP hit." }),
        ("pear juice", new() { Name = "Pear Juice (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Pear juice is very high in excess fructose and sorbitol." }),
        ("mango", new() { Name = "Mango (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Mango contains moderate excess fructose. Small portions may be tolerated." }),
        ("fruit juice concentrate", new() { Name = "Fruit Juice Concentrate (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Concentrated fruit juice is a concentrated source of excess fructose." }),
        ("crystalline fructose", new() { Name = "Crystalline Fructose", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Pure fructose added as sweetener. Directly triggers fructose malabsorption symptoms." }),

        // ── Polyols — Sorbitol ───────────────────────────────────────────
        ("sorbitol", new() { Name = "Sorbitol (Polyol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Sorbitol (E420) is poorly absorbed, draws water into the bowel causing bloating, cramps, and osmotic diarrhea." }),
        ("glucitol", new() { Name = "Sorbitol/Glucitol (Polyol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Glucitol is the chemical name for sorbitol — same FODMAP impact." }),

        // ── Polyols — Mannitol ───────────────────────────────────────────
        ("mannitol", new() { Name = "Mannitol (Polyol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Mannitol (E421) causes significant bloating and diarrhea. Symptoms common even at low doses (>0.5g)." }),

        // ── Polyols — Other ──────────────────────────────────────────────
        ("maltitol", new() { Name = "Maltitol (Polyol)", Category = "Polyol", SubCategory = "Maltitol", Severity = "High",
            Explanation = "Maltitol (E965) is one of the worst sugar alcohols for FODMAP symptoms. Widely used in sugar-free products." }),
        ("xylitol", new() { Name = "Xylitol (Polyol)", Category = "Polyol", SubCategory = "Xylitol", Severity = "High",
            Explanation = "Xylitol (E967) is a FODMAP polyol. Dose-dependent symptoms — common above 10g." }),
        ("isomalt", new() { Name = "Isomalt (Polyol)", Category = "Polyol", SubCategory = "Isomalt", Severity = "High",
            Explanation = "Isomalt (E953) causes dose-dependent GI distress in FODMAP-sensitive individuals." }),
        ("erythritol", new() { Name = "Erythritol (Polyol)", Category = "Polyol", SubCategory = "Erythritol", Severity = "Low",
            Explanation = "Erythritol (E968) is the best-tolerated polyol — 90% absorbed before reaching the colon. Generally low FODMAP at normal doses." }),
        ("lactitol", new() { Name = "Lactitol (Polyol)", Category = "Polyol", SubCategory = "Lactitol", Severity = "High",
            Explanation = "Lactitol is poorly absorbed, causing dose-dependent bloating and diarrhea." }),
        ("hydrogenated starch", new() { Name = "Hydrogenated Starch Hydrolysate (Polyol)", Category = "Polyol", SubCategory = "Mixed Polyols", Severity = "High",
            Explanation = "HSH contains a mixture of sorbitol, maltitol, and other polyols. High FODMAP." }),

        // ── Mixed / Cross-category ───────────────────────────────────────
        ("apple", new() { Name = "Apple (Fructose + Sorbitol)", Category = "Monosaccharide + Polyol", SubCategory = "Excess Fructose + Sorbitol", Severity = "High",
            Explanation = "Apples contain both excess fructose AND sorbitol — double FODMAP load. One of the most common IBS food triggers." }),
        ("pear", new() { Name = "Pear (Fructose + Sorbitol)", Category = "Monosaccharide + Polyol", SubCategory = "Excess Fructose + Sorbitol", Severity = "High",
            Explanation = "Pears have very high excess fructose and sorbitol. Avoid during elimination phase." }),
        ("watermelon", new() { Name = "Watermelon (Fructose + Mannitol)", Category = "Monosaccharide + Polyol", SubCategory = "Excess Fructose + Mannitol", Severity = "High",
            Explanation = "Watermelon contains excess fructose, mannitol, and fructans — triple FODMAP trigger." }),
        ("cherry", new() { Name = "Cherry (Fructose + Sorbitol)", Category = "Monosaccharide + Polyol", SubCategory = "Excess Fructose + Sorbitol", Severity = "High",
            Explanation = "Cherries are high in both excess fructose and sorbitol." }),
        ("apricot", new() { Name = "Apricot (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Apricots contain moderate sorbitol. Small fresh portions may be tolerated; dried apricots are higher." }),
        ("peach", new() { Name = "Peach (Sorbitol + Fructose)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Peaches contain moderate sorbitol and some excess fructose." }),
        ("plum", new() { Name = "Plum (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Plums contain moderate sorbitol. Prunes (dried plums) are much higher." }),
        ("prune", new() { Name = "Prune (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Prunes are dried plums with concentrated sorbitol — very high FODMAP." }),
        ("nectarine", new() { Name = "Nectarine (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Nectarines contain moderate sorbitol and excess fructose." }),
        ("mushroom", new() { Name = "Mushroom (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Most mushrooms are high in mannitol. Button mushrooms have lower levels; shiitake and portobello are very high." }),
        ("cauliflower", new() { Name = "Cauliflower (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "Moderate",
            Explanation = "Cauliflower contains moderate mannitol. Small portions (≤1/2 cup) may be tolerated." }),
        ("celery", new() { Name = "Celery (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "Moderate",
            Explanation = "Celery contains moderate mannitol, especially in larger portions." }),
        ("sweet potato", new() { Name = "Sweet Potato (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "Moderate",
            Explanation = "Sweet potato contains moderate mannitol. Half a medium sweet potato (~70g) is generally tolerated." }),
        ("snow pea", new() { Name = "Snow Peas (Fructan + GOS)", Category = "Oligosaccharide", SubCategory = "Fructan + GOS", Severity = "Moderate",
            Explanation = "Snow peas contain moderate fructans and GOS." }),
        ("sugar snap", new() { Name = "Sugar Snap Peas (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Sugar snap peas contain moderate excess fructose." }),

        // ── Carrageenan ──────────────────────────────────────────────────
        ("carrageenan", new() { Name = "Carrageenan (Gut Irritant)", Category = "Other", SubCategory = "Other", Severity = "Moderate",
            Explanation = "Carrageenan is an emulsifier that can trigger gut inflammation and worsen IBS symptoms in sensitive individuals." }),

        // ── EXPANDED DATABASE — Fructans (grains & wheat products) ──────
        ("couscous", new() { Name = "Couscous (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Couscous is made from wheat semolina — high in fructans." }),
        ("semolina", new() { Name = "Semolina (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Semolina is durum wheat — high fructan content." }),
        ("bulgur", new() { Name = "Bulgur (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Bulgur is cracked wheat — high in fructans." }),
        ("spelt", new() { Name = "Spelt (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Spelt contains fructans but at lower levels than modern wheat. Sourdough spelt may be better tolerated." }),
        ("kamut", new() { Name = "Kamut (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Kamut (Khorasan wheat) contains moderate fructans." }),
        ("farro", new() { Name = "Farro (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Farro is an ancient wheat grain with moderate fructan content." }),
        ("freekeh", new() { Name = "Freekeh (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Freekeh is roasted green wheat — high in fructans." }),
        ("breadcrumb", new() { Name = "Breadcrumbs (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Breadcrumbs are dried wheat bread — high fructan content." }),
        ("panko", new() { Name = "Panko (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Panko breadcrumbs are wheat-based — high in fructans." }),
        ("pasta", new() { Name = "Pasta (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Standard pasta is wheat-based. Rice or corn pasta are low-FODMAP alternatives." }),
        ("noodle", new() { Name = "Noodles (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Wheat noodles are high in fructans. Rice noodles are a low-FODMAP alternative." }),
        ("pita", new() { Name = "Pita Bread (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Pita is wheat bread — high in fructans." }),
        ("naan", new() { Name = "Naan (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Naan is wheat flatbread, often containing garlic — double fructan source." }),
        ("tortilla", new() { Name = "Tortilla (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Wheat tortillas contain fructans. Corn tortillas are a low-FODMAP alternative." }),
        ("cracker", new() { Name = "Crackers (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Most crackers are wheat-based. Rice crackers are a low-FODMAP alternative." }),
        ("pretzel", new() { Name = "Pretzel (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Pretzels are made from wheat flour — high in fructans." }),
        ("croissant", new() { Name = "Croissant (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Croissants are wheat-based with butter — high fructan content." }),
        ("brioche", new() { Name = "Brioche (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Brioche is enriched wheat bread — high in fructans." }),
        ("crouton", new() { Name = "Croutons (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Croutons are toasted wheat bread, often seasoned with garlic." }),
        ("breadstick", new() { Name = "Breadsticks (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Breadsticks are wheat-based — high in fructans." }),
        ("rye bread", new() { Name = "Rye Bread (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Rye bread contains high fructans from rye flour." }),
        ("pumpernickel", new() { Name = "Pumpernickel (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Pumpernickel is rye-based — high in fructans." }),
        ("muffin", new() { Name = "Muffin (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Most muffins are wheat-based with moderate fructan content." }),
        ("scone", new() { Name = "Scone (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Scones are wheat-based with moderate fructan content." }),
        ("biscuit", new() { Name = "Biscuit (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Most biscuits contain wheat flour — moderate fructan content." }),
        ("wafer", new() { Name = "Wafer (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Wafers are typically wheat-based." }),
        ("stuffing", new() { Name = "Stuffing (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Stuffing contains wheat bread plus onion — double fructan source." }),
        ("granola", new() { Name = "Granola (Mixed FODMAP)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Granola often contains wheat, honey, dried fruits, and inulin/chicory fiber." }),
        ("muesli", new() { Name = "Muesli (Mixed FODMAP)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Muesli often contains wheat flakes and dried fruits with FODMAP content." }),
        ("cereal bar", new() { Name = "Cereal Bar (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Cereal bars often contain wheat, chicory root fiber, and dried fruits." }),
        ("protein bar", new() { Name = "Protein Bar (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Many protein bars contain chicory root fiber (inulin) and sugar alcohols." }),
        ("fiber supplement", new() { Name = "Fiber Supplement (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Many fiber supplements use inulin or FOS — high FODMAP fructans." }),

        // ── EXPANDED — Fructans (vegetables & alliums) ──────────────────
        ("spring onion", new() { Name = "Spring Onion White Part (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "The white part of spring onions is high in fructans. Green tops are low FODMAP." }),
        ("scallion", new() { Name = "Scallion White Part (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "White part of scallions is high in fructans. Use green tops only for low FODMAP." }),
        ("savoy cabbage", new() { Name = "Savoy Cabbage (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Savoy cabbage contains moderate fructans." }),
        ("radicchio", new() { Name = "Radicchio (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Radicchio is a chicory family vegetable with moderate fructans." }),
        ("dandelion", new() { Name = "Dandelion Greens (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Dandelion greens contain moderate fructans from the chicory family." }),
        ("sun-dried tomato", new() { Name = "Sun-Dried Tomato (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Sun-dried tomatoes have concentrated fructans compared to fresh tomatoes." }),
        ("dried date", new() { Name = "Dried Dates (Fructan + Fructose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Dried dates contain concentrated fructans and excess fructose." }),
        ("date paste", new() { Name = "Date Paste (Fructan + Fructose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Date paste is concentrated dates — high in fructans and fructose." }),
        ("dried fig", new() { Name = "Dried Fig (Fructan + Fructose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Dried figs contain concentrated fructans and excess fructose." }),
        ("dried cranberry", new() { Name = "Dried Cranberry (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Dried cranberries often have added sweeteners and concentrated fructans." }),
        ("persimmon", new() { Name = "Persimmon (Fructan + Fructose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Persimmons contain both fructans and excess fructose." }),
        ("chicory coffee", new() { Name = "Chicory Coffee (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Chicory coffee is made from chicory root — extremely high in fructans/inulin." }),
        ("prebiotic", new() { Name = "Prebiotic Fiber (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Most prebiotic supplements contain inulin or FOS — high FODMAP fructans." }),

        // ── EXPANDED — GOS (more legumes & soy) ────────────────────────
        ("cannellini", new() { Name = "Cannellini Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Cannellini beans are high in GOS." }),
        ("borlotti", new() { Name = "Borlotti Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Borlotti/cranberry beans are high in GOS." }),
        ("broad bean", new() { Name = "Broad Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Broad beans (fava beans) are high in GOS." }),
        ("fava bean", new() { Name = "Fava Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Fava beans are high in GOS." }),
        ("mung bean", new() { Name = "Mung Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Moderate",
            Explanation = "Mung beans have moderate GOS — lower than most legumes." }),
        ("adzuki", new() { Name = "Adzuki Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Adzuki beans are high in GOS." }),
        ("edamame", new() { Name = "Edamame (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Moderate",
            Explanation = "Edamame (young soybeans) have moderate GOS content." }),
        ("tempeh", new() { Name = "Tempeh (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Low",
            Explanation = "Fermentation significantly reduces GOS in tempeh — generally low FODMAP." }),
        ("soy flour", new() { Name = "Soy Flour (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Soy flour retains high GOS levels from whole soybeans." }),
        ("soy protein", new() { Name = "Soy Protein (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Moderate",
            Explanation = "Soy protein concentrate may retain moderate GOS. Soy protein isolate is lower." }),
        ("pea protein", new() { Name = "Pea Protein (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Moderate",
            Explanation = "Pea protein may contain residual GOS from pea processing." }),
        ("lupin", new() { Name = "Lupin (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Lupin flour and beans are high in GOS." }),
        ("haricot", new() { Name = "Haricot Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Haricot beans (navy beans) are high in GOS." }),
        ("refried bean", new() { Name = "Refried Beans (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Refried beans retain high GOS content." }),
        ("bean paste", new() { Name = "Bean Paste (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Bean pastes are concentrated legumes — high in GOS." }),
        ("black-eyed pea", new() { Name = "Black-Eyed Pea (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Black-eyed peas are high in GOS." }),
        ("butter bean", new() { Name = "Butter Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Butter beans (lima beans) are high in GOS." }),
        ("garbanzo", new() { Name = "Garbanzo Bean (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Garbanzo beans (chickpeas) are very high in GOS." }),
        ("dal makhani", new() { Name = "Dal Makhani (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Dal makhani is black lentil/kidney bean dish — very high GOS." }),
        ("chana", new() { Name = "Chana (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Chana (chickpea) preparations are high in GOS." }),

        // ── EXPANDED — Lactose (dairy products) ────────────────────────
        ("yogurt", new() { Name = "Yogurt (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Yogurt has moderate lactose — bacterial fermentation reduces some but not all." }),
        ("mascarpone", new() { Name = "Mascarpone (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Mascarpone is a fresh cream cheese — high in lactose." }),
        ("paneer", new() { Name = "Paneer (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Paneer is fresh unaged cheese — retains significant lactose." }),
        ("fresh mozzarella", new() { Name = "Fresh Mozzarella (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Fresh mozzarella has moderate lactose — more than aged varieties." }),
        ("goat cheese", new() { Name = "Goat Cheese (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Fresh goat cheese has moderate lactose. Aged goat cheese is lower." }),
        ("cream", new() { Name = "Cream (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Cream contains moderate lactose. Small amounts may be tolerated." }),
        ("sour cream", new() { Name = "Sour Cream (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Sour cream has moderate lactose — fermentation reduces some." }),
        ("half and half", new() { Name = "Half and Half (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Half and half contains moderate lactose from milk and cream." }),
        ("dulce de leche", new() { Name = "Dulce de Leche (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Dulce de leche is concentrated sweetened milk — very high lactose." }),
        ("milk chocolate", new() { Name = "Milk Chocolate (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Milk chocolate contains milk solids with moderate lactose." }),
        ("white chocolate", new() { Name = "White Chocolate (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "White chocolate contains milk solids with moderate lactose." }),
        ("cheese spread", new() { Name = "Cheese Spread (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Processed cheese spreads contain moderate lactose." }),
        ("processed cheese", new() { Name = "Processed Cheese (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Processed cheese slices retain moderate lactose." }),
        ("milk solid", new() { Name = "Milk Solids (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Milk solids/milk solids non-fat are concentrated lactose sources." }),
        ("whipping cream", new() { Name = "Whipping Cream (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Whipping cream has moderate lactose content." }),
        ("clotted cream", new() { Name = "Clotted Cream (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Clotted cream has moderate lactose content." }),
        ("infant formula", new() { Name = "Infant Formula (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Most infant formulas contain lactose as primary carbohydrate." }),

        // ── EXPANDED — Excess Fructose ──────────────────────────────────
        ("glucose-fructose syrup", new() { Name = "Glucose-Fructose Syrup (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "EU name for HFCS — contains excess fructose over glucose." }),
        ("fructose-glucose syrup", new() { Name = "Fructose-Glucose Syrup (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Variant name for HFCS — high in excess fructose." }),
        ("fruit sugar", new() { Name = "Fruit Sugar (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Fruit sugar is another name for fructose — direct FODMAP trigger." }),
        ("date syrup", new() { Name = "Date Syrup (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Date syrup is concentrated fructose from dates." }),
        ("golden syrup", new() { Name = "Golden Syrup (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Golden syrup contains moderate levels of fructose from invert sugar." }),
        ("treacle", new() { Name = "Treacle (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Treacle contains moderate fructose from sugar processing." }),
        ("tamarillo", new() { Name = "Tamarillo (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Tamarillo contains high excess fructose." }),
        ("boysenberry", new() { Name = "Boysenberry (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Boysenberries contain high excess fructose." }),
        ("fig", new() { Name = "Fig (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Fresh figs have moderate excess fructose. Dried figs are higher." }),
        ("guava", new() { Name = "Guava (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Guava has moderate excess fructose content." }),
        ("jackfruit", new() { Name = "Jackfruit (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Jackfruit contains moderate excess fructose." }),
        ("cider", new() { Name = "Cider (Excess Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Apple cider contains high excess fructose from apples." }),
        ("pomegranate", new() { Name = "Pomegranate (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Pomegranate contains moderate excess fructose." }),
        ("grape juice", new() { Name = "Grape Juice (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Grape juice has moderate excess fructose in concentrated form." }),
        ("fruit nectar", new() { Name = "Fruit Nectar (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Fruit nectars often contain concentrated fructose." }),
        ("fruit paste", new() { Name = "Fruit Paste (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Fruit paste is concentrated fruit — high fructose content." }),
        ("fruit leather", new() { Name = "Fruit Leather (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Fruit leather is dehydrated concentrated fruit — high fructose." }),

        // ── EXPANDED — Sorbitol (more fruits, sugar-free products) ──────
        ("blackberry", new() { Name = "Blackberry (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Blackberries contain moderate sorbitol." }),
        ("lychee", new() { Name = "Lychee (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Lychees contain moderate sorbitol." }),
        ("longan", new() { Name = "Longan (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Longan fruit contains moderate sorbitol." }),
        ("avocado", new() { Name = "Avocado (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "Moderate",
            Explanation = "Avocado contains moderate sorbitol. Small portions (1/8 avocado) may be tolerated." }),
        ("dried apple", new() { Name = "Dried Apple (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Dried apple has concentrated sorbitol — much higher than fresh." }),
        ("dried pear", new() { Name = "Dried Pear (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Dried pear has concentrated sorbitol and fructose." }),
        ("dried peach", new() { Name = "Dried Peach (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Dried peach has concentrated sorbitol." }),
        ("dried cherry", new() { Name = "Dried Cherry (Sorbitol)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Dried cherries have concentrated sorbitol and fructose." }),
        ("sugar-free gum", new() { Name = "Sugar-Free Gum (Polyols)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Sugar-free gum typically contains sorbitol, xylitol, or mannitol — high FODMAP." }),
        ("sugar-free candy", new() { Name = "Sugar-Free Candy (Polyols)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Sugar-free candy usually contains sorbitol or maltitol — high FODMAP." }),
        ("sugar-free mint", new() { Name = "Sugar-Free Mints (Polyols)", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Sugar-free mints typically contain sorbitol or xylitol." }),
        ("sugar-free chocolate", new() { Name = "Sugar-Free Chocolate (Polyols)", Category = "Polyol", SubCategory = "Maltitol", Severity = "High",
            Explanation = "Sugar-free chocolate usually contains maltitol — one of the worst FODMAP polyols." }),
        ("sugar free", new() { Name = "Sugar-Free Product (Polyols)", Category = "Polyol", SubCategory = "Mixed Polyols", Severity = "High",
            Explanation = "Sugar-free products commonly use sugar alcohols (polyols) as sweeteners — high FODMAP risk." }),

        // ── EXPANDED — Mannitol (mushroom varieties) ────────────────────
        ("shiitake", new() { Name = "Shiitake Mushroom (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Shiitake mushrooms are very high in mannitol." }),
        ("portobello", new() { Name = "Portobello Mushroom (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Portobello mushrooms are high in mannitol." }),
        ("oyster mushroom", new() { Name = "Oyster Mushroom (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Oyster mushrooms are high in mannitol." }),
        ("porcini", new() { Name = "Porcini Mushroom (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Porcini mushrooms are high in mannitol." }),
        ("enoki", new() { Name = "Enoki Mushroom (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "Moderate",
            Explanation = "Enoki mushrooms have moderate mannitol content." }),
        ("truffle", new() { Name = "Truffle (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Truffles are very high in mannitol." }),
        ("chanterelle", new() { Name = "Chanterelle (Mannitol)", Category = "Polyol", SubCategory = "Mannitol", Severity = "Moderate",
            Explanation = "Chanterelles have moderate mannitol content." }),

        // ── EXPANDED — Sauces, condiments, prepared ingredients ─────────
        ("teriyaki", new() { Name = "Teriyaki Sauce (Mixed FODMAP)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Teriyaki sauce often contains garlic, onion, and HFCS." }),
        ("barbecue sauce", new() { Name = "BBQ Sauce (Mixed FODMAP)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "BBQ sauce commonly contains onion, garlic, and HFCS." }),
        ("ketchup", new() { Name = "Ketchup (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Ketchup often contains HFCS or concentrated tomato with excess fructose." }),
        ("pasta sauce", new() { Name = "Pasta Sauce (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Most pasta sauces contain onion and garlic — fructan sources." }),
        ("pizza sauce", new() { Name = "Pizza Sauce (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Pizza sauce typically contains onion and garlic." }),
        ("curry paste", new() { Name = "Curry Paste (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Most curry pastes contain onion, garlic, and shallots." }),
        ("stock cube", new() { Name = "Stock Cube (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Stock cubes/bouillon often contain onion and garlic powder." }),
        ("bouillon", new() { Name = "Bouillon (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Bouillon commonly contains onion and garlic derivatives." }),
        ("gravy", new() { Name = "Gravy (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Gravy mixes often contain wheat flour and onion powder." }),
        ("seasoning mix", new() { Name = "Seasoning Mix (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Seasoning mixes frequently contain onion and garlic powder." }),
        ("onion powder", new() { Name = "Onion Powder (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Onion powder is concentrated onion — very high in fructans even in small amounts." }),
        ("garlic powder", new() { Name = "Garlic Powder (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Garlic powder is concentrated garlic — extremely high in fructans." }),
        ("garlic salt", new() { Name = "Garlic Salt (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Garlic salt contains garlic powder — high fructan content." }),
        ("onion salt", new() { Name = "Onion Salt (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Onion salt contains onion powder — high fructan content." }),
        ("ranch dressing", new() { Name = "Ranch Dressing (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Ranch dressing typically contains garlic, onion, and buttermilk." }),
        ("caesar dressing", new() { Name = "Caesar Dressing (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Caesar dressing usually contains garlic." }),
        ("salsa", new() { Name = "Salsa (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Salsa typically contains onion and sometimes garlic." }),
        ("pesto", new() { Name = "Pesto (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Traditional pesto contains garlic — a fructan source." }),
        ("chutney", new() { Name = "Chutney (Mixed FODMAP)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Chutneys often contain onion, garlic, and fruit — multiple FODMAP sources." }),
        ("relish", new() { Name = "Relish (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Relish often contains onion." }),
        ("kimchi", new() { Name = "Kimchi (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Kimchi contains garlic — a fructan source. Fermentation may reduce some FODMAPs." }),
        ("tzatziki", new() { Name = "Tzatziki (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Tzatziki contains garlic and yogurt — fructan and lactose sources." }),
        ("guacamole", new() { Name = "Guacamole (Fructan + Sorbitol)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Guacamole contains onion (fructan) and avocado (sorbitol)." }),
        ("worcestershire", new() { Name = "Worcestershire Sauce (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Worcestershire sauce contains garlic and onion derivatives." }),
        ("hoisin", new() { Name = "Hoisin Sauce (GOS + Fructan)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Moderate",
            Explanation = "Hoisin sauce is soybean-based with garlic — GOS and fructan content." }),
        ("oyster sauce", new() { Name = "Oyster Sauce (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Oyster sauce often contains garlic and wheat." }),
        ("fish sauce", new() { Name = "Fish Sauce (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Low",
            Explanation = "Fish sauce is generally low FODMAP in normal serving sizes." }),
        ("miso", new() { Name = "Miso Paste (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "Low",
            Explanation = "Miso is fermented soybean paste — fermentation reduces GOS significantly. Generally low FODMAP in small serves." }),

        // ── EXPANDED — Beverages ────────────────────────────────────────
        ("kombucha", new() { Name = "Kombucha (Mixed FODMAP)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Kombucha may contain residual fructose and fructans from fermentation." }),
        ("chai", new() { Name = "Chai (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Chai latte is usually made with milk — moderate lactose. Chai tea bags alone are low FODMAP." }),
        ("coconut water", new() { Name = "Coconut Water (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Coconut water has moderate fructose — may trigger symptoms in large amounts (>200ml)." }),
        ("rum", new() { Name = "Rum (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Low",
            Explanation = "Rum is generally low FODMAP in standard serves. Mixers are the usual issue." }),
        ("dessert wine", new() { Name = "Dessert Wine (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Dessert wines have residual sugars including fructose." }),
        ("port", new() { Name = "Port Wine (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Port has residual fructose from fortification process." }),

        // ── EXPANDED — Dried fruits & trail mix ─────────────────────────
        ("dried mango", new() { Name = "Dried Mango (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "High",
            Explanation = "Dried mango has concentrated excess fructose." }),
        ("dried pineapple", new() { Name = "Dried Pineapple (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Dried pineapple has concentrated fructose." }),
        ("trail mix", new() { Name = "Trail Mix (Mixed FODMAP)", Category = "Polyol", SubCategory = "Mixed Polyols", Severity = "Moderate",
            Explanation = "Trail mix often contains dried fruits (sorbitol/fructose) and cashews (fructan/GOS)." }),
        ("raisin", new() { Name = "Raisins (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Raisins have moderate excess fructose — larger portions increase FODMAP load." }),
        ("sultana", new() { Name = "Sultanas (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Sultanas have moderate excess fructose similar to raisins." }),
        ("currant", new() { Name = "Currants (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Dried currants have moderate excess fructose." }),

        // ── EXPANDED — Spreads & condiments (low FODMAP options noted) ──
        ("jam", new() { Name = "Jam (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Jam contains concentrated fruit sugars. Strawberry jam is lower FODMAP than stone fruit jams." }),
        ("marmalade", new() { Name = "Marmalade (Fructose)", Category = "Monosaccharide", SubCategory = "Excess Fructose", Severity = "Moderate",
            Explanation = "Marmalade contains concentrated citrus sugars with moderate fructose." }),
    ];

    // Whole food patterns matched against product name
    static readonly (string pattern, FodmapTriggerDto info)[] WholeFood_Triggers =
    [
        ("garlic bread", new() { Name = "Garlic Bread (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Contains both wheat (fructan) and garlic (fructan) — very high FODMAP." }),
        ("onion ring", new() { Name = "Onion Rings (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Contains both wheat coating and onion — double fructan source." }),
        ("bean soup", new() { Name = "Bean Soup (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Bean-based soups are high in GOS." }),
        ("dal", new() { Name = "Dal/Dhal (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Lentil-based dal is high in GOS." }),
        ("dhal", new() { Name = "Dal/Dhal (GOS)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Lentil-based dal is high in GOS." }),
        ("falafel", new() { Name = "Falafel (GOS + Fructan)", Category = "Oligosaccharide", SubCategory = "GOS + Fructan", Severity = "High",
            Explanation = "Made from chickpeas (GOS) and onion/garlic (fructans). Very high FODMAP." }),
        ("pizza", new() { Name = "Pizza (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Pizza combines wheat crust, cheese, and often garlic/onion." }),
        ("burrito", new() { Name = "Burrito (GOS + Fructan)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Burritos contain wheat tortilla, beans, cheese, and onion." }),
        ("lasagna", new() { Name = "Lasagna (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Lasagna contains wheat pasta, cheese, cream, and garlic/onion." }),
        ("lasagne", new() { Name = "Lasagne (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Lasagne contains wheat pasta, cheese, cream, and garlic/onion." }),
        ("ramen", new() { Name = "Ramen (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Ramen has wheat noodles and broth made with garlic and onion." }),
        ("udon", new() { Name = "Udon (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Udon noodles are wheat-based, high in fructans." }),
        ("gyoza", new() { Name = "Gyoza (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Gyoza have wheat wrappers and garlic filling." }),
        ("dumpling", new() { Name = "Dumplings (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Dumplings typically have wheat wrappers." }),
        ("samosa", new() { Name = "Samosa (Fructan + GOS)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Samosas contain wheat pastry, onion, and peas." }),
        ("spring roll", new() { Name = "Spring Roll (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Spring rolls often have wheat wrappers." }),
        ("empanada", new() { Name = "Empanada (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Empanadas have wheat pastry and often onion filling." }),
        ("quiche", new() { Name = "Quiche (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Quiche has wheat crust, dairy filling, and usually onion." }),
        ("carbonara", new() { Name = "Carbonara (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Carbonara has wheat pasta, cheese, cream, and garlic." }),
        ("alfredo", new() { Name = "Alfredo (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Alfredo sauce contains cream, cheese, garlic, and wheat pasta." }),
        ("bolognese", new() { Name = "Bolognese (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Bolognese sauce is made with onion and garlic on wheat pasta." }),
        ("minestrone", new() { Name = "Minestrone (GOS + Fructan)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Minestrone contains beans, pasta, and onion." }),
        ("french onion", new() { Name = "French Onion Soup (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "French onion soup is almost entirely onion, very high fructans." }),
        ("cream of mushroom", new() { Name = "Cream of Mushroom (Mannitol + Lactose)", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Contains mushrooms (mannitol) and cream (lactose)." }),
        ("mac and cheese", new() { Name = "Mac and Cheese (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Macaroni and cheese combines wheat pasta and dairy." }),
        ("grilled cheese", new() { Name = "Grilled Cheese (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Grilled cheese has wheat bread and cheese." }),
        ("tabbouleh", new() { Name = "Tabbouleh (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Tabbouleh is made with bulgur wheat and onion." }),
        ("bruschetta", new() { Name = "Bruschetta (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Bruschetta has wheat bread with garlic." }),
        ("focaccia", new() { Name = "Focaccia (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Focaccia is wheat bread often with garlic or onion." }),
        ("sourdough", new() { Name = "Sourdough (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Sourdough fermentation reduces some fructans." }),
        ("bagel", new() { Name = "Bagel (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Bagels are dense wheat bread, high in fructans." }),
        ("pancake", new() { Name = "Pancakes (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Pancakes contain wheat flour and milk." }),
        ("waffle", new() { Name = "Waffles (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Waffles contain wheat flour and milk." }),
        ("french toast", new() { Name = "French Toast (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "French toast is wheat bread soaked in milk." }),
        ("protein shake", new() { Name = "Protein Shake (Lactose + Fructan)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Protein shakes often contain whey and inulin." }),
        ("milkshake", new() { Name = "Milkshake (Lactose)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Milkshakes combine milk and ice cream, very high lactose." }),
        ("chili con carne", new() { Name = "Chili Con Carne (GOS + Fructan)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Chili contains beans with onion and garlic." }),
        ("tikka masala", new() { Name = "Tikka Masala (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Tikka masala has cream with onion and garlic." }),
        ("butter chicken", new() { Name = "Butter Chicken (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Butter chicken has cream/butter with onion and garlic." }),
        ("korma", new() { Name = "Korma (Fructan + Lactose + GOS)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Korma contains cream, onion, garlic, and cashews." }),
        ("vindaloo", new() { Name = "Vindaloo (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Vindaloo contains onion and garlic." }),
        ("biryani", new() { Name = "Biryani (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Biryani contains onion and garlic." }),
        ("shepherd pie", new() { Name = "Shepherd Pie (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Contains onion and garlic in the filling." }),
        ("cottage pie", new() { Name = "Cottage Pie (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Contains onion and garlic in the filling." }),
        ("clam chowder", new() { Name = "Clam Chowder (Lactose + Fructan)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Clam chowder contains cream, onion, and wheat flour." }),
        ("pad thai", new() { Name = "Pad Thai (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Pad Thai sauce often contains garlic and shallots." }),
        ("fried rice", new() { Name = "Fried Rice (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Fried rice typically contains garlic and onion." }),
        ("fish and chips", new() { Name = "Fish and Chips (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Battered fish uses wheat flour." }),
        ("chicken nugget", new() { Name = "Chicken Nuggets (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Chicken nuggets have wheat breadcrumb coating." }),
        ("pot pie", new() { Name = "Pot Pie (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Pot pie has wheat crust, cream sauce, and onion." }),
        ("calzone", new() { Name = "Calzone (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Calzone has wheat dough with cheese and garlic." }),
        ("quesadilla", new() { Name = "Quesadilla (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Quesadillas have flour tortilla and cheese." }),
        ("enchilada", new() { Name = "Enchilada (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Enchiladas have tortillas, cheese, and onion sauce." }),
        ("nachos", new() { Name = "Nachos (Lactose + GOS)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "Moderate",
            Explanation = "Nachos often have cheese, beans, and onion salsa." }),
        ("risotto", new() { Name = "Risotto (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Risotto contains onion, garlic, butter, and parmesan." }),
        ("ravioli", new() { Name = "Ravioli (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Ravioli has wheat pasta with cheese filling." }),
        ("tortellini", new() { Name = "Tortellini (Fructan + Lactose)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Tortellini has wheat pasta with cheese filling." }),
        ("sausage roll", new() { Name = "Sausage Roll (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Sausage rolls have wheat pastry and onion in the filling." }),
        ("meat pie", new() { Name = "Meat Pie (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Meat pies have wheat pastry and onion/garlic in filling." }),
        ("wrap", new() { Name = "Wrap (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Wraps use flour tortilla with wheat fructans." }),
        ("sandwich", new() { Name = "Sandwich (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Sandwiches use wheat bread." }),
        ("taco", new() { Name = "Taco (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Tacos often contain onion and sometimes wheat tortilla." }),
        ("hummus", new() { Name = "Hummus (GOS + Fructan)", Category = "Oligosaccharide", SubCategory = "GOS", Severity = "High",
            Explanation = "Made from chickpeas (GOS) and garlic (fructan)." }),
        ("baba ganoush", new() { Name = "Baba Ganoush (Fructan)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "Moderate",
            Explanation = "Contains garlic and sometimes onion." }),
    ];

    // Sugar alcohol additives by E-number
    static readonly Dictionary<string, FodmapTriggerDto> FodmapAdditives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["E420"] = new()
        {
            Name = "Sorbitol (E420)",
            Category = "Polyol",
            SubCategory = "Sorbitol",
            Severity = "High",
            Explanation = "Sugar alcohol polyol — poorly absorbed, causes osmotic diarrhea, bloating, and cramps."
        },
        ["E421"] = new()
        {
            Name = "Mannitol (E421)",
            Category = "Polyol",
            SubCategory = "Mannitol",
            Severity = "High",
            Explanation = "Sugar alcohol polyol — causes significant GI distress even in small amounts."
        },
        ["E953"] = new()
        {
            Name = "Isomalt (E953)",
            Category = "Polyol",
            SubCategory = "Isomalt",
            Severity = "High",
            Explanation = "Sugar alcohol polyol — dose-dependent bloating and diarrhea."
        },
        ["E965"] = new()
        {
            Name = "Maltitol (E965)",
            Category = "Polyol",
            SubCategory = "Maltitol",
            Severity = "High",
            Explanation = "One of the worst sugar alcohols for FODMAP symptoms."
        },
        ["E966"] = new()
        {
            Name = "Lactitol (E966)",
            Category = "Polyol",
            SubCategory = "Lactitol",
            Severity = "High",
            Explanation = "Poorly absorbed polyol — significant GI distress."
        },
        ["E967"] = new()
        {
            Name = "Xylitol (E967)",
            Category = "Polyol",
            SubCategory = "Xylitol",
            Severity = "High",
            Explanation = "FODMAP polyol — symptoms common above 10g."
        },
        ["E968"] = new()
        {
            Name = "Erythritol (E968)",
            Category = "Polyol",
            SubCategory = "Erythritol",
            Severity = "Low",
            Explanation = "Best-tolerated polyol — 90% absorbed before colon. Generally low FODMAP at normal doses."
        },
    };

    static readonly (string pattern, FodmapTriggerDto info)[] AdditiveNameTriggers =
    [
        ("sorbitol", new() { Name = "Sorbitol", Category = "Polyol", SubCategory = "Sorbitol", Severity = "High",
            Explanation = "Sugar alcohol polyol — poorly absorbed, causes osmotic diarrhea." }),
        ("mannitol", new() { Name = "Mannitol", Category = "Polyol", SubCategory = "Mannitol", Severity = "High",
            Explanation = "Sugar alcohol polyol causing significant GI distress." }),
        ("maltitol", new() { Name = "Maltitol", Category = "Polyol", SubCategory = "Maltitol", Severity = "High",
            Explanation = "One of the worst sugar alcohols for FODMAP symptoms." }),
        ("xylitol", new() { Name = "Xylitol", Category = "Polyol", SubCategory = "Xylitol", Severity = "High",
            Explanation = "FODMAP polyol — dose-dependent symptoms." }),
        ("isomalt", new() { Name = "Isomalt", Category = "Polyol", SubCategory = "Isomalt", Severity = "High",
            Explanation = "Sugar alcohol causing dose-dependent GI distress." }),
        ("erythritol", new() { Name = "Erythritol", Category = "Polyol", SubCategory = "Erythritol", Severity = "Low",
            Explanation = "Best-tolerated polyol — generally low FODMAP." }),
        ("lactitol", new() { Name = "Lactitol", Category = "Polyol", SubCategory = "Lactitol", Severity = "High",
            Explanation = "Poorly absorbed polyol causing significant GI distress." }),
        ("lactose", new() { Name = "Lactose (Additive)", Category = "Disaccharide", SubCategory = "Lactose", Severity = "High",
            Explanation = "Lactose added as additive/excipient." }),
        ("inulin", new() { Name = "Inulin (Additive)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Fructan fiber added for 'fiber enrichment' — high FODMAP trigger." }),
        ("fructooligosaccharide", new() { Name = "FOS (Additive)", Category = "Oligosaccharide", SubCategory = "Fructan", Severity = "High",
            Explanation = "Fructan-type prebiotic fiber — high FODMAP." }),
    ];

    static readonly HashSet<string> GenericWholeFoodPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "protein shake", "milkshake", "smoothie", "protein bar", "cereal bar",
        "granola", "muesli", "sandwich", "wrap", "taco",
    };
}
