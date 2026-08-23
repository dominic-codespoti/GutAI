using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// AI meal photo scanning pipeline (P1–P5 of docs/meal-scan-detailed-design.md).
/// Stage A: vision decomposition (identity + grams + confidence — never calories).
/// Stage B: grounding via IFoodSearchService (web cascade arrives in P4).
/// Stage C: deterministic macro computation from DB per-100g values × grams.
/// </summary>
public interface IMealScanService
{
    /// <summary>Run the full pipeline on a preprocessed meal photo; persists a PendingReview session.</summary>
    Task<MealScanDraftDto> ScanMealImageAsync(Guid userId, Stream imageStream, string contentType, CancellationToken ct = default);

    /// <summary>Fetch a previously produced draft.</summary>
    Task<MealScanDraftDto?> GetDraftAsync(Guid userId, Guid scanSessionId, CancellationToken ct = default);

    /// <summary>Delete a draft without logging.</summary>
    Task DiscardAsync(Guid userId, Guid scanSessionId, CancellationToken ct = default);
}

/// <summary>Persisted session record surfaced through ITableStore.</summary>
public sealed record ScanSessionRecord
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>"PendingReview" | "Confirmed" | "Discarded"</summary>
    public required string Status { get; init; }

    public required string RawVisionJson { get; init; }
    public required string DraftItemsJson { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool ReferenceObjectVisible { get; init; }
    public required decimal OverallConfidence { get; init; }

    public required string ModelDeployment { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
