using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public partial class NaturalLanguageFallbackService
{
    private readonly IFoodApiService _foodApi;
    private readonly ITableStore _store;
    private readonly ILogger<NaturalLanguageFallbackService> _logger;

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(4);

    public NaturalLanguageFallbackService(IFoodApiService foodApi, ITableStore store, ILogger<NaturalLanguageFallbackService> logger)
    {
        _foodApi = foodApi;
        _store = store;
        _logger = logger;
    }

    public virtual async Task<List<ParsedFoodItemDto>> ParseAsync(string text, CancellationToken ct = default)
    {
        var cleaned = PreprocessText(text);
        var segments = SplitIntoFoodSegments(cleaned);
        var results = new List<ParsedFoodItemDto>();

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var (quantity, unit, foodName) = ExtractQuantityAndFood(segment.Trim());

            if (string.IsNullOrWhiteSpace(foodName))
                continue;

            var sizeMultiplier = ExtractSizeMultiplier(ref foodName);
            foodName = CleanFoodName(foodName);

            if (string.IsNullOrWhiteSpace(foodName))
                continue;

            try
            {
                using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                searchCts.CancelAfter(SearchTimeout);

                var searchResults = await _foodApi.SearchAsync(foodName, searchCts.Token);
                var match = PickBestMatch(searchResults, foodName);

                if (match is not null)
                {
                    var confidence = ComputeConfidence(searchResults, match, foodName);
                    var unitWeightG = EstimateUnitWeightG(match, unit, foodName) * sizeMultiplier;
                    var totalWeightG = unitWeightG * quantity;
                    var scale = totalWeightG / 100m;

                    Guid? foodProductId = null;
                    try
                    {
                        var product = new FoodProduct
                        {
                            Id = Guid.NewGuid(),
                            Name = match.Name,
                            Barcode = match.Barcode,
                            Brand = match.Brand,
                            Ingredients = match.Ingredients,
                            ImageUrl = match.ImageUrl,
                            NovaGroup = match.NovaGroup,
                            NutriScore = match.NutriScore,
                            AllergensTags = match.AllergensTags ?? [],
                            Calories100g = match.Calories100g,
                            Protein100g = match.Protein100g,
                            Carbs100g = match.Carbs100g,
                            Fat100g = match.Fat100g,
                            Fiber100g = match.Fiber100g,
                            Sugar100g = match.Sugar100g,
                            Sodium100g = match.Sodium100g,
                            ServingSize = match.ServingSize,
                            ServingQuantity = match.ServingQuantity,
                            DataSource = match.DataSource ?? "OpenFoodFacts",
                            ExternalId = match.ExternalId ?? match.Barcode,
                            CachedAt = DateTime.UtcNow,
                            CacheTtlHours = 168
                        };
                        await _store.UpsertFoodProductAsync(product, ct);
                        foodProductId = product.Id;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist FoodProduct for '{Name}'", match.Name);
                    }

                    results.Add(new ParsedFoodItemDto
                    {
                        Name = match.Name,
                        FoodProductId = foodProductId,
                        Calories = Round(match.Calories100g, scale),
                        ProteinG = Round(match.Protein100g, scale),
                        CarbsG = Round(match.Carbs100g, scale),
                        FatG = Round(match.Fat100g, scale),
                        FiberG = Round(match.Fiber100g, scale),
                        SugarG = Round(match.Sugar100g, scale),
                        SodiumMg = Round(match.Sodium100g, scale),
                        ServingWeightG = totalWeightG,
                        ServingSize = FormatServingSize(quantity, unit),
                        ServingQuantity = quantity,
                        MatchConfidence = confidence
                    });
                }
                else
                {
                    _logger.LogDebug("No food match found for '{Segment}', using generic estimate", segment);
                    results.Add(CreateGenericEstimate(foodName, quantity, unit, sizeMultiplier));
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Food search timed out for '{Segment}', using generic estimate", segment);
                results.Add(CreateGenericEstimate(foodName, quantity, unit, sizeMultiplier));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to look up food for segment '{Segment}'", segment);
                results.Add(CreateGenericEstimate(foodName, quantity, unit, sizeMultiplier));
            }
        }

        return results;
    }

    internal static string PreprocessText(string text)
    {
        var result = text.Trim();

        // Replace unicode fractions
        result = result.Replace("½", " 1/2").Replace("⅓", " 1/3").Replace("⅔", " 2/3")
            .Replace("¼", " 1/4").Replace("¾", " 3/4").Replace("⅕", " 1/5")
            .Replace("⅛", " 1/8").Replace("⅜", " 3/8").Replace("⅝", " 5/8").Replace("⅞", " 7/8");

        // Strip leading filler phrases like "I had", "I ate", "I just ate", "for lunch I had", etc.
        result = LeadingFillerPattern().Replace(result, "").Trim();

        // Remove trailing periods
        result = result.TrimEnd('.');

        return result;
    }

    internal static string FormatServingSize(decimal quantity, string unit)
    {
        var qtyStr = quantity == Math.Floor(quantity) ? ((int)quantity).ToString() : quantity.ToString("0.##");
        return string.IsNullOrEmpty(unit) ? qtyStr : $"{qtyStr} {unit}";
    }

    internal static List<string> SplitIntoFoodSegments(string text)
    {
        var normalized = text.Trim();
        var parts = SplitPattern().Split(normalized);
        return parts
            .Select(p => p.Trim())
            .Select(p => LeadingAndOrPattern().Replace(p, "").Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    internal static (decimal quantity, string unit, string foodName) ExtractQuantityAndFood(string segment)
    {
        // Strip leading filler words: "some", "about", "around", "approximately", "roughly", "maybe", "like"
        var cleaned = LeadingFillerWordPattern().Replace(segment, "").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = segment;

        var wordMatch = WordQuantityPattern().Match(cleaned);
        if (wordMatch.Success)
        {
            var wordQty = ParseWordNumber(wordMatch.Groups["word"].Value);
            var wordUnit = wordMatch.Groups["unit"].Value.Trim();
            var wordFood = wordMatch.Groups["food"].Value.Trim();
            wordFood = StripOfPrefix(wordFood);
            if (string.IsNullOrEmpty(wordFood) && !string.IsNullOrEmpty(wordUnit))
            {
                wordFood = wordUnit;
                wordUnit = "";
            }
            return (wordQty, wordUnit, wordFood);
        }

        var fracMatch = FractionPattern().Match(cleaned);
        if (fracMatch.Success)
        {
            var whole = string.IsNullOrEmpty(fracMatch.Groups["whole"].Value) ? 0m : decimal.Parse(fracMatch.Groups["whole"].Value);
            var numerator = decimal.Parse(fracMatch.Groups["num"].Value);
            var denominator = decimal.Parse(fracMatch.Groups["den"].Value);
            var qty = whole + (denominator != 0 ? numerator / denominator : 0);
            var unit = fracMatch.Groups["unit"].Value.Trim();
            var food = fracMatch.Groups["food"].Value.Trim();
            food = StripOfPrefix(food);
            if (string.IsNullOrEmpty(food) && !string.IsNullOrEmpty(unit))
            {
                food = unit;
                unit = "";
            }
            return (qty, unit, food);
        }

        var numMatch = NumericQuantityPattern().Match(cleaned);
        if (numMatch.Success)
        {
            var qty = decimal.Parse(numMatch.Groups["qty"].Value);
            var unit = numMatch.Groups["unit"].Value.Trim();
            var food = numMatch.Groups["food"].Value.Trim();
            food = StripOfPrefix(food);
            if (string.IsNullOrEmpty(food) && !string.IsNullOrEmpty(unit))
            {
                food = unit;
                unit = "";
            }
            return (qty, unit, food);
        }

        return (1m, "", cleaned);
    }

    internal static string StripOfPrefix(string food)
    {
        if (food.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
            return food[3..].Trim();
        return food;
    }

    internal static string CleanFoodName(string foodName)
    {
        // Remove parenthetical descriptions: "(grilled)", "(raw)", "(cooked)"
        var result = ParentheticalPattern().Replace(foodName, "").Trim();

        // Strip trailing preposition phrases if the core food was already captured
        // e.g., "chicken on the side" → "chicken"
        result = TrailingPrepPattern().Replace(result, "").Trim();

        return result;
    }

    internal static decimal ExtractSizeMultiplier(ref string foodName)
    {
        var match = SizeModifierPattern().Match(foodName);
        if (!match.Success)
            return 1m;

        var size = match.Groups["size"].Value.ToLowerInvariant();
        foodName = foodName[match.Length..].Trim();

        return size switch
        {
            "small" or "mini" or "tiny" => 0.7m,
            "medium" or "med" => 1m,
            "large" or "big" or "lg" => 1.3m,
            "extra large" or "xl" or "extra-large" or "huge" or "jumbo" => 1.5m,
            _ => 1m
        };
    }

    internal static decimal EstimateUnitWeightG(FoodProductDto product, string unit, string foodName)
    {
        if (!string.IsNullOrEmpty(unit) && IsWeightUnit(unit))
            return WeightUnitToGrams(unit);

        if (!string.IsNullOrEmpty(unit) && IsVolumeUnit(unit))
            return VolumeUnitToGrams(unit, foodName);

        if (!string.IsNullOrEmpty(unit) && IsCountUnit(unit))
            return product.ServingQuantity is > 0 ? product.ServingQuantity.Value : EstimateDefaultServingG(foodName);

        if (product.ServingQuantity is > 0)
            return product.ServingQuantity.Value;

        return EstimateDefaultServingG(foodName);
    }

    // Keep the old name as a forwarding method for binary compat
    internal static decimal EstimateServingWeightG(FoodProductDto product, string unit, string foodName)
        => EstimateUnitWeightG(product, unit, foodName);

    private static bool IsWeightUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "g" or "gram" or "grams" or "kg" or "kilogram" or "kilograms"
            or "oz" or "ounce" or "ounces" or "lb" or "lbs" or "pound" or "pounds"
            or "mg" or "milligram" or "milligrams" => true,
        _ => false
    };

    private static decimal WeightUnitToGrams(string unit) => unit.ToLowerInvariant() switch
    {
        "g" or "gram" or "grams" => 1m,
        "kg" or "kilogram" or "kilograms" => 1000m,
        "oz" or "ounce" or "ounces" => 28.35m,
        "lb" or "lbs" or "pound" or "pounds" => 453.6m,
        "mg" or "milligram" or "milligrams" => 0.001m,
        _ => 1m
    };

    private static bool IsVolumeUnit(string unit) => unit.ToLowerInvariant() switch
    {
        "cup" or "cups" or "tbsp" or "tablespoon" or "tablespoons"
            or "tsp" or "teaspoon" or "teaspoons" or "ml" or "milliliter" or "milliliters"
            or "l" or "liter" or "liters" or "litre" or "litres"
            or "glass" or "glasses" or "bowl" or "bowls"
            or "fl oz" or "fl" or "pint" or "pints" or "quart" or "quarts" or "gallon" or "gallons" => true,
        _ => false
    };

    private static decimal VolumeUnitToGrams(string unit, string foodName) => unit.ToLowerInvariant() switch
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

    private static bool IsCountUnit(string unit) => unit.ToLowerInvariant() switch
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
            or "ring" or "rings" or "stick" or "sticks" => true,
        _ => false
    };

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

    internal static ParsedFoodItemDto CreateGenericEstimate(string foodName, decimal quantity, string unit, decimal sizeMultiplier)
    {
        var servingG = EstimateDefaultServingG(foodName) * sizeMultiplier;
        var totalG = servingG * quantity;
        var cals = EstimateGenericCaloriesPer100g(foodName);
        var scale = totalG / 100m;

        return new ParsedFoodItemDto
        {
            Name = foodName,
            Calories = Math.Round(cals.calories * scale, 1),
            ProteinG = Math.Round(cals.protein * scale, 1),
            CarbsG = Math.Round(cals.carbs * scale, 1),
            FatG = Math.Round(cals.fat * scale, 1),
            FiberG = 0m,
            SugarG = 0m,
            SodiumMg = 0m,
            ServingWeightG = totalG,
            ServingSize = FormatServingSize(quantity, unit),
            ServingQuantity = quantity
        };
    }

    internal static (decimal calories, decimal protein, decimal carbs, decimal fat) EstimateGenericCaloriesPer100g(string foodName)
    {
        var lower = foodName.ToLowerInvariant();

        if (lower.Contains("protein") && (lower.Contains("shake") || lower.Contains("drink") || lower.Contains("smoothie")))
            return (80m, 10m, 5m, 1.5m);
        if (lower.Contains("protein") && lower.Contains("bar"))
            return (350m, 25m, 30m, 12m);
        if (lower.Contains("shake") || lower.Contains("smoothie"))
            return (70m, 3m, 12m, 1.5m);
        if (lower.Contains("juice"))
            return (45m, 0.5m, 10m, 0.1m);
        if (lower.Contains("soda") || lower.Contains("cola") || lower.Contains("pop") || lower.Contains("lemonade"))
            return (40m, 0m, 10m, 0m);
        if (lower.Contains("coffee") || lower.Contains("latte") || lower.Contains("cappuccino") || lower.Contains("espresso"))
            return (40m, 2m, 4m, 2m);
        if (lower.Contains("tea"))
            return (1m, 0m, 0.3m, 0m);
        if (lower.Contains("beer") || lower.Contains("ale") || lower.Contains("lager"))
            return (43m, 0.5m, 3.5m, 0m);
        if (lower.Contains("wine"))
            return (85m, 0.1m, 2.5m, 0m);
        if (lower.Contains("energy drink") || lower.Contains("energy"))
            return (45m, 0m, 11m, 0m);
        if (lower.Contains("milk"))
            return (60m, 3.3m, 5m, 3.2m);
        if (lower.Contains("salad"))
            return (20m, 1.5m, 3m, 0.3m);
        if (lower.Contains("soup") || lower.Contains("broth"))
            return (35m, 2m, 5m, 1m);
        if (lower.Contains("sandwich") || lower.Contains("wrap") || lower.Contains("sub"))
            return (220m, 10m, 25m, 8m);
        if (lower.Contains("pizza"))
            return (270m, 11m, 33m, 10m);
        if (lower.Contains("burger"))
            return (250m, 14m, 20m, 13m);
        if (lower.Contains("fries") || lower.Contains("chips"))
            return (310m, 3.5m, 40m, 15m);
        if (lower.Contains("cake") || lower.Contains("brownie") || lower.Contains("pastry"))
            return (370m, 5m, 50m, 16m);
        if (lower.Contains("cookie") || lower.Contains("biscuit"))
            return (450m, 5m, 60m, 22m);
        if (lower.Contains("candy") || lower.Contains("sweet") || lower.Contains("gummy"))
            return (380m, 2m, 80m, 5m);
        if (lower.Contains("chip") || lower.Contains("crisp") || lower.Contains("snack"))
            return (530m, 6m, 53m, 33m);
        if (lower.Contains("cereal") || lower.Contains("granola") || lower.Contains("muesli"))
            return (370m, 8m, 70m, 7m);

        // Generic food fallback: roughly balanced macros
        return (150m, 5m, 20m, 5m);
    }

    private static decimal ParseWordNumber(string word) => word.ToLowerInvariant() switch
    {
        "a" or "an" or "one" => 1m,
        "two" or "couple" => 2m,
        "three" => 3m,
        "four" => 4m,
        "five" => 5m,
        "six" => 6m,
        "seven" => 7m,
        "eight" => 8m,
        "nine" => 9m,
        "ten" => 10m,
        "eleven" => 11m,
        "twelve" or "dozen" => 12m,
        "fifteen" => 15m,
        "twenty" => 20m,
        "half" => 0.5m,
        "quarter" => 0.25m,
        "few" => 3m,
        "several" => 4m,
        "some" => 2m,
        _ => 1m
    };

    private static decimal Round(decimal? value, decimal scale) =>
        Math.Round((value ?? 0m) * scale, 1);

    // Split on comma, "and", "plus", "with", "&", "+", newline, semicolon, period-followed-by-space, "then"
    [GeneratedRegex(@"\s*(?:,\s*|\s+and\s+|\s+plus\s+|\s+with\s+|\s+then\s+|\s*&\s*|\s*\+\s*|\s*;\s*|\.\s+|\n+)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SplitPattern();

    // Put longer alternations first to avoid partial matches (e.g. "lbs" before "lb", "ounces" before "oz")
    private const string UnitGroup = @"cups?|tablespoons?|tbsp|teaspoons?|tsp|ounces?|oz|grams?|g|kilograms?|kg|lbs|lb|pounds?|slices?|pieces?|milliliters?|ml|liters?|litres?|l|glass(?:es)?|bowls?|handfuls?|servings?|cans?|bottles?|scoops?|bars?|packets?|strips?|fillets?|patt(?:y|ies)|wings?|thighs?|drumsticks?|breasts?|cloves?|stalks?|sprigs?|lea(?:f|ves)|wedges?|chunks?|rings?|sticks?|pints?|quarts?|gallons?|fl\s?oz";

    [GeneratedRegex(@$"^(?<word>a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|half|quarter|dozen|couple|few|several|some)\s+(?:(?<unit>{UnitGroup})\s+)?(?<food>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WordQuantityPattern();

    [GeneratedRegex(@$"^(?<qty>\d+\.?\d*)\s*(?<unit>{UnitGroup})?\s*(?<food>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericQuantityPattern();

    [GeneratedRegex(@$"^(?:(?<whole>\d+)\s+)?(?<num>\d+)/(?<den>\d+)\s*(?<unit>{UnitGroup})?\s*(?<food>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"^(?:some|about|around|approximately|roughly|maybe|like|just|probably)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFillerWordPattern();

    [GeneratedRegex(@"^(?:(?:for\s+)?(?:breakfast|lunch|dinner|supper|brunch|snack|my\s+snack|my\s+meal)\s+)?(?:i\s+)?(?:just\s+)?(?:had|ate|eaten|consumed|grabbed|munched|snacked\s+on|drank|drunk|downed)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFillerPattern();

    [GeneratedRegex(@"\s*\([^)]*\)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ParentheticalPattern();

    [GeneratedRegex(@"\s+(?:on the side|on top|for dessert|for dinner|for lunch|for breakfast|for supper|for snack|this morning|tonight|yesterday|today|last night|earlier)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingPrepPattern();

    [GeneratedRegex(@"^(?<size>small|mini|tiny|medium|med|large|big|lg|extra[\s-]?large|xl|huge|jumbo)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SizeModifierPattern();

    [GeneratedRegex(@"^(?:and|or)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingAndOrPattern();

    internal static FoodProductDto? PickBestMatch(List<FoodProductDto> results, string query)
    {
        if (results.Count == 0) return null;
        if (results.Count == 1) return results[0];

        var queryLower = query.ToLowerInvariant().Trim();
        var queryTokens = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var queryNorm = FoodSearchIndex.NormalizeFoodName(query);
        var normTokens = queryNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return results
            .OrderByDescending(r => ScoreMatch(r, queryLower, queryTokens, queryNorm, normTokens))
            .First();
    }

    internal static decimal ComputeConfidence(List<FoodProductDto> results, FoodProductDto chosen, string query)
    {
        if (results.Count == 0) return 0m;

        var queryLower = query.ToLowerInvariant().Trim();
        var queryTokens = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var queryNorm = FoodSearchIndex.NormalizeFoodName(query);
        var normTokens = queryNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameNorm = FoodSearchIndex.NormalizeFoodName(chosen.Name);
        var nameLower = chosen.Name.ToLowerInvariant();

        if (nameLower == queryLower || nameNorm == queryNorm)
            return 1.0m;

        var nameNormTokens = nameNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int matched = 0;
        foreach (var qt in normTokens)
        {
            foreach (var nt in nameNormTokens)
            {
                if (nt == qt || nt.StartsWith(qt) || qt.StartsWith(nt))
                { matched++; break; }
            }
        }
        float coverage = normTokens.Length > 0 ? (float)matched / normTokens.Length : 0f;

        if (coverage >= 1f && nameLower.StartsWith(queryLower))
            return 0.95m;
        if (coverage >= 1f)
            return 0.85m;
        if (coverage >= 0.5f)
            return 0.6m;

        string[] penaltyTerms = [
            "frozen", "canned", "dehydrated", "powder", "powdered",
            "mix", "mixture", "substitute", "imitation", "instant",
            "baby food", "infant", "formula",
            "alaska native", "industrial", "fast food",
            "ns as to", "usda commodity", "as purchased", "not further specified",
            "nfs", "ready-to-eat", "glucose reduced", "stabilized"
        ];
        bool hasPenalty = penaltyTerms.Any(t => nameLower.Contains(t));
        if (hasPenalty)
            return Math.Max(0.1m, (decimal)coverage * 0.3m);

        return Math.Max(0.2m, (decimal)coverage * 0.5m);
    }

    private static double ScoreMatch(FoodProductDto food, string queryLower, string[] queryTokens,
        string queryNorm, string[] normTokens)
    {
        var name = food.Name;
        var nameLower = name.ToLowerInvariant();
        var nameNorm = FoodSearchIndex.NormalizeFoodName(name);
        var nameNormTokens = nameNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double score = 0;

        // Exact match is perfect
        if (nameLower == queryLower || nameNorm == queryNorm)
            return 1000;

        // --- Token coverage (biggest differentiator) ---
        int matched = 0;
        foreach (var qt in normTokens)
        {
            foreach (var nt in nameNormTokens)
            {
                if (nt == qt || nt.StartsWith(qt) || qt.StartsWith(nt))
                { matched++; break; }
            }
        }
        float coverage = normTokens.Length > 0 ? (float)matched / normTokens.Length : 0f;
        score += coverage * 80;
        if (coverage >= 1f) score += 30;

        // --- Token order bonus ---
        if (normTokens.Length > 1 && matched > 1)
        {
            int lastIdx = -1;
            bool inOrder = true;
            foreach (var qt in normTokens)
            {
                int foundAt = -1;
                for (int i = 0; i < nameNormTokens.Length; i++)
                {
                    if (nameNormTokens[i] == qt || nameNormTokens[i].StartsWith(qt))
                    { foundAt = i; break; }
                }
                if (foundAt >= 0)
                {
                    if (foundAt <= lastIdx) { inOrder = false; break; }
                    lastIdx = foundAt;
                }
            }
            if (inOrder) score += 20;
        }

        // Strong bonus: name starts with query
        if (nameLower.StartsWith(queryLower))
            score += 100;
        if (nameNorm.StartsWith(queryNorm))
            score += 80;

        // --- Name length: prefer shorter, non-linear penalty for long ---
        if (name.Length <= 30)
            score += Math.Max(0, 50 - name.Length * 0.5);
        else
            score += 50 - 15 - (name.Length - 30) * (name.Length - 30) / 40.0;

        // Fewer commas
        var commaCount = name.Count(c => c == ',');
        score -= commaCount * 6;

        // Parenthetical penalty
        var parenCount = name.Count(c => c == '(');
        score -= parenCount * 8;

        // --- Hard weirdness penalties ---
        string[] hardPenalty = [
            "frozen", "canned", "dehydrated", "powder", "powdered",
            "mix", "mixture", "substitute", "imitation", "instant",
            "baby food", "infant", "formula",
            "alaska native", "industrial", "fast food",
            "ns as to", "usda commodity", "as purchased", "not further specified",
            "nfs", "ready-to-eat", "glucose reduced", "stabilized"
        ];
        foreach (var term in hardPenalty)
            if (nameLower.Contains(term))
                score -= 35;

        string[] softPenalty = [
            "navajo", "hopi", "southwest", "shoshone", "apache",
            "pasteurized", "restaurant", "commercial", "institutional"
        ];
        foreach (var term in softPenalty)
            if (nameLower.Contains(term))
                score -= 18;

        // --- Preparation modifier handling ---
        // For simple queries (1-2 tokens), treat prep terms as secondary
        string[] prepTerms = ["raw", "fresh", "whole", "plain"];
        if (queryTokens.Length <= 2)
        {
            foreach (var term in prepTerms)
                if (nameLower.Contains(term))
                    score += 8;
        }

        // --- Data quality ---
        if (food.DataSource == "USDA")
            score += 10;
        if (food.Calories100g.HasValue)
            score += 5;
        if (food.Protein100g.HasValue && food.Carbs100g.HasValue && food.Fat100g.HasValue)
            score += 5;

        // --- Nutrition plausibility ---
        score += NutritionPlausibilityScore(food, queryLower);

        // --- Brand penalty for generic queries ---
        if (!string.IsNullOrEmpty(food.Brand) && food.Brand.Length > 1)
        {
            bool queryMentionsBrand = queryTokens.Any(t =>
                food.Brand.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!queryMentionsBrand)
                score -= 10;
        }

        return score;
    }

    private static double NutritionPlausibilityScore(FoodProductDto dto, string queryLower)
    {
        if (!dto.Calories100g.HasValue) return 0;

        double penalty = 0;
        var cal = dto.Calories100g.Value;
        var protein = dto.Protein100g ?? 0m;
        var carbs = dto.Carbs100g ?? 0m;
        var fat = dto.Fat100g ?? 0m;

        if (queryLower.Contains("chicken") || queryLower.Contains("beef") ||
            queryLower.Contains("fish") || queryLower.Contains("turkey") ||
            queryLower.Contains("pork") || queryLower.Contains("lamb") ||
            queryLower.Contains("steak") || queryLower.Contains("salmon"))
        {
            if (carbs > 40m) penalty -= 15;
            if (protein < 5m && cal > 50m) penalty -= 10;
        }

        if (queryLower.Contains("oil") || queryLower.Contains("butter") || queryLower.Contains("lard"))
        {
            if (fat < 20m && cal > 100m) penalty -= 15;
        }

        if (queryLower.Contains("lettuce") || queryLower.Contains("spinach") ||
            queryLower.Contains("kale") || queryLower.Contains("celery") ||
            queryLower.Contains("cucumber"))
        {
            if (cal > 100m) penalty -= 15;
        }

        if (queryLower.Contains("juice") || queryLower.Contains("water") ||
            queryLower.Contains("tea") || queryLower.Contains("coffee"))
        {
            if (fat > 20m) penalty -= 10;
        }

        return penalty;
    }
}
