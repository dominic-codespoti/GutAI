using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Helpers;

/// <summary>
/// Deterministic semantic validation of Stage-A vision output.
///
/// Structured output guarantees SYNTACTIC shape (valid JSON matching the schema),
/// not semantic validity. Everything the downstream pipeline relies on is checked
/// here, outside the model: gram-range ordering, bounds, non-empty names,
/// component caps. Invalid components are dropped (never guessed at); an all-invalid
/// result fails the scan.
/// </summary>
public static class MealVisionValidator
{
    public sealed record ValidatedVision(
        List<ScannedComponent> Components,
        bool ReferenceObjectVisible,
        string ScaleNotes,
        decimal OverallConfidence,
        IReadOnlyList<string> DroppedNotes);

    /// <exception cref="MealScanValidationException">Nothing usable survived validation.</exception>
    public static ValidatedVision Validate(MealVisionResult raw, int maxComponents)
    {
        if (raw.Components.Count == 0)
            throw new MealScanValidationException("No food components were identified in the photo.");

        var dropped = new List<string>();
        var valid = new List<ScannedComponent>();

        foreach (var c in raw.Components.Take(maxComponents * 2)) // hard read-cap before filtering
        {
            if (valid.Count >= maxComponents)
            {
                dropped.Add($"Component limit ({maxComponents}) reached — '{Truncate(c.Name)}' ignored.");
                continue;
            }

            var cleanedName = CleanseComponentName(c.Name);
            if (string.IsNullOrWhiteSpace(cleanedName))
            {
                dropped.Add("Unnamed component dropped.");
                continue;
            }

            if (c.EstimatedGramsLow < 0 || c.EstimatedGramsMidpoint < 0 || c.EstimatedGramsHigh < 0
                || c.EstimatedGramsLow > c.EstimatedGramsMidpoint || c.EstimatedGramsMidpoint > c.EstimatedGramsHigh)
            {
                dropped.Add($"'{Truncate(c.Name)}' dropped — implausible portion range.");
                continue;
            }

            // Physiological sanity ceiling for a single photographed food item.
            if (c.EstimatedGramsHigh > 5000m)
            {
                dropped.Add($"'{Truncate(c.Name)}' dropped — portion estimate exceeds 5 kg.");
                continue;
            }

            var servingHint = NormalizeServingHint(
                c.ServingHintUnit, c.ServingHintUnitPlural, c.ServingHintUnitGrams);
            if (c.Confidence is < 0m or > 1m)
            {
                // Clamp rather than drop — an over/under-confident model output doesn't
                // invalidate the component's identity itself.
                valid.Add(new ScannedComponent
                {
                    Name = cleanedName,
                    EstimatedGramsLow = decimal.Round(c.EstimatedGramsLow, 1),
                    EstimatedGramsMidpoint = decimal.Round(c.EstimatedGramsMidpoint, 1),
                    EstimatedGramsHigh = decimal.Round(c.EstimatedGramsHigh, 1),
                    Confidence = Clamp01(c.Confidence),
                    PortionConfidence = decimal.Round(Clamp01(c.PortionConfidence), 2),
                    IsGarnish = c.IsGarnish || c.EstimatedGramsMidpoint <= 5m,
                    ServingHintUnit = servingHint.Unit,
                    ServingHintUnitPlural = servingHint.Plural,
                    ServingHintUnitGrams = servingHint.Grams,
                    SearchQueries = NormalizeSearchQueries(c.SearchQueries),
                    PreparationNote = (c.PreparationNote ?? "").Trim(),
                });
                continue;
            }

            valid.Add(new ScannedComponent
            {
                Name = cleanedName,
                EstimatedGramsLow = decimal.Round(c.EstimatedGramsLow, 1),
                EstimatedGramsMidpoint = decimal.Round(c.EstimatedGramsMidpoint, 1),
                EstimatedGramsHigh = decimal.Round(c.EstimatedGramsHigh, 1),
                Confidence = decimal.Round(Clamp01(c.Confidence), 2),
                PortionConfidence = decimal.Round(Clamp01(c.PortionConfidence), 2),
                IsGarnish = c.IsGarnish || c.EstimatedGramsMidpoint <= 5m,
                ServingHintUnit = servingHint.Unit,
                ServingHintUnitPlural = servingHint.Plural,
                ServingHintUnitGrams = servingHint.Grams,
                SearchQueries = NormalizeSearchQueries(c.SearchQueries),
                PreparationNote = (c.PreparationNote ?? "").Trim(),
            });

        }

        if (valid.Count == 0)
            throw new MealScanValidationException(
                "Food components were detected but none had usable identity or portion data.");

        return new ValidatedVision(
            valid,
            raw.ReferenceObjectVisible,
            (raw.ScaleNotes ?? "").Trim(),
            Clamp01(raw.OverallConfidence),
            dropped);
    }

    private static decimal Clamp01(decimal v) => Math.Clamp(v, 0m, 1m);

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..40] + "…";

    private static string CleanseComponentName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "";
        var s = rawName.Trim();

        // Strip common LLM disjunction patterns ("A or B", "A / B") by taking the first primary term
        if (s.Contains(" or ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = s.Split([" or ", " Or ", " OR "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && parts[0].Length >= 2) s = parts[0];
        }
        else if (s.Contains(" / "))
        {
            var parts = s.Split([" / "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && parts[0].Length >= 2) s = parts[0];
        }

        // Strip trailing cut/shape/serving noise descriptors ("pieces", "chunks", "slices", "bits", "bites", "strips", "diced")
        string[] shapeNoiseSuffixes = [" pieces", " chunks", " slices", " bits", " bites", " strips", " diced"];
        foreach (var suffix in shapeNoiseSuffixes)
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && s.Length > suffix.Length + 2)
            {
                s = s[..^suffix.Length].Trim();
                break;
            }
        }

        return s.Trim();
    }
    private static (string Unit, string Plural, decimal Grams) NormalizeServingHint(
        string? unit, string? plural, decimal grams)
    {
        var normalizedGrams = grams is > 0m and <= 1000m ? decimal.Round(grams, 1) : 0m;
        if (normalizedGrams == 0m) return ("", "", 0m);

        return (
            NormalizeServingHintUnit(unit),
            NormalizeServingHintUnit(plural),
            normalizedGrams);
    }

    private static string NormalizeServingHintUnit(string? value)
    {
        var cleaned = (value ?? "").Trim();
        return cleaned.Length switch
        {
            0 => "",
            <= 60 => cleaned,
            _ => cleaned[..60],
        };
    }
    private static List<string> NormalizeSearchQueries(IEnumerable<string>? queries) =>
        (queries ?? [])
            .Select(CleanseComponentName)
            .Where(q => q.Length is >= 2 and <= 120)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
}

public sealed class MealScanValidationException(string message) : Exception(message);
