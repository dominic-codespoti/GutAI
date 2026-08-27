using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Deterministic food-form and specificity safety policies for Stage-B grounding.
/// Evaluates proposed observed <see cref="ScannedComponent"/> + candidate <see cref="FoodProductDto"/>
/// pairs before auto-selection to reject unsafe, overly specific, or mismatched product forms.
/// </summary>
public static class FoodFormPolicy
{
    private static readonly string[] FruitTokens =
    [
        "berry", "berries", "blueberry", "blueberries", "strawberry", "strawberries",
        "raspberry", "raspberries", "blackberry", "blackberries", "cranberry", "cranberries",
        "apple", "apples", "banana", "bananas", "orange", "oranges", "grape", "grapes",
        "mango", "mangoes", "pineapple", "pineapples", "peach", "peaches", "pear", "pears",
        "watermelon", "watermelons", "melon", "melons", "cherry", "cherries", "plum", "plums",
        "kiwi", "kiwis", "fruit", "fruits"
    ];

    private static readonly string[] FruitRejectedProductTokens =
    [
        "juice", "juices", "drink", "drinks", "beverage", "beverages",
        "pop", "pops", "popsicle", "popsicles", "ice pop", "ice pops", "freeze pop", "freeze pops",
        "bar", "bars", "snack bar", "snack bars", "granola bar", "granola bars",
        "concentrate", "concentrates", "concentrated",
        "topping", "toppings", "syrup", "syrups", "sauce", "sauces",
        "yogurt", "yogurts", "yoghurt", "yoghurts", "puree", "purees"
    ];

    private static readonly string[] OatmealTokens =
    [
        "oatmeal", "porridge", "oat porridge", "rolled oats", "steel cut oats",
        "quick oats", "instant oats", "oat bowl"
    ];

    private static readonly string[] OatmealRejectedProductTokens =
    [
        "bread", "breads", "loaf", "loaves", "toast", "bun", "buns", "roll", "rolls",
        "dry cereal", "cereal", "granola", "muesli", "crisps", "flakes",
        "farina", "malt", "malted", "cookie", "cookies", "muffin", "muffins", "cake", "cakes"
    ];

    private static readonly string[] OatmealAllowedCerealTokens =
    [
        "cooked", "prepared with water", "prepared with milk", "instant", "regular"
    ];

    private static readonly string[] SmoothieTokens =
    [
        "smoothie", "smoothies", "shake", "shakes", "protein shake", "protein smoothie",
        "fruit smoothie", "green smoothie"
    ];

    private static readonly string[] SmoothieRejectedProductTokens =
    [
        "bar", "bars", "snack bar", "snack bars", "protein bar", "protein bars",
        "granola bar", "granola bars",
        "pop", "pops", "popsicle", "popsicles", "ice pop", "ice pops",
        "cookie", "cookies", "bites", "candy", "mix", "powder"
    ];

    private static readonly string[] SpecificCandidateProductTerms =
    [
        "pop", "pops", "popsicle", "popsicles",
        "bar", "bars", "bite", "bites", "crisp", "crisps", "chip", "chips",
        "candy", "candies", "gummy", "gummies", "snack", "snacks",
        "pastry", "pastries", "muffin", "muffins", "cookie", "cookies",
        "cake", "cakes", "donut", "donuts", "doughnut", "doughnuts",
        "syrup", "sauce", "dressing", "topping", "supplement"
    ];

    /// <summary>
    /// Evaluates food-form compatibility and specificity constraints.
    /// Returns a descriptive rejection reason string if vetoed, or null if allowed.
    /// </summary>
    public static string? Evaluate(ScannedComponent observation, FoodProductDto candidate)
    {
        var formRejection = EvaluateFormPolicy(observation, candidate);
        if (formRejection is not null)
            return formRejection;

        var specificityRejection = EvaluateSpecificityPolicy(observation, candidate);
        if (specificityRejection is not null)
            return specificityRejection;

        return null;
    }

