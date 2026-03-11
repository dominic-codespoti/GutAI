using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// All food scoring helpers: index-time static quality, post-Lucene re-ranking,
/// and shared text utilities (primary noun extraction, normalization, depluralization).
/// </summary>
internal static class FoodScoring
{
    // ════════════════════════════════════════════════════════════════
    //  TEXT HELPERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// USDA convention: "PrimaryNoun, descriptor, descriptor" → returns "PrimaryNoun".
    /// </summary>
    public static string ExtractPrimaryNoun(string name)
    {
        var commaIdx = name.IndexOf(',');
        if (commaIdx > 0)
            return name[..commaIdx].Trim();
        return name.Trim();
    }

    /// <summary>
    /// Normalizes a food name for fuzzy comparison: strips punctuation/parens, removes stop words, depluralizes.
    /// Used by NaturalLanguageFallbackService scoring.
    /// </summary>
    public static string NormalizeFoodName(string name)
    {
        var s = name.ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\([^)]*\)", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[,;:/\-–—]", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9 ]", "");
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var noise = new HashSet<string>
        {
            "with", "and", "in", "of", "style", "flavored", "flavoured",
            "ns", "as", "to", "the", "for", "a", "an",
            "nfs", "not", "further", "specified", "type", "all", "purpose",
            "usda", "commodity", "purchased", "commercially", "prepared"
        };
        return string.Join(" ", tokens.Where(t => !noise.Contains(t)).Select(Depluralize));
    }

    public static string Depluralize(string word)
    {
        if (word.Length <= 3) return word;
        if (word.EndsWith("ies") && word.Length > 4)
            return word[..^3] + "y";
        if (word.EndsWith("ers") && word.Length > 4)
            return word[..^1];
        // -oes → strip "es" (tomatoes→tomato, potatoes→potato, mangoes→mango)
        if (word.EndsWith("oes") && word.Length > 4)
            return word[..^2];
        // -ses → strip trailing "s" only (sauces→sauce, cheeses→cheese)
        if (word.EndsWith("ses") && word.Length > 4)
            return word[..^1];
        // -ches, -shes → strip trailing "s" only (sandwiches→sandwich not needed, matches→matche is wrong, keep as-is)
        if (word.EndsWith("es") && word.Length > 4 &&
            !word.EndsWith("ches") && !word.EndsWith("shes"))
            return word[..^1];
        if (word.EndsWith('s') && !word.EndsWith("ss") && !word.EndsWith("us") && !word.EndsWith("is"))
            return word[..^1];
        return word;
    }

    // ════════════════════════════════════════════════════════════════
    //  INDEX-TIME: static quality (stored in Lucene as "quality" field)
    // ════════════════════════════════════════════════════════════════

    public static float ComputeStaticQuality(FoodProductDto dto)
    {
        var nameLower = dto.Name.ToLowerInvariant();
        float q = 0f;

        // Source trust
        if (dto.DataSource is "USDA" or "AUSNUT") q += 0.4f;

        // Richness boost: images and ingredients improve UX, but only for non-whole-foods.
        // USDA whole foods never have images — don't let metadata bias crush them.
        bool isTrustedWholeFood = dto.DataSource is "USDA" or "AUSNUT" ||
            dto.FoodKind == GutAI.Domain.Enums.FoodKind.WholeFood;
        if (!string.IsNullOrEmpty(dto.ImageUrl))
            q += isTrustedWholeFood ? 0.1f : 0.25f;
        if (!string.IsNullOrEmpty(dto.Ingredients))
            q += isTrustedWholeFood ? 0.05f : 0.15f;

        // Nutrition completeness
        if (dto.Calories100g.HasValue) q += 0.06f;
        if (dto.Protein100g.HasValue) q += 0.04f;
        if (dto.Carbs100g.HasValue) q += 0.03f;
        if (dto.Fat100g.HasValue) q += 0.03f;
        if (dto.Fiber100g.HasValue) q += 0.02f;
        if (dto.Sugar100g.HasValue) q += 0.02f;

        // Whole-food boost
        if (dto.FoodKind == GutAI.Domain.Enums.FoodKind.WholeFood) q += 0.5f;
        else if (dto.FoodKind == GutAI.Domain.Enums.FoodKind.Unknown)
        {
            bool looksWhole = string.IsNullOrEmpty(dto.Brand) &&
                (string.IsNullOrEmpty(dto.Ingredients) || !dto.Ingredients.Contains(','));
            if (looksWhole) q += 0.5f;
        }

        // Name length — shorter = better
        if (dto.Name.Length <= 40)
            q += Math.Max(0f, 1f - dto.Name.Length / 60f) * 0.3f;
        else
            q -= (dto.Name.Length - 40) * (dto.Name.Length - 40) / 10000f;

        // Comma penalty (light — USDA uses structural commas)
        q -= dto.Name.Count(c => c == ',') * 0.05f;

        // Parenthetical penalty
        q -= dto.Name.Count(c => c == '(') * 0.15f;

        // Hard penalties
        foreach (var term in FoodScoringTerms.HardPenaltyTerms)
            if (nameLower.Contains(term)) q -= 1.2f;

        // Soft penalties
        foreach (var term in FoodScoringTerms.SoftPenaltyTerms)
            if (nameLower.Contains(term)) q -= 0.7f;

        return q;
    }

    // ════════════════════════════════════════════════════════════════
    //  POST-LUCENE RE-RANKING (query-dependent signals)
    // ════════════════════════════════════════════════════════════════

    public static float FinalScore(FoodProductDto dto, float luceneScore, string queryLower, string[] queryTokens, string[] analyzedTokens, bool queryHasBrand = false)
    {
        float score = luceneScore;
        var primaryNoun = ExtractPrimaryNoun(dto.Name).ToLowerInvariant();
        var nameLower = dto.Name.ToLowerInvariant();

        // ── Generic coverage signals ──
        score += ComputeCoverageSignals(dto, queryLower, queryTokens, analyzedTokens, primaryNoun, nameLower, queryHasBrand,
            out float nameCoverage, out float primaryCoverage);

        // ── Source / kind signals ──
        score += ComputeSourceKindSignals(dto, queryTokens, queryHasBrand);

        // ── Penalty arrays (imitation, organ, derived, cured) ──
        score += ComputePenaltySignals(nameLower, queryLower, queryTokens);

        // ── Category-specific scoring rules ──
        score += ComputeCategorySignals(dto, nameLower, queryLower, queryTokens);

        return score;
    }

    // ────────────────────────────────────────────────────────────────
    //  Coverage sub-scores
    // ────────────────────────────────────────────────────────────────

    private static float ComputeCoverageSignals(FoodProductDto dto, string queryLower, string[] queryTokens, string[] analyzedTokens,
        string primaryNoun, string nameLower, bool queryHasBrand,
        out float nameCoverage, out float primaryCoverage)
    {
        float score = 0f;

        var allQueryTokens = queryTokens.Concat(analyzedTokens).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Token coverage against primary noun
        var primaryTokens = primaryNoun.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int primaryMatched = allQueryTokens.Count(qt => primaryTokens.Any(pt =>
            pt == qt || pt.StartsWith(qt) || qt.StartsWith(pt)));
        primaryCoverage = allQueryTokens.Length > 0 ? (float)primaryMatched / allQueryTokens.Length : 0f;
        score += primaryCoverage * 20f;
        if (primaryCoverage >= 1f) score += 15f;

        // Token coverage against FULL name
        var nameTokens = nameLower.Split([' ', ',', '(', ')', '/', '-'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int nameMatched = allQueryTokens.Count(qt => nameTokens.Any(nt =>
            nt == qt || nt.StartsWith(qt) || qt.StartsWith(nt)));
        nameCoverage = allQueryTokens.Length > 0 ? (float)nameMatched / allQueryTokens.Length : 0f;
        score += nameCoverage * 15f;
        if (nameCoverage >= 1f) score += 10f;

        // Primary noun first-token match bonus
        if (queryTokens.Length > 0 && primaryTokens.Length > 0)
        {
            var pt0 = primaryTokens[0];
            var qt0 = queryTokens[0];
            float firstTokenBonus = 0f;
            if (pt0 == qt0)
                firstTokenBonus = 20f;
            else if (pt0.StartsWith(qt0) && pt0.Length <= qt0.Length + 3)
                firstTokenBonus = 12f;
            else if (qt0.StartsWith(pt0))
                firstTokenBonus = 10f;

            if (queryTokens.Length >= 2)
                firstTokenBonus *= nameCoverage;

            score += firstTokenBonus;
        }

        // Bonus when ALL query tokens appear in name (multi-word queries)
        if (queryTokens.Length >= 2 && nameCoverage >= 1f)
            score += 15f;

        // Exact name match bonuses (with depluralization)
        var nameStem = Depluralize(nameLower);
        var queryStem = Depluralize(queryLower);
        if (nameLower == queryLower) score += 50f;
        else if (nameStem == queryStem) score += 45f;
        if (nameLower.StartsWith(queryLower)) score += 20f;
        else if (nameStem.StartsWith(queryStem) && Math.Abs(nameStem.Length - queryStem.Length) <= nameStem.Length) score += 18f;

        // Single-word query matching a USDA descriptor (e.g. "cheddar" in "Cheese, cheddar")
        if (queryTokens.Length == 1 && primaryTokens.Length > 0)
        {
            var descriptorPart = nameLower.Contains(',') ? nameLower[(nameLower.IndexOf(',') + 1)..].Trim() : "";
            var descTokens = descriptorPart.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (descTokens.Any(dt => dt == queryLower || Depluralize(dt) == queryStem))
                score += 20f;
        }

        // Prefer plain/raw variants for simple queries
        if (queryTokens.Length <= 2)
        {
            foreach (var term in FoodScoringTerms.RawFreshTerms)
                if (nameLower.Contains(term)) score += 12f;
            foreach (var term in FoodScoringTerms.PlainTerms)
                if (nameLower.Contains(term)) score += 5f;

            // Penalize processed forms only when the user didn't ask for them.
            // Check against both exact query and depluralized query tokens to avoid
            // false penalties (e.g. "tomato sauce" should not penalize "sauce" results)
            var queryTokenSet = new HashSet<string>(queryTokens, StringComparer.OrdinalIgnoreCase);
            var queryDepluralized = queryTokens.Select(Depluralize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var term in FoodScoringTerms.ProcessedTerms)
            {
                if (nameLower.Contains(term) && !queryLower.Contains(term)
                    && !queryTokenSet.Contains(term) && !queryDepluralized.Contains(term))
                {
                    score -= queryTokens.Length == 1 ? 12f : 6f;
                    break;
                }
            }
        }

        // Poor query-token coverage on primary noun
        if (queryTokens.Length >= 2)
        {
            if (primaryCoverage < 0.5f)
                score -= 20f;
            else if (primaryCoverage == 0.5f)
                score -= 10f;
        }

        // Nutrition plausibility
        score += NutritionPlausibilityScore(dto, queryLower);

        // Brand scoring
        if (!string.IsNullOrEmpty(dto.Brand))
        {
            var brandLower = dto.Brand.ToLowerInvariant();
            if (queryLower.Contains(brandLower))
                score += 40f;
            else if (queryTokens.Any(t => brandLower.Contains(t)))
                score += 20f;
        }

        return score;
    }

    // ────────────────────────────────────────────────────────────────
    //  Source / kind signals
    // ────────────────────────────────────────────────────────────────

    private static float ComputeSourceKindSignals(FoodProductDto dto, string[] queryTokens, bool queryHasBrand)
    {
        float score = 0f;

        if (queryTokens.Length <= 3)
        {
            if (dto.FoodKind == GutAI.Domain.Enums.FoodKind.WholeFood)
                score += 10f;
            else if (dto.FoodKind == GutAI.Domain.Enums.FoodKind.Branded && !queryHasBrand)
                score -= 15f;

            if (!queryHasBrand && !string.IsNullOrEmpty(dto.Brand) && dto.Brand.Length > 1)
                score -= 5f;
        }

        return score;
    }

    // ────────────────────────────────────────────────────────────────
    //  Penalty arrays (imitation, organ meat, derived, cured, etc.)
    // ────────────────────────────────────────────────────────────────

    private static float ComputePenaltySignals(string nameLower, string queryLower, string[] queryTokens)
    {
        float score = 0f;

        // Imitation / meatless
        foreach (var term in FoodScoringTerms.ImitationTerms)
        {
            if (nameLower.Contains(term) && !queryLower.Contains(term))
            {
                score -= 40f;
                break;
            }
        }

        // Organ meat
        foreach (var term in FoodScoringTerms.OrganMeatTerms)
        {
            if (nameLower.Contains(term) && !queryLower.Contains(term))
            {
                score -= 30f;
                break;
            }
        }

        // Mechanically processed
        if (nameLower.Contains("mechanically deboned") || nameLower.Contains("mechanically separated"))
        {
            if (!queryLower.Contains("mechanically"))
                score -= 40f;
        }

        // Derived form
        foreach (var term in FoodScoringTerms.DerivedFormTerms)
        {
            if (nameLower.Contains(term) && !queryLower.Contains(term))
            {
                score -= 15f;
                break;
            }
        }

        // Cured / salt
        if (queryTokens.Length <= 2)
        {
            foreach (var term in FoodScoringTerms.CuredTerms)
            {
                if (nameLower.Contains(term) && !queryLower.Contains(term))
                {
                    score -= 15f;
                    break;
                }
            }
        }

        // Regional specialty
        if (queryTokens.Length == 1)
        {
            if (nameLower.Contains("(hopi)") || nameLower.Contains("(navajo)") || nameLower.Contains("(apache)") ||
                nameLower.Contains("(alaska native)") || nameLower.Contains("hohoysi") || nameLower.Contains("shoshone") ||
                nameLower.Contains("tundra") || nameLower.Contains("laborador"))
                score -= 25f;
        }

        return score;
    }

    // ────────────────────────────────────────────────────────────────
    //  Category-specific scoring rules
    // ────────────────────────────────────────────────────────────────

    private static float ComputeCategorySignals(FoodProductDto dto, string nameLower, string queryLower, string[] queryTokens)
    {
        float score = 0f;

        // Egg / milk: prefer "whole" variants
        if (queryLower is "egg" or "eggs" or "milk")
        {
            if (nameLower.Contains("whole")) score += 15f;
            if (nameLower.Contains("white") && !queryLower.Contains("white")) score -= 10f;
            if (nameLower.Contains("yolk") && !queryLower.Contains("yolk")) score -= 10f;
            if (nameLower.Contains("buttermilk") && !queryLower.Contains("buttermilk")) score -= 20f;
            if (nameLower.Contains("dry") && !queryLower.Contains("dry")) score -= 15f;
        }

        // Yogurt: prefer plain
        if (queryLower is "yogurt" or "yoghurt")
        {
            if (nameLower.Contains("plain")) score += 15f;
            if (nameLower.Contains("strawberry") || nameLower.Contains("blueberry") || nameLower.Contains("vanilla"))
                score -= 10f;
        }

        // Bacon: prefer pork over turkey
        if (queryLower is "bacon")
        {
            if (nameLower.Contains("turkey") && !queryLower.Contains("turkey")) score -= 25f;
        }

        // Spice queries: prefer spice form
        if (queryTokens.Length == 1 && FoodScoringTerms.SpiceTerms.Contains(queryLower))
        {
            if (nameLower.StartsWith("spices,") || nameLower.Contains("ground")) score += 25f;
            if (nameLower.Contains("buns") || nameLower.Contains("bread") || nameLower.Contains("pastry") || nameLower.Contains("danish") || nameLower.Contains("frosted"))
                score -= 30f;
        }

        // Coffee: prefer brewed beverages
        if (queryLower is "coffee")
        {
            if (nameLower.Contains("soymilk") || nameLower.Contains("soy milk")) score -= 30f;
            if (nameLower.Contains("beverages") && nameLower.Contains("coffee")) score += 10f;
        }

        // Tea: prefer brewed beverages
        if (queryLower is "tea")
        {
            if (nameLower.Contains("beverages") && nameLower.Contains("tea")) score += 10f;
            if (nameLower.Contains("hohoysi") || nameLower.Contains("hopi") || nameLower.Contains("alaska native"))
                score -= 30f;
        }

        // Coconut milk: penalize non-coconut milks
        if (queryLower is "coconut milk")
        {
            if (!nameLower.Contains("coconut")) score -= 20f;
        }

        // Crackers: prefer generic over branded
        if (queryLower is "crackers" or "cracker")
        {
            if (nameLower.Contains("whole-wheat") || nameLower.Contains("whole wheat")) score += 10f;
            if (nameLower.Contains("goya") || nameLower.Contains("ritz") || nameLower.Contains("nabisco"))
                score -= 20f;
        }

        // Fruit queries: prefer whole fruit over juice
        if (queryLower is "lime" or "lemon" or "orange" or "grapefruit")
        {
            if (nameLower.Contains("juice") && !queryLower.Contains("juice")) score -= 25f;
            if (nameLower.Contains("raw") && !nameLower.Contains("juice")) score += 10f;
        }

        // Beans: penalize liquid
        if (queryLower.Contains("bean") && nameLower.Contains("liquid")) score -= 30f;

        // Crab vs crabapple
        if (queryLower is "crab" or "crabs")
        {
            if (nameLower.Contains("crabapple")) score -= 60f;
            if (nameLower.Contains("crustacean")) score += 15f;
        }

        // Mustard: prefer condiment over greens
        if (queryLower is "mustard")
        {
            if (nameLower.Contains("greens") || nameLower.Contains("spinach")) score -= 25f;
            if (nameLower.Contains("prepared") || nameLower.Contains("yellow")) score += 10f;
        }

        // Bread: penalize pan dulce
        if (queryLower is "bread" && nameLower.Contains("pan dulce")) score -= 15f;

        // Corn: penalize "corned" prefix match
        if (queryLower.Contains("corn") && !queryLower.Contains("corned"))
        {
            if (nameLower.Contains("corned")) score -= 60f;
        }

        return score;
    }

    // ────────────────────────────────────────────────────────────────
    //  Nutrition plausibility
    // ────────────────────────────────────────────────────────────────

    private static float NutritionPlausibilityScore(FoodProductDto dto, string queryLower)
    {
        if (!dto.Calories100g.HasValue) return 0f;

        float penalty = 0f;
        var cal = dto.Calories100g.Value;
        var protein = dto.Protein100g ?? 0m;
        var carbs = dto.Carbs100g ?? 0m;
        var fat = dto.Fat100g ?? 0m;

        if (queryLower.Contains("chicken") || queryLower.Contains("beef") ||
            queryLower.Contains("fish") || queryLower.Contains("turkey") ||
            queryLower.Contains("pork") || queryLower.Contains("lamb") ||
            queryLower.Contains("steak") || queryLower.Contains("salmon"))
        {
            if (carbs > 40m) penalty -= 15f;
            if (protein < 5m && cal > 50m) penalty -= 10f;
        }

        if (queryLower.Contains("oil") || queryLower.Contains("butter") || queryLower.Contains("lard"))
        {
            if (fat < 20m && cal > 100m) penalty -= 15f;
        }

        if (queryLower.Contains("lettuce") || queryLower.Contains("spinach") ||
            queryLower.Contains("kale") || queryLower.Contains("celery") ||
            queryLower.Contains("cucumber"))
        {
            if (cal > 100m) penalty -= 15f;
        }

        if (queryLower.Contains("juice") || queryLower.Contains("water") ||
            queryLower.Contains("tea") || queryLower.Contains("coffee"))
        {
            if (fat > 20m) penalty -= 10f;
        }

        return penalty;
    }
}
