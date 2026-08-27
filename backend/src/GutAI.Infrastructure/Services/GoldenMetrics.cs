using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Pure metric computation for the golden-image regression gate.
/// Unit-tested without any AI involvement; the harness only supplies IO.
///
/// Matching rule: a scanned component matches an expected component when their
/// normalized names overlap (token Jaccard ≥ 0.5), or the scanned name is a superstring
/// of the expected name (the model was at least as specific as expected).
/// Normalization: lowercase, strip punctuation, drop generic filler tokens
/// ("of", "with", "a", "the", "some", "piece", "pieces", "side").
/// </summary>
public static class GoldenMetrics
{
    private static readonly string[] StopTokens =
        ["of", "with", "a", "an", "the", "some", "piece", "pieces", "side", "fresh", "cooked"];

    private static readonly Dictionary<string, string> PhraseAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mixed green"] = "salad green",
        ["leafy green"] = "salad green",
        ["leafy salad"] = "salad green",
        ["salad green"] = "salad green",
        ["green salad"] = "salad green",
        ["cheese sauce"] = "queso",
        ["queso dip"] = "queso",
        ["fruit smoothie"] = "smoothie",
        ["orange smoothie"] = "smoothie",
        ["corn vegetable hash"] = "mixed vegetable",
        ["mixed cooked vegetable"] = "mixed vegetable",
        ["mixed vegetable"] = "mixed vegetable",
    };

    public static IReadOnlySet<(int ExpectedIdx, int ScannedIdx)> MatchComponents(
        IReadOnlyList<GoldenExpected> expected, IReadOnlyList<ScannedComponent> scanned)
    {
        var result = new HashSet<(int, int)>();
        var usedScanned = new HashSet<int>();

        for (var e = 0; e < expected.Count; e++)
        {
            var expPhrase = NormalizePhrase(expected[e].Name);
            var expTokens = Tokenize(expected[e].Name);
            var bestIdx = -1;
            var bestScore = 0.0;

            for (var s = 0; s < scanned.Count; s++)
            {
                if (usedScanned.Contains(s)) continue;

                var scanPhrase = NormalizePhrase(scanned[s].Name);
                var scanTokens = Tokenize(scanned[s].Name);
                var score = Jaccard(expTokens, scanTokens);
                // Credit the model for being at least as specific as the expected label
                // (it reported a more detailed name that still contains the generic ground
                // truth, e.g. scanned "mixed green salad" vs expected "salad"). Do NOT credit
                // the reverse direction: a scanned name that is only a substring of a longer,
                // more specific expected name has LOST identity detail (e.g. scanned
                // "spaghetti" vs expected "spaghetti with tomato sauce") and must clear the
                // same token-overlap bar as everything else instead of an automatic 1.0.
                if (!string.IsNullOrEmpty(expPhrase) && scanPhrase.Contains(expPhrase))
                    score = Math.Max(score, 1.0);

                if (score >= 0.5 && score > bestScore)
                {
                    bestScore = score;
                    bestIdx = s;
                }
            }

            if (bestIdx >= 0)
            {
                result.Add((e, bestIdx));
                usedScanned.Add(bestIdx);
            }
        }

        return result;
    }

    /// <summary>|scanned midpoint − expected grams| / expected grams.</summary>
    public static double GramErrorPercent(ScannedComponent scanned, decimal expectedGrams)
    {
        if (expectedGrams <= 0) return double.NaN;
        return (double)Math.Abs(scanned.EstimatedGramsMidpoint - expectedGrams) / (double)expectedGrams * 100.0;
    }

    public sealed record CaseScore(
        string Image,
        int ExpectedCount,
        int ScannedCount,
        int MatchedCount,
        double Recall,
        double MeanGramErrorPercent,
        List<(string Expected, string? Matched, double ErrorPercent)> PerComponent);

    public static CaseScore ScoreCase(GoldenCase c, IReadOnlyList<ScannedComponent> scanned)
    {
        var matches = MatchComponents(c.Expected, scanned);
        var perComponent = new List<(string, string?, double)>();
        double errorSum = 0;
        var errorCount = 0;

        foreach (var (e, s) in matches)
        {
            var err = GramErrorPercent(scanned[s], c.Expected[e].Grams);
            if (!double.IsNaN(err))
            {
                errorSum += err;
                errorCount++;
            }
            perComponent.Add((c.Expected[e].Name, scanned[s].Name, double.IsNaN(err) ? -1 : Math.Round(err, 1)));
        }

        foreach (var x in c.Expected.Select((exp, idx) => (exp, idx)))
        {
            if (!matches.Any(mm => mm.ExpectedIdx == x.idx))
                perComponent.Add((x.exp.Name, null, -1));
        }

        return new CaseScore(
            c.Image,
            c.Expected.Count,
            scanned.Count,
            matches.Count,
            c.Expected.Count == 0 ? 1.0 : (double)matches.Count / c.Expected.Count,
            errorCount == 0 ? double.NaN : errorSum / errorCount,
            perComponent);
    }

    internal static string NormalizePhrase(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var cleaned = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        var rawTokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !StopTokens.Contains(t))
            .Select(NormalizeTokenPlural)
            .ToArray();
        var normalizedText = string.Join(' ', rawTokens);
        if (PhraseAliases.TryGetValue(normalizedText, out var aliased))
        {
            return aliased;
        }
        return normalizedText;
    }

    internal static string NormalizeTokenPlural(string token)
    {
        if (token.Length <= 3) return token;
        if (token.EndsWith("ies", StringComparison.Ordinal) && token.Length > 4)
            return token[..^3] + "y";
        if (token.EndsWith("oes", StringComparison.Ordinal) && token.Length > 4)
            return token[..^2];
        if (token.EndsWith("ses", StringComparison.Ordinal) && token.Length > 4)
            return token[..^1];
        if ((token.EndsWith("ches", StringComparison.Ordinal) || token.EndsWith("shes", StringComparison.Ordinal)) && token.Length > 4)
            return token[..^2];
        if (token.EndsWith("es", StringComparison.Ordinal) && token.Length > 4)
            return token[..^1];
        if (token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal) && !token.EndsWith("us", StringComparison.Ordinal) && !token.EndsWith("is", StringComparison.Ordinal))
            return token[..^1];
        return token;
    }

    internal static string[] Tokenize(string name)
    {
        var phrase = NormalizePhrase(name);
        if (string.IsNullOrWhiteSpace(phrase)) return [];
        return phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !StopTokens.Contains(t))
            .Distinct()
            .ToArray();
    }

    internal static double Jaccard(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var setA = a.ToHashSet();
        var setB = b.ToHashSet();
        return (double)setA.Intersect(setB).Count() / setA.Union(setB).Count();
    }
}
