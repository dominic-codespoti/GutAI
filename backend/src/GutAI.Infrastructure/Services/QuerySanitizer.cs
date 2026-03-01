using System.Text.RegularExpressions;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Strips quantities, units, filler words, and meal context from raw user input,
/// returning just the food name suitable for search/Lucene queries.
/// Reuses patterns from NaturalLanguageFallbackService.
/// </summary>
public static partial class QuerySanitizer
{
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = raw.Trim();

        // Strip meal-context preamble: "for breakfast I had ...", "I just ate ..."
        s = MealPreamblePattern().Replace(s, "").Trim();

        // Strip leading filler: "some", "about", "around", "approximately", etc.
        s = LeadingFillerPattern().Replace(s, "").Trim();

        // Strip leading numeric quantity + optional unit: "2 cups of", "100g", "3 slices of"
        s = LeadingQuantityPattern().Replace(s, "").Trim();

        // Strip leading fraction + optional unit: "1/2 cup of"
        s = LeadingFractionPattern().Replace(s, "").Trim();

        // Strip leading word-number + optional unit: "two slices of"
        s = LeadingWordQuantityPattern().Replace(s, "").Trim();

        // Strip "of" prefix left behind: "of toast" → "toast"
        if (s.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
            s = s[3..].Trim();

        // Strip trailing meal context: "for dinner", "this morning", etc.
        s = TrailingContextPattern().Replace(s, "").Trim();

        // Strip parentheticals: "(grilled)", "(raw)"
        s = ParentheticalPattern().Replace(s, " ").Trim();

        // Strip size modifiers: "large", "small", "medium" at start
        s = SizeModifierPattern().Replace(s, "").Trim();

        // Collapse whitespace
        s = WhitespacePattern().Replace(s, " ").Trim();

        return s;
    }

    // "for breakfast I had", "I just ate", "I consumed", "had"
    [GeneratedRegex(@"^(?:(?:for\s+)?(?:breakfast|lunch|dinner|supper|brunch|snack|my\s+(?:snack|meal))\s+)?(?:i\s+)?(?:just\s+)?(?:had|ate|eaten|consumed|grabbed|munched|snacked\s+on|drank|drunk|downed)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex MealPreamblePattern();

    // "some", "about", "around", "approximately", "roughly", "maybe", "like", "just", "probably"
    [GeneratedRegex(@"^(?:some|about|around|approximately|roughly|maybe|like|just|probably|a\s+bit\s+of|a\s+little)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFillerPattern();

    private const string UnitGroup =
        @"(?:g|mg|kg|oz|ounce|ounces|lb|lbs|pound|pounds|cup|cups|"
        + @"tbsp|tablespoon|tablespoons|tsp|teaspoon|teaspoons|"
        + @"ml|milliliter|milliliters|l|liter|liters|litre|litres|"
        + @"slice|slices|piece|pieces|serving|servings|"
        + @"handful|handfuls|pinch|pinches|dash|dashes|"
        + @"can|cans|bottle|bottles|glass|glasses|bowl|bowls|"
        + @"strip|strips|fillet|fillets|rasher|rashers)s?";

    // "2 cups of", "100g", "250ml", "3 slices of"
    [GeneratedRegex(@$"^\d+\.?\d*\s*(?:{UnitGroup})?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingQuantityPattern();

    // "1/2 cup of", "3/4 of"
    [GeneratedRegex(@$"^(?:\d+\s+)?\d+/\d+\s*(?:{UnitGroup})?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFractionPattern();

    // "two slices of", "a cup of", "half a"
    [GeneratedRegex(@$"^(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|half|quarter|dozen|couple|few|several|some)\s+(?:(?:{UnitGroup})\s+)?(?:of\s+)?(?:a\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingWordQuantityPattern();

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
