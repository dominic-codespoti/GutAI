using System.Text;
using System.Text.Json;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class CorrectionAnalyticsTests
{
    [Fact]
    public void Computes_medians_percentiles_method_rates_removals_and_acceptance()
    {
        var drafts = new[]
        {
            Draft(MealDraftOrigins.Photo, MealDraftStatuses.Committed,
                Delta("rice", 1m, method: "vision_candidate_selection", swapped: true),
                Delta("rice", 3m, method: "resolve_async", removed: true)),
            Draft(MealDraftOrigins.Suggestion, MealDraftStatuses.Committed,
                Delta("rice", 5m, method: "agent_tool_review")),
            Draft(MealDraftOrigins.Mcp, MealDraftStatuses.Committed,
                Delta("rice", 3m, method: "vision_candidate_selection")),
            Draft(MealDraftOrigins.Suggestion, MealDraftStatuses.Discarded),
            Draft(MealDraftOrigins.Suggestion, MealDraftStatuses.Expired),
            Draft(MealDraftOrigins.Photo, MealDraftStatuses.Discarded)
        };

        var report = CorrectionAnalytics.Compute(drafts, new CorrectionAnalyticsOptions { MinSamples = 3 });

        var cell = report.Portion.Should().ContainSingle().Which;
        cell.N.Should().Be(3);
        cell.MedianRatio.Should().Be(3m);
        cell.P25.Should().Be(2m);
        cell.P75.Should().Be(4m);
        report.SwapsByMethod.Should().ContainSingle(x => x.Method == "vision_candidate_selection" && x.N == 2 && x.Swaps == 1 && x.Rate == 0.5m);
        report.SwapsByMethod.Should().ContainSingle(x => x.Method == "resolve_async" && x.N == 1 && x.Swaps == 0);
        report.Removal.Overall.Should().Be(new RemovalRate(4, 1, 0.25m));
        report.Removal.ByOrigin.Should().ContainSingle(x => x.Name == MealDraftOrigins.Photo && x.Metrics.Removed == 1);
        report.Removal.ByMethod.Should().ContainSingle(x => x.Name == "resolve_async" && x.Metrics.Removed == 1);
        report.AcceptanceByOrigin.Should().ContainSingle(x => x.Origin == MealDraftOrigins.Suggestion &&
            x.Committed == 1 && x.Discarded == 1 && x.Expired == 1 && x.Suggested == 3 && x.AcceptanceRate == 1m / 3m);
        report.Calibration.Factors.Should().ContainSingle(x => x.Samples == 3 && x.Factor == 3m);
        report.Calibration.BelowMinimum.Should().BeEmpty();
    }

    [Fact]
    public void Applies_minimum_sample_gate_and_round_trips_proposed_calibration_into_portion_calibrator()
    {
        var drafts = Enumerable.Range(0, 20)
            .Select(i => Draft(MealDraftOrigins.Photo, MealDraftStatuses.Committed, Delta("rice", i % 2 == 0 ? 1.1m : 1.3m)))
            .Append(Draft(MealDraftOrigins.Photo, MealDraftStatuses.Committed, Delta("chicken", 1.2m)));
        var report = CorrectionAnalytics.Compute(drafts, new CorrectionAnalyticsOptions { MinSamples = 20, ProposedVersion = "reviewed-v2" });

        report.Calibration.Factors.Should().ContainSingle(x => x.FoodClass == "starch" && x.Samples == 20 && x.Factor == 1.2m);
        report.Calibration.BelowMinimum.Should().ContainSingle(x => x.FoodClass == "protein" && x.N == 1);
        using var snippet = JsonDocument.Parse(report.Calibration.ConfigJson);
        snippet.RootElement.GetProperty("MealScan").GetProperty("PortionCalibration").GetProperty("Version").GetString().Should().Be("reviewed-v2");
        using var snippetStream = new MemoryStream(Encoding.UTF8.GetBytes(report.Calibration.ConfigJson));
        var config = new ConfigurationBuilder().AddJsonStream(snippetStream)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Features:PortionCalibration"] = "true" }).Build();
        var calibrator = new PortionCalibrator(config);
        calibrator.Version.Should().Be("reviewed-v2");
        var item = new MealDraftItemDto { ItemId = Guid.NewGuid(), Name = "rice", Source = "ai", Grams = 100m, MatchConfidence = 0.9m, PortionConfidence = 0.8m };
        calibrator.Apply(item).Grams.Should().Be(120m);
    }

    [Fact]
    public void Counts_and_skips_malformed_json_rows_without_losing_valid_rows()
    {
        var malformedDelta = Draft(MealDraftOrigins.Photo, MealDraftStatuses.Committed) with { CorrectionDeltaJson = "{" };
        var malformedItems = Draft(MealDraftOrigins.Photo, MealDraftStatuses.Committed, Delta("rice", 1m)) with { ItemsJson = "not-json" };
        var valid = Draft(MealDraftOrigins.Photo, MealDraftStatuses.Committed, Delta("rice", 1m));

        var report = CorrectionAnalytics.Compute([malformedDelta, malformedItems, valid], new CorrectionAnalyticsOptions());

        report.MalformedRows.Should().Be(2);
        report.Portion.Should().ContainSingle(x => x.N == 1 && x.MedianRatio == 1m);
        report.DraftCount.Should().Be(3);
    }

    private static MealDraftRecord Draft(string origin, string status, params string[] deltas)
    {
        var rows = deltas.Length == 0 ? "[]" : $"[{string.Join(',', deltas)}]";
        var itemRows = deltas.Select(ReadDeltaIdentity).ToArray();
        var itemIds = itemRows.Select(row => row.ItemId).ToArray();
        var items = itemIds.Length == 0 ? "[]" : $"[{string.Join(",", itemIds.Select((id, index) => $"{{\"itemId\":\"{id}\",\"grounding\":{{\"method\":\"{itemRows[index].Method}\"}}}}"))}]";
        return new MealDraftRecord
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Origin = origin,
            Status = status,
            ItemsJson = items,
            CorrectionDeltaJson = rows,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
    }

    private static string Delta(string foodClass, decimal? ratio, string method = "resolve_async", bool removed = false, bool swapped = false)
    {
        var itemId = Guid.NewGuid();
        return JsonSerializer.Serialize(new
        {
            itemId,
            foodClass = foodClass switch { "rice" => "starch", "chicken" => "protein", _ => foodClass },
            portionConfidenceTier = "high",
            gramRatio = ratio,
            removed,
            swappedFromKey = swapped ? "candidate:old" : null,
            swappedToKey = swapped ? "candidate:new" : null,
            replacementFoodProductId = (Guid?)null,
            method
        });
    }
    private static (string ItemId, string Method) ReadDeltaIdentity(string json)
    {
        using var document = JsonDocument.Parse(json);
        return (document.RootElement.GetProperty("itemId").GetString()!, document.RootElement.GetProperty("method").GetString()!);
    }
}

