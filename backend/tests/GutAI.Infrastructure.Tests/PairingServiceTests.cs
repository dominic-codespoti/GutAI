using System.Security.Cryptography;
using FluentAssertions;
using GutAI.Application.Common.Interfaces;
using GutAI.Application.Common.Services;
using GutAI.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class PairingServiceTests
{
    private readonly Mock<ITableStore> _storeMock = new();

    private PairingService CreateService()
    {
        return new PairingService(
            _storeMock.Object,
            NullLogger<PairingService>.Instance);
    }

    private static string Hash(string normalized) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));

    [Fact]
    public async Task CreatePairingCodeAsync_ReturnsFormattedCodeAndPersistsHashedEntity()
    {
        // Arrange
        var userId = Guid.NewGuid();
        PairingCode? persisted = null;

        _storeMock
            .Setup(s => s.UpsertPairingCodeAsync(It.IsAny<PairingCode>(), It.IsAny<CancellationToken>()))
            .Callback<PairingCode, CancellationToken>((code, _) => persisted = code)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var before = DateTime.UtcNow;

        // Act
        var result = await service.CreatePairingCodeAsync(userId);
        var after = DateTime.UtcNow;

        // Assert
        result.Should().NotBeNull();
        result.Code.Should().MatchRegex("^[A-Z0-9]{4}-[A-Z0-9]{4}$");
        result.ExpiresAt.Should().BeOnOrAfter(before.Add(PairingCode.Lifetime));
        result.ExpiresAt.Should().BeOnOrBefore(after.Add(PairingCode.Lifetime));

        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(userId);
        persisted.Id.Should().NotBeEmpty();
        persisted.CodeHash.Should().NotBe(result.Code);
        persisted.CodeHash.Should().Be(Hash(result.Code.Replace("-", "")));
        persisted.UsedAt.Should().BeNull();
        persisted.FailedAttempts.Should().Be(0);

        // Verify across multiple runs that ambiguous characters (I, L, O, 0, 1) are never generated
        const string forbidden = "ILO01ilo";
        for (var i = 0; i < 20; i++)
        {
            var run = await service.CreatePairingCodeAsync(userId);
            run.Code.IndexOfAny(forbidden.ToCharArray()).Should().Be(-1, "unambiguous alphabet must exclude I, L, O, 0, 1");
        }
    }

    [Fact]
    public async Task RedeemPairingCodeAsync_HappyPath_ReturnsPatAndMarksCodeUsed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        const string rawCode = " ABCD-EF23 ";
        var normalized = "ABCDEF23";
        var codeHash = Hash(normalized);

        var existingCode = new PairingCode
        {
            Id = codeId,
            UserId = userId,
            CodeHash = codeHash,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddMinutes(9),
            FailedAttempts = 0,
            UsedAt = null
        };

        var user = new User
        {
            Id = userId,
            Email = "tester@example.com",
            DisplayName = "Tester"
        };

        _storeMock
            .Setup(s => s.GetPairingCodeByHashAsync(codeHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCode);

        _storeMock
            .Setup(s => s.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        PairingCode? savedCode = null;
        PersonalAccessToken? savedPat = null;

        _storeMock
            .Setup(s => s.UpsertPairingCodeAsync(It.IsAny<PairingCode>(), It.IsAny<CancellationToken>()))
            .Callback<PairingCode, CancellationToken>((c, _) => savedCode = c)
            .Returns(Task.CompletedTask);

        _storeMock
            .Setup(s => s.UpsertPersonalAccessTokenAsync(It.IsAny<PersonalAccessToken>(), It.IsAny<CancellationToken>()))
            .Callback<PersonalAccessToken, CancellationToken>((pat, _) => savedPat = pat)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.RedeemPairingCodeAsync(rawCode);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().StartWith("gutai_pat_");
        result.AccessToken.Length.Should().BeInRange(50, 60);

        result.Token.Should().NotBeNull();
        result.Token.UserId.Should().Be(userId);
        result.Token.Email.Should().Be("tester@example.com");
        result.Token.Scopes.Should().BeEquivalentTo(["read"]);
        result.Token.TokenHash.Should().Be(Hash(result.AccessToken));
        result.Token.TokenPrefix.Should().Be(result.AccessToken[..16]);

        savedCode.Should().NotBeNull();
        savedCode!.UsedAt.Should().NotBeNull();
        savedCode.UsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        savedPat.Should().NotBeNull();
        savedPat!.Id.Should().Be(result.Token.Id);
        savedPat.UserId.Should().Be(userId);
        savedPat.Scopes.Should().BeEquivalentTo(["read"]);
        savedPat.TokenHash.Should().Be(Hash(result.AccessToken));

        _storeMock.Verify(s => s.UpsertPairingCodeAsync(existingCode, It.IsAny<CancellationToken>()), Times.Once);
        _storeMock.Verify(s => s.UpsertPersonalAccessTokenAsync(It.IsAny<PersonalAccessToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RedeemPairingCodeAsync_NullOrWhitespace_ThrowsPairingCodeInvalidException(string emptyCode)
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () => service.RedeemPairingCodeAsync(emptyCode);

        // Assert
        await act.Should().ThrowAsync<PairingCodeInvalidException>();
        _storeMock.Verify(s => s.GetPairingCodeByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedeemPairingCodeAsync_UnknownCode_ThrowsPairingCodeInvalidException()
    {
        // Arrange
        _storeMock
            .Setup(s => s.GetPairingCodeByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PairingCode?)null);

        var service = CreateService();

        // Act
        var act = () => service.RedeemPairingCodeAsync("ABCD-EF23");

        // Assert
        await act.Should().ThrowAsync<PairingCodeInvalidException>();
        _storeMock.Verify(s => s.UpsertPairingCodeAsync(It.IsAny<PairingCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedeemPairingCodeAsync_ExpiredCode_ThrowsAndDoesNotMintPat()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string rawCode = "ABCD-EF23";
        var codeHash = Hash("ABCDEF23");

        var expiredCode = new PairingCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = codeHash,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
            FailedAttempts = 0,
            UsedAt = null
        };

        _storeMock
            .Setup(s => s.GetPairingCodeByHashAsync(codeHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredCode);

        var service = CreateService();

        // Act
        var act = () => service.RedeemPairingCodeAsync(rawCode);

        // Assert
        await act.Should().ThrowAsync<PairingCodeInvalidException>();
        _storeMock.Verify(s => s.UpsertPersonalAccessTokenAsync(It.IsAny<PersonalAccessToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedeemPairingCodeAsync_AlreadyUsedCode_ThrowsWithoutAdditionalWrites()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string rawCode = "ABCD-EF23";
        var codeHash = Hash("ABCDEF23");

        var usedCode = new PairingCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = codeHash,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            FailedAttempts = 1,
            UsedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        _storeMock
            .Setup(s => s.GetPairingCodeByHashAsync(codeHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usedCode);

        var service = CreateService();

        // Act
        var act = () => service.RedeemPairingCodeAsync(rawCode);

        // Assert
        await act.Should().ThrowAsync<PairingCodeInvalidException>();
        // Dead rows reject without writes: probing a known-used hash must not cause
        // table-write amplification (the Times.Once above is the setup, not the service).
        _storeMock.Invocations
            .Where(i => i.Method.Name == nameof(ITableStore.UpsertPairingCodeAsync))
            .Should().BeEmpty();
        _storeMock.Verify(s => s.UpsertPersonalAccessTokenAsync(It.IsAny<PersonalAccessToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedeemPairingCodeAsync_MaxFailedAttemptsReached_ThrowsEvenIfOtherwiseValid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string rawCode = "ABCD-EF23";
        var codeHash = Hash("ABCDEF23");

        var exhaustedCode = new PairingCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = codeHash,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            ExpiresAt = DateTime.UtcNow.AddMinutes(8),
            FailedAttempts = PairingCode.MaxFailedAttempts, // 5
            UsedAt = null
        };

        _storeMock
            .Setup(s => s.GetPairingCodeByHashAsync(codeHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exhaustedCode);

        var service = CreateService();

        // Act
        var act = () => service.RedeemPairingCodeAsync(rawCode);

        // Assert
        await act.Should().ThrowAsync<PairingCodeInvalidException>();
        _storeMock.Verify(s => s.UpsertPersonalAccessTokenAsync(It.IsAny<PersonalAccessToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("abcd-ef23", "ABCDEF23")]
    [InlineData("  ab-cd  ef-23  ", "ABCDEF23")]
    [InlineData("a_b-c+d.e!f@2#3", "ABCDEF23")]
    public async Task RedeemPairingCodeAsync_NormalizesFormattingBeforeLookup(string rawInput, string expectedNormalized)
    {
        // Arrange
        var expectedHash = Hash(expectedNormalized);
        var userId = Guid.NewGuid();

        var code = new PairingCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = expectedHash,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddMinutes(9),
            FailedAttempts = 0,
            UsedAt = null
        };

        var user = new User
        {
            Id = userId,
            Email = "user@example.com",
            DisplayName = "Normalized User"
        };

        _storeMock
            .Setup(s => s.GetPairingCodeByHashAsync(expectedHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);

        _storeMock
            .Setup(s => s.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act
        var result = await service.RedeemPairingCodeAsync(rawInput);

        // Assert
        result.Should().NotBeNull();
        _storeMock.Verify(s => s.GetPairingCodeByHashAsync(expectedHash, It.IsAny<CancellationToken>()), Times.Once);
    }
}
