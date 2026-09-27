using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Helpers;

/// <summary>Shared rules for <see cref="NutritionProvenance"/> strings (AGENTS.md N4).</summary>
public static class NutritionProvenanceRules
{
    /// <summary>
    /// True for provenance that must be surfaced as uncertain: anything not read from a
    /// resolved catalog product or typed by the user. Coach/MCP low-confidence notes,
    /// review warnings and analytics use this single definition.
    /// </summary>
    public static bool IsEstimateLike(string? provenance) => provenance is
        nameof(NutritionProvenance.Estimated)
        or nameof(NutritionProvenance.ModelEstimated)
        or nameof(NutritionProvenance.Web)
        or nameof(NutritionProvenance.Unknown);

    /// <summary>Exact, case-sensitive parse of a persisted/requested provenance name.</summary>
    public static bool TryParse(string? value, out NutritionProvenance provenance)
    {
        provenance = default;
        return !string.IsNullOrEmpty(value)
               && Enum.TryParse(value, ignoreCase: false, out provenance)
               && Enum.IsDefined(provenance);
    }

    /// <summary>
    /// Provenance for a meal-draft item from its grounding source key
    /// (<c>usda|off|au|db|…</c> catalog sources, <c>web</c>, <c>ai</c>) and whether a
    /// per-100 g basis exists. No basis always means <see cref="NutritionProvenance.Unknown"/>.
    /// </summary>
    public static NutritionProvenance ForDraftSource(string? source, bool hasBasis)
    {
        if (!hasBasis) return NutritionProvenance.Unknown;
        return source?.ToLowerInvariant() switch
        {
            "web" => NutritionProvenance.Web,
            "ai" => NutritionProvenance.ModelEstimated,
            "estimate" => NutritionProvenance.Estimated,
            _ => NutritionProvenance.Sourced,
        };
    }
}
