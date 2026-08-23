using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Pure metric computation for the golden-image regression gate.
/// Unit-tested without any AI involvement; the harness only supplies IO.
///
/// Matching rule: a scanned component matches an expected component when their
/// normalized names overlap (token Jaccard ≥ 0.5 or substring containment).
/// Normalization: lowercase, strip punctuation, drop generic filler tokens
/// ("of", "with", "a", "the", "some", "piece", "pieces", "side").
/// </summary>
public static class GoldenMetrics
{
    private static readonly string[] StopTokens =
        ["of", "with", "a", "an", "the", "some", "piece", "pieces", "side", "fresh", "cooked"];

    public static IReadOnlySet<(int ExpectedIdx, int ScannedIdx)> MatchComponents(
        IReadOnlyList<GoldenExpected> expected, IReadOnlyList<ScannedComponent> scanned)
    {
        var result = new HashSet<(int, int)>();
        var usedScanned = new HashSet<int>();

        for (var e = 0; e < expected.Count; e++)
        {
            var expTokens = Tokenize(expected[e].Name);
            var bestIdx = -1;
            var bestScore = 0.0;

            for (var s = 0; s < scanned.Count; s++)
            {
                if (usedScanned.Contains(s)) continue;

                var scanTokens = Tokenize(scanned[s].Name);
                var score = Jaccard(expTokens, scanTokens);
                var n = expected[e].Name.ToLowerInvariant().Trim();
                var m = scanned[s].Name.ToLowerInvariant().Trim();
                if (n.Contains(m) || m.Contains(n))
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

    internal static string[] Tokenize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        var cleaned = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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
