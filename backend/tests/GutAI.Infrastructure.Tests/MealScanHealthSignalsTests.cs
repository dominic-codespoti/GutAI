using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Constants;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// P5 health-signal enrichment tests. Safety-critical invariant under test:
/// web/ai items (no FoodProductId) NEVER receive FODMAP/gut-risk signals —
/// only catalogue-grounded items do.
/// </summary>
public class MealScanHealthSignalsTests
{
    private static MealScanItemDto GroundedItem(Guid productId) => new()
    {
        ItemId = Guid.NewGuid(),
        Name = "grilled chicken",
        FoodProductId = productId,
        Source = "usda",
        Grams = 120m,
        MatchConfidence = 0.95m,
        VisionConfidence = 0.9m,
    };

    private static MealScanItemDto AiItem() => new()
    {
        ItemId = Guid.NewGuid(),
        Name = "grandma mystery casserole",
        Source = "ai",
        Grams = 200m,
        MatchConfidence = 1m,
        VisionConfidence = 0.7m,
    };

    private static (MealScanHealthSignalsEnricherHarness Harness, InMemoryAdditiveStore Store) MakeHarness()
    {
        var store = new InMemoryAdditiveStore();
        var harness = new MealScanHealthSignalsEnricherHarness(store);
        return (harness, store);
    }

    [Fact]
    public async Task Enrich_GroundedProduct_SetsSignalsWithoutThrowing()
    {
        var (harness, store) = MakeHarness();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = "Rice, white, cooked",
            DataSource = DataSources.Usda,
            Calories100g = 130m,
            Protein100g = 2.7m,
        };
        await store.SeedAsync(product);

        var item = GroundedItem(product.Id);
        await harness.EnrichAsync(item);

        // Exact status depends on FodmapData matching, but the fields must be
        // populated and consistent for a real catalogue entry.
        item.FodmapStatus.Should().NotBeNullOrEmpty();
        item.GutRating.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Enrich_AiOrWebItem_NeverReceivesSignals()
    {
        var (harness, _) = MakeHarness();
        var item = AiItem();

        await harness.EnrichAsync(item);

        item.FodmapStatus.Should().BeNull("web/ai items must never imply FODMAP safety");
        item.FodmapTriggers.Should().BeNull();
        item.GutRating.Should().BeNull();
    }

    [Fact]
    public async Task Enrich_MissingProduct_FailsSoft()
    {
        var (harness, _) = MakeHarness();
        var item = GroundedItem(Guid.NewGuid()); // no product seeded

        var act = () => harness.EnrichAsync(item);
        await act.Should().NotThrowAsync();
    }

    /// <summary>Thin wrapper so tests exercise the real enricher with real services.</summary>
    private sealed class MealScanHealthSignalsEnricherHarness(ITableStore store)
    {
        public Task EnrichAsync(MealScanItemDto item) =>
            MealScanHealthSignals.EnrichAsync(
                item, store,
                new Infrastructure.Services.FodmapService(),
                new Infrastructure.Services.GutRiskService());
    }

    /// <summary>Minimal ITableStore standing in for additive lookups; everything else throws.</summary>
    private sealed class InMemoryAdditiveStore : ITableStore
    {
        private readonly Dictionary<Guid, FoodProduct> _products = new();

        public async Task SeedAsync(FoodProduct p)
        {
            _products[p.Id] = p;
            // BuildFoodProductDto calls GetAllFoodAdditivesAsync + GetFoodProductAsync
            await Task.CompletedTask;
        }

        public Task<FoodProduct?> GetFoodProductAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_products.GetValueOrDefault(id));
        public Task<List<FoodAdditive>> GetAllFoodAdditivesAsync(CancellationToken ct = default)
            => Task.FromResult(new List<FoodAdditive>());

        public Task<User?> GetUserAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertUserAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteUserAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IdentityRecord?> GetIdentityByIdAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IdentityRecord?> GetIdentityByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertIdentityAsync(IdentityRecord identity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteIdentityAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MealLog?> GetMealLogAsync(Guid userId, Guid mealId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<MealLog>> GetMealLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<MealLog>> GetMealLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertMealLogAsync(MealLog meal, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<MealItem>> GetMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<MealItem>> GetAllUserMealItemsAsync(Guid userId, int limit = 100, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertMealItemsAsync(Guid userId, Guid mealLogId, List<MealItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SymptomLog?> GetSymptomLogAsync(Guid userId, Guid symptomId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<SymptomLog>> GetSymptomLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<SymptomLog>> GetSymptomLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertSymptomLogAsync(SymptomLog symptom, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<SymptomType>> GetAllSymptomTypesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SymptomType?> GetSymptomTypeAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertSymptomTypeAsync(SymptomType type, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SymptomTypeExistsAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FoodProduct?> GetFoodProductByBarcodeAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FoodProduct?> GetFoodProductBySourceAsync(string dataSource, string externalId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<FoodProduct>> SearchFoodProductsAsync(string query, int maxResults, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Dictionary<Guid, string?>> GetFoodProductSafetyRatingsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertFoodProductAsync(FoodProduct product, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FoodAdditive?> GetFoodAdditiveAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertFoodAdditiveAsync(FoodAdditive additive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<int>> GetAdditiveIdsForProductAsync(Guid foodProductId, CancellationToken ct = default) => Task.FromResult(new List<int>());
        public Task SetAdditiveIdsForProductAsync(Guid foodProductId, List<int> additiveIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RefreshToken?> GetRefreshTokenByValueAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<RefreshToken>> GetActiveRefreshTokensAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertRefreshTokenAsync(RefreshToken token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteRefreshTokensForUserAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DailyNutritionSummary?> GetDailyNutritionSummaryAsync(Guid userId, DateOnly date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertDailyNutritionSummaryAsync(DailyNutritionSummary summary, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<UserFoodAlert>> GetUserFoodAlertsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UserFoodAlert?> GetUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertUserFoodAlertAsync(UserFoodAlert alert, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<FavoriteFoodProduct>> GetUserFavoriteFoodsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FavoriteFoodProduct?> GetUserFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertFavoriteFoodAsync(FavoriteFoodProduct favorite, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InsightReport?> GetInsightReportAsync(Guid userId, Guid reportId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<InsightReport>> GetInsightReportsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertInsightReportAsync(InsightReport report, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CustomFood?> GetCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<CustomFood>> GetCustomFoodsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertCustomFoodAsync(CustomFood food, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<CoachChatMessage>> GetRecentCoachMessagesAsync(Guid userId, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertCoachMessageAsync(Guid userId, DateTimeOffset at, string role, string text, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteCoachMessagesAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertScanSessionAsync(ScanSessionRecord session, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ScanSessionRecord?> GetScanSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteScanSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WebNutritionResult?> GetWebNutritionCacheAsync(string normalizedName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertWebNutritionCacheAsync(WebNutritionResult result, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
