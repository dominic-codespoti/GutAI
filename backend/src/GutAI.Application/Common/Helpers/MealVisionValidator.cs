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

            if (string.IsNullOrWhiteSpace(c.Name))
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

            if (c.Confidence is < 0m or > 1m)
            {
                // Clamp rather than drop — an over/under-confident model output doesn't
                // invalidate the component's identity itself.
                valid.Add(new ScannedComponent
                {
                    Name = c.Name.Trim(),
                    EstimatedGramsLow = decimal.Round(c.EstimatedGramsLow, 1),
                    EstimatedGramsMidpoint = decimal.Round(c.EstimatedGramsMidpoint, 1),
                    EstimatedGramsHigh = decimal.Round(c.EstimatedGramsHigh, 1),
                    Confidence = Clamp01(c.Confidence),
                    PreparationNote = (c.PreparationNote ?? "").Trim(),
                });
                continue;
            }

            valid.Add(new ScannedComponent
            {
                Name = c.Name.Trim(),
                EstimatedGramsLow = decimal.Round(c.EstimatedGramsLow, 1),
                EstimatedGramsMidpoint = decimal.Round(c.EstimatedGramsMidpoint, 1),
                EstimatedGramsHigh = decimal.Round(c.EstimatedGramsHigh, 1),
                Confidence = decimal.Round(Clamp01(c.Confidence), 2),
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
}

public sealed class MealScanValidationException(string message) : Exception(message);
