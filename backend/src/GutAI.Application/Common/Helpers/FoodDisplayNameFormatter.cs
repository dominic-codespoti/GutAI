using System.Linq;
using System.Globalization;

namespace GutAI.Application.Common.Helpers;

/// <summary>Formats provider food names for user-facing persistence and logs.</summary>
public static class FoodDisplayNameFormatter
{
    public static string ToTitleCase(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return string.Empty;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            // Preserve short provider acronyms such as USDA and AU.
            if (words[i].Length <= 4 && words[i].All(char.IsLetter) && words[i].All(char.IsUpper))
                continue;

            words[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[i].ToLowerInvariant());
        }

        return string.Join(' ', words);
    }
}
