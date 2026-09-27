using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>AI meal photo scan pipeline; every scan persists a pending photo draft.</summary>
public interface IMealScanService
{
    Task<MealDraftDto> ScanMealImageAsync(Guid userId, Stream imageStream, string contentType, string? note = null, CancellationToken ct = default);
}

/// <summary>Stage A alone, exposed for the golden-image regression harness.</summary>
public interface IMealVisionStage
{
    Task<VisionDecomposition> DecomposeAsync(Stream imageStream, string contentType, CancellationToken ct = default);
}

/// <summary>Full Stage-A output incl. provenance needed by the regression harness.</summary>
public sealed record VisionDecomposition(
    IReadOnlyList<ScannedComponent> Components,
    bool ReferenceObjectVisible,
    string ScaleNotes,
    decimal OverallConfidence,
    IReadOnlyList<string> DroppedNotes,
    string RawJson,
    string PromptVersion,
    int? InputTokens,
    int? OutputTokens)
{
    /// <summary>
    /// Components inferred from a visible cue rather than seen directly (e.g. frying oil on a
    /// glossy stir-fry). Populated only under <c>Features:HiddenCalories</c>, whose Stage-A
    /// schema/prompt carry their own version; the default Stage-A schema is unchanged. Identity
    /// and grams only (AGENTS.md #8); drafts show them as opt-in lines (decision D4).
    /// </summary>
    public IReadOnlyList<ScannedComponent> InferredComponents { get; init; } = [];
}
