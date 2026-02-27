using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

public class SubstitutionService
{
    public SubstitutionResultDto GetSubstitutions(FoodProductDto product)
    {
        var suggestions = new List<SubstitutionDto>();
        var lower = (product.Ingredients ?? "").ToLowerInvariant();
        var name = (product.Name ?? "").ToLowerInvariant();

        foreach (var (pattern, subs) in IngredientSubstitutions)
        {
            if (lower.Contains(pattern) || name.Contains(pattern))
            {
                foreach (var sub in subs)
                {
                    if (!HasSuggestion(suggestions, sub))
                        suggestions.Add(sub);
                }
            }
        }

        // Check allergen tags for additional substitutions
        if (product.AllergensTags is { Length: > 0 })
        {
            foreach (var tag in product.AllergensTags)
            {
                var tagLower = tag.ToLowerInvariant().Replace("en:", "");
                if (AllergenSubstitutions.TryGetValue(tagLower, out var subs))
                {
                    foreach (var sub in subs)
                    {
                        if (!HasSuggestion(suggestions, sub))
                            suggestions.Add(sub);
                    }
                }
            }
        }

        // Check NOVA group — suggest less processed alternatives
        if (product.NovaGroup >= 4)
        {
            var novaSub = new SubstitutionDto
            {
                Original = "Ultra-processed product (NOVA 4)",
                Substitute = "Homemade or minimally processed version",
                Reason = "NOVA 4 ultra-processed foods contain industrial additives that some research has associated with gut inflammation and microbiome changes.",
                Category = "Processing",
                GutBenefit = "Reduced additive exposure, better microbiome diversity",
                Confidence = "High",
            };
            if (!HasSuggestion(suggestions, novaSub))
                suggestions.Add(novaSub);
        }

        // High sodium suggestion
        if (product.Sodium100g > 1.0m)
        {
            var sodiumSub = new SubstitutionDto
            {
                Original = "High sodium content",
                Substitute = "Low-sodium version or season with herbs/spices instead",
                Reason = $"This product has {product.Sodium100g:F1}g sodium per 100g. High sodium intake has been associated with bloating and water retention in some individuals.",
                Category = "Sodium",
                GutBenefit = "Reduced bloating and water retention",
                Confidence = "High",
            };
            if (!HasSuggestion(suggestions, sodiumSub))
                suggestions.Add(sodiumSub);
        }

        // High sugar suggestion
        if (product.Sugar100g > 20m)
        {
            var sugarSub = new SubstitutionDto
            {
                Original = "High sugar content",
                Substitute = "Low-sugar or naturally sweetened alternative",
                Reason = $"This product has {product.Sugar100g:F0}g sugar per 100g. Excess sugar has been associated with changes in gut bacteria composition and may contribute to fermentation in some people.",
                Category = "Sugar",
                GutBenefit = "Less fermentation, reduced gas and bloating",
                Confidence = "High",
            };
            if (!HasSuggestion(suggestions, sugarSub))
                suggestions.Add(sugarSub);
        }

        return new SubstitutionResultDto
        {
            ProductName = product.Name,
            SuggestionCount = suggestions.Count,
            Suggestions = suggestions,
            Summary = suggestions.Count == 0
                ? "No common gut-related ingredient concerns identified in this product."
                : $"Found {suggestions.Count} gut-friendly substitution(s) to consider.",
        };
    }

    public SubstitutionResultDto GetSubstitutionsForText(string text)
    {
        var suggestions = new List<SubstitutionDto>();
        var lower = text.ToLowerInvariant();

        foreach (var (pattern, subs) in IngredientSubstitutions)
        {
            if (lower.Contains(pattern))
            {
                foreach (var sub in subs)
                {
                    if (!HasSuggestion(suggestions, sub))
                        suggestions.Add(sub);
                }
            }
        }

        return new SubstitutionResultDto
        {
            ProductName = text,
            SuggestionCount = suggestions.Count,
            Suggestions = suggestions,
            Summary = suggestions.Count == 0
                ? "No gut-specific substitutions identified from the text."
                : $"Found {suggestions.Count} gut-friendly substitution(s) to consider.",
        };
    }

    static bool HasSuggestion(List<SubstitutionDto> list, SubstitutionDto sub)
        => list.Any(s => s.Original.Equals(sub.Original, StringComparison.OrdinalIgnoreCase)
            && s.Substitute.Equals(sub.Substitute, StringComparison.OrdinalIgnoreCase));

