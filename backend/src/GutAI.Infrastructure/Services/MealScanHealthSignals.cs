using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// P5: attaches FODMAP + gut-risk signals to grounded scan items so the
/// confirmation sheet shows the same rating chips as the food detail page.
///
/// Safety rule (docs/meal-scan-detailed-design.md §4.4 guardrails): signals are
/// computed ONLY for items grounded to a catalogue product (FoodProductId set).
/// Web-scraped and ai-estimate items stay signal-free — scraped nutrition must
/// never imply FODMAP safety, and ai estimates have no ingredient list to assess.
/// Fail-soft: enrichment problems never fail the scan.
/// </summary>
public static class MealScanHealthSignals
{
    public static async Task EnrichAsync(
        MealScanItemDto item,
        ITableStore store,
        IFodmapService fodmapService,
        IGutRiskService gutRiskService,
        CancellationToken ct = default)
    {
        if (item.FoodProductId is null) return;   // web/ai items stay signal-free

        try
        {
            var entity = await store.GetFoodProductAsync(item.FoodProductId.Value, ct);
            if (entity is null) return;

            var dto = await FoodDtoHelper.BuildFoodProductDto(entity, store, ct);
            var fodmap = fodmapService.Assess(dto);
            var risk = gutRiskService.Assess(dto);

            item.FodmapStatus = fodmap.Status;
            item.FodmapTriggers = fodmap.Triggers.Take(3)
                .Select(t => $"{t.Name} ({t.Severity})")
                .ToList();
            item.GutRating = risk.GutRating;
        }
        catch (Exception)
        {
            // Fail-soft by design — a scoring hiccup must not lose a scanned meal.
        }
    }

    /// <summary>Enrich a batch sequentially (services are in-memory; no parallelism needed).</summary>
    public static async Task EnrichAllAsync(
        IEnumerable<MealScanItemDto> items,
        ITableStore store,
        IFodmapService fodmapService,
        IGutRiskService gutRiskService,
        CancellationToken ct = default)
    {
        foreach (var item in items)
            await EnrichAsync(item, store, fodmapService, gutRiskService, ct);
    }
}
