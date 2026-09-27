using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class MealDraftServiceTests
{
    private readonly Mock<ITableStore> _store = new();
    private readonly Dictionary<Guid, MealDraftRecord> _drafts = [];
    private readonly List<MealLog> _meals = [];
    private readonly Dictionary<Guid, List<MealItem>> _items = [];
    private readonly Dictionary<Guid, FoodProduct> _products = [];
    private readonly MealDraftService _service;
    private readonly Guid _user = Guid.NewGuid();
    private int _draftVersion;

    public MealDraftServiceTests()
    {
        _store.Setup(s => s.UpsertMealDraftAsync(It.IsAny<MealDraftRecord>(), It.IsAny<CancellationToken>()))
            .Callback<MealDraftRecord, CancellationToken>((d, _) => _drafts[d.Id] = d with { ETag = (++_draftVersion).ToString() }).Returns(Task.CompletedTask);
        _store.Setup(s => s.TryReplaceMealDraftAsync(It.IsAny<MealDraftRecord>(), It.IsAny<CancellationToken>()))
            .Returns<MealDraftRecord, CancellationToken>((d, _) =>
            {
                if (!_drafts.TryGetValue(d.Id, out var current) || current.ETag != d.ETag) return Task.FromResult<string?>(null);
                var etag = (++_draftVersion).ToString();
                _drafts[d.Id] = d with { ETag = etag };
                return Task.FromResult<string?>(etag);
            });
        _store.Setup(s => s.GetMealDraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, CancellationToken>((u, id, _) => Task.FromResult(_drafts.TryGetValue(id, out var d) && d.UserId == u ? d : null));
        _store.Setup(s => s.GetMealDraftsByStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, CancellationToken>((u, status, _) => Task.FromResult(_drafts.Values.Where(d => d.UserId == u && d.Status == status).ToList()));
        _store.Setup(s => s.UpsertMealLogAsync(It.IsAny<MealLog>(), It.IsAny<CancellationToken>()))
            .Callback<MealLog, CancellationToken>((m, _) => _meals.Add(m)).Returns(Task.CompletedTask);
        _store.Setup(s => s.UpsertMealItemsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<MealItem>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, List<MealItem>, CancellationToken>((_, id, rows, _) => _items[id] = rows).Returns(Task.CompletedTask);
        _store.Setup(s => s.GetFoodProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, _) => Task.FromResult(_products.GetValueOrDefault(id)));
        _store.Setup(s => s.SearchFoodProductsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, CancellationToken>((_, _, _) => Task.FromResult(new List<FoodProduct>()));
        _store.Setup(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>())).Callback<FoodProduct, CancellationToken>((p, _) => _products[p.Id] = p).Returns(Task.CompletedTask);
        _service = new MealDraftService(_store.Object, new ConfigurationBuilder().AddInMemoryCollection().Build(), TimeProvider.System, NullLogger<MealDraftService>.Instance);
    }

    private static NutritionPer100gDto Basis(decimal kcal) => new() { CaloriesKcal = kcal, ProteinG = 10, CarbsG = 20, FatG = 5 };
    private static MealDraftItemDto Item(string name, decimal kcal, string source = "usda", bool included = true) => new()
    {
        ItemId = Guid.NewGuid(),
        Name = name,
        Source = source,
        Grams = 100,
        Per100g = Basis(kcal),
        IncludedByDefault = included,
        MatchConfidence = .92m,
        PortionConfidence = .8m,
        NutritionProvenance = "Unknown"
    };
    private Task<MealDraftDto> Create(params MealDraftItemDto[] items) => _service.CreateAsync(_user, new MealDraftCreateRequest { Origin = "photo", Items = items, OverallConfidence = .9m });

    private static MealDraftItemDto NeedsChoiceItem(string name = "ambiguous food", bool included = true) => Item(name, 900, included: included) with
    {
        NeedsChoice = true,
        Grounding = new GroundingAttemptDto
        {
            Query = name,
            ResolutionStatus = "ambiguous",
            AutoSelected = false,
            MatchConfidence = .5m,
            Method = "resolve_async",
            Candidates = [new GroundingCandidateDto("candidate food", Guid.NewGuid(), "usda", .8m, Calories100g: 200, Protein100g: 10, Carbs100g: 20, Fat100g: 5, CandidateKey: "previewed-candidate")]
        }
    };

    [Fact]
    public async Task Commit_RecomputesFreshExistingAndWebItemsFromTheirBases()
    {
        var fresh = Item("fresh", 165, "usda");
        var catalog = Item("catalog", 130, "off") with { FoodProductId = Guid.NewGuid() };
        var web = Item("web result", 120, "web");
        var draft = await Create(fresh, catalog, web);
        var result = await _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest
        {
            Items = draft.Items.Select(i => new MealDraftCommitItem { ItemId = i.ItemId, Grams = 200 }).ToList()
        });
        Assert.Equal(830m, result.TotalCalories);
        Assert.Equal(new[] { 330m, 260m, 240m }, _items[result.MealId].Select(i => i.Calories));
        Assert.Equal(new[] { "Sourced", "Sourced", "Web" }, _items[result.MealId].Select(i => i.NutritionProvenance));
    }

    [Fact]
    public async Task Create_NormalizesNumbersAndTotalsFromBasis()
    {
        var item = Item("rice", 130);
        item.Calories = 999;
        var draft = await Create(item);
        Assert.Equal(130m, draft.Items[0].Calories);
        Assert.Equal(130m, draft.Totals.Calories);
        Assert.Equal("Sourced", draft.Items[0].NutritionProvenance);
        Assert.NotEqual(Guid.Empty, draft.Items[0].ItemId);
    }
    [Fact]
    public async Task Create_CanonicalizesMealTypeAndCommitUsesIt()
    {
        var draft = await _service.CreateAsync(_user, new MealDraftCreateRequest
        {
            Origin = "photo",
            MealType = "lunch",
            Items = [Item("rice", 130)]
        });

        Assert.Equal("Lunch", draft.MealType);
        Assert.Equal("Lunch", _drafts[draft.DraftId].MealType);
        await _service.CommitAsync(_user, draft.DraftId, null);

        Assert.Equal(GutAI.Domain.Enums.MealType.Lunch, _meals.Single().MealType);
    }

    [Theory]
    [InlineData("Meal")]
    [InlineData("Beverage")]
    public async Task Create_RejectsInvalidMealTypeWithoutPersistingDraft(string mealType)
    {
        var exception = await Assert.ThrowsAsync<MealDraftException>(() => _service.CreateAsync(_user, new MealDraftCreateRequest
        {
            Origin = "photo",
            MealType = mealType,
            Items = [Item("rice", 130)]
        }));

        Assert.Equal(MealDraftErrorCode.Validation, exception.Code);
        Assert.Empty(_drafts);
    }

    [Fact]
    public async Task Create_NullMealTypeCommitsWithDefault()
    {
        var draft = await Create(Item("rice", 130));
        Assert.Null(draft.MealType);

        await _service.CommitAsync(_user, draft.DraftId, null);

        Assert.Equal(GutAI.Domain.Enums.MealType.Snack, _meals.Single().MealType);
    }


    [Fact]
    public async Task Commit_RejectsUnknownItemAndInvalidGrams()
    {
        var draft = await Create(Item("rice", 130));
        var unknown = await Assert.ThrowsAsync<MealDraftException>(() => _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest { Items = [new() { ItemId = Guid.NewGuid(), Grams = 100 }] }));
        Assert.Equal(MealDraftErrorCode.Validation, unknown.Code);
        var badGrams = await Assert.ThrowsAsync<MealDraftException>(() => _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest { Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 0 }] }));
        Assert.Equal(MealDraftErrorCode.Validation, badGrams.Code);
    }

    [Fact]
    public async Task Commit_RequiresExplicitChoiceForMissingBasis()
    {
        var item = new MealDraftItemDto { ItemId = Guid.NewGuid(), Name = "unknown", Source = "ai", Grams = 50, MatchConfidence = .2m };
        var draft = await Create(item);
        var exception = await Assert.ThrowsAsync<MealDraftUnresolvedItemsException>(() => _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest { Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 50 }] }));
        Assert.Equal(new[] { draft.Items[0].ItemId }, exception.ItemIds);
        var result = await _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest { Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 50, LogWithoutCalories = true }] });
        Assert.Equal(1, result.ItemsWithoutNutrition);
        Assert.Equal("Unknown", _items[result.MealId][0].NutritionProvenance);
        Assert.Equal(0, _items[result.MealId][0].Calories);
    }

    [Fact]
    public async Task Commit_NeedsChoiceRequiresExplicitChoiceAndCanLogWithoutCalories()
    {
        var draft = await Create(NeedsChoiceItem());
        var itemId = draft.Items[0].ItemId;

        var exception = await Assert.ThrowsAsync<MealDraftUnresolvedItemsException>(() => _service.CommitAsync(_user, draft.DraftId,
            new MealDraftCommitRequest { Items = [new() { ItemId = itemId, Grams = 50 }] }));
        Assert.Equal(new[] { itemId }, exception.ItemIds);
        Assert.Empty(_meals);
        Assert.Equal(MealDraftStatuses.PendingReview, _drafts[draft.DraftId].Status);

        var selected = await _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest
        {
            Items = [new() { ItemId = itemId, Grams = 50, SelectedCandidateKey = "previewed-candidate" }]
        });
        Assert.Equal(100m, selected.TotalCalories);
        Assert.Equal(100m, _items[selected.MealId][0].Calories);
        Assert.Equal("Candidate Food", _items[selected.MealId][0].FoodName);
        Assert.NotNull(_items[selected.MealId][0].FoodProductId);

        var withoutCaloriesDraft = await Create(NeedsChoiceItem("another ambiguous food"));
        var withoutCaloriesItemId = withoutCaloriesDraft.Items[0].ItemId;
        var withoutCalories = await _service.CommitAsync(_user, withoutCaloriesDraft.DraftId, new MealDraftCommitRequest
        {
            Items = [new() { ItemId = withoutCaloriesItemId, Grams = 50, LogWithoutCalories = true }]
        });
        var logged = Assert.Single(_items[withoutCalories.MealId]);
        Assert.Equal(1, withoutCalories.ItemsWithoutNutrition);
        Assert.Equal("Another Ambiguous Food", logged.FoodName);
        Assert.Equal("Unknown", logged.NutritionProvenance);
        Assert.Null(logged.FoodProductId);
        Assert.Equal(0m, logged.Calories);
    }

    [Fact]
    public async Task Commit_ExcludedNeedsChoiceItemDoesNotBlockIncludedItems()
    {
        var draft = await Create(Item("included", 100), NeedsChoiceItem("excluded", included: false));
        var result = await _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest
        {
            Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 100 }]
        });
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(100m, result.TotalCalories);
        Assert.Equal("Included", Assert.Single(_items[result.MealId]).FoodName);
    }

    [Fact]
    public async Task Update_NeedsChoiceDoesNotRequireChoice()
    {
        var draft = await Create(NeedsChoiceItem());
        var updated = await _service.UpdateAsync(_user, draft.DraftId, new MealDraftUpdateRequest
        {
            Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 50 }]
        });
        Assert.Equal(450m, updated.Items[0].Calories);
        Assert.Equal(MealDraftStatuses.PendingReview, updated.Status);
    }

    [Fact]
    public async Task Commit_NullItemsIncludesOnlyDefaultAndSecondCommitConflicts()
    {
        var draft = await Create(Item("included", 100), Item("opt in", 80, included: false));
        var result = await _service.CommitAsync(_user, draft.DraftId, null);
        Assert.Equal(1, result.ItemCount);
        var exception = await Assert.ThrowsAsync<MealDraftException>(() => _service.CommitAsync(_user, draft.DraftId, null));
        Assert.Equal(MealDraftErrorCode.NotPending, exception.Code);
    }

    [Fact]
    public async Task Commit_EnforcesOriginAndSameTurnGuards()
    {
        var draft = await Create(Item("rice", 100));
        var sameTurn = await Assert.ThrowsAsync<MealDraftException>(() => _service.CommitAsync(_user, draft.DraftId, null, new MealDraftCommitGuard(CreatedBefore: draft.CreatedAt)));
        Assert.Equal(MealDraftErrorCode.SameTurnCommit, sameTurn.Code);
        var origin = await Assert.ThrowsAsync<MealDraftException>(() => _service.CommitAsync(_user, draft.DraftId, null, new MealDraftCommitGuard(RequiredOrigin: "coach")));
        Assert.Equal(MealDraftErrorCode.OriginMismatch, origin.Code);
    }

    [Fact]
    public async Task Discard_RetainsClosedDraftAndIsIdempotent()
    {
        var draft = await Create(Item("rice", 100));

        await _service.DiscardAsync(_user, draft.DraftId);

        Assert.Equal(MealDraftStatuses.Discarded, _drafts[draft.DraftId].Status);
        Assert.Null(await _service.GetAsync(_user, draft.DraftId));
        Assert.Empty(await _service.ListPendingAsync(_user));

        await _service.DiscardAsync(_user, draft.DraftId);

        Assert.Equal(MealDraftStatuses.Discarded, _drafts[draft.DraftId].Status);
        Assert.Contains(draft.DraftId, _drafts.Keys);
    }

    [Fact]
    public async Task Commit_SwapsByCandidateKeyAndClearsIdentityConfidence()
    {
        _store.Setup(s => s.SearchFoodProductsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("name index unavailable"));
        var item = Item("rice", 10) with { Grounding = new GroundingAttemptDto { Query = "rice", ResolutionStatus = "Ambiguous", AutoSelected = false, MatchConfidence = .5m, Method = "resolve_async", Candidates = [new("brown rice", null, "usda", .9m, Calories100g: 220, Protein100g: 5, CandidateKey: "brown")] } };
        var draft = await Create(item);
        var result = await _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest { Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 100, SelectedCandidateKey = "brown" }] });
        Assert.Equal(220m, _items[result.MealId][0].Calories);
        Assert.Null(_items[result.MealId][0].MatchConfidence);
        using var delta = System.Text.Json.JsonDocument.Parse(_drafts[draft.DraftId].CorrectionDeltaJson!);
        Assert.Equal("brown", delta.RootElement[0].GetProperty("swappedToKey").GetString());
    }

    [Fact]
    public async Task Commit_ReplacementProductRecomputesAndCorrectionDeltaCapturesChanges()
    {
        var replacementId = Guid.NewGuid();
        _products[replacementId] = new FoodProduct { Id = replacementId, Name = "Boiled potato", Calories100g = 87, Protein100g = 2 };
        var draft = await Create(Item("potato", 77), Item("removed bread", 200));
        var committed = await _service.CommitAsync(_user, draft.DraftId, new MealDraftCommitRequest
        {
            Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 150, ReplacementFoodProductId = replacementId }]
        });
        Assert.Equal(131m, _items[committed.MealId][0].Calories);
        Assert.Null(_items[committed.MealId][0].MatchConfidence);
        using var delta = System.Text.Json.JsonDocument.Parse(_drafts[draft.DraftId].CorrectionDeltaJson!);
        Assert.Equal(1.5m, delta.RootElement[0].GetProperty("gramRatio").GetDecimal());
        Assert.False(delta.RootElement[0].GetProperty("removed").GetBoolean());
        Assert.True(delta.RootElement[1].GetProperty("removed").GetBoolean());
        Assert.Equal(replacementId, delta.RootElement[0].GetProperty("replacementFoodProductId").GetGuid());
    }

    [Fact]
    public async Task Get_MarksExpiredDraftAndReturnsNull()
    {
        var draft = await Create(Item("rice", 100));
        _drafts[draft.DraftId] = _drafts[draft.DraftId] with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
        Assert.Null(await _service.GetAsync(_user, draft.DraftId));
        var exception = await Assert.ThrowsAsync<MealDraftException>(() => _service.CommitAsync(_user, draft.DraftId, null));
        Assert.Equal(MealDraftErrorCode.NotFound, exception.Code);
        Assert.Equal(MealDraftStatuses.Expired, _drafts[draft.DraftId].Status);
    }

    [Fact]
    public async Task Commit_MealWriteFailureRevertsClaimToPending()
    {
        var draft = await Create(Item("rice", 100));
        _store.Setup(s => s.UpsertMealLogAsync(It.IsAny<MealLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("meal write failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CommitAsync(_user, draft.DraftId, null));
        Assert.Equal(MealDraftStatuses.PendingReview, _drafts[draft.DraftId].Status);
        Assert.Null(_drafts[draft.DraftId].CommittedMealId);
        Assert.Null(_drafts[draft.DraftId].CommittedAt);
    }

    [Fact]
    public async Task Update_RecomputesListedItemsAndKeepsUnlistedItems()
    {
        var draft = await Create(Item("rice", 100), Item("chicken", 165));
        var result = await _service.UpdateAsync(_user, draft.DraftId, new MealDraftUpdateRequest
        {
            Items = [new() { ItemId = draft.Items[0].ItemId, Grams = 200 }]
        });
        Assert.Equal(200m, result.Items[0].Grams);
        Assert.Equal(200m, result.Items[0].Calories); // 100 kcal/100 g × 200 g
        Assert.Equal(100m, result.Items[1].Grams);
        Assert.Equal(165m, result.Items[1].Calories);
        Assert.Equal(365m, result.Totals.Calories);
    }
}