    /// <summary>
    /// Conservative product-form vetoes:
    /// - Observed raw berries/raw fruit → reject juice, pops, bars, concentrate, topping, yogurt products
    /// - Observed oatmeal/porridge → reject bread, dry cereal, farina, malt products
    /// - Observed smoothie → reject bar/pops products; allow plausible beverage candidates
    /// </summary>
    public static string? EvaluateFormPolicy(ScannedComponent observation, FoodProductDto candidate)
    {
        var obsText = string.Join(" ", new[] { observation.Name, observation.PreparationNote }
            .Concat(observation.SearchQueries)).ToLowerInvariant();
        var obsTokens = FoodTextNormalizer.Tokenize(obsText);
        var candLower = candidate.Name.ToLowerInvariant();
        var candTokens = FoodTextNormalizer.Tokenize(candLower);

        // 1. Raw fruit / raw berries
        var hasFruitToken = FruitTokens.Any(ft => ContainsToken(obsTokens, ft) || obsText.Contains(ft, StringComparison.OrdinalIgnoreCase));
        var isObsRawFruit = hasFruitToken && (
            obsText.Contains("raw", StringComparison.OrdinalIgnoreCase) ||
            obsText.Contains("fresh", StringComparison.OrdinalIgnoreCase) ||
            (!obsText.Contains("juice", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("smoothie", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("cooked", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("baked", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("jam", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("jelly", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("pie", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("bar", StringComparison.OrdinalIgnoreCase) &&
             !obsText.Contains("pop", StringComparison.OrdinalIgnoreCase)));

        if (isObsRawFruit)
        {
            foreach (var rejected in FruitRejectedProductTokens)
            {
                if (ContainsToken(candTokens, rejected) || MatchesPhrase(candLower, rejected))
                {
                    // If candidate is a rejected form (e.g. juice, pops, bar, concentrate, topping, yogurt)
                    // and observation did not explicitly request that form, veto.
                    if (!ContainsToken(obsTokens, rejected) && !MatchesPhrase(obsText, rejected))
                    {
                        return $"Observed raw/fresh fruit '{observation.Name}' cannot auto-ground to product form '{rejected}' in '{candidate.Name}'.";
                    }
                }
            }
        }

        // 2. Oatmeal / porridge
        var isObsOatmeal = OatmealTokens.Any(ot => ContainsToken(obsTokens, ot) || MatchesPhrase(obsText, ot));
        if (isObsOatmeal)
        {
            foreach (var rejected in OatmealRejectedProductTokens)
            {
                if (ContainsToken(candTokens, rejected) || MatchesPhrase(candLower, rejected))
                {
                    // Special case: USDA has entries like "Cereals, oats, regular and quick, not fortified, cooked with water"
                    // which contains "cereal" or "cereals", but is actual cooked oatmeal.
                    if (rejected is "cereal" or "dry cereal")
                    {
                        var isCookedHotCereal = candLower.Contains("cooked", StringComparison.OrdinalIgnoreCase) ||
                                                candLower.Contains("prepared with", StringComparison.OrdinalIgnoreCase) ||
                                                candLower.Contains("oatmeal", StringComparison.OrdinalIgnoreCase) ||
                                                candLower.Contains("porridge", StringComparison.OrdinalIgnoreCase);

                        if (isCookedHotCereal && !candLower.Contains("dry", StringComparison.OrdinalIgnoreCase) && !candLower.Contains("ready-to-eat", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (!ContainsToken(obsTokens, rejected) && !MatchesPhrase(obsText, rejected))
                    {
                        return $"Observed oatmeal/porridge '{observation.Name}' cannot auto-ground to '{rejected}' product form in '{candidate.Name}'.";
                    }
                }
            }
        }

        // 3. Smoothie
        var isObsSmoothie = SmoothieTokens.Any(st => ContainsToken(obsTokens, st) || MatchesPhrase(obsText, st));
        if (isObsSmoothie)
        {
            foreach (var rejected in SmoothieRejectedProductTokens)
            {
                if (ContainsToken(candTokens, rejected) || MatchesPhrase(candLower, rejected))
                {
                    if (!ContainsToken(obsTokens, rejected) && !MatchesPhrase(obsText, rejected))
                    {
                        return $"Observed smoothie/shake '{observation.Name}' cannot auto-ground to solid/snack form '{rejected}' in '{candidate.Name}'.";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Specificity policy:
    /// - Observed specific + candidate generic same dish/category → allow (e.g. pork katsu curry → Katsu curry)
    /// - Observed generic + candidate specific product/brand/form → reject (e.g. blueberry → Blueberry Pops)
    /// - Exact/same-category matches remain allowed
    /// </summary>
    public static string? EvaluateSpecificityPolicy(ScannedComponent observation, FoodProductDto candidate)
    {
        var obsName = observation.Name.Trim().ToLowerInvariant();
        var obsTokens = FoodTextNormalizer.Tokenize(obsName);
        var candName = candidate.Name.Trim().ToLowerInvariant();
        var candTokens = FoodTextNormalizer.Tokenize(candName);

        // If candidate is branded and observation did not mention brand
        if (!string.IsNullOrWhiteSpace(candidate.Brand))
        {
            var brandTokens = FoodTextNormalizer.Tokenize(candidate.Brand.ToLowerInvariant());
            var brandMentioned = brandTokens.Length > 0 && brandTokens.All(bt => ContainsToken(obsTokens, bt));
            // If observation is generic whole food / generic dish without brand, reject branded candidate for auto-select
            if (!brandMentioned && candidate.FoodKind == FoodKind.Branded && obsTokens.Length <= 3)
            {
                // Check if candidate contains specific product forms or extra branded qualifiers
                if (SpecificCandidateProductTerms.Any(term => ContainsToken(candTokens, term)))
                {
                    return $"Observed generic item '{observation.Name}' cannot auto-ground to specific branded product '{candidate.Name}'.";
                }
            }
        }

        // Check if observation is generic (e.g., "blueberry", "apple", "chicken") but candidate is a specific product form / packaged snack
        var obsHasSpecificForm = SpecificCandidateProductTerms.Any(term => ContainsToken(obsTokens, term));
        if (!obsHasSpecificForm)
        {
            foreach (var term in SpecificCandidateProductTerms)
            {
                if (ContainsToken(candTokens, term))
                {
                    return $"Observed generic item '{observation.Name}' cannot auto-ground to specific product form '{term}' in '{candidate.Name}'.";
                }
            }
        }

        return null;
    }

    private static bool ContainsToken(string[] tokens, string token)
    {
        var depluralized = FoodTextNormalizer.Depluralize(token);
        return tokens.Any(t =>
            t.Equals(token, StringComparison.OrdinalIgnoreCase) ||
            FoodTextNormalizer.Depluralize(t).Equals(depluralized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPhrase(string text, string phrase)
    {
        return text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
    }
}
