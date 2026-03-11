using System.Text.RegularExpressions;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Result of sanitizing a raw food query, including any extracted serving info.
/// </summary>
public record SanitizeResult(
    string Query,
    decimal Quantity,
    string? Unit,
    decimal? EstimatedGrams);

/// <summary>
/// Strips quantities, units, filler words, and meal context from raw user input,
/// returning just the food name suitable for search/Lucene queries.
/// Reuses patterns from NaturalLanguageFallbackService.
/// </summary>
public static partial class QuerySanitizer
{
    public static string Sanitize(string raw)
        => SanitizeWithServing(raw).Query;

    public static SanitizeResult SanitizeWithServing(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new SanitizeResult(string.Empty, 1m, null, null);

        var s = raw.Trim();

        // Strip meal-context preamble: "for breakfast I had ...", "I just ate ..."
        s = MealPreamblePattern().Replace(s, "").Trim();

        // Strip leading filler: "some", "about", "around", "approximately", etc.
        s = LeadingFillerPattern().Replace(s, "").Trim();

        // --- Extract quantity + unit BEFORE stripping them ---
        // Order matters: try fraction BEFORE numeric to avoid "1/2" matching as "1"
        decimal quantity = 1m;
        string? unit = null;

        // Try fraction first: "1/2 cup of", "3/4 of"
        var fracMatch = LeadingFractionCapture().Match(s);
        if (fracMatch.Success)
        {
            var whole = string.IsNullOrEmpty(fracMatch.Groups["whole"].Value) ? 0m : decimal.Parse(fracMatch.Groups["whole"].Value);
            var num = decimal.Parse(fracMatch.Groups["num"].Value);
            var den = decimal.Parse(fracMatch.Groups["den"].Value);
            quantity = whole + (den != 0 ? num / den : 0);
            var u = fracMatch.Groups["unit"].Value.Trim();
            if (!string.IsNullOrEmpty(u))
                unit = u.Trim();
        }
        else
        {
            // Try numeric: "2 cups of", "100g", "250ml"
            var numMatch = LeadingQuantityCapture().Match(s);
            if (numMatch.Success)
            {
                quantity = decimal.Parse(numMatch.Groups["qty"].Value);
                var u = numMatch.Groups["unit"].Value.Trim();
                if (!string.IsNullOrEmpty(u))
                    unit = u.Trim();
            }
            else
            {
                // Try word-number: "two cups of", "a glass of"
                var wordMatch = LeadingWordQuantityCapture().Match(s);
                if (wordMatch.Success)
                {
                    quantity = ParseWordNumber(wordMatch.Groups["word"].Value);
                    var u = wordMatch.Groups["unit"].Value.Trim();
                    if (!string.IsNullOrEmpty(u))
                        unit = u.Trim();
                }
                else
                {
                    // Try bare unit: "glass of juice", "bowl of soup" (no quantity prefix)
                    var bareMatch = BareUnitCapture().Match(s);
                    if (bareMatch.Success)
                    {
                        unit = bareMatch.Groups["unit"].Value.Trim();
                        quantity = 1m;
                    }
                }
            }
        }

        // Now strip them — order matters: fraction before numeric
        s = LeadingFractionPattern().Replace(s, "").Trim();
        s = LeadingQuantityPattern().Replace(s, "").Trim();
        s = LeadingWordQuantityPattern().Replace(s, "").Trim();
        s = BareUnitPattern().Replace(s, "").Trim();

        // Strip "of" prefix left behind: "of toast" → "toast"
        if (s.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
            s = s[3..].Trim();

        // Strip trailing meal context: "for dinner", "this morning", etc.
        s = TrailingContextPattern().Replace(s, "").Trim();

        // Strip parentheticals: "(grilled)", "(raw)"
        s = ParentheticalPattern().Replace(s, " ").Trim();

        // Check for size modifier before stripping it
        decimal sizeMultiplier = 1m;
        var sizeMatch = SizeModifierPattern().Match(s);
        if (sizeMatch.Success)
        {
            sizeMultiplier = sizeMatch.Value.Trim().ToLowerInvariant() switch
            {
                "small" or "mini" or "tiny" => 0.7m,
                "medium" or "med" => 1m,
                "large" or "big" or "lg" => 1.3m,
                var x when x.StartsWith("extra") || x is "xl" or "huge" or "jumbo" => 1.5m,
                _ => 1m
            };
        }
        s = SizeModifierPattern().Replace(s, "").Trim();

        // Collapse whitespace
        s = WhitespacePattern().Replace(s, " ").Trim();

        // Estimate grams from unit + food name
        decimal? estimatedGrams = null;
        if (unit is not null && s.Length > 0)
        {
            var perUnit = ServingEstimator.EstimateUnitGrams(unit, s);
            if (perUnit is not null)
                estimatedGrams = Math.Round(perUnit.Value * quantity * sizeMultiplier, 1);
        }

        return new SanitizeResult(s, quantity, unit, estimatedGrams);
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

    // "for breakfast I had", "I just ate", "I consumed", "had"
    [GeneratedRegex(@"^(?:(?:for\s+)?(?:breakfast|lunch|dinner|supper|brunch|snack|my\s+(?:snack|meal))\s+)?(?:i\s+)?(?:just\s+)?(?:had|ate|eaten|consumed|grabbed|munched|snacked\s+on|drank|drunk|downed)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex MealPreamblePattern();

    // "some", "about", "around", "approximately", "roughly", "maybe", "like", "just", "probably"
    [GeneratedRegex(@"^(?:some|about|around|approximately|roughly|maybe|like|just|probably|a\s+bit\s+of|a\s+little)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFillerPattern();

    private const string UnitGroup =
        @"(?:glasses|glass|gallons|gallon|grams|gram|"
        + @"kg|kilograms|kilogram|mg|milligrams|milligram|g|"
        + @"ounces|ounce|oz|lbs|lb|pounds|pound|"
        + @"cups|cup|tablespoons|tablespoon|tbsp|teaspoons|teaspoon|tsp|"
        + @"milliliters|milliliter|ml|litres|litre|liters|liter|l|"
        + @"slices|slice|pieces|piece|servings|serving|"
        + @"handfuls|handful|pinches|pinch|dashes|dash|"
        + @"cans|can|bottles|bottle|bowls|bowl|"
        + @"strips|strip|fillets|fillet|rashers|rasher|"
        + @"scoops|scoop|pints|pint|quarts|quart|fl\s?oz)";

    // "2 cups of", "100g", "250ml", "3 slices of" — non-capturing (for stripping)
    [GeneratedRegex(@$"^\d+\.?\d*\s*(?:{UnitGroup})?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingQuantityPattern();

    // Capturing version to extract quantity + unit
    [GeneratedRegex(@$"^(?<qty>\d+\.?\d*)\s*(?<unit>{UnitGroup})?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingQuantityCapture();

    // "1/2 cup of", "3/4 of" — non-capturing
    [GeneratedRegex(@$"^(?:\d+\s+)?\d+/\d+\s*(?:{UnitGroup})?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFractionPattern();

    // Capturing version for fractions
    [GeneratedRegex(@$"^(?:(?<whole>\d+)\s+)?(?<num>\d+)/(?<den>\d+)\s*(?<unit>{UnitGroup})?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFractionCapture();

    // "two slices of", "a cup of", "half a cup of" — non-capturing
    [GeneratedRegex(@$"^(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|half|quarter|dozen|couple|few|several|some)\s+(?:a\s+)?(?:(?:{UnitGroup})\s+)?(?:of\s+)?(?:a\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingWordQuantityPattern();

    // Capturing version for word-numbers
    [GeneratedRegex(@$"^(?<word>a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|half|quarter|dozen|couple|few|several|some)\s+(?:a\s+)?(?:(?<unit>{UnitGroup})\s+)?(?:of\s+)?(?:a\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingWordQuantityCapture();

    // Bare unit without quantity prefix: "glass of juice", "bowl of soup", "pint of beer"
    [GeneratedRegex(@$"^(?:{UnitGroup})\s+(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex BareUnitPattern();

    // Capturing version for bare unit
    [GeneratedRegex(@$"^(?<unit>{UnitGroup})\s+(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex BareUnitCapture();

    // "for dinner", "this morning", "yesterday", "tonight"
    [GeneratedRegex(@"\s+(?:on the side|on top|for dessert|for dinner|for lunch|for breakfast|for supper|for snack|this morning|tonight|yesterday|today|last night|earlier)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingContextPattern();

    // "(grilled)", "(raw)"
    [GeneratedRegex(@"\([^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ParentheticalPattern();

    // "small", "medium", "large" etc. at start
    [GeneratedRegex(@"^(?:small|mini|tiny|medium|med|large|big|lg|extra[\s-]?large|xl|huge|jumbo)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SizeModifierPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespacePattern();
}
