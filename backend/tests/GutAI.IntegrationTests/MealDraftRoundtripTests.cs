using Azure.Data.Tables;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class MealDraftRoundtripTests(AzuriteFixture fx)
{
    [Fact]
    public async Task MealDraftRoundtripPreservesEveryFieldAndNormalizesOffsets()
    {
        var userId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var committedMealId = Guid.NewGuid();
        var draft = new MealDraftRecord
        {
            Id = draftId,
            UserId = userId,
            Origin = MealDraftOrigins.Photo,
            Status = MealDraftStatuses.Committed,
            MealType = "lunch",
            LoggedAt = new DateTimeOffset(2026, 4, 5, 12, 30, 0, TimeSpan.FromHours(5)),
            ItemsJson = "[{\"name\":\"rice\"}]",
            Warnings = ["review portion", "low confidence"],
            ReferenceObjectVisible = true,
            OverallConfidence = 0.8765m,
            RawModelJson = "{\"raw\":true}",
            PromptVersion = "vision-v4",
            ModelDeployment = "gpt-test",
            CalibrationVersion = "cal-2",
            CorrectionDeltaJson = "[{\"item\":0}]",
            CommittedMealId = committedMealId,
            CreatedAt = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.FromHours(-7)),
            ExpiresAt = new DateTimeOffset(2026, 4, 12, 10, 0, 0, TimeSpan.FromHours(-7)),
            CommittedAt = new DateTimeOffset(2026, 4, 5, 13, 0, 0, TimeSpan.FromHours(3)),
        };

        await fx.Store.UpsertMealDraftAsync(draft);
        var actual = await fx.Store.GetMealDraftAsync(userId, draftId);

        actual.Should().NotBeNull();
        actual!.ETag.Should().NotBeNullOrWhiteSpace();
        actual.Should().BeEquivalentTo(draft with
        {
            LoggedAt = draft.LoggedAt?.ToUniversalTime(),
            CreatedAt = draft.CreatedAt.ToUniversalTime(),
            ExpiresAt = draft.ExpiresAt.ToUniversalTime(),
            CommittedAt = draft.CommittedAt?.ToUniversalTime(),
            ETag = actual.ETag,
        });
        var persistedEntity = await fx.ServiceClient.GetTableClient("gutai")
            .GetEntityAsync<TableEntity>(userId.ToString(), $"DRAFT|{draftId}");
        persistedEntity.Value.ContainsKey("ETag").Should().BeFalse();
        actual!.LoggedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        actual.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        actual.ExpiresAt.Offset.Should().Be(TimeSpan.Zero);
        actual.CommittedAt!.Value.Offset.Should().Be(TimeSpan.Zero);

        var optionalDraft = draft with
        {
            MealType = null,
            LoggedAt = null,
            RawModelJson = null,
            PromptVersion = null,
            ModelDeployment = null,
            CalibrationVersion = null,
            CorrectionDeltaJson = null,
            CommittedMealId = null,
            CommittedAt = null,
        };
        await fx.Store.UpsertMealDraftAsync(optionalDraft);
        var nullOptionals = await fx.Store.GetMealDraftAsync(userId, draft.Id);
        nullOptionals.Should().NotBeNull();
        nullOptionals!.MealType.Should().BeNull();
        nullOptionals.LoggedAt.Should().BeNull();
        nullOptionals.RawModelJson.Should().BeNull();
        nullOptionals.PromptVersion.Should().BeNull();
        nullOptionals.ModelDeployment.Should().BeNull();
        nullOptionals.CalibrationVersion.Should().BeNull();
        nullOptionals.CorrectionDeltaJson.Should().BeNull();
        nullOptionals.CommittedMealId.Should().BeNull();
        nullOptionals.CommittedAt.Should().BeNull();
    }

    [Fact]
    public async Task DraftStatusQueryFiltersByStatusAndUser()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var pending = Draft(userId, MealDraftStatuses.PendingReview);
        var committed = Draft(userId, MealDraftStatuses.Committed);
        var otherUserPending = Draft(otherUserId, MealDraftStatuses.PendingReview);
        await fx.Store.UpsertMealDraftAsync(pending);
        await fx.Store.UpsertMealDraftAsync(committed);
        await fx.Store.UpsertMealDraftAsync(otherUserPending);

        var results = await fx.Store.GetMealDraftsByStatusAsync(userId, MealDraftStatuses.PendingReview);
        results.Should().ContainSingle().Which.Id.Should().Be(pending.Id);
    }


    [Fact]
    public async Task PurgeDeletesExpiredPendingAndOldClosedDraftsButRetainsRecentDiscardedAndActivePending()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var pendingExpiredBefore = now.AddDays(-1);
        var closedCreatedBefore = now.AddDays(-90);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var drafts = new[]
        {
            Draft(users[0], MealDraftStatuses.PendingReview, now.AddDays(-2), now.AddDays(-100)),
            Draft(users[1], MealDraftStatuses.Expired, now.AddDays(30), now.AddDays(-100)),
            Draft(users[0], MealDraftStatuses.Committed, now.AddDays(30), now.AddDays(-100)),
            Draft(users[1], MealDraftStatuses.Discarded, now.AddDays(30), now.AddDays(-100)),
            Draft(users[0], MealDraftStatuses.Discarded, now.AddDays(30), now.AddDays(-10)),
            Draft(users[1], MealDraftStatuses.PendingReview, now.AddDays(2), now.AddDays(-100)),
        };
        foreach (var draft in drafts)
            await fx.Store.UpsertMealDraftAsync(draft);

        var count = await fx.Store.PurgeMealDraftsAsync(pendingExpiredBefore, closedCreatedBefore);

        count.Should().Be(4);
        foreach (var deleted in drafts.Take(4))
            (await fx.Store.GetMealDraftAsync(deleted.UserId, deleted.Id)).Should().BeNull();
        foreach (var retained in drafts.Skip(4))
            (await fx.Store.GetMealDraftAsync(retained.UserId, retained.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task OversizeItemsJsonIsRejectedWithPropertyName()
    {
        var draft = Draft(Guid.NewGuid(), MealDraftStatuses.PendingReview) with
        {
            ItemsJson = new string('x', 32_001),
        };

        var act = () => fx.Store.UpsertMealDraftAsync(draft);

        var exception = await Assert.ThrowsAsync<ArgumentException>(act);
        exception.ParamName.Should().Be(nameof(MealDraftRecord.ItemsJson));
    }

    [Fact]
    public async Task CoachSessionStateRoundtripsAndCanBeDeleted()
    {
        var userId = Guid.NewGuid();
        var state = new CoachSessionState
        {
            ResolvedFoods = [new CoachResolvedFood("oats", Guid.NewGuid(), new NutritionPer100gDto { CaloriesKcal = 372m, ProteinG = 13.5m }, 0.93m)],
            OpenDraftIds = [Guid.NewGuid(), Guid.NewGuid()],
            UpdatedAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(4)),
        };

        await fx.Store.UpsertCoachSessionStateAsync(userId, state);
        (await fx.Store.GetCoachSessionStateAsync(userId)).Should().BeEquivalentTo(state with { UpdatedAt = state.UpdatedAt.ToUniversalTime() });
        await fx.Store.DeleteCoachSessionStateAsync(userId);
        (await fx.Store.GetCoachSessionStateAsync(userId)).Should().BeNull();
    }

    [Fact]
    public async Task WebNutritionCacheSupportsPositiveNegativeAndReplacement()
    {
        const string key = "roundtrip-food|au";
        var positive = new WebNutritionResult
        {
            CaloriesKcal = 123.4m,
            ProteinG = 5.6m,
            CarbsG = 21.2m,
            FatG = 2.1m,
            FiberG = 3.4m,
            SugarG = 4.5m,
            SodiumMg = 67.8m,
            SourceName = "Test source",
            SourceUrl = "https://example.test/food",
            CacheKey = key,
        };

        await fx.Store.UpsertWebNutritionNegativeCacheAsync(key);
        var negative = await fx.Store.GetWebNutritionCacheEntryAsync(key.ToUpperInvariant());
        negative.Should().NotBeNull();
        negative!.IsNegative.Should().BeTrue();
        negative.Result.Should().BeNull();
        negative.CachedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        await fx.Store.UpsertWebNutritionCacheAsync(positive);
        var cached = await fx.Store.GetWebNutritionCacheEntryAsync(key);
        cached.Should().NotBeNull();
        cached!.IsNegative.Should().BeFalse();
        cached.Result.Should().BeEquivalentTo(positive);
        cached.CachedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task UserPreferredFoodRegionRoundtrips()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.test",
            PreferredFoodRegion = FoodRegion.Au,
        };

        await fx.Store.UpsertUserAsync(user);

        (await fx.Store.GetUserAsync(user.Id))!.PreferredFoodRegion.Should().Be(FoodRegion.Au);
    }

    [Fact]
    public async Task CustomFoodNutritionMetadataRoundtrips()
    {
        var food = new CustomFood
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "test food",
            ServingSize = 100,
            Calories = 99,
            ProteinG = 2,
            CarbG = 3,
            FatG = 4,
            NutritionProvenance = "verified-label",
            ExtractionConfidence = 0.91m,
        };

        await fx.Store.UpsertCustomFoodAsync(food);

        var actual = await fx.Store.GetCustomFoodAsync(food.UserId, food.Id);
        actual.Should().NotBeNull();
        actual!.NutritionProvenance.Should().Be(food.NutritionProvenance);
        actual.ExtractionConfidence.Should().Be(food.ExtractionConfidence);
    }

    private static MealDraftRecord Draft(
        Guid userId,
        string status,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Origin = MealDraftOrigins.Coach,
            Status = status,
            ItemsJson = "[]",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
        };
}
