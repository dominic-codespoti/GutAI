using System.Text.Json.Serialization;

namespace GutAI.Application.Common.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
// Meal photo scan pipeline contracts (see docs/meal-scan-detailed-design.md).
//
// Design rule: the LLM NEVER produces nutrition numbers — only component
// identity, gram estimates and confidence. All displayed values trace to a
// named source ("usda" | "off" | "au" | "web" | "ai").
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Stage A output — strict JSON shape returned by the vision model.</summary>
public sealed class MealVisionResult
{
    [JsonPropertyName("components")]
    public List<ScannedComponent> Components { get; set; } = [];

    [JsonPropertyName("reference_object_visible")]
    public bool ReferenceObjectVisible { get; set; }

    /// <summary>e.g. "fork ≈18cm at plate edge" — empty when no reference present.</summary>
    [JsonPropertyName("scale_notes")]
    public string ScaleNotes { get; set; } = "";

    /// <summary>Model's overall confidence, 0..1.</summary>
    [JsonPropertyName("overall_confidence")]
    public decimal OverallConfidence { get; set; }
}

public sealed class ScannedComponent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("estimated_grams_low")]
    public decimal EstimatedGramsLow { get; set; }

    [JsonPropertyName("estimated_grams_midpoint")]
    public decimal EstimatedGramsMidpoint { get; set; }

    [JsonPropertyName("estimated_grams_high")]
    public decimal EstimatedGramsHigh { get; set; }

    /// <summary>0..1 — reflects BOTH identity certainty AND portion certainty.</summary>
    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; }

    /// <summary>Cooking-method note ("appears fried", "sauce visible").</summary>
    [JsonPropertyName("preparation_note")]
    public string PreparationNote { get; set; } = "";
}

/// <summary>One resolved line item in the draft returned to the client.</summary>
public sealed record MealScanItemDto
{
    public required Guid ItemId { get; init; }

    /// <summary>Original Stage-A component name — quantities attach HERE, never to the catalogue entry.</summary>
    public required string Name { get; init; }

    /// <summary>Canonical catalogue name when grounded (may differ from Name).</summary>
    public string? CanonicalName { get; init; }

    public Guid? FoodProductId { get; init; }

    /// <summary>"usda" | "off" | "au" | "web" | "ai"</summary>
    public required string Source { get; init; }

    /// <summary>Citation URL when Source == "web".</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Editable portion estimate (grams) — always the STAGE-A estimate, never a catalogue serving size.</summary>
    public required decimal Grams { get; set; }

    // Computed deterministically from DB per-100g × grams (null while Source == "ai").
    public decimal? Calories { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? CarbsG { get; set; }
    public decimal? FatG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? SugarG { get; set; }
    public decimal? SodiumMg { get; set; }

    /// <summary>Stage-B database resolution confidence (1 when Source == "ai").</summary>
    public required decimal MatchConfidence { get; init; }

    /// <summary>Stage-A vision confidence carried through.</summary>
    public required decimal VisionConfidence { get; init; }

    /// <summary>Alternate DB candidates for quick swap in the review UI (top-3).</summary>
    public IReadOnlyList<string>? CandidateNames { get; init; }

    /// <summary>Full provenance chain for this item's grounding (P3).</summary>
    public GroundingAttemptDto? Grounding { get; init; }

    // ── Gut-health signals (P5) — ONLY for DB-grounded items. Web/ai items stay
    //    signal-free by design: scraped values must never imply FODMAP safety.
    [JsonPropertyName("fodmap_status")]
    public string? FodmapStatus { get; set; }

    [JsonPropertyName("fodmap_triggers")]
    public List<string>? FodmapTriggers { get; set; } // top 3, "Name (Severity)"

    [JsonPropertyName("gut_rating")]
    public string? GutRating { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Stage B grounding provenance (P3)
//
// DetectedComponent -> GroundingAttempt -> GroundedComponent.
// The attempt records the WHOLE chain: normalized query, top candidates,
// selection decision and its method. "Unresolved" is a first-class outcome —
// grounding never fabricates a match just to complete the meal.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record GroundingCandidateDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("food_product_id")] Guid? FoodProductId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("match_confidence")] decimal MatchConfidence);

public sealed record GroundingAttemptDto
{
    /// <summary>Query sent to the resolver (the Stage-A component name, sanitized).</summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    /// <summary>"exact" | "probable" | "ambiguous" | "unresolved" — resolver's own decision.</summary>
    [JsonPropertyName("resolution_status")]
    public required string ResolutionStatus { get; init; }

    /// <summary>True when the frozen auto-select criterion was met; false means human choice needed.</summary>
    [JsonPropertyName("auto_selected")]
    public required bool AutoSelected { get; init; }

    [JsonPropertyName("selected_food_product_id")]
    public Guid? SelectedFoodProductId { get; init; }

    [JsonPropertyName("canonical_name")]
    public string? CanonicalName { get; init; }

    /// <summary>Top candidates at decision time (max 3), including the selected one.</summary>
    [JsonPropertyName("candidates")]
    public IReadOnlyList<GroundingCandidateDto> Candidates { get; init; } = [];

    [JsonPropertyName("match_confidence")]
    public required decimal MatchConfidence { get; init; }

    /// <summary>Which mechanism produced the decision ("resolve_async" today; "web_cascade" in P4).</summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }
}

/// <summary>Persisted scan draft (PendingReview) returned by the scan endpoints.</summary>
public sealed class MealScanDraftDto
{
    public required Guid ScanSessionId { get; init; }
    public required IReadOnlyList<MealScanItemDto> Items { get; init; }

    /// <summary>User-facing warnings, e.g. "No reference object visible — portions are rough estimates."</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public required bool ReferenceObjectVisible { get; init; }
    public required decimal OverallConfidence { get; init; }
}
