using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>Input for creating a pending meal draft.</summary>
public sealed record MealDraftCreateRequest
{
    /// <summary>One of <see cref="MealDraftOrigins"/>.</summary>
    public required string Origin { get; init; }

    public string? MealType { get; init; }
    public DateTimeOffset? LoggedAt { get; init; }
    public required IReadOnlyList<MealDraftItemDto> Items { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool ReferenceObjectVisible { get; init; }
    public decimal OverallConfidence { get; init; }
    public string? RawModelJson { get; init; }
    public string? PromptVersion { get; init; }
    public string? ModelDeployment { get; init; }
    public string? CalibrationVersion { get; init; }
}

/// <summary>
/// Extra commit conditions for agent-initiated commits (AGENTS.md N3): the Coach may only
/// commit its own drafts, and never one created during the current turn.
/// </summary>
public sealed record MealDraftCommitGuard(string? RequiredOrigin = null, DateTimeOffset? CreatedBefore = null);

public enum MealDraftErrorCode
{
    /// <summary>Missing, expired, or owned by another user.</summary>
    NotFound,

    /// <summary>Already committed or discarded.</summary>
    NotPending,

    /// <summary>Malformed request: unknown item ids, bad grams, unknown candidate/product.</summary>
    Validation,

    /// <summary>Items without a nutrition basis were submitted without LogWithoutCalories.</summary>
    UnresolvedItems,

    /// <summary>The guard's origin requirement was not met.</summary>
    OriginMismatch,

    /// <summary>The guard forbids committing a draft created during the current turn.</summary>
    SameTurnCommit,
}

public class MealDraftException(MealDraftErrorCode code, string message) : Exception(message)
{
    public MealDraftErrorCode Code { get; } = code;
}

/// <summary>
/// The single path from AI-originated meal proposals to the diary (AGENTS.md N1/N3). Commit
/// recomputes every item from its per-100 g basis × grams with <c>NutritionCalculator</c>;
/// client-sent nutrition is never accepted.
/// </summary>
public interface IMealDraftService
{
    Task<MealDraftDto> CreateAsync(Guid userId, MealDraftCreateRequest request, CancellationToken ct = default);

    /// <summary>Pending draft, or null when missing, expired or no longer pending.</summary>
    Task<MealDraftDto?> GetAsync(Guid userId, Guid draftId, CancellationToken ct = default);

    Task<IReadOnlyList<MealDraftDto>> ListPendingAsync(Guid userId, CancellationToken ct = default);

    /// <exception cref="MealDraftException">Draft not found/pending, or the request is invalid.</exception>
    Task<MealDraftDto> UpdateAsync(Guid userId, Guid draftId, MealDraftUpdateRequest request, CancellationToken ct = default);

    /// <exception cref="MealDraftException">See <see cref="MealDraftErrorCode"/>.</exception>
    Task<MealDraftCommitResult> CommitAsync(
        Guid userId,
        Guid draftId,
        MealDraftCommitRequest? request,
        MealDraftCommitGuard? guard = null,
        CancellationToken ct = default);

    Task DiscardAsync(Guid userId, Guid draftId, CancellationToken ct = default);
}
