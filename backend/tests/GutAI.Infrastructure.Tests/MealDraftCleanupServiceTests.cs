using FluentAssertions;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class MealDraftCleanupServiceTests
{
    [Fact]
    public async Task RunOnceAsync_uses_configured_cutoffs_and_returns_deleted_count()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var store = new Mock<ITableStore>();
        store.Setup(x => x.PurgeMealDraftsAsync(now, now.AddDays(-30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        var logger = new Mock<ILogger<MealDraftCleanupService>>();
        var service = CreateService(store, timeProvider, logger, new Dictionary<string, string?>
        {
            ["MealDrafts:ClosedRetentionDays"] = "30"
        });

        var deletedCount = await service.RunOnceAsync(CancellationToken.None);

        deletedCount.Should().Be(4);
        store.Verify(x => x.PurgeMealDraftsAsync(now, now.AddDays(-30), CancellationToken.None), Times.Once);
        logger.VerifyLogged(LogLevel.Information);
    }

    [Fact]
    public async Task RunOnceAsync_logs_store_failure_and_returns_zero()
    {
        var store = new Mock<ITableStore>();
        store.Setup(x => x.PurgeMealDraftsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));
        var logger = new Mock<ILogger<MealDraftCleanupService>>();
        var service = CreateService(store, new FixedTimeProvider(DateTimeOffset.UtcNow), logger);

        var deletedCount = await service.RunOnceAsync(CancellationToken.None);

        deletedCount.Should().Be(0);
        logger.VerifyLogged(LogLevel.Error);
    }

    private static MealDraftCleanupService CreateService(
        Mock<ITableStore> store,
        TimeProvider timeProvider,
        Mock<ILogger<MealDraftCleanupService>> logger,
        Dictionary<string, string?>? overrides = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
            .Build();
        return new MealDraftCleanupService(store.Object, timeProvider, configuration, logger.Object);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

}

internal static class LoggerMockExtensions
{
    public static void VerifyLogged<T>(this Mock<ILogger<T>> logger, LogLevel level)
    {
        logger.Verify(x => x.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
