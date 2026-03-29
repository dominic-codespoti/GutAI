namespace GutAI.Infrastructure.Services;

/// <summary>
/// Provides unit normalization and conversion utilities for nutrition label parsing.
/// Handles various unit formats, fuzzy matching for OCR errors, and standardization.
/// </summary>
public static class UnitNormalizationService
{
    // Standard unit constants
    public const string GRAMS = "g";
    public const string MILLILITERS = "ml";
    public const string KILOGRAMS = "kg";
    public const string MILLIGRAMS = "mg";
    public const string OUNCES = "oz";
    public const string FLUID_OUNCES = "fl oz";
    public const string LITERS = "L";
    public const string CUPS = "cup";
    public const string TABLESPOONS = "tbsp";
    public const string TEASPOONS = "tsp";
    public const string PIECES = "piece";
    public const string SERVINGS = "serving";
    public const string INTERNATIONAL_UNITS = "IU";
    public const string MICROGRAMS = "mcg";

    /// <summary>
    /// Maps various unit aliases to standardized units.
    /// Key: Raw unit string from OCR/label
    /// Value: Normalized standard unit
    /// </summary>
    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Weight - Grams
        ["g"] = GRAMS,
        ["gram"] = GRAMS,
        ["grams"] = GRAMS,
        ["grammes"] = GRAMS,
        ["gm"] = GRAMS,
        ["gr"] = GRAMS,
        ["grm"] = GRAMS,
        ["grms"] = GRAMS,
        
        // Weight - Kilograms
        ["kg"] = KILOGRAMS,
        ["kilogram"] = KILOGRAMS,
        ["kilograms"] = KILOGRAMS,
        ["kilo"] = KILOGRAMS,
        ["kilos"] = KILOGRAMS,
        ["kgs"] = KILOGRAMS,
        
        // Weight - Milligrams
        ["mg"] = MILLIGRAMS,
        ["milligram"] = MILLIGRAMS,
        ["milligrams"] = MILLIGRAMS,
        ["milligrm"] = MILLIGRAMS,
        
        // Volume - Milliliters
        ["ml"] = MILLILITERS,
        ["milliliter"] = MILLILITERS,
        ["milliliters"] = MILLILITERS,
        ["millilitre"] = MILLILITERS,
        ["millilitres"] = MILLILITERS,
        ["cc"] = MILLILITERS,
        ["mL"] = MILLILITERS,
        ["mili"] = MILLILITERS,
        ["mil"] = MILLILITERS,
        
        // Volume - Liters
        ["l"] = LITERS,
        ["liter"] = LITERS,
        ["liters"] = LITERS,
        ["litre"] = LITERS,
        ["litres"] = LITERS,
        ["L"] = LITERS,
        ["ltr"] = LITERS,
        ["ltrs"] = LITERS,
        
        // Volume - Fluid Ounces
        ["fl oz"] = FLUID_OUNCES,
        ["floz"] = FLUID_OUNCES,
        ["fluid oz"] = FLUID_OUNCES,
        ["fluid ounce"] = FLUID_OUNCES,
        ["fluid ounces"] = FLUID_OUNCES,
        ["fl. oz"] = FLUID_OUNCES,
        ["fl.oz"] = FLUID_OUNCES,
        ["fluidoz"] = FLUID_OUNCES,
        
        // Weight - Ounces
        ["oz"] = OUNCES,
        ["ounce"] = OUNCES,
        ["ounces"] = OUNCES,
        ["ozs"] = OUNCES,
        
        // US Customary - Cups
        ["cup"] = CUPS,
        ["cups"] = CUPS,
        ["c"] = CUPS,
        ["cp"] = CUPS,
        
        // US Customary - Tablespoons
        ["tbsp"] = TABLESPOONS,
        ["tablespoon"] = TABLESPOONS,
        ["tablespoons"] = TABLESPOONS,
        ["tbs"] = TABLESPOONS,
        ["tblsp"] = TABLESPOONS,
        ["tbl"] = TABLESPOONS,
        ["tb"] = TABLESPOONS,
        
        // US Customary - Teaspoons
        ["tsp"] = TEASPOONS,
        ["teaspoon"] = TEASPOONS,
        ["teaspoons"] = TEASPOONS,
        ["tsps"] = TEASPOONS,
        ["tspn"] = TEASPOONS,
        
        // Count units
        ["piece"] = PIECES,
        ["pieces"] = PIECES,
        ["pc"] = PIECES,
        ["pcs"] = PIECES,
        ["pkt"] = PIECES,
        ["pkts"] = PIECES,
        ["pack"] = PIECES,
        ["packs"] = PIECES,
        ["sachet"] = PIECES,
        ["sachets"] = PIECES,
        ["bar"] = PIECES,
        ["bars"] = PIECES,
        ["biscuit"] = PIECES,
        ["biscuits"] = PIECES,
        ["cookie"] = PIECES,
        ["cookies"] = PIECES,
        
