namespace GutAI.Domain.Entities;

/// <summary>
/// Single-use, short-lived code shown in the app so an external AI consumer can
/// link itself via the MCP gutai_link_account tool. Only the SHA-256 hash is stored.
/// </summary>
public class PairingCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Uppercase hex of SHA-256 over the normalized (dash-stripped) code.</summary>
    public string CodeHash { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    /// <summary>Failed redemption attempts against this code; dead after MaxFailedAttempts.</summary>
    public int FailedAttempts { get; set; }

    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsUsed => UsedAt != null;
    public bool IsRedeemable => !IsExpired && !IsUsed && FailedAttempts < MaxFailedAttempts;
}
