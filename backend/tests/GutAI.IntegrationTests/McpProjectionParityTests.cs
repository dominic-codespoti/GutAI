using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GutAI.Api.Mcp;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GutAI.IntegrationTests;

/// <summary>
/// Proves the MCP tools (used by the chat assistant) and the direct engine calls (used by
/// the Insights/Food Diary screens) are genuinely one computation, not two implementations
/// that happen to agree today. <see cref="MealSymptomTools.GetTriggerFoods"/> and
/// <see cref="MealSymptomTools.GetEliminationDietStatus"/> both hold references to the same
/// <see cref="ICorrelationEngine"/>/<see cref="IFoodDiaryAnalysisService"/> instances the
/// Insights/Food Diary endpoints use â€” this locks that architectural invariant in with a
/// behavioral assertion instead of relying on code inspection alone.
/// </summary>
[Collection("Azurite")]
public class McpProjectionParityTests(AzuriteFixture fx)
{
    private static HttpContext MakeHttpContext(Guid userId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())]))
        };
        return context;
    }

    [Fact]
    public async Task GetTriggerFoods_AgreesWithDirectCorrelationEngineCall()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = 8001, Name = "Bloating", Category = "GI" });

        // 8 "Garlic Bread" meals, 6 with a Bloating symptom 3h later (75% exposed rate).
        for (var i = 0; i < 8; i++)
        {
            var mealTime = now.AddDays(-i * 3);
            var mealId = Guid.NewGuid();
            await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = mealTime, MealType = MealType.Lunch });
            await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = "Garlic Bread", Calories = 200 }]);
            if (i < 6)
                await fx.Store.UpsertSymptomLogAsync(new SymptomLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SymptomTypeId = 8001,
                    Severity = 6,
                    OccurredAt = mealTime.AddHours(3)
                });
        }

        // 4 baseline "Rice" meals with no symptoms, so a real baseline exists.
        for (var i = 0; i < 4; i++)
        {
            var mealId = Guid.NewGuid();
            await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = now.AddDays(-(i * 5 + 45)), MealType = MealType.Lunch });
            await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = "Rice" }]);
        }

        var engine = new CorrelationEngine(fx.Store);
        var groundTruth = (await engine.ComputeCorrelationsAsync(
                userId, DateOnly.FromDateTime(now.AddDays(-60)), DateOnly.FromDateTime(now.AddDays(1))))
            .First(c => c.FoodOrAdditive == "Garlic Bread");

        var tools = new MealSymptomTools(fx.Store, null!, engine, new FoodDiaryAnalysisService(), NullLogger<MealSymptomTools>.Instance);
        var json = await tools.GetTriggerFoods(MakeHttpContext(userId), days: 61, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var mcpEntry = doc.RootElement.EnumerateArray().First(e => e.GetProperty("food").GetString() == "Garlic Bread");

        mcpEntry.GetProperty("totalOccurrences").GetInt32().Should().Be(groundTruth.Occurrences,
            "the MCP tool must report the same occurrence count the Insights engine computed, not an independently derived number");
        ((decimal)mcpEntry.GetProperty("avgSeverity").GetDouble()).Should().BeApproximately(groundTruth.AverageSeverity, 0.01m,
            "the MCP tool must report the same average severity the Insights engine computed, not a re-derived value");
    }

    [Fact]
    public async Task GetEliminationDietStatus_AgreesWithDirectFoodDiaryAnalysisServiceCall()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await fx.Store.UpsertSymptomTypeAsync(new SymptomType { Id = 8002, Name = "Bloating", Category = "GI" });

        for (var i = 0; i < 8; i++)
        {
            var mealTime = now.AddDays(-i * 3);
            var mealId = Guid.NewGuid();
            await fx.Store.UpsertMealLogAsync(new MealLog { Id = mealId, UserId = userId, LoggedAt = mealTime, MealType = MealType.Lunch });
            await fx.Store.UpsertMealItemsAsync(userId, mealId, [new() { Id = Guid.NewGuid(), FoodName = "Garlic Bread", Calories = 200 }]);
            if (i < 6)
                await fx.Store.UpsertSymptomLogAsync(new SymptomLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SymptomTypeId = 8002,
                    Severity = 6,
                    OccurredAt = mealTime.AddHours(3)
                });
        }

        // Neither call below takes an explicit date range — both derive it identically inside
        // the shared service, so this is a strict same-inputs comparison, not an approximation.
        var diaryService = new FoodDiaryAnalysisService();
        var groundTruth = await diaryService.GetEliminationStatusAsync(userId, fx.Store);

        var tools = new MealSymptomTools(fx.Store, null!, new CorrelationEngine(fx.Store), diaryService, NullLogger<MealSymptomTools>.Instance);
        var json = await tools.GetEliminationDietStatus(MakeHttpContext(userId), CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("phase").GetString().Should().Be(groundTruth.Phase);
        var mcpFoodsToEliminate = doc.RootElement.GetProperty("foodsToEliminate").EnumerateArray().Select(e => e.GetString()).ToList();
        mcpFoodsToEliminate.Should().BeEquivalentTo(groundTruth.FoodsToEliminate,
            "the MCP tool's elimination candidates must be exactly the Food Diary screen's candidates, not a separately filtered list");
    }
}