    // ─── Ingredient-based substitution database ────────────────────────

    static readonly (string Pattern, SubstitutionDto[] Subs)[] IngredientSubstitutions =
    [
        // ── Dairy / Lactose ──
        ("milk", [
            new() { Original = "Milk", Substitute = "Oat milk or lactose-free milk", Reason = "Lactose in regular milk is a known FODMAP that may cause gas, bloating, or diarrhea in lactose-sensitive individuals.", Category = "Dairy", GutBenefit = "Eliminates lactose fermentation", Confidence = "High" },
        ]),
        ("cream cheese", [
            new() { Original = "Cream cheese", Substitute = "Cashew cream cheese or lactose-free cream cheese", Reason = "Cream cheese contains lactose. Plant-based or lactose-free versions eliminate this FODMAP trigger.", Category = "Dairy", GutBenefit = "Eliminates lactose trigger", Confidence = "High" },
        ]),
        ("cream", [
            new() { Original = "Cream", Substitute = "Coconut cream or oat cream", Reason = "Heavy cream contains lactose. Coconut cream is naturally lactose-free.", Category = "Dairy", GutBenefit = "Eliminates lactose, adds MCTs", Confidence = "High" },
        ]),
        ("butter", [
            new() { Original = "Butter", Substitute = "Ghee (clarified butter) or olive oil", Reason = "Butter contains trace lactose and casein. Ghee has these largely removed during clarification. Olive oil is rich in oleic acid.", Category = "Dairy", GutBenefit = "Removes lactose/casein", Confidence = "Medium" },
        ]),
        ("cheese", [
            new() { Original = "Cheese", Substitute = "Aged cheese (Parmesan, aged Cheddar) or nutritional yeast", Reason = "Fresh cheeses are higher in lactose. Aged cheeses have most lactose consumed by bacteria during aging.", Category = "Dairy", GutBenefit = "Much lower lactose content", Confidence = "Medium" },
        ]),
        ("yogurt", [
            new() { Original = "Yogurt", Substitute = "Coconut yogurt or lactose-free yogurt", Reason = "Regular yogurt contains lactose, though less than milk due to fermentation. Coconut yogurt is fully lactose-free.", Category = "Dairy", GutBenefit = "Eliminates lactose while keeping probiotics", Confidence = "Medium" },
        ]),
        ("whey", [
            new() { Original = "Whey protein", Substitute = "Pea protein or rice protein isolate", Reason = "Whey contains lactose and can cause bloating. Plant proteins are lactose-free and often easier to digest.", Category = "Dairy", GutBenefit = "Eliminates lactose, often better tolerated", Confidence = "High" },
        ]),
        ("casein", [
            new() { Original = "Casein", Substitute = "Pea protein or hemp protein", Reason = "Casein is slow-digesting and can cause bloating in sensitive individuals. Plant alternatives are easier on the gut.", Category = "Dairy", GutBenefit = "Easier digestion, no dairy sensitivity", Confidence = "Medium" },
        ]),

        // ── Gluten / Wheat ──
        ("wheat flour", [
            new() { Original = "Wheat flour", Substitute = "Rice flour, oat flour, or sourdough wheat", Reason = "Wheat contains fructans (a FODMAP) and gluten. Sourdough fermentation reduces fructan content by ~90%.", Category = "Gluten/Wheat", GutBenefit = "Reduced fructans, easier digestion", Confidence = "High" },
        ]),
        ("wheat starch", [
            new() { Original = "Wheat starch", Substitute = "Tapioca starch or potato starch", Reason = "Wheat starch may contain residual fructans. Tapioca and potato starch are naturally FODMAP-free.", Category = "Gluten/Wheat", GutBenefit = "Eliminates fructan exposure", Confidence = "High" },
        ]),
        ("wheat", [
            new() { Original = "Wheat", Substitute = "Rice, quinoa, or sourdough wheat", Reason = "Wheat is high in fructans (a FODMAP). Sourdough reduces fructans by ~90%; rice and quinoa are naturally FODMAP-free.", Category = "Gluten/Wheat", GutBenefit = "Reduced fructans", Confidence = "High" },
        ]),
        ("gluten", [
            new() { Original = "Gluten", Substitute = "Gluten-free alternative (rice, corn, buckwheat)", Reason = "Gluten may cause discomfort in some sensitive individuals. Research on its broader effects is ongoing.", Category = "Gluten/Wheat", GutBenefit = "Reduced intestinal inflammation", Confidence = "Medium" },
        ]),
        ("barley", [
            new() { Original = "Barley", Substitute = "Brown rice or quinoa", Reason = "Barley contains both gluten and fructans. Rice and quinoa are free of both.", Category = "Gluten/Wheat", GutBenefit = "Eliminates fructans and gluten", Confidence = "High" },
        ]),
        ("rye", [
            new() { Original = "Rye", Substitute = "Sourdough rye (reduced FODMAP) or rice bread", Reason = "Rye is very high in fructans. Sourdough fermentation significantly reduces fructan content.", Category = "Gluten/Wheat", GutBenefit = "Reduced fructans through fermentation", Confidence = "High" },
        ]),

        // ── FODMAP Triggers ──
        ("garlic", [
            new() { Original = "Garlic", Substitute = "Garlic-infused oil or asafoetida (hing)", Reason = "Garlic is high in fructans (a FODMAP category). Since fructans are water-soluble but not oil-soluble, garlic-infused oil may provide flavor with a lower FODMAP load.", Category = "FODMAP", GutBenefit = "Garlic flavor without fructans", Confidence = "High" },
        ]),
        ("onion", [
            new() { Original = "Onion", Substitute = "Green part of spring onions (scallions) or chives", Reason = "Onion bulbs are very high in fructans. The green tops of spring onions are low-FODMAP and provide similar flavor.", Category = "FODMAP", GutBenefit = "Similar flavor, minimal fructans", Confidence = "High" },
        ]),
        ("honey", [
            new() { Original = "Honey", Substitute = "Maple syrup or rice malt syrup", Reason = "Honey has ~40% fructose vs ~30% glucose — significant excess fructose. Maple syrup has balanced fructose:glucose ratio.", Category = "FODMAP", GutBenefit = "Balanced sugar ratio, less malabsorption", Confidence = "High" },
        ]),
        ("high fructose corn syrup", [
            new() { Original = "High fructose corn syrup", Substitute = "Glucose syrup or cane sugar", Reason = "HFCS has excess fructose that may contribute to malabsorption and digestive discomfort in some individuals.", Category = "FODMAP", GutBenefit = "Balanced fructose:glucose, less fermentation", Confidence = "High" },
        ]),
        ("hfcs", [
            new() { Original = "HFCS", Substitute = "Glucose syrup or cane sugar", Reason = "HFCS has excess fructose that causes malabsorption and fermentation.", Category = "FODMAP", GutBenefit = "Balanced fructose:glucose ratio", Confidence = "High" },
        ]),
        ("agave", [
            new() { Original = "Agave syrup", Substitute = "Maple syrup or stevia", Reason = "Agave is ~90% fructose — the highest fructose sweetener available. Major FODMAP trigger.", Category = "FODMAP", GutBenefit = "Dramatically reduced fructose load", Confidence = "High" },
        ]),
        ("inulin", [
            new() { Original = "Inulin", Substitute = "Psyllium husk or partially hydrolyzed guar gum (PHGG)", Reason = "Inulin is a fructan fiber that rapidly ferments in the colon. PHGG provides similar prebiotic benefits with less gas.", Category = "FODMAP", GutBenefit = "Prebiotic fiber without rapid fermentation", Confidence = "High" },
        ]),
        ("chicory", [
            new() { Original = "Chicory root fiber", Substitute = "Psyllium husk or acacia fiber", Reason = "Chicory root is the primary source of inulin — extremely high in fructans. Acacia fiber ferments slowly and is better tolerated.", Category = "FODMAP", GutBenefit = "Slow fermentation, less gas and bloating", Confidence = "High" },
        ]),
        ("fructooligosaccharide", [
            new() { Original = "FOS (fructooligosaccharides)", Substitute = "Partially hydrolyzed guar gum (PHGG)", Reason = "FOS are short-chain fructans that ferment rapidly in the colon causing gas and bloating.", Category = "FODMAP", GutBenefit = "Prebiotic effect without rapid fermentation", Confidence = "High" },
        ]),

        // ── Sugar Alcohols / Polyols ──
        ("sorbitol", [
            new() { Original = "Sorbitol (E420)", Substitute = "Erythritol or stevia", Reason = "Sorbitol is poorly absorbed and ferments in the colon. Erythritol is 90% absorbed before reaching the colon.", Category = "Polyol", GutBenefit = "Minimal colonic fermentation", Confidence = "High" },
        ]),
        ("maltitol", [
            new() { Original = "Maltitol (E965)", Substitute = "Erythritol or monk fruit", Reason = "Maltitol is only 40% absorbed — the worst-tolerated polyol. Known for causing severe osmotic diarrhea.", Category = "Polyol", GutBenefit = "Eliminates osmotic diarrhea risk", Confidence = "High" },
        ]),
        ("xylitol", [
            new() { Original = "Xylitol (E967)", Substitute = "Erythritol or stevia", Reason = "Xylitol causes dose-dependent GI symptoms in most people above 20g. Erythritol is better tolerated.", Category = "Polyol", GutBenefit = "Reduced GI symptoms", Confidence = "High" },
        ]),
        ("mannitol", [
            new() { Original = "Mannitol (E421)", Substitute = "Erythritol", Reason = "Mannitol is poorly absorbed and a known FODMAP trigger. Erythritol is the best-tolerated sugar alcohol.", Category = "Polyol", GutBenefit = "Better absorption, less fermentation", Confidence = "High" },
        ]),
        ("isomalt", [
            new() { Original = "Isomalt (E953)", Substitute = "Erythritol or stevia", Reason = "Isomalt is only 10% absorbed — even worse than maltitol for GI tolerance.", Category = "Polyol", GutBenefit = "Dramatically better absorption", Confidence = "High" },
        ]),

        // ── Artificial Sweeteners ──
        ("sucralose", [
            new() { Original = "Sucralose", Substitute = "Stevia or monk fruit extract", Reason = "Some research suggests sucralose may influence gut microbiome composition, though findings are still being studied.", Category = "Sweetener", GutBenefit = "No microbiome disruption", Confidence = "Medium" },
        ]),
        ("aspartame", [
            new() { Original = "Aspartame", Substitute = "Stevia or monk fruit extract", Reason = "Some studies have explored whether aspartame may influence gut bacteria, though results are mixed and ongoing.", Category = "Sweetener", GutBenefit = "Natural sweetener, no microbiome concern", Confidence = "Medium" },
        ]),
        ("acesulfame", [
            new() { Original = "Acesulfame K", Substitute = "Stevia or monk fruit extract", Reason = "Some animal studies have explored potential effects of Acesulfame-K on gut bacteria, though human evidence is limited.", Category = "Sweetener", GutBenefit = "No artificial sweetener exposure", Confidence = "Low" },
        ]),

        // ── Emulsifiers & Thickeners ──
        ("carrageenan", [
            new() { Original = "Carrageenan (E407)", Substitute = "Gellan gum or agar agar", Reason = "Some research has explored whether carrageenan may contribute to gut discomfort in sensitive individuals.", Category = "Additive", GutBenefit = "Reduced intestinal inflammation risk", Confidence = "Medium" },
        ]),
        ("polysorbate", [
            new() { Original = "Polysorbate 80 (E433)", Substitute = "Sunflower lecithin or no emulsifier", Reason = "Some research suggests Polysorbate 80 may affect the gut's mucus layer, though most evidence comes from laboratory studies.", Category = "Additive", GutBenefit = "Preserved mucus barrier integrity", Confidence = "Medium" },
        ]),
        ("carboxymethyl", [
            new() { Original = "CMC / Carboxymethylcellulose (E466)", Substitute = "Guar gum or xanthan gum (small amounts)", Reason = "Some studies have explored whether CMC may influence gut microbiome composition and intestinal comfort.", Category = "Additive", GutBenefit = "Less microbiome disruption", Confidence = "Medium" },
        ]),
        ("sodium benzoate", [
            new() { Original = "Sodium benzoate (E211)", Substitute = "Natural preservation (vitamin E tocopherols, rosemary extract)", Reason = "Sodium benzoate may affect gut bacteria and can form benzene when combined with vitamin C.", Category = "Preservative", GutBenefit = "No antimicrobial effect on gut flora", Confidence = "Medium" },
        ]),
        ("potassium sorbate", [
            new() { Original = "Potassium sorbate (E202)", Substitute = "Natural preservation methods", Reason = "Sorbates can inhibit beneficial gut bacteria at high concentrations.", Category = "Preservative", GutBenefit = "Preserved beneficial bacteria", Confidence = "Low" },
        ]),

        // ── Fiber & Digestion ──
        ("soy protein", [
            new() { Original = "Soy protein", Substitute = "Pea protein or hemp protein", Reason = "Soy contains GOS (galacto-oligosaccharides), a FODMAP. Pea protein is lower in FODMAPs.", Category = "FODMAP", GutBenefit = "Lower GOS content, less gas", Confidence = "Medium" },
        ]),
        ("soy", [
            new() { Original = "Soy / soybean", Substitute = "Firm tofu (drained) or tempeh", Reason = "Whole soy is high in GOS. Firm tofu has most GOS drained out; tempeh's fermentation reduces GOS.", Category = "FODMAP", GutBenefit = "Reduced GOS through processing/fermentation", Confidence = "Medium" },
        ]),

        // ── Cooking Oils ──
        ("palm oil", [
            new() { Original = "Palm oil", Substitute = "Olive oil or avocado oil", Reason = "Palm oil is high in saturated fat which can increase bile acid secretion and irritate the gut.", Category = "Fat", GutBenefit = "Anti-inflammatory monounsaturated fats", Confidence = "Medium" },
        ]),
        ("sunflower oil", [
            new() { Original = "Sunflower oil", Substitute = "Extra virgin olive oil", Reason = "Sunflower oil is high in omega-6 which promotes inflammation. Olive oil is anti-inflammatory.", Category = "Fat", GutBenefit = "Anti-inflammatory oleic acid + polyphenols", Confidence = "Medium" },
        ]),
        ("vegetable oil", [
            new() { Original = "Vegetable oil", Substitute = "Extra virgin olive oil or avocado oil", Reason = "Generic vegetable oils are typically high in pro-inflammatory omega-6 fatty acids.", Category = "Fat", GutBenefit = "Better omega-3:omega-6 ratio, anti-inflammatory", Confidence = "Medium" },
        ]),
        ("canola oil", [
            new() { Original = "Canola oil", Substitute = "Extra virgin olive oil", Reason = "While canola is decent, EVOO provides superior polyphenols that feed beneficial gut bacteria.", Category = "Fat", GutBenefit = "Polyphenols support microbiome diversity", Confidence = "Low" },
        ]),

        // ── Common Irritants ──
        ("caffeine", [
            new() { Original = "Caffeine", Substitute = "Decaf version or herbal tea", Reason = "Caffeine may stimulate gastric acid and gut motility, which some people find contributes to digestive discomfort.", Category = "Stimulant", GutBenefit = "Reduced acid reflux and motility issues", Confidence = "Medium" },
        ]),
        ("alcohol", [
            new() { Original = "Alcohol", Substitute = "Non-alcoholic version or kombucha", Reason = "Alcohol consumption has been associated with changes in gut barrier function, microbiome composition, and stomach comfort.", Category = "Irritant", GutBenefit = "Preserved gut barrier, healthy microbiome", Confidence = "High" },
        ]),
    ];

    // ── Allergen-tag based substitutions ────────────────────────────────

    static readonly Dictionary<string, SubstitutionDto[]> AllergenSubstitutions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gluten"] = [
            new() { Original = "Gluten-containing ingredients", Substitute = "Gluten-free grains (rice, quinoa, buckwheat, millet)", Reason = "Gluten may cause discomfort in some sensitive individuals. Research on its broader effects is ongoing.", Category = "Gluten/Wheat", GutBenefit = "Reduced intestinal inflammation", Confidence = "High" },
        ],
        ["milk"] = [
            new() { Original = "Dairy ingredients", Substitute = "Plant-based dairy alternatives (oat, coconut, almond)", Reason = "Dairy contains lactose (a FODMAP) and casein, which some people find may contribute to digestive symptoms.", Category = "Dairy", GutBenefit = "Eliminates lactose and casein triggers", Confidence = "High" },
        ],
        ["soybeans"] = [
            new() { Original = "Soy ingredients", Substitute = "Firm tofu (drained) or alternative legumes", Reason = "Soy contains GOS (galacto-oligosaccharides). Firm tofu has most GOS removed during processing.", Category = "FODMAP", GutBenefit = "Reduced GOS exposure", Confidence = "Medium" },
        ],
    };
}
