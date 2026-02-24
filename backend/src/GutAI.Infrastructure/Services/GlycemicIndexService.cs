using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

public class GlycemicIndexService
{
    public GlycemicAssessmentDto Assess(FoodProductDto product)
    {
        var matches = new List<GlycemicMatchDto>();
        var lower = (product.Ingredients ?? "").ToLowerInvariant();
        var name = (product.Name ?? "").ToLowerInvariant();
        var matchedPatterns = new List<string>();

        // Match ingredients against GI database (sorted longest-first for specificity)
        foreach (var (pattern, entry) in GiDatabase.OrderByDescending(x => x.Pattern.Length))
        {
            if (lower.Contains(pattern) || name.Contains(pattern))
            {
                // Skip if a more specific pattern already covers this one
                if (matchedPatterns.Any(mp => mp.Contains(pattern)))
                    continue;

                if (!matches.Any(m => m.Food.Equals(entry.Food, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(entry);
                    matchedPatterns.Add(pattern);
                }
            }
        }

        // If no ingredient matches, try product-level estimation
        if (matches.Count == 0)
        {
            var estimated = EstimateFromNutrition(product);
            if (estimated is not null)
                matches.Add(estimated);
        }

        if (matches.Count == 0)
        {
            return new GlycemicAssessmentDto
            {
                EstimatedGI = null,
                GiCategory = "Unknown",
                EstimatedGL = null,
                GlCategory = "Unknown",
                MatchCount = 0,
                Matches = [],
                GutImpactSummary = "Insufficient data to estimate glycemic impact.",
                Recommendations = [],
            };
        }

        // Weighted average GI based on matched ingredients
        var avgGI = (int)Math.Round(matches.Average(m => m.GI));

        // Estimate GL using product carbs if available
        decimal? estimatedGL = null;
        if (product.Carbs100g is > 0)
        {
            // GL = (GI × available carbs per serving) / 100
            // Use 100g as reference serving
            estimatedGL = Math.Round(avgGI * product.Carbs100g.Value / 100m, 1);
        }

        var giCategory = ClassifyGI(avgGI);
        var glCategory = estimatedGL.HasValue ? ClassifyGL(estimatedGL.Value) : "Unknown";

        var recommendations = new List<string>();
        if (avgGI >= 70)
        {
            recommendations.Add("Pair with protein or healthy fat to slow glucose absorption.");
            recommendations.Add("Consider a lower-GI alternative to reduce blood sugar spikes.");
        }
        else if (avgGI >= 56)
        {
            recommendations.Add("Moderate GI — pair with fiber-rich foods for better blood sugar control.");
        }

        if (estimatedGL >= 20)
        {
            recommendations.Add("High glycemic load — watch portion size to limit blood sugar impact.");
        }

        if (product.Fiber100g is > 3)
        {
            recommendations.Add("Good fiber content helps slow glucose absorption and feeds beneficial gut bacteria.");
        }
        else if (product.Fiber100g is < 1 && product.Carbs100g is > 30)
        {
            recommendations.Add("Low fiber with high carbs — consider adding a fiber source to this meal.");
        }

        return new GlycemicAssessmentDto
        {
            EstimatedGI = avgGI,
            GiCategory = giCategory,
            EstimatedGL = estimatedGL,
            GlCategory = glCategory,
            MatchCount = matches.Count,
            Matches = matches,
            GutImpactSummary = BuildGutImpactSummary(avgGI, estimatedGL, product),
            Recommendations = recommendations,
        };
    }

    public GlycemicAssessmentDto AssessText(string text)
    {
        var matches = new List<GlycemicMatchDto>();
        var lower = text.ToLowerInvariant();
        var matchedPatterns = new List<string>();

        foreach (var (pattern, entry) in GiDatabase.OrderByDescending(x => x.Pattern.Length))
        {
            if (lower.Contains(pattern))
            {
                if (matchedPatterns.Any(mp => mp.Contains(pattern)))
                    continue;

                if (!matches.Any(m => m.Food.Equals(entry.Food, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(entry);
                    matchedPatterns.Add(pattern);
                }
            }
        }

        if (matches.Count == 0)
            return new GlycemicAssessmentDto
            {
                EstimatedGI = null,
                GiCategory = "Unknown",
                EstimatedGL = null,
                GlCategory = "Unknown",
                MatchCount = 0,
                Matches = [],
                GutImpactSummary = "No foods with known glycemic index found in the text.",
                Recommendations = [],
            };

        var avgGI = (int)Math.Round(matches.Average(m => m.GI));

        return new GlycemicAssessmentDto
        {
            EstimatedGI = avgGI,
            GiCategory = ClassifyGI(avgGI),
            EstimatedGL = null,
            GlCategory = "Unknown",
            MatchCount = matches.Count,
            Matches = matches,
            GutImpactSummary = BuildGutImpactSummary(avgGI, null, null),
            Recommendations = avgGI >= 70
                ? ["Pair with protein or healthy fat to slow glucose absorption."]
                : [],
        };
    }

    static string ClassifyGI(int gi) => gi switch
    {
        <= 55 => "Low",
        <= 69 => "Medium",
        _ => "High",
    };

    static string ClassifyGL(decimal gl) => gl switch
    {
        <= 10 => "Low",
        <= 19 => "Medium",
        _ => "High",
    };

    static string BuildGutImpactSummary(int gi, decimal? gl, FoodProductDto? product)
    {
        var parts = new List<string>();

        if (gi >= 70)
            parts.Add("High GI foods cause rapid blood sugar spikes which can trigger insulin surges, promote inflammation, and feed pathogenic gut bacteria.");
        else if (gi >= 56)
            parts.Add("Medium GI — moderate blood sugar impact. Generally acceptable for gut health when eaten with fiber or protein.");
        else
            parts.Add("Low GI — slow glucose release supports stable blood sugar, reduces inflammation, and promotes healthy gut microbiome diversity.");

        if (gl is >= 20)
            parts.Add("The high glycemic load means this portion delivers a significant glucose hit — consider smaller portions or pairing with fat/protein.");

        if (product?.Fiber100g is > 5)
            parts.Add("High fiber content provides prebiotic benefits and helps moderate the glycemic response.");

        return string.Join(" ", parts);
    }

    static GlycemicMatchDto? EstimateFromNutrition(FoodProductDto product)
    {
        // Rough estimation when no specific food match found
        if (product.Carbs100g is null or 0) return null;

        var carbs = product.Carbs100g.Value;
        var fiber = product.Fiber100g ?? 0;
        var sugar = product.Sugar100g ?? 0;
        var protein = product.Protein100g ?? 0;
        var fat = product.Fat100g ?? 0;

        // Higher sugar ratio → higher GI estimate
        // Higher fiber, protein, fat → lower GI estimate
        var sugarRatio = carbs > 0 ? sugar / carbs : 0;
        var baseGI = 55m; // Start at medium

        // Adjust up for high sugar
        if (sugarRatio > 0.6m) baseGI += 15;
        else if (sugarRatio > 0.3m) baseGI += 8;

        // Adjust down for fiber
        if (fiber > 5) baseGI -= 12;
        else if (fiber > 2) baseGI -= 6;

        // Adjust down for protein and fat (slow gastric emptying)
        if (protein > 10) baseGI -= 8;
        else if (protein > 5) baseGI -= 4;

        if (fat > 10) baseGI -= 6;
        else if (fat > 5) baseGI -= 3;

        var gi = (int)Math.Clamp(baseGI, 20, 95);

        return new GlycemicMatchDto
        {
            Food = product.Name,
            GI = gi,
            GiCategory = ClassifyGI(gi),
            Source = "Estimated",
            Notes = "Estimated from nutritional composition. Actual GI may vary.",
        };
    }

    // ─── Glycemic Index Database ───────────────────────────────────────
    // Based on International Tables of Glycemic Index (University of Sydney)
    // GI values use glucose = 100 as reference

    static readonly (string Pattern, GlycemicMatchDto Entry)[] GiDatabase =
    [
        // ── Breads & Bakery ──
        ("white bread", new() { Food = "White bread", GI = 75, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Refined wheat, rapid digestion" }),
        ("whole wheat bread", new() { Food = "Whole wheat bread", GI = 74, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Despite whole grain, still high GI" }),
        ("wholemeal bread", new() { Food = "Wholemeal bread", GI = 74, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Similar to whole wheat" }),
        ("sourdough", new() { Food = "Sourdough bread", GI = 54, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Fermentation lowers GI + reduces fructans" }),
        ("rye bread", new() { Food = "Rye bread", GI = 58, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Dense structure slows digestion" }),
        ("pumpernickel", new() { Food = "Pumpernickel", GI = 46, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Very dense, slow release" }),
        ("multigrain", new() { Food = "Multigrain bread", GI = 62, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "" }),
        ("pita bread", new() { Food = "Pita bread", GI = 68, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "" }),
        ("bagel", new() { Food = "Bagel", GI = 72, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Dense refined wheat" }),
        ("croissant", new() { Food = "Croissant", GI = 67, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Fat content lowers GI somewhat" }),

        // ── Rice & Grains ──
        ("white rice", new() { Food = "White rice", GI = 73, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Rapid starch digestion" }),
        ("brown rice", new() { Food = "Brown rice", GI = 68, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Fiber slows digestion slightly" }),
        ("basmati", new() { Food = "Basmati rice", GI = 58, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Amylose structure resists digestion" }),
        ("jasmine rice", new() { Food = "Jasmine rice", GI = 89, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Very high amylopectin content" }),
        ("quinoa", new() { Food = "Quinoa", GI = 53, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "High protein + fiber" }),
        ("oats", new() { Food = "Oats (rolled)", GI = 55, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Beta-glucan slows digestion" }),
        ("oatmeal", new() { Food = "Oatmeal", GI = 55, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Soluble fiber rich" }),
        ("instant oat", new() { Food = "Instant oats", GI = 79, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Processing increases GI significantly" }),
        ("couscous", new() { Food = "Couscous", GI = 65, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Essentially tiny pasta" }),
        ("bulgur", new() { Food = "Bulgur wheat", GI = 48, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Parboiled, retains structure" }),
        ("millet", new() { Food = "Millet", GI = 71, GiCategory = "High", Source = "Sydney GI Tables", Notes = "" }),
        ("buckwheat", new() { Food = "Buckwheat", GI = 49, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Not actually wheat, gluten-free" }),
        ("corn flake", new() { Food = "Corn flakes", GI = 81, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Highly processed" }),

        // ── Pasta ──
        ("spaghetti", new() { Food = "Spaghetti (white)", GI = 49, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Compact structure slows digestion" }),
        ("pasta", new() { Food = "Pasta (white)", GI = 49, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Dense gluten matrix" }),
        ("noodle", new() { Food = "Noodles", GI = 47, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Egg and wheat noodles" }),
        ("rice noodle", new() { Food = "Rice noodles", GI = 53, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("macaroni", new() { Food = "Macaroni", GI = 47, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),

        // ── Potatoes & Tubers ──
        ("baked potato", new() { Food = "Baked potato", GI = 85, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Gelatinized starch, very high" }),
        ("boiled potato", new() { Food = "Boiled potato", GI = 78, GiCategory = "High", Source = "Sydney GI Tables", Notes = "High but less than baked" }),
        ("mashed potato", new() { Food = "Mashed potato", GI = 87, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Processing increases surface area" }),
        ("french fries", new() { Food = "French fries", GI = 63, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Fat lowers GI vs plain potato" }),
        ("sweet potato", new() { Food = "Sweet potato", GI = 63, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Lower than white potato" }),
        ("potato", new() { Food = "Potato", GI = 78, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Varies by preparation" }),
        ("yam", new() { Food = "Yam", GI = 37, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "True yam, not sweet potato" }),
        ("taro", new() { Food = "Taro", GI = 53, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),

        // ── Fruits ──
        ("apple", new() { Food = "Apple", GI = 36, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Fructose + fiber" }),
        ("banana", new() { Food = "Banana", GI = 51, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Ripe banana has higher GI" }),
        ("orange", new() { Food = "Orange", GI = 43, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("grape", new() { Food = "Grapes", GI = 46, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("mango", new() { Food = "Mango", GI = 51, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("pineapple", new() { Food = "Pineapple", GI = 59, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "" }),
        ("watermelon", new() { Food = "Watermelon", GI = 76, GiCategory = "High", Source = "Sydney GI Tables", Notes = "High GI but low GL due to low carb density" }),
        ("strawberr", new() { Food = "Strawberry", GI = 40, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("blueberr", new() { Food = "Blueberry", GI = 53, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("cherry", new() { Food = "Cherry", GI = 22, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Very low GI" }),
        ("pear", new() { Food = "Pear", GI = 38, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("peach", new() { Food = "Peach", GI = 42, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("plum", new() { Food = "Plum", GI = 39, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("kiwi", new() { Food = "Kiwi", GI = 53, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("date", new() { Food = "Dates", GI = 42, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Low GI but very high GL per portion" }),
        ("raisin", new() { Food = "Raisins", GI = 64, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Concentrated sugar" }),

        // ── Fruit Juices ──
        ("apple juice", new() { Food = "Apple juice", GI = 41, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "No fiber to slow absorption" }),
        ("orange juice", new() { Food = "Orange juice", GI = 50, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),

        // ── Legumes ──
        ("lentil", new() { Food = "Lentils", GI = 32, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Excellent low-GI protein source" }),
        ("chickpea", new() { Food = "Chickpeas", GI = 28, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Very low GI" }),
        ("kidney bean", new() { Food = "Kidney beans", GI = 24, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("black bean", new() { Food = "Black beans", GI = 30, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("baked bean", new() { Food = "Baked beans", GI = 48, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Sauce adds some sugar" }),

        // ── Sugars & Sweeteners ──
        ("glucose", new() { Food = "Glucose", GI = 100, GiCategory = "High", Source = "Reference", Notes = "Reference standard" }),
        ("sucrose", new() { Food = "Sucrose (table sugar)", GI = 65, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "50% glucose, 50% fructose" }),
        ("fructose", new() { Food = "Fructose", GI = 15, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Low GI but FODMAP concern" }),
        ("honey", new() { Food = "Honey", GI = 61, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Varies by type" }),
        ("maple syrup", new() { Food = "Maple syrup", GI = 54, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("agave", new() { Food = "Agave syrup", GI = 15, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Low GI but very high fructose (FODMAP)" }),

        // ── Dairy & Alternatives ──
        ("milk", new() { Food = "Whole milk", GI = 27, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Fat + protein lower GI" }),
        ("skim milk", new() { Food = "Skim milk", GI = 32, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "" }),
        ("yogurt", new() { Food = "Yogurt (plain)", GI = 36, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Fermentation lowers GI" }),
        ("ice cream", new() { Food = "Ice cream", GI = 51, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Fat content lowers GI" }),

        // ── Snacks & Processed ──
        ("chocolate", new() { Food = "Chocolate", GI = 40, GiCategory = "Low", Source = "Sydney GI Tables", Notes = "Fat lowers GI; dark better than milk" }),
        ("popcorn", new() { Food = "Popcorn", GI = 65, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "" }),
        ("rice cake", new() { Food = "Rice cakes", GI = 82, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Puffed rice, very rapid digestion" }),
        ("pretzel", new() { Food = "Pretzels", GI = 83, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Refined wheat" }),
        ("cracker", new() { Food = "Crackers", GI = 74, GiCategory = "High", Source = "Sydney GI Tables", Notes = "" }),
        ("donut", new() { Food = "Donut", GI = 76, GiCategory = "High", Source = "Sydney GI Tables", Notes = "" }),
        ("doughnut", new() { Food = "Doughnut", GI = 76, GiCategory = "High", Source = "Sydney GI Tables", Notes = "" }),
        ("cake", new() { Food = "Cake", GI = 67, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Fat and eggs moderate GI" }),
        ("muffin", new() { Food = "Muffin", GI = 69, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "" }),
        ("cookie", new() { Food = "Cookie", GI = 62, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Fat content lowers GI" }),

        // ── Beverages ──
        ("coca cola", new() { Food = "Coca-Cola", GI = 63, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "HFCS-sweetened" }),
        ("cola", new() { Food = "Cola", GI = 63, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "" }),
        ("sports drink", new() { Food = "Sports drink", GI = 78, GiCategory = "High", Source = "Sydney GI Tables", Notes = "Designed for rapid absorption" }),
        ("energy drink", new() { Food = "Energy drink", GI = 70, GiCategory = "High", Source = "Sydney GI Tables", Notes = "" }),

        // ── Breakfast Cereals ──
        ("muesli", new() { Food = "Muesli", GI = 57, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Untoasted, with nuts" }),
        ("granola", new() { Food = "Granola", GI = 56, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "Nuts and fat lower GI" }),
        ("bran flake", new() { Food = "Bran flakes", GI = 74, GiCategory = "High", Source = "Sydney GI Tables", Notes = "" }),
        ("weetabix", new() { Food = "Weetabix", GI = 69, GiCategory = "Medium", Source = "Sydney GI Tables", Notes = "" }),
        ("cheerios", new() { Food = "Cheerios", GI = 74, GiCategory = "High", Source = "Sydney GI Tables", Notes = "" }),
    ];
}
