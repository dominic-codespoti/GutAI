using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Aggregate metrics: precision, recall, and F1 are case means; kcal absolute and signed errors
/// are case means over complete expected kcal cases, with the absolute error's median also retained.
/// Identity precision, abstention rate, interval coverage, nutrition-backed rate, and false-positive
/// rate are item rates. Method accuracy groups identity correctness by method. IdentityEce is the
/// ten-bin confidence calibration error. MeanKcalCvPercent and MeanRecallCvPercent average per-case
/// population coefficients of variation from repeated runs. MedianGramErrorPercent is the median
/// absolute relative error over matched pairs.
/// Metrics with no eligible data are NaN.
/// </summary>
public sealed record GoldenAggregate(
    double MeanPrecision,
    double MeanRecall,
    double MeanF1,
    double MedianAbsoluteKcalErrorPercent,
    double MeanAbsoluteKcalErrorPercent,
    double MeanSignedKcalBiasPercent,
    double IdentityPrecision,
    double AbstentionRate,
    IReadOnlyDictionary<string, double> AccuracyByMethod,
    double IdentityEce,
    double IntervalCoverageRate,
    double MeanKcalCvPercent,
    double MedianGramErrorPercent,
    double MinNutritionBackedRate,
    double FalsePositiveRate)
{
    public IReadOnlyDictionary<string, double> MeanAbsoluteMacroErrorPercent { get; init; }
        = new Dictionary<string, double>();
    public double MeanRecallCvPercent { get; init; } = double.NaN;
}

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

    /// <summary>One predicted food item and its measured identity, amount, nutrition, and selection metadata.</summary>
    public sealed record GoldenPredictedItem(
        string Name,
        decimal Grams,
        decimal? LowGrams,
        decimal? HighGrams,
        string? Identity,
        string? Method,
        decimal? Confidence,
        bool AutoSelected,
        decimal? Kcal,
        decimal? ProteinG,
        decimal? CarbsG,
        decimal? FatG);

    /// <summary>One-to-one expected/predicted component association; gram error is absolute error divided by expected grams × 100.</summary>
    public sealed record MatchedPair(int ExpectedIndex, int PredictedIndex, GoldenExpected Expected,
        GoldenPredictedItem Predicted, double GramErrorPercent);

    /// <summary>Per-case component, nutrition, identity, calibration, abstention, and interval results.</summary>
    public sealed record CaseEvaluation(
        string Image,
        IReadOnlyList<MatchedPair> MatchedPairs,
        double Precision,
        double Recall,
        double F1,
        IReadOnlyList<double> GramErrorsPercent,
        decimal? PredictedKcal,
        decimal? PredictedProteinG,
        decimal? PredictedCarbsG,
        decimal? PredictedFatG,
        decimal? ExpectedKcal,
        decimal? ExpectedProteinG,
        decimal? ExpectedCarbsG,
        decimal? ExpectedFatG,
        double KcalAbsoluteErrorPercent,
        double KcalSignedErrorPercent,
        IReadOnlyList<bool> IdentityCorrect,
        int Abstentions,
        int PredictedCount,
        int ExpectedCount,
        IReadOnlyList<bool> IntervalCovered,
        IReadOnlyList<(double Confidence, bool Correct)> ConfidenceResults,
        IReadOnlyList<(string Method, bool Correct)> MethodResults);


    /// <summary>A gate metric comparison, retaining the configured threshold and observed value.</summary>
    public sealed record GateCheck(string Name, double Threshold, double Actual);

    /// <summary>Gate outcome; unmeasurable configured metrics are reported separately and do not fail.</summary>
    public sealed record GoldenGateResult(bool Passed, IReadOnlyList<GateCheck> Failed,
        IReadOnlyList<GateCheck> NotEvaluated);

    /// <summary>
    /// Evaluates one case using the existing one-to-one name matcher. Precision is matched/predicted
    /// (1 for no predictions); recall is matched/expected (1 for no expectations); F1 is their harmonic mean.
    /// Expected nutrient totals are present only when every expected item has that nutrient; predicted
    /// totals sum every available value and are null only when none are available. Kcal percentage
    /// errors use (predicted - expected)/expected × 100, with absolute error reported separately.
    /// Identity precision includes matched expected items that specify acceptable identities, compared
    /// case-insensitively. Abstentions are unselected predictions without identity. Interval coverage
    /// includes matches with both predicted bounds and tests whether expected grams lie inclusively within them.
    /// Confidence and method outcomes are retained for matched items with an acceptable-identity set and
    /// the respective value; correctness is membership in that set, case-insensitively.
    /// </summary>
    public static CaseEvaluation EvaluateCase(GoldenCase c, IReadOnlyList<GoldenPredictedItem> predicted)
    {
        var expected = c.Expected;
        var matches = MatchComponents(expected,
            predicted.Select(p => new ScannedComponent
            {
                Name = p.Name,
                EstimatedGramsLow = p.LowGrams ?? p.Grams,
                EstimatedGramsMidpoint = p.Grams,
                EstimatedGramsHigh = p.HighGrams ?? p.Grams,
                Confidence = p.Confidence ?? 0m,
                PreparationNote = "",
            }).ToList());
        var pairs = matches.OrderBy(m => m.ExpectedIdx).Select(m => new MatchedPair(
            m.ExpectedIdx, m.ScannedIdx, expected[m.ExpectedIdx], predicted[m.ScannedIdx],
            expected[m.ExpectedIdx].Grams <= 0 ? double.NaN :
                (double)Math.Abs(predicted[m.ScannedIdx].Grams - expected[m.ExpectedIdx].Grams) /
                (double)expected[m.ExpectedIdx].Grams * 100d)).ToList();
        var precision = predicted.Count == 0 ? 1d : (double)pairs.Count / predicted.Count;
        var recall = expected.Count == 0 ? 1d : (double)pairs.Count / expected.Count;
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

        static decimal? TotalIfComplete(IEnumerable<decimal?> values)
        {
            var materialized = values.ToList();
            return materialized.All(v => v.HasValue) ? materialized.Sum(v => v!.Value) : null;
        }

        var expectedKcal = TotalIfComplete(expected.Select(e => e.Kcal));
        var predictedKcal = predicted.Any(p => p.Kcal.HasValue)
            ? predicted.Where(p => p.Kcal.HasValue).Sum(p => p.Kcal!.Value)
            : (decimal?)null;
        var kcalAbs = expectedKcal is > 0 && predictedKcal.HasValue
            ? (double)Math.Abs(predictedKcal.Value - expectedKcal.Value) / (double)expectedKcal.Value * 100d
            : double.NaN;
        var kcalSigned = expectedKcal is > 0 && predictedKcal.HasValue
            ? (double)(predictedKcal.Value - expectedKcal.Value) / (double)expectedKcal.Value * 100d
            : double.NaN;
        return new CaseEvaluation(
            c.Image, pairs, precision, recall, f1,
            pairs.Select(p => p.GramErrorPercent).Where(double.IsFinite).ToList(),
            predicted.Any(p => p.Kcal.HasValue) ? predicted.Where(p => p.Kcal.HasValue).Sum(p => p.Kcal!.Value) : null,
            predicted.Where(p => p.ProteinG.HasValue).Sum(p => p.ProteinG!.Value),
            predicted.Where(p => p.CarbsG.HasValue).Sum(p => p.CarbsG!.Value),
            predicted.Where(p => p.FatG.HasValue).Sum(p => p.FatG!.Value),
            expectedKcal, TotalIfComplete(expected.Select(e => e.ProteinG)),
            TotalIfComplete(expected.Select(e => e.CarbsG)), TotalIfComplete(expected.Select(e => e.FatG)),
            kcalAbs, kcalSigned,
            pairs.Where(p => p.Expected.AcceptableIdentities.Count > 0)
                .Select(p => p.Expected.AcceptableIdentities.Contains(p.Predicted.Identity ?? "",
                    StringComparer.OrdinalIgnoreCase)).ToList(),
            predicted.Count(p => !p.AutoSelected && p.Identity is null), predicted.Count, expected.Count,
            pairs.Where(p => p.Predicted.LowGrams.HasValue && p.Predicted.HighGrams.HasValue)
                .Select(p => p.Expected.Grams >= p.Predicted.LowGrams!.Value &&
                    p.Expected.Grams <= p.Predicted.HighGrams!.Value)
                .ToList(),
            pairs.Where(p => p.Expected.AcceptableIdentities.Count > 0 && p.Predicted.Confidence.HasValue)
                .Select(p => ((double)p.Predicted.Confidence!.Value,
                    p.Expected.AcceptableIdentities.Contains(p.Predicted.Identity ?? "",
                        StringComparer.OrdinalIgnoreCase))).ToList(),
            pairs.Where(p => p.Expected.AcceptableIdentities.Count > 0 &&
                !string.IsNullOrWhiteSpace(p.Predicted.Method))
                .Select(p => (p.Predicted.Method!, p.Expected.AcceptableIdentities.Contains(
                    p.Predicted.Identity ?? "", StringComparer.OrdinalIgnoreCase))).ToList());
    }

    /// <summary>
    /// Aggregates case metrics as unweighted case means; kcal errors use only cases with complete,
    /// positive expected kcal. Identity precision and interval coverage are item-level rates.
    /// ECE uses ten equal-width confidence bins on [0,1], weighted by bin population. Repeated-run
    /// kcal and recall stability use population standard deviation divided by run mean × 100,
    /// averaged over cases with at least two runs.
    public static GoldenAggregate Aggregate(IReadOnlyList<CaseEvaluation> cases,
        IReadOnlyDictionary<string, IReadOnlyList<double>>? repeatedKcalByCase = null,
        IReadOnlyDictionary<string, IReadOnlyList<double>>? repeatedRecallByCase = null)
    {
        static double Mean(IEnumerable<double> values)
        {
            var array = values.Where(double.IsFinite).ToArray();
            return array.Length == 0 ? double.NaN : array.Average();
        }
        var absolute = cases.Select(c => c.KcalAbsoluteErrorPercent).Where(double.IsFinite).ToArray();
        var signed = cases.Select(c => c.KcalSignedErrorPercent).Where(double.IsFinite).ToArray();
        var identities = cases.SelectMany(c => c.IdentityCorrect).ToArray();
        var interval = cases.SelectMany(c => c.IntervalCovered).ToArray();
        var confidence = cases.SelectMany(c => c.ConfidenceResults).ToArray();
        var methods = cases.SelectMany(c => c.MethodResults).GroupBy(x => x.Method, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Average(x => x.Correct ? 1d : 0d), StringComparer.OrdinalIgnoreCase);
        var ece = double.NaN;
        if (confidence.Length > 0)
        {
            ece = 0;
            for (var bin = 0; bin < 10; bin++)
            {
                var lower = bin / 10d;
                var upper = (bin + 1) / 10d;
                var members = confidence.Where(x => x.Confidence >= lower &&
                    (bin == 9 ? x.Confidence <= upper : x.Confidence < upper)).ToArray();
                if (members.Length > 0)
                    ece += (double)members.Length / confidence.Length *
                        Math.Abs(members.Average(x => x.Correct ? 1d : 0d) - members.Average(x => x.Confidence));
            }
        }
        var cvs = new List<double>();
        if (repeatedKcalByCase is not null)
            foreach (var runs in repeatedKcalByCase.Values.Where(r => r.Count >= 2))
            {
                var mean = runs.Average();
                if (mean == 0) continue;
                cvs.Add(Math.Sqrt(runs.Sum(v => Math.Pow(v - mean, 2)) / runs.Count) / Math.Abs(mean) * 100d);
            }
        var recallCvs = new List<double>();
        if (repeatedRecallByCase is not null)
            foreach (var runs in repeatedRecallByCase.Values.Where(r => r.Count >= 2))
            {
                var mean = runs.Average();
                if (mean == 0) continue;
                recallCvs.Add(Math.Sqrt(runs.Sum(v => Math.Pow(v - mean, 2)) / runs.Count) /
                    Math.Abs(mean) * 100d);
            }
        var totalPredicted = cases.Sum(c => c.PredictedCount);
        var totalAbstentions = cases.Sum(c => c.Abstentions);
        var gramErrors = cases.SelectMany(c => c.GramErrorsPercent).ToArray();
        double MacroError(Func<CaseEvaluation, decimal?> expected, Func<CaseEvaluation, decimal?> predicted)
            => Mean(cases.Where(c => expected(c) is > 0 && predicted(c).HasValue)
                .Select(c => (double)Math.Abs(predicted(c)!.Value - expected(c)!.Value) /
                    (double)expected(c)!.Value * 100d));
        var macroErrors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["protein_g"] = MacroError(c => c.ExpectedProteinG, c => c.PredictedProteinG),
            ["carbs_g"] = MacroError(c => c.ExpectedCarbsG, c => c.PredictedCarbsG),
            ["fat_g"] = MacroError(c => c.ExpectedFatG, c => c.PredictedFatG),
        };
        var meanRecall = Mean(cases.Select(c => c.Recall));
        var expectedCount = cases.Sum(c => c.ExpectedCount);
        var backedCount = cases.Sum(c => c.MatchedPairs.Count(p => p.Predicted.Kcal.HasValue));
        var matched = cases.Sum(c => c.MatchedPairs.Count);

        return new GoldenAggregate(
            Mean(cases.Select(c => c.Precision)), meanRecall, Mean(cases.Select(c => c.F1)),
            absolute.Length == 0 ? double.NaN : Percentile(absolute, 50), Mean(absolute), Mean(signed),
            identities.Length == 0 ? double.NaN : identities.Count(x => x) / (double)identities.Length,
            totalPredicted == 0 ? double.NaN : (double)totalAbstentions / totalPredicted,
            methods, ece, interval.Length == 0 ? double.NaN : interval.Count(x => x) / (double)interval.Length,
            Mean(cvs), gramErrors.Length == 0 ? double.NaN : Percentile(gramErrors, 50),
            expectedCount == 0 ? double.NaN : (double)backedCount / expectedCount,
            totalPredicted == 0 ? double.NaN : (double)(totalPredicted - matched) / totalPredicted)
        {
            MeanAbsoluteMacroErrorPercent = macroErrors,
            MeanRecallCvPercent = Mean(recallCvs),
        };
    }

    /// <summary>Calculates percentile with linear interpolation between closest ranks; empty input returns NaN.</summary>
    public static double Percentile(IEnumerable<double> values, double p)
    {
        if (p < 0 || p > 100) throw new ArgumentOutOfRangeException(nameof(p));
        var sorted = values.Where(double.IsFinite).OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return double.NaN;
        if (sorted.Length == 1) return sorted[0];
        var rank = p / 100d * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }

    /// <summary>Evaluates configured thresholds; greater/less-than directions match each metric name.
    /// Configured metrics with no finite observation are NotEvaluated and do not fail the gate.</summary>
    public static GoldenGateResult EvaluateGate(GateThresholds thresholds, GoldenAggregate aggregate,
        double? p95LatencySeconds, double? p95CostUsd, IReadOnlySet<string>? notApplicable = null)
    {
        var failed = new List<GateCheck>();
        var notEvaluated = new List<GateCheck>();
        void Check(string name, double? threshold, double actual, bool minimum)
        {
            if (!threshold.HasValue) return;
            var check = new GateCheck(name, threshold.Value, actual);
            if (notApplicable?.Contains(name) == true || !double.IsFinite(actual)) notEvaluated.Add(check);
            else if (minimum ? actual < threshold.Value : actual > threshold.Value) failed.Add(check);
        }
        Check("min_recall", thresholds.MinRecall, aggregate.MeanRecall, true);
        Check("max_median_gram_error_percent", thresholds.MaxMedianGramErrorPercent, aggregate.MedianGramErrorPercent, false);
        Check("min_nutrition_backed_rate", thresholds.MinNutritionBackedRate, aggregate.MinNutritionBackedRate, true);
        Check("max_false_positive_rate", thresholds.MaxFalsePositiveRate, aggregate.FalsePositiveRate, false);
        Check("min_precision", thresholds.MinPrecision, aggregate.MeanPrecision, true);
        Check("max_median_kcal_error_percent", thresholds.MaxMedianKcalErrorPercent, aggregate.MedianAbsoluteKcalErrorPercent, false);
        Check("max_abs_kcal_bias_percent", thresholds.MaxAbsKcalBiasPercent, Math.Abs(aggregate.MeanSignedKcalBiasPercent), false);
        Check("min_identity_precision", thresholds.MinIdentityPrecision, aggregate.IdentityPrecision, true);
        Check("max_abstention_rate", thresholds.MaxAbstentionRate, aggregate.AbstentionRate, false);
        Check("min_interval_coverage", thresholds.MinIntervalCoverage, aggregate.IntervalCoverageRate, true);
        Check("max_identity_ece", thresholds.MaxIdentityEce, aggregate.IdentityEce, false);
        Check("max_kcal_cv_percent", thresholds.MaxKcalCvPercent, aggregate.MeanKcalCvPercent, false);
        Check("max_p95_latency_seconds", thresholds.MaxP95LatencySeconds, p95LatencySeconds ?? double.NaN, false);
        Check("max_p95_cost_usd", thresholds.MaxP95CostUsd, p95CostUsd ?? double.NaN, false);
        return new GoldenGateResult(failed.Count == 0, failed, notEvaluated);
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
    public sealed record StageObservation(string Stage, double LatencySeconds, double InputTokens,
        double OutputTokens, double CostUsd, string? ModelId = null);

    public sealed record StagePercentiles(double P50LatencySeconds, double P95LatencySeconds,
        double P50InputTokens, double P95InputTokens, double P50OutputTokens,
        double P95OutputTokens, double P50CostUsd, double P95CostUsd);

    /// <summary>Per-stage nearest-rank-independent, linearly interpolated p50/p95 values.</summary>
    public static IReadOnlyDictionary<string, StagePercentiles> AggregateStages(
        IEnumerable<StageObservation> observations)
        => observations.GroupBy(x => x.Stage, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new StagePercentiles(
                Percentile(g.Select(x => x.LatencySeconds), 50),
                Percentile(g.Select(x => x.LatencySeconds), 95),
                Percentile(g.Select(x => x.InputTokens), 50),
                Percentile(g.Select(x => x.InputTokens), 95),
                Percentile(g.Select(x => x.OutputTokens), 50),
                Percentile(g.Select(x => x.OutputTokens), 95),
                Percentile(g.Select(x => x.CostUsd), 50),
                Percentile(g.Select(x => x.CostUsd), 95)),
                StringComparer.OrdinalIgnoreCase);
}
