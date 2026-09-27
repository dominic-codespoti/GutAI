namespace GutAI.Application.Common.Interfaces;

/// <summary>Where a meal draft came from.</summary>
public static class MealDraftOrigins
{
    public const string Photo = "photo";
    public const string Coach = "coach";
    public const string Mcp = "mcp";
    public const string Nlp = "nlp";
    public const string Suggestion = "suggestion";
}

/// <summary>Lifecycle of a meal draft.</summary>
public static class MealDraftStatuses
{
    public const string PendingReview = "PendingReview";
    public const string Committed = "Committed";
    public const string Discarded = "Discarded";
    public const string Expired = "Expired";
}

/// <summary>
/// Persisted meal draft (AGENTS.md N3): every AI-originated meal proposal — photo scan, Coach,
/// MCP, natural-language parse, suggestion — is stored here and only becomes a diary entry
/// when the user commits it. Partition = user id, row key <c>DRAFT|{Id}</c>. String payloads
/// are bounded by the Table Storage property limit (32 000 UTF-16 characters each).
/// </summary>
public sealed record MealDraftRecord
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>One of <see cref="MealDraftOrigins"/>.</summary>
    public required string Origin { get; init; }

    /// <summary>One of <see cref="MealDraftStatuses"/>.</summary>
    public required string Status { get; init; }

    public string? MealType { get; init; }
    public DateTimeOffset? LoggedAt { get; init; }

    /// <summary>JSON array of draft items (grams, per-100 g basis, candidates, provenance).</summary>
    public required string ItemsJson { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool ReferenceObjectVisible { get; init; }
    public decimal OverallConfidence { get; init; }

    /// <summary>Raw model output for audit (Stage-A vision JSON for photo drafts).</summary>
    public string? RawModelJson { get; init; }

    public string? PromptVersion { get; init; }

    /// <summary>Deployment that actually served the model calls.</summary>
    public string? ModelDeployment { get; init; }

    public string? CalibrationVersion { get; init; }

    /// <summary>Per-item user corrections recorded at commit (feedback signal).</summary>
    public string? CorrectionDeltaJson { get; init; }

    public Guid? CommittedMealId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? CommittedAt { get; init; }
    /// <summary>Azure Table Storage concurrency token; not persisted as a property.</summary>
    public string? ETag { get; init; }
}
