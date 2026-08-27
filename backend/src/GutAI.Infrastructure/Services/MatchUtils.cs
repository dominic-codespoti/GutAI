using System.Text.RegularExpressions;

namespace GutAI.Infrastructure.Services;

public static class MatchUtils
{
    static readonly Regex LactoseFreeRegex = new(@"lactose[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex GlutenFreeRegex = new(@"gluten[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex DairyFreeRegex = new(@"dairy[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "not lactose-free", "non-gluten-free" etc. previously matched the claim regex and
    // wrongly suppressed every trigger of that class — a bare substring claim test.
    static readonly Regex NotLactoseFreeRegex = new(@"\b(?:not|never)\s+(?:lactose[\s-]*free)|(?:\bnon-?)\s*lactose[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex NotGlutenFreeRegex = new(@"\b(?:not|never)\s+(?:gluten[\s-]*free)|(?:\bnon-?)\s*gluten[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex NotDairyFreeRegex = new(@"\b(?:not|never)\s+(?:dairy[\s-]*free)|(?:\bnon-?)\s*dairy[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static Regex WordBoundary(string pattern)
        => new(@"\b" + Regex.Escape(pattern) + @"\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool WordMatch(string text, string pattern, Regex? regex)
        => regex != null ? regex.IsMatch(text) : text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    /// <summary>True only when the claim is present AND not negated nearby
    /// ("not lactose-free", "non-dairy-free") — negated claims must not suppress triggers.</summary>
    public static bool IsLactoseFree(string text) => LactoseFreeRegex.IsMatch(text) && !NotLactoseFreeRegex.IsMatch(text);
    public static bool IsGlutenFree(string text) => GlutenFreeRegex.IsMatch(text) && !NotGlutenFreeRegex.IsMatch(text);
    public static bool IsDairyFree(string text) => DairyFreeRegex.IsMatch(text) && !NotDairyFreeRegex.IsMatch(text);
}
