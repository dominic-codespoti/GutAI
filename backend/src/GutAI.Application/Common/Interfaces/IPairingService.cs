using GutAI.Domain.Entities;

namespace GutAI.Application.Common.Interfaces;

public sealed record PairingCodeIssued(string Code, DateTime ExpiresAt);

public sealed record PairingRedeemed(PersonalAccessToken Token, string AccessToken);

/// <summary>Raised when a pairing code is unknown, expired, used, or exhausted.</summary>
public sealed class PairingCodeInvalidException : Exception
{
    public PairingCodeInvalidException()
        : base("That pairing code is invalid, expired, or already used.") { }
}

public interface IPairingService
{
    /// <summary>Mints a single-use ~10-minute pairing code for the user (plaintext returned once).</summary>
    Task<PairingCodeIssued> CreatePairingCodeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Exchanges a valid pairing code for a read-only personal access token.</summary>
    /// <exception cref="PairingCodeInvalidException">Unknown/expired/used/exhausted code.</exception>
    Task<PairingRedeemed> RedeemPairingCodeAsync(string rawCode, CancellationToken ct = default);
}
