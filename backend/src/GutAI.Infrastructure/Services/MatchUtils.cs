using System.Text.RegularExpressions;

namespace GutAI.Infrastructure.Services;

public static class MatchUtils
{
    static readonly Regex LactoseFreeRegex = new(@"lactose[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex GlutenFreeRegex = new(@"gluten[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex DairyFreeRegex = new(@"dairy[\s-]*free", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static Regex WordBoundary(string pattern)
        => new(@"\b" + Regex.Escape(pattern) + @"\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool WordMatch(string text, string pattern, Regex? regex)
        => regex != null ? regex.IsMatch(text) : text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    public static bool IsLactoseFree(string text) => LactoseFreeRegex.IsMatch(text);
    public static bool IsGlutenFree(string text) => GlutenFreeRegex.IsMatch(text);
    public static bool IsDairyFree(string text) => DairyFreeRegex.IsMatch(text);
}
