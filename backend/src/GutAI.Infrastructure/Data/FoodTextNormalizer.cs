namespace GutAI.Infrastructure.Data;

/// <summary>
/// Single source of truth for food-name/query text normalization: tokenization,
/// depluralization, primary-noun extraction, and synonym expansion. Used symmetrically
/// by both indexing (candidate names) and search (queries) so matching stays consistent —
/// replaces the previous split between the Lucene analyzer's synonym/stemmer pipeline and
/// a separate ad hoc normalizer used only by the NLP fallback matcher.
/// </summary>
internal static class FoodTextNormalizer
{
    private static readonly char[] Delimiters = [' ', ',', '(', ')', '/', '-'];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "and", "in", "of", "style", "flavored", "flavoured",
        "ns", "as", "to", "the", "for", "a", "an",
        "nfs", "not", "further", "specified", "type", "all", "purpose",
        "usda", "commodity", "purchased", "commercially", "prepared",
        "ready", "eat",
    };

    /// <summary>Splits on food-name delimiters, lowercases, and drops empty entries. No stop-word removal —
    /// used for query token extraction where every token (even short/common ones) may matter.</summary>
    public static string[] Tokenize(string text) =>
        text.ToLowerInvariant().Split(Delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// USDA convention: "PrimaryNoun, descriptor, descriptor" → returns "PrimaryNoun".
    /// </summary>
    public static string ExtractPrimaryNoun(string name)
    {
        var commaIdx = name.IndexOf(',');
        return commaIdx > 0 ? name[..commaIdx].Trim() : name.Trim();
    }

    /// <summary>
    /// Normalizes a food name for fuzzy comparison: strips punctuation/parens, removes stop
    /// words, depluralizes every token. Used for whole-name fuzzy comparison (NLP fallback matching).
    /// </summary>
    public static string NormalizeFoodName(string name)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"\([^)]*\)", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[,;:/\-]", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9 ]", "");
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens.Where(t => !StopWords.Contains(t)).Select(Depluralize));
    }

    /// <summary>Lightweight suffix-based depluralization tuned for short food-noun phrases —
    /// deliberately not full stemming; food search queries are noun phrases, not conjugated
    /// verbs, so plural normalization plus the explicit synonym table (<see cref="FoodSynonyms"/>)
    /// covers the realistic query space without a Porter-stemmer dependency.</summary>
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
        // -ches, -shes → strip "es" (matches→match, dishes→dish, wishes→wish)
        if ((word.EndsWith("ches") || word.EndsWith("shes")) && word.Length > 4)
            return word[..^2];
        // Remaining -es words (e.g. boxes→box) — ches/shes/ses/oes already handled above.
        if (word.EndsWith("es") && word.Length > 4)
            return word[..^1];
        if (word.EndsWith('s') && !word.EndsWith("ss") && !word.EndsWith("us") && !word.EndsWith("is"))
            return word[..^1];
        return word;
    }

    /// <summary>Depluralizes every token in an array.</summary>
    public static string[] DepluralizeAll(string[] tokens) => tokens.Select(Depluralize).ToArray();
}
