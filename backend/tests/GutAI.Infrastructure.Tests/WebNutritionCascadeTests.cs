using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class WebNutritionCascadeTests
{
    // ── DDG HTML parsing ──

    private const string DdgFixture = """
        <div class="result results_links">
          <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Ffdc.nal.usda.gov%2Ffood-details%2F123&rut=abc">Rice, white, cooked - FoodData Central</a>
          <a class="result__snippet" href="#">Per 100 g: 130 kcal</a>
        </div>
        <div class="result">
          <a rel="nofollow" class="result__a" href="https://www.nutritionvalue.org/Rice%2C_white%2C_cooked.html">Rice white cooked — NutritionValue</a>
        </div>
        <a class="result__a" href="https://blocked.example.com/page">Some blog</a>
        """;

    [Fact]
    public void ParseResults_ExtractsAndDecodesRedirects()
    {
        var results = WebNutritionCascade.DuckDuckGoParser.ParseResults(DdgFixture);

        results.Should().HaveCount(3);
        results[0].Url.Should().Be("https://fdc.nal.usda.gov/food-details/123");
        results[0].Title.Should().Contain("FoodData Central");
        results[1].Url.Should().Contain("nutritionvalue.org");
    }

    [Fact]
    public void ParseResults_EmptyOrGarbage_ReturnsEmpty()
    {
        WebNutritionCascade.DuckDuckGoParser.ParseResults("").Should().BeEmpty();
        WebNutritionCascade.DuckDuckGoParser.ParseResults("<html><body>no results</body></html>").Should().BeEmpty();
    }

    // ── Plausibility gate ──

    private static WebNutritionResult Result(
        decimal kcal = 130m, decimal p = 2.7m, decimal c = 28m, decimal f = 0.3m,
        string url = "https://fdc.nal.usda.gov/x") => new()
    {
        CaloriesKcal = kcal, ProteinG = p, CarbsG = c, FatG = f,
        SourceName = "USDA", SourceUrl = url,
    };

    [Fact]
    public void IsPlausible_TypicalValues_Pass()
    {
        WebNutritionCascade.IsPlausible(Result()).Should().BeTrue();
    }

    [Theory]
    [InlineData(1500, 5, 20, 3)]     // absurd kcal
    [InlineData(0, 5, 20, 3)]        // zero kcal with macros
    [InlineData(200, 500, 20, 3)]    // protein 500g/100g
    [InlineData(200, 5, 20, 900)]    // fat 900g/100g
    public void IsPlausible_OutOfRange_Rejects(decimal kcal, decimal p, decimal c, decimal f)
    {
        WebNutritionCascade.IsPlausible(Result(kcal: kcal, p: p, c: c, f: f)).Should().BeFalse();
    }

    [Fact]
    public void IsPlausible_SugarExceedingCarbs_Rejected()
    {
        var r = Result(kcal: 400m, p: 5m, c: 10m, f: 3m);
        var withSugar = r with { SugarG = 80m };
        WebNutritionCascade.IsPlausible(withSugar).Should().BeFalse("sugar cannot exceed total carbs by a wide margin");
    }

    [Fact]
    public void Validate_MacroEnergyMismatch_Rejected()
    {
        // 400 kcal claimed but macros only account for ~40 kcal → unit/row mixup
        var r = new WebNutritionResult
        {
            CaloriesKcal = 400m, ProteinG = 2m, CarbsG = 5m, FatG = 1m,
            SourceName = "x", SourceUrl = "https://example.com",
        };
        WebNutritionCascade.IsPlausible(r).Should().BeFalse();
    }

    // ── Orchestration (network overridden) ──

    private sealed class FakeCascade : WebNutritionCascade
    {
        public List<string> Searches = [];
        public Dictionary<string, List<(string, string)>> SearchResults = new();
        public Dictionary<string, string?> Pages = new();

        public FakeCascade(ITableStore store) : base(
            new FakeChatClient(), store, MakeConfig(), new HttpClient(), NullLogger<WebNutritionCascade>.Instance) { }

        internal override Task<List<(string Title, string Url)>> SearchDuckDuckGo(string query, CancellationToken ct)
        {
            Searches.Add(query);
            return Task.FromResult(SearchResults.GetValueOrDefault(query) ?? []);
        }

        internal override Task<string?> FetchViaJina(string url, CancellationToken ct)
            => Task.FromResult(Pages.GetValueOrDefault(url));

        internal override Task<WebNutritionExtraction?> ExtractAsync(string foodName, string url, string markdown, CancellationToken ct)
            => Task.FromResult<WebNutritionExtraction?>(new WebNutritionExtraction
            {
                Found = true, CaloriesKcal = 130m, ProteinG = 27m, CarbsG = 0m, FatG = 1.5m,
                SourceName = "USDA FoodData Central", SourceUrl = url,
            });

        private static IConfiguration MakeConfig()
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:WebGrounding"] = "true",
                ["MealScan:MaxWebQueriesPerScan"] = "2",
            }).Build();
            return cfg;
        }
    }

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("extraction is overridden in tests");
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class MemoryStore : ITableStore
    {
        public readonly Dictionary<string, WebNutritionResult> Cache = new();
        public Task<WebNutritionResult?> GetWebNutritionCacheAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Cache.GetValueOrDefault(key.ToLowerInvariant().Trim()));
        public Task UpsertWebNutritionCacheAsync(WebNutritionResult result, CancellationToken ct = default)
            => Task.FromResult(Cache.TryAdd(result.CacheKey!, result));
        // unused members throw
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
        public Task<FoodProduct?> GetFoodProductAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FoodProduct?> GetFoodProductByBarcodeAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FoodProduct?> GetFoodProductBySourceAsync(string dataSource, string externalId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<FoodProduct>> SearchFoodProductsAsync(string query, int maxResults, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Dictionary<Guid, string?>> GetFoodProductSafetyRatingsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertFoodProductAsync(FoodProduct product, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<FoodAdditive>> GetAllFoodAdditivesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FoodAdditive?> GetFoodAdditiveAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertFoodAdditiveAsync(FoodAdditive additive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<int>> GetAdditiveIdsForProductAsync(Guid foodProductId, CancellationToken ct = default) => throw new NotSupportedException();
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
    }
}
