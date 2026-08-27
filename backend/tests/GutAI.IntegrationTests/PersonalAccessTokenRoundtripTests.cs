using FluentAssertions;
using GutAI.Domain.Entities;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class PersonalAccessTokenRoundtripTests(AzuriteFixture fx)
{
    [Fact]
    public async Task PersonalAccessToken_RoundtripsAllFields_WhenLastUsedAtIsNull()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var tokenHash = $"hash_{Guid.NewGuid():N}";
        var tokenPrefix = "gutai_pat_test12";
        var createdAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
        var expiresAt = new DateTime(2027, 8, 25, 10, 0, 0, DateTimeKind.Utc);

        var token = new PersonalAccessToken
        {
            Id = tokenId,
            UserId = userId,
            Name = "Claude Desktop",
            Email = "pat-test@example.com",
            TokenHash = tokenHash,
            TokenPrefix = tokenPrefix,
            Scopes = ["read", "write"],
            CreatedAt = createdAt,
            LastUsedAt = null,
            RevokedAt = null,
            ExpiresAt = expiresAt
        };

        // Act
        await fx.Store.UpsertPersonalAccessTokenAsync(token);
        var loaded = await fx.Store.GetPersonalAccessTokenByHashAsync(tokenHash);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(tokenId);
        loaded.UserId.Should().Be(userId);
        loaded.Name.Should().Be("Claude Desktop");
        loaded.Email.Should().Be("pat-test@example.com");
        loaded.TokenHash.Should().Be(tokenHash);
        loaded.TokenPrefix.Should().Be(tokenPrefix);
        loaded.Scopes.Should().BeEquivalentTo(["read", "write"]);
        loaded.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromMilliseconds(10));
        loaded.LastUsedAt.Should().BeNull();
        loaded.RevokedAt.Should().BeNull();
        loaded.ExpiresAt.Should().NotBeNull();
        loaded.ExpiresAt!.Value.Should().BeCloseTo(expiresAt, TimeSpan.FromMilliseconds(10));
        loaded.IsActive.Should().BeTrue();
        loaded.IsRevoked.Should().BeFalse();
        loaded.IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task PersonalAccessToken_RoundtripsAllFields_WhenLastUsedAtAndRevokedAtAreSet()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var tokenHash = $"hash_{Guid.NewGuid():N}";
        var tokenPrefix = "gutai_pat_active";
        var createdAt = DateTime.UtcNow.AddDays(-10);
        var lastUsedAt = DateTime.UtcNow.AddMinutes(-5);
        var revokedAt = DateTime.UtcNow.AddMinutes(-1);
        var expiresAt = DateTime.UtcNow.AddDays(30);

        var token = new PersonalAccessToken
        {
            Id = tokenId,
            UserId = userId,
            Name = "ChatGPT Plugin",
            Email = "chatgpt@example.com",
            TokenHash = tokenHash,
            TokenPrefix = tokenPrefix,
            Scopes = ["read"],
            CreatedAt = createdAt,
            LastUsedAt = lastUsedAt,
            RevokedAt = revokedAt,
            ExpiresAt = expiresAt
        };

        // Act
        await fx.Store.UpsertPersonalAccessTokenAsync(token);
        var loaded = await fx.Store.GetPersonalAccessTokenByHashAsync(tokenHash);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(tokenId);
        loaded.UserId.Should().Be(userId);
        loaded.Name.Should().Be("ChatGPT Plugin");
        loaded.Email.Should().Be("chatgpt@example.com");
        loaded.TokenHash.Should().Be(tokenHash);
        loaded.TokenPrefix.Should().Be(tokenPrefix);
        loaded.Scopes.Should().BeEquivalentTo(["read"]);
        loaded.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromMilliseconds(50));
        loaded.LastUsedAt.Should().NotBeNull();
        loaded.LastUsedAt!.Value.Should().BeCloseTo(lastUsedAt, TimeSpan.FromMilliseconds(50));
        loaded.RevokedAt.Should().NotBeNull();
        loaded.RevokedAt!.Value.Should().BeCloseTo(revokedAt, TimeSpan.FromMilliseconds(50));
        loaded.ExpiresAt.Should().NotBeNull();
        loaded.ExpiresAt!.Value.Should().BeCloseTo(expiresAt, TimeSpan.FromMilliseconds(50));
        loaded.IsActive.Should().BeFalse();
        loaded.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task PairingCode_RoundtripsAllFields_NullAndSetUsedAt()
    {
        // Arrange - Unused PairingCode
        var userId = Guid.NewGuid();
        var codeId1 = Guid.NewGuid();
        var hash1 = $"codehash_{Guid.NewGuid():N}";
        var createdAt1 = DateTime.UtcNow.AddMinutes(-2);
        var expiresAt1 = DateTime.UtcNow.AddMinutes(8);

        var unused = new PairingCode
        {
            Id = codeId1,
            UserId = userId,
            CodeHash = hash1,
            CreatedAt = createdAt1,
            ExpiresAt = expiresAt1,
            FailedAttempts = 2,
            UsedAt = null
        };

        // Act 1: Upsert & Get unused code
        await fx.Store.UpsertPairingCodeAsync(unused);
        var loadedUnused = await fx.Store.GetPairingCodeByHashAsync(hash1);

        // Assert 1
        loadedUnused.Should().NotBeNull();
        loadedUnused!.Id.Should().Be(codeId1);
        loadedUnused.UserId.Should().Be(userId);
        loadedUnused.CodeHash.Should().Be(hash1);
        loadedUnused.CreatedAt.Should().BeCloseTo(createdAt1, TimeSpan.FromMilliseconds(50));
        loadedUnused.ExpiresAt.Should().BeCloseTo(expiresAt1, TimeSpan.FromMilliseconds(50));
        loadedUnused.FailedAttempts.Should().Be(2);
        loadedUnused.UsedAt.Should().BeNull();
        loadedUnused.IsUsed.Should().BeFalse();
        loadedUnused.IsRedeemable.Should().BeTrue();

        // Arrange - Used PairingCode
        var codeId2 = Guid.NewGuid();
        var hash2 = $"codehash_{Guid.NewGuid():N}";
        var createdAt2 = DateTime.UtcNow.AddMinutes(-5);
        var expiresAt2 = DateTime.UtcNow.AddMinutes(5);
        var usedAt2 = DateTime.UtcNow.AddMinutes(-1);

        var used = new PairingCode
        {
            Id = codeId2,
            UserId = userId,
            CodeHash = hash2,
            CreatedAt = createdAt2,
            ExpiresAt = expiresAt2,
            FailedAttempts = 0,
            UsedAt = usedAt2
        };

        // Act 2: Upsert & Get used code
        await fx.Store.UpsertPairingCodeAsync(used);
        var loadedUsed = await fx.Store.GetPairingCodeByHashAsync(hash2);

        // Assert 2
        loadedUsed.Should().NotBeNull();
        loadedUsed!.Id.Should().Be(codeId2);
        loadedUsed.UserId.Should().Be(userId);
        loadedUsed.CodeHash.Should().Be(hash2);
        loadedUsed.CreatedAt.Should().BeCloseTo(createdAt2, TimeSpan.FromMilliseconds(50));
        loadedUsed.ExpiresAt.Should().BeCloseTo(expiresAt2, TimeSpan.FromMilliseconds(50));
        loadedUsed.FailedAttempts.Should().Be(0);
        loadedUsed.UsedAt.Should().NotBeNull();
        loadedUsed.UsedAt!.Value.Should().BeCloseTo(usedAt2, TimeSpan.FromMilliseconds(50));
        loadedUsed.IsUsed.Should().BeTrue();
        loadedUsed.IsRedeemable.Should().BeFalse();
    }

    [Fact]
    public async Task GetActivePersonalAccessTokensAsync_FiltersOutRevokedAndExpiredTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var activeToken = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Active PAT",
            Email = "user@example.com",
            TokenHash = $"active_hash_{Guid.NewGuid():N}",
            TokenPrefix = "gutai_pat_act",
            Scopes = ["read"],
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            RevokedAt = null
        };

        var revokedToken = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Revoked PAT",
            Email = "user@example.com",
            TokenHash = $"revoked_hash_{Guid.NewGuid():N}",
            TokenPrefix = "gutai_pat_rev",
            Scopes = ["read"],
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(25),
            RevokedAt = DateTime.UtcNow.AddDays(-1)
        };

        var expiredToken = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Expired PAT",
            Email = "user@example.com",
            TokenHash = $"expired_hash_{Guid.NewGuid():N}",
            TokenPrefix = "gutai_pat_exp",
            Scopes = ["read"],
            CreatedAt = DateTime.UtcNow.AddDays(-40),
            ExpiresAt = DateTime.UtcNow.AddDays(-10),
            RevokedAt = null
        };

        await fx.Store.UpsertPersonalAccessTokenAsync(activeToken);
        await fx.Store.UpsertPersonalAccessTokenAsync(revokedToken);
        await fx.Store.UpsertPersonalAccessTokenAsync(expiredToken);

        // Act
        var activeTokens = await fx.Store.GetActivePersonalAccessTokensAsync(userId);

        // Assert
        activeTokens.Should().ContainSingle(t => t.Id == activeToken.Id);
        activeTokens.Should().NotContain(t => t.Id == revokedToken.Id);
        activeTokens.Should().NotContain(t => t.Id == expiredToken.Id);
    }

    [Fact]
    public async Task DeletePersonalAccessTokensForUserAsync_RemovesLookupAndTokenRows()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var hash1 = $"token_hash_del1_{Guid.NewGuid():N}";
        var hash2 = $"token_hash_del2_{Guid.NewGuid():N}";

        var token1 = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Token 1",
            Email = "user@example.com",
            TokenHash = hash1,
            TokenPrefix = "gutai_pat_t1",
            Scopes = ["read"],
            CreatedAt = DateTime.UtcNow
        };

        var token2 = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Token 2",
            Email = "user@example.com",
            TokenHash = hash2,
            TokenPrefix = "gutai_pat_t2",
            Scopes = ["read"],
            CreatedAt = DateTime.UtcNow
        };

        await fx.Store.UpsertPersonalAccessTokenAsync(token1);
        await fx.Store.UpsertPersonalAccessTokenAsync(token2);

        // Act
        await fx.Store.DeletePersonalAccessTokensForUserAsync(userId);

        // Assert
        (await fx.Store.GetActivePersonalAccessTokensAsync(userId)).Should().BeEmpty();
        (await fx.Store.GetPersonalAccessTokenByHashAsync(hash1)).Should().BeNull();
        (await fx.Store.GetPersonalAccessTokenByHashAsync(hash2)).Should().BeNull();
    }
}
