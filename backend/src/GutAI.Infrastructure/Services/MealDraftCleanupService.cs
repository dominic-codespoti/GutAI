using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Services;

public sealed class MealDraftCleanupService(
    ITableStore tableStore,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<MealDraftCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(
        configuration.GetValue("MealDrafts:CleanupIntervalHours", 24));
    private readonly int _closedRetentionDays = configuration.GetValue("MealDrafts:ClosedRetentionDays", 90);

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var deletedCount = await tableStore.PurgeMealDraftsAsync(
                pendingExpiredBefore: now,
                closedCreatedBefore: now.AddDays(-_closedRetentionDays),
                ct: ct);
            logger.LogInformation("Deleted {DeletedCount} expired meal drafts", deletedCount);
            return deletedCount;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to clean up meal drafts");
            return 0;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_cleanupInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
