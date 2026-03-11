namespace GutAI.Infrastructure.Services;

/// <summary>
/// Shared utility for converting food units (glass, cup, bowl, slice, etc.)
/// to estimated grams. Extracted from NaturalLanguageFallbackService so both
/// QuerySanitizer and the NLP meal-logging pipeline can reuse the same logic.
/// </summary>
internal static class ServingEstimator
{
    internal static bool IsWeightUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "g" or "gram" or "grams" or "kg" or "kilogram" or "kilograms"
            or "oz" or "ounce" or "ounces" or "lb" or "lbs" or "pound" or "pounds"
            or "mg" or "milligram" or "milligrams" => true,
        _ => false
    };

    internal static decimal WeightUnitToGrams(string unit) => unit.ToLowerInvariant() switch
    {
        "g" or "gram" or "grams" => 1m,
        "kg" or "kilogram" or "kilograms" => 1000m,
        "oz" or "ounce" or "ounces" => 28.35m,
        "lb" or "lbs" or "pound" or "pounds" => 453.6m,
        "mg" or "milligram" or "milligrams" => 0.001m,
        _ => 1m
    };

    internal static bool IsVolumeUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "cup" or "cups" or "tbsp" or "tablespoon" or "tablespoons"
            or "tsp" or "teaspoon" or "teaspoons" or "ml" or "milliliter" or "milliliters"
            or "l" or "liter" or "liters" or "litre" or "litres"
            or "glass" or "glasses" or "bowl" or "bowls"
            or "fl oz" or "fl" or "pint" or "pints" or "quart" or "quarts" or "gallon" or "gallons" => true,
        _ => false
    };

    internal static decimal VolumeUnitToGrams(string unit, string foodName) => unit.ToLowerInvariant() switch
    {
        "cup" or "cups" => EstimateCupWeightG(foodName),
        "tbsp" or "tablespoon" or "tablespoons" => 15m,
        "tsp" or "teaspoon" or "teaspoons" => 5m,
        "ml" or "milliliter" or "milliliters" => 1m,
        "l" or "liter" or "liters" or "litre" or "litres" => 1000m,
        "glass" or "glasses" => 240m,
        "bowl" or "bowls" => 300m,
        "fl oz" or "fl" => 30m,
        "pint" or "pints" => 473m,
        "quart" or "quarts" => 946m,
        "gallon" or "gallons" => 3785m,
        _ => 100m
    };

    internal static bool IsCountUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "slice" or "slices" or "piece" or "pieces"
            or "handful" or "handfuls" or "serving" or "servings"
            or "can" or "cans" or "bottle" or "bottles"
            or "scoop" or "scoops" or "bar" or "bars"
            or "packet" or "packets" or "strip" or "strips"
            or "fillet" or "fillets" or "patty" or "patties"
            or "wing" or "wings" or "thigh" or "thighs"
            or "drumstick" or "drumsticks" or "breast" or "breasts"
            or "clove" or "cloves" or "stalk" or "stalks"
            or "sprig" or "sprigs" or "leaf" or "leaves"
            or "wedge" or "wedges" or "chunk" or "chunks"
            or "ring" or "rings" or "stick" or "sticks"
            or "rasher" or "rashers" => true,
        _ => false
    };

    /// <summary>
    /// Estimates grams for one unit of the given food, dispatching to weight/volume/count helpers.
    /// <paramref name="productServingQty"/> is the product's per-serving gram weight from the DB (nullable).
    /// </summary>
    internal static decimal EstimateUnitWeightG(decimal? productServingQty, string unit, string foodName)
    {
        if (!string.IsNullOrEmpty(unit) && IsWeightUnit(unit))
            return WeightUnitToGrams(unit);

        if (!string.IsNullOrEmpty(unit) && IsVolumeUnit(unit))
            return VolumeUnitToGrams(unit, foodName);

        if (!string.IsNullOrEmpty(unit) && IsCountUnit(unit))
            return productServingQty is > 0 ? productServingQty.Value : EstimateDefaultServingG(foodName);

        if (productServingQty is > 0)
            return productServingQty.Value;

        return EstimateDefaultServingG(foodName);
    }

    internal static decimal EstimateCupWeightG(string foodName)
    {
        var lower = foodName.ToLowerInvariant();
        if (lower.Contains("rice") || lower.Contains("pasta") || lower.Contains("oat") || lower.Contains("quinoa") || lower.Contains("couscous")) return 185m;
        if (lower.Contains("flour") || lower.Contains("sugar") || lower.Contains("cocoa")) return 125m;
        if (lower.Contains("milk") || lower.Contains("water") || lower.Contains("juice") || lower.Contains("broth") || lower.Contains("stock")) return 240m;
        if (lower.Contains("yogurt") || lower.Contains("yoghurt") || lower.Contains("kefir")) return 245m;
        if (lower.Contains("berr") || lower.Contains("fruit") || lower.Contains("grape") || lower.Contains("cherry") || lower.Contains("blueberr")) return 150m;
        if (lower.Contains("vegetable") || lower.Contains("spinach") || lower.Contains("lettuce") || lower.Contains("kale") || lower.Contains("arugula")) return 60m;
        if (lower.Contains("bean") || lower.Contains("lentil") || lower.Contains("chickpea")) return 180m;
        if (lower.Contains("nut") || lower.Contains("almond") || lower.Contains("peanut") || lower.Contains("cashew") || lower.Contains("walnut") || lower.Contains("pecan")) return 140m;
        if (lower.Contains("granola") || lower.Contains("cereal") || lower.Contains("muesli")) return 120m;
        if (lower.Contains("honey") || lower.Contains("syrup") || lower.Contains("maple")) return 340m;
        if (lower.Contains("oil") || lower.Contains("butter")) return 220m;
        if (lower.Contains("cottage cheese") || lower.Contains("ricotta")) return 225m;
        if (lower.Contains("ice cream") || lower.Contains("gelato") || lower.Contains("sorbet")) return 140m;
        if (lower.Contains("soup") || lower.Contains("stew") || lower.Contains("chili") || lower.Contains("chilli")) return 240m;
        return 150m;
    }

    internal static decimal EstimateDefaultServingG(string foodName)
    {
        var lower = foodName.ToLowerInvariant();
        if (lower.Contains("shake") || lower.Contains("smoothie"))
            return 350m;
        if (lower.Contains("juice") || lower.Contains("soda") || lower.Contains("cola") || lower.Contains("lemonade"))
            return 330m;
        if (lower.Contains("coffee") || lower.Contains("latte") || lower.Contains("cappuccino") || lower.Contains("espresso"))
            return 240m;
        if (lower.Contains("tea"))
            return 240m;
        if (lower.Contains("beer") || lower.Contains("ale") || lower.Contains("lager"))
            return 355m;
        if (lower.Contains("wine"))
            return 150m;
        if (lower.Contains("energy drink"))
            return 250m;
        if (lower.Contains("egg")) return 50m;
        if (lower.Contains("banana")) return 120m;
        if (lower.Contains("apple") || lower.Contains("orange") || lower.Contains("pear") || lower.Contains("peach") || lower.Contains("nectarine")) return 180m;
        if (lower.Contains("chicken") || lower.Contains("beef") || lower.Contains("steak")
            || lower.Contains("pork") || lower.Contains("fish") || lower.Contains("salmon")
            || lower.Contains("turkey") || lower.Contains("lamb") || lower.Contains("tuna")
            || lower.Contains("shrimp") || lower.Contains("prawn")) return 140m;
        if (lower.Contains("bread") || lower.Contains("toast")) return 30m;
        if (lower.Contains("avocado")) return 150m;
        if (lower.Contains("cheese")) return 30m;
        if (lower.Contains("butter") || lower.Contains("oil") || lower.Contains("margarine")) return 14m;
        if (lower.Contains("milk")) return 240m;
        if (lower.Contains("yogurt") || lower.Contains("yoghurt")) return 150m;
        if (lower.Contains("rice") || lower.Contains("pasta") || lower.Contains("noodle") || lower.Contains("quinoa") || lower.Contains("couscous")) return 200m;
        if (lower.Contains("potato") || lower.Contains("sweet potato")) return 150m;
        if (lower.Contains("tomato")) return 125m;
        if (lower.Contains("carrot")) return 60m;
        if (lower.Contains("broccoli") || lower.Contains("cauliflower")) return 90m;
        if (lower.Contains("pizza")) return 110m;
        if (lower.Contains("burger") || lower.Contains("sandwich") || lower.Contains("wrap") || lower.Contains("burrito")) return 200m;
        if (lower.Contains("taco")) return 80m;
        if (lower.Contains("cookie") || lower.Contains("biscuit")) return 30m;
        if (lower.Contains("donut") || lower.Contains("doughnut")) return 60m;
        if (lower.Contains("muffin")) return 115m;
        if (lower.Contains("croissant") || lower.Contains("pastry") || lower.Contains("danish")) return 60m;
        if (lower.Contains("pancake") || lower.Contains("waffle")) return 75m;
        if (lower.Contains("sausage") || lower.Contains("hot dog") || lower.Contains("hotdog")) return 50m;
        if (lower.Contains("bacon")) return 8m;
        if (lower.Contains("granola") || lower.Contains("cereal") || lower.Contains("muesli")) return 55m;
        if (lower.Contains("protein bar") || lower.Contains("energy bar") || lower.Contains("bar")) return 50m;
        if (lower.Contains("chocolate")) return 40m;
        if (lower.Contains("ice cream") || lower.Contains("gelato")) return 65m;
        if (lower.Contains("soup") || lower.Contains("stew") || lower.Contains("chili") || lower.Contains("chilli")) return 240m;
        if (lower.Contains("hummus") || lower.Contains("guacamole") || lower.Contains("salsa") || lower.Contains("dip")) return 30m;
        if (lower.Contains("almond") || lower.Contains("walnut") || lower.Contains("cashew") || lower.Contains("peanut") || lower.Contains("nut") || lower.Contains("pecan")) return 30m;
        if (lower.Contains("raisin") || lower.Contains("dried") || lower.Contains("date")) return 40m;
        if (lower.Contains("olive")) return 15m;
        if (lower.Contains("onion")) return 110m;
        if (lower.Contains("pepper") || lower.Contains("capsicum")) return 120m;
        if (lower.Contains("cucumber") || lower.Contains("zucchini") || lower.Contains("courgette")) return 150m;
        if (lower.Contains("mushroom")) return 70m;
        if (lower.Contains("corn")) return 90m;
        if (lower.Contains("garlic")) return 4m;
        if (lower.Contains("ginger")) return 5m;
        if (lower.Contains("lemon") || lower.Contains("lime")) return 60m;
        if (lower.Contains("mango") || lower.Contains("papaya")) return 200m;
        if (lower.Contains("pineapple")) return 165m;
        if (lower.Contains("watermelon") || lower.Contains("melon")) return 280m;
        if (lower.Contains("strawberr")) return 150m;
        if (lower.Contains("grape")) return 80m;
        if (lower.Contains("kiwi")) return 75m;
        if (lower.Contains("plum")) return 65m;
        if (lower.Contains("fig")) return 50m;
        if (lower.Contains("coconut")) return 45m;
        if (lower.Contains("tofu") || lower.Contains("tempeh")) return 125m;
        return 100m;
    }

    /// <summary>
    /// Extracts a size modifier (small/large/etc.) from the start of a food name,
    /// returns the multiplier, and trims the modifier from the name.
    /// </summary>
    internal static decimal ExtractSizeMultiplier(ref string foodName)
    {
        if (string.IsNullOrWhiteSpace(foodName))
            return 1m;

        var lower = foodName.TrimStart();
        string? matched = null;
        decimal multiplier = 1m;

        // Check multi-word first
        foreach (var (prefix, mult) in new[]
        {
            ("extra large ", 1.5m), ("extra-large ", 1.5m),
        })
        {
            if (lower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                matched = prefix;
                multiplier = mult;
                break;
            }
        }

        if (matched is null)
        {
            // Single-word modifiers
            var spaceIdx = lower.IndexOf(' ');
            if (spaceIdx > 0)
            {
                var firstWord = lower[..spaceIdx].ToLowerInvariant();
                multiplier = firstWord switch
                {
                    "small" or "mini" or "tiny" => 0.7m,
                    "medium" or "med" => 1m,
                    "large" or "big" or "lg" => 1.3m,
                    "xl" or "huge" or "jumbo" => 1.5m,
                    _ => -1m // sentinel: no match
                };

                if (multiplier > 0)
                    matched = lower[..(spaceIdx + 1)];
                else
                    multiplier = 1m;
            }
        }

        if (matched is not null)
            foodName = foodName[matched.Length..].TrimStart();

        return multiplier;
    }

    /// <summary>
    /// Formats a human-readable label for a unit, e.g. "glass" → "1 glass (240ml)".
    /// Returns null if unit is empty or unrecognized.
    /// </summary>
    internal static string? FormatUnitLabel(decimal quantity, string unit, string foodName)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return null;

        var qtyStr = quantity == Math.Floor(quantity) ? ((int)quantity).ToString() : quantity.ToString("0.##");
        var grams = EstimateUnitGrams(unit, foodName);

        if (grams is null)
            return $"{qtyStr} {unit}";

        return $"{qtyStr} {unit} ({grams.Value}g)";
    }

    /// <summary>
    /// Estimates grams for a single unit, without product context.
    /// Returns null if unit is unrecognized.
    /// </summary>
    internal static decimal? EstimateUnitGrams(string unit, string foodName)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return null;

        if (IsWeightUnit(unit))
            return WeightUnitToGrams(unit);

        if (IsVolumeUnit(unit))
            return VolumeUnitToGrams(unit, foodName);

        if (IsCountUnit(unit))
            return EstimateDefaultServingG(foodName);

        return null;
    }
}
