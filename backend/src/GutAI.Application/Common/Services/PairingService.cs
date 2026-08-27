using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GutAI.Application.Common.Services;

/// <summary>
/// Pairing-code lifecycle for linking external AI consumers (MCP clients) to a user
/// account. Codes are single-use, 10-minute, 8-char unambiguous-alphabet secrets stored
/// only as SHA-256 hashes; redemption mints a read-only PersonalAccessToken whose
/// plaintext is returned exactly once.
/// </summary>
public partial class PairingService : IPairingService
{
    // No I/L/O/0/1 — avoids transcription ambiguity when read off a phone screen and
    // dictated to another device.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphanumeric();

    private readonly ITableStore _store;
    private readonly ILogger<PairingService> _logger;

    public PairingService(ITableStore store, ILogger<PairingService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<PairingCodeIssued> CreatePairingCodeAsync(Guid userId, CancellationToken ct = default)
    {
        var chars = RandomNumberGenerator.GetString(Alphabet, 8);
        var code = $"{chars[..4]}-{chars[4..]}";

        var pairing = new PairingCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = Hash(Normalize(code)),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(PairingCode.Lifetime),
        };
        await _store.UpsertPairingCodeAsync(pairing, ct);

        _logger.LogInformation("Issued pairing code {CodeId} for user {UserId}", pairing.Id, userId);
        return new PairingCodeIssued(code, pairing.ExpiresAt);
    }

    public async Task<PairingRedeemed> RedeemPairingCodeAsync(string rawCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
            throw new PairingCodeInvalidException();

        var hash = Hash(Normalize(rawCode));
        var pairing = await _store.GetPairingCodeByHashAsync(hash, ct);
        if (pairing is null || !pairing.IsRedeemable)
            throw new PairingCodeInvalidException();

        // Count the failed attempt before anything else so probing a used/expired code
        // still burns the row.
        pairing.FailedAttempts++;
        if (!pairing.IsRedeemable)
        {
            await _store.UpsertPairingCodeAsync(pairing, ct);
            throw new PairingCodeInvalidException();
        }

        var user = await _store.GetUserAsync(pairing.UserId, ct);
        if (user is null)
            throw new PairingCodeInvalidException();

        var accessToken = $"gutai_pat_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=')}";

        var token = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "AI assistant",
            Email = user.Email,
            TokenHash = Hash(accessToken),
            TokenPrefix = accessToken[..16],
            Scopes = [.. PersonalAccessToken.DefaultScopes],
            CreatedAt = DateTime.UtcNow,
        };

        pairing.UsedAt = DateTime.UtcNow;
        await _store.UpsertPairingCodeAsync(pairing, ct);
        await _store.UpsertPersonalAccessTokenAsync(token, ct);

        _logger.LogInformation("Redeemed pairing code {CodeId} into PAT {TokenId} for user {UserId}",
            pairing.Id, token.Id, user.Id);
        return new PairingRedeemed(token, accessToken);
    }

    internal static string Normalize(string rawCode) =>
        NonAlphanumeric().Replace(rawCode.ToUpperInvariant(), "");

    internal static string Hash(string normalized) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
}
