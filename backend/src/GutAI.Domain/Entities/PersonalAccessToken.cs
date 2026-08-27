namespace GutAI.Domain.Entities;

/// <summary>
/// Long-lived scoped credential minted for external AI consumers (MCP clients).
/// The plaintext token is shown exactly once at link time; only its SHA-256 hash
/// is stored. Valid solely for MCP traffic (enforced by PatAuthenticationHandler).
/// </summary>
public class PersonalAccessToken
{
    /// <summary>All tokens issued by the pairing flow start read-only.</summary>
    public static readonly IReadOnlyList<string> DefaultScopes = ["read"];

    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>User-facing label, e.g. "AI assistant".</summary>
    public string Name { get; set; } = "AI assistant";

    /// <summary>Email snapshot at creation so tool identity claims need no user lookup.</summary>
    public string Email { get; set; } = "";

    public string TokenHash { get; set; } = default!;

    /// <summary>Display-safe leading fragment (never enough to reconstruct the token).</summary>
    public string TokenPrefix { get; set; } = "";

    public List<string> Scopes { get; set; } = ["read"];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