        // Servings
        ["serving"] = SERVINGS,
        ["servings"] = SERVINGS,
        ["srv"] = SERVINGS,
        ["srvs"] = SERVINGS,
        ["serve"] = SERVINGS,
        ["serves"] = SERVINGS,
        
        // International Units (Vitamins A, D, E)
        ["iu"] = INTERNATIONAL_UNITS,
        ["IU"] = INTERNATIONAL_UNITS,
        ["international unit"] = INTERNATIONAL_UNITS,
        ["international units"] = INTERNATIONAL_UNITS,
        
        // Micrograms
        ["mcg"] = MICROGRAMS,
        ["µg"] = MICROGRAMS,
        ["microgram"] = MICROGRAMS,
        ["micrograms"] = MICROGRAMS,
        ["ug"] = MICROGRAMS,
    };

    /// <summary>
    /// Normalizes a raw unit string to a standard unit.
    /// Uses fuzzy matching for common OCR errors.
    /// </summary>
    /// <param name="rawUnit">The raw unit string from OCR or label</param>
    /// <returns>Normalized standard unit, defaults to "g" if unrecognized</returns>
    public static string Normalize(string? rawUnit)
    {
        if (string.IsNullOrWhiteSpace(rawUnit))
            return GRAMS;

        var cleaned = rawUnit.Trim().ToLowerInvariant();

        // Direct match
        if (UnitAliases.TryGetValue(cleaned, out var normalized))
            return normalized;

        // Try removing punctuation and retry
        var cleanedNoPunct = new string(cleaned.Where(c => !char.IsPunctuation(c)).ToArray());
        if (UnitAliases.TryGetValue(cleanedNoPunct, out normalized))
            return normalized;

        // Fuzzy matching for OCR errors (Levenshtein distance <= 2)
        var closestMatch = FindClosestMatch(cleaned);
        if (closestMatch != null)
            return UnitAliases[closestMatch];

        // Return cleaned version if no match (preserve original intent)
        return cleaned;
    }

    /// <summary>
    /// Converts a serving amount to grams for comparison purposes.
    /// Uses standard conversion factors.
    /// </summary>
    /// <param name="amount">The numeric amount</param>
    /// <param name="unit">The unit (will be normalized)</param>
    /// <returns>Equivalent amount in grams, or original amount if conversion unknown</returns>
    public static decimal ConvertToGrams(decimal amount, string unit)
    {
        var normalized = Normalize(unit);

        return normalized switch
        {
            "g" => amount,
            "kg" => amount * 1000m,
            "mg" => amount / 1000m,
            "oz" => amount * 28.3495m,
            "fl oz" => amount * 29.5735m,
            "ml" => amount,
            "L" => amount * 1000m,
            "cup" => amount * 240m,
            "tbsp" => amount * 15m,
            "tsp" => amount * 5m,
            "piece" => amount,
            "serving" => amount,
            _ => amount
        };
    }

    /// <summary>
    /// Parses a serving size string into amount and unit components.
    /// Handles formats like "33 pieces (28g)", "1 cup", "2.5 fl oz", etc.
    /// </summary>
    /// <param name="input">Raw serving size string</param>
    /// <returns>Tuple of (amount, rawUnit, normalizedUnit)</returns>
    public static (decimal amount, string rawUnit, string normalizedUnit) ParseServingSize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (0m, "", GRAMS);

        // Remove parentheses content for initial parsing
        var mainPart = input.Split('(')[0].Trim();
        
        // Try to find the first number anywhere in the string
        var numberMatch = System.Text.RegularExpressions.Regex.Match(mainPart, @"(\d+(?:\.\d+)?)");
        decimal amount = 0m;
        string unit = "";

        if (numberMatch.Success)
        {
            amount = decimal.Parse(numberMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            unit = mainPart[(numberMatch.Index + numberMatch.Length)..].Trim();
        }
        else
        {
            // No number found, treat entire string as unit
            unit = mainPart;
        }

        // If unit is empty but there was parenthetical content, try parsing that
        if (string.IsNullOrWhiteSpace(unit) && input.Contains('('))
        {
            var parenMatch = System.Text.RegularExpressions.Regex.Match(input, @"\((\d+(?:\.\d+)?)\s*(\w+)\)");
            if (parenMatch.Success)
            {
                amount = decimal.Parse(parenMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                unit = parenMatch.Groups[2].Value;
            }
        }

        var normalized = Normalize(unit);
        return (amount, unit, normalized);
    }

    /// <summary>
    /// Finds the closest matching unit using Levenshtein distance.
    /// Returns match if distance <= 2 (handles single character errors, swaps, insertions, deletions).
    /// </summary>
    private static string? FindClosestMatch(string input)
    {
        const int maxDistance = 2;
        
        foreach (var kvp in UnitAliases)
        {
            var distance = LevenshteinDistance(input, kvp.Key);
            if (distance <= maxDistance)
                return kvp.Key;
        }
        
        return null;
    }

    /// <summary>
    /// Calculates Levenshtein distance between two strings.
    /// Used for fuzzy matching of OCR errors.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a?.Length ?? 0;

        var matrix = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[a.Length, b.Length];
    }
}
