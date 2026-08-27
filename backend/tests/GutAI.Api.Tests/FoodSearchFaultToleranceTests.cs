using System.Net;
using Azure.Data.Tables;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GutAI.Api.Tests;
[Collection("WebApi")]
public class FoodSearchFaultToleranceTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task SearchFoodProducts_WhenLocalStoreSearchThrows_StillReturns200WithResults()
    {
        // Arrange: arm the process-wide fault-injection store so SearchFoodProductsAsync
        // throws while this test runs. The fault is injected into the SHARED host because
        // spinning a second WebApplicationFactory host double-registers OpenTelemetry
        // listeners (process-global TracerProviderSdk), which crashes Azure Tables calls
        // with duplicate-tag sampling errors.
        FaultInjectionTableStore.ThrowOnSearch = true;
        try
        {
            var client = factory.CreateClient();
            var email = $"fault-search-{Guid.NewGuid():N}@test.com";
            var regResp = await client.PostAsJsonAsync("/api/auth/register", new
            {
                email,
                password = "TestPass123",
                displayName = "Fault Test User"
            });
            regResp.EnsureSuccessStatusCode();
            var regJson = await regResp.Content.ReadFromJsonAsync<JsonElement>();
            var token = regJson.GetProperty("accessToken").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Act: Search for food product with failing local store search
            var response = await client.GetAsync("/api/food/search?q=apple");

            // Assert: Degrades gracefully to 200 OK with array results instead of 500
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.ValueKind.Should().Be(JsonValueKind.Array);
        }
        finally
        {
            FaultInjectionTableStore.ThrowOnSearch = false;
        }
    }

    internal sealed class FaultInjectionTableStore(ITableStore inner) : ITableStore
    {
        // Collection-scoped tests run serially, so a plain static flag is a safe arm switch.
        internal static bool ThrowOnSearch;

        public Task<User?> GetUserAsync(Guid userId, CancellationToken ct = default) => inner.GetUserAsync(userId, default);

        public Task UpsertUserAsync(User user, CancellationToken ct = default) => inner.UpsertUserAsync(user, default);

        public Task DeleteUserAsync(Guid userId, CancellationToken ct = default) => inner.DeleteUserAsync(userId, default);

        public Task<IdentityRecord?> GetIdentityByIdAsync(Guid userId, CancellationToken ct = default) => inner.GetIdentityByIdAsync(userId, default);

        public Task<IdentityRecord?> GetIdentityByEmailAsync(string email, CancellationToken ct = default) => inner.GetIdentityByEmailAsync(email, default);

        public Task UpsertIdentityAsync(IdentityRecord identity, CancellationToken ct = default) => inner.UpsertIdentityAsync(identity, default);

        public Task DeleteIdentityAsync(Guid userId, CancellationToken ct = default) => inner.DeleteIdentityAsync(userId, default);

        public Task<MealLog?> GetMealLogAsync(Guid userId, Guid mealId, CancellationToken ct = default) => inner.GetMealLogAsync(userId, mealId, default);

        public Task<List<MealLog>> GetMealLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct = default) => inner.GetMealLogsByDateAsync(userId, date, default);

        public Task<List<MealLog>> GetMealLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default) => inner.GetMealLogsByDateRangeAsync(userId, from, to, default);

        public Task<MealLog?> GetMealLogByExternalRefAsync(Guid userId, string source, string externalId, CancellationToken ct = default) => inner.GetMealLogByExternalRefAsync(userId, source, externalId, default);

        public Task UpsertMealLogAsync(MealLog meal, CancellationToken ct = default) => inner.UpsertMealLogAsync(meal, default);

        public Task<List<MealItem>> GetMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct = default) => inner.GetMealItemsAsync(userId, mealLogId, default);

        public Task<List<MealItem>> GetAllUserMealItemsAsync(Guid userId, int limit = 100, CancellationToken ct = default) => inner.GetAllUserMealItemsAsync(userId, 100, default);

        public Task UpsertMealItemsAsync(Guid userId, Guid mealLogId, List<MealItem> items, CancellationToken ct = default) => inner.UpsertMealItemsAsync(userId, mealLogId, items, default);

        public Task DeleteMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct = default) => inner.DeleteMealItemsAsync(userId, mealLogId, default);

        public Task<SymptomLog?> GetSymptomLogAsync(Guid userId, Guid symptomId, CancellationToken ct = default) => inner.GetSymptomLogAsync(userId, symptomId, default);

        public Task<List<SymptomLog>> GetSymptomLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct = default) => inner.GetSymptomLogsByDateAsync(userId, date, default);

        public Task<List<SymptomLog>> GetSymptomLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default) => inner.GetSymptomLogsByDateRangeAsync(userId, from, to, default);

        public Task UpsertSymptomLogAsync(SymptomLog symptom, CancellationToken ct = default) => inner.UpsertSymptomLogAsync(symptom, default);

        public Task<List<SymptomType>> GetAllSymptomTypesAsync(CancellationToken ct = default) => inner.GetAllSymptomTypesAsync(default);

        public Task<SymptomType?> GetSymptomTypeAsync(int id, CancellationToken ct = default) => inner.GetSymptomTypeAsync(id, default);

        public Task UpsertSymptomTypeAsync(SymptomType type, CancellationToken ct = default) => inner.UpsertSymptomTypeAsync(type, default);

        public Task<bool> SymptomTypeExistsAsync(int id, CancellationToken ct = default) => inner.SymptomTypeExistsAsync(id, default);

        public Task<FoodProduct?> GetFoodProductAsync(Guid id, CancellationToken ct = default) => inner.GetFoodProductAsync(id, default);

        public Task<FoodProduct?> GetFoodProductByBarcodeAsync(string barcode, CancellationToken ct = default) => inner.GetFoodProductByBarcodeAsync(barcode, default);

        public Task<FoodProduct?> GetFoodProductBySourceAsync(string dataSource, string externalId, CancellationToken ct = default) => inner.GetFoodProductBySourceAsync(dataSource, externalId, default);

        public Task<List<FoodProduct>> SearchFoodProductsAsync(string query, int maxResults, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated local food store search blip / storage failure");

        public Task<Dictionary<Guid, string?>> GetFoodProductSafetyRatingsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => inner.GetFoodProductSafetyRatingsAsync(ids, default);

        public Task UpsertFoodProductAsync(FoodProduct product, CancellationToken ct = default) => inner.UpsertFoodProductAsync(product, default);

        public Task<List<FoodAdditive>> GetAllFoodAdditivesAsync(CancellationToken ct = default) => inner.GetAllFoodAdditivesAsync(default);

        public Task<FoodAdditive?> GetFoodAdditiveAsync(int id, CancellationToken ct = default) => inner.GetFoodAdditiveAsync(id, default);

        public Task UpsertFoodAdditiveAsync(FoodAdditive additive, CancellationToken ct = default) => inner.UpsertFoodAdditiveAsync(additive, default);

        public Task<List<int>> GetAdditiveIdsForProductAsync(Guid foodProductId, CancellationToken ct = default) => inner.GetAdditiveIdsForProductAsync(foodProductId, default);

        public Task SetAdditiveIdsForProductAsync(Guid foodProductId, List<int> additiveIds, CancellationToken ct = default) => inner.SetAdditiveIdsForProductAsync(foodProductId, additiveIds, default);

        public Task<RefreshToken?> GetRefreshTokenByValueAsync(string token, CancellationToken ct = default) => inner.GetRefreshTokenByValueAsync(token, default);

        public Task<List<RefreshToken>> GetActiveRefreshTokensAsync(Guid userId, CancellationToken ct = default) => inner.GetActiveRefreshTokensAsync(userId, default);

        public Task UpsertRefreshTokenAsync(RefreshToken token, CancellationToken ct = default) => inner.UpsertRefreshTokenAsync(token, default);

        public Task DeleteRefreshTokensForUserAsync(Guid userId, CancellationToken ct = default) => inner.DeleteRefreshTokensForUserAsync(userId, default);

        public Task<PairingCode?> GetPairingCodeByHashAsync(string codeHash, CancellationToken ct = default) => inner.GetPairingCodeByHashAsync(codeHash, default);

        public Task UpsertPairingCodeAsync(PairingCode code, CancellationToken ct = default) => inner.UpsertPairingCodeAsync(code, default);

        public Task DeletePairingCodesForUserAsync(Guid userId, CancellationToken ct = default) => inner.DeletePairingCodesForUserAsync(userId, default);

        public Task<PersonalAccessToken?> GetPersonalAccessTokenByHashAsync(string tokenHash, CancellationToken ct = default) => inner.GetPersonalAccessTokenByHashAsync(tokenHash, default);

        public Task<List<PersonalAccessToken>> GetActivePersonalAccessTokensAsync(Guid userId, CancellationToken ct = default) => inner.GetActivePersonalAccessTokensAsync(userId, default);

        public Task UpsertPersonalAccessTokenAsync(PersonalAccessToken token, CancellationToken ct = default) => inner.UpsertPersonalAccessTokenAsync(token, default);

        public Task DeletePersonalAccessTokensForUserAsync(Guid userId, CancellationToken ct = default) => inner.DeletePersonalAccessTokensForUserAsync(userId, default);

        public Task<DailyNutritionSummary?> GetDailyNutritionSummaryAsync(Guid userId, DateOnly date, CancellationToken ct = default) => inner.GetDailyNutritionSummaryAsync(userId, date, default);

        public Task UpsertDailyNutritionSummaryAsync(DailyNutritionSummary summary, CancellationToken ct = default) => inner.UpsertDailyNutritionSummaryAsync(summary, default);

        public Task<List<UserFoodAlert>> GetUserFoodAlertsAsync(Guid userId, CancellationToken ct = default) => inner.GetUserFoodAlertsAsync(userId, default);

        public Task<UserFoodAlert?> GetUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct = default) => inner.GetUserFoodAlertAsync(userId, additiveId, default);

        public Task UpsertUserFoodAlertAsync(UserFoodAlert alert, CancellationToken ct = default) => inner.UpsertUserFoodAlertAsync(alert, default);

        public Task DeleteUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct = default) => inner.DeleteUserFoodAlertAsync(userId, additiveId, default);

        public Task<List<FavoriteFoodProduct>> GetUserFavoriteFoodsAsync(Guid userId, CancellationToken ct = default) => inner.GetUserFavoriteFoodsAsync(userId, default);

        public Task<FavoriteFoodProduct?> GetUserFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct = default) => inner.GetUserFavoriteFoodAsync(userId, foodProductId, default);

        public Task UpsertFavoriteFoodAsync(FavoriteFoodProduct favorite, CancellationToken ct = default) => inner.UpsertFavoriteFoodAsync(favorite, default);

        public Task DeleteFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct = default) => inner.DeleteFavoriteFoodAsync(userId, foodProductId, default);

        public Task<InsightReport?> GetInsightReportAsync(Guid userId, Guid reportId, CancellationToken ct = default) => inner.GetInsightReportAsync(userId, reportId, default);

        public Task<List<InsightReport>> GetInsightReportsAsync(Guid userId, CancellationToken ct = default) => inner.GetInsightReportsAsync(userId, default);

        public Task UpsertInsightReportAsync(InsightReport report, CancellationToken ct = default) => inner.UpsertInsightReportAsync(report, default);

        public Task<CustomFood?> GetCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct = default) => inner.GetCustomFoodAsync(userId, foodId, default);

        public Task<List<CustomFood>> GetCustomFoodsAsync(Guid userId, CancellationToken ct = default) => inner.GetCustomFoodsAsync(userId, default);

        public Task UpsertCustomFoodAsync(CustomFood food, CancellationToken ct = default) => inner.UpsertCustomFoodAsync(food, default);

        public Task DeleteCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct = default) => inner.DeleteCustomFoodAsync(userId, foodId, default);

        public Task<List<CoachChatMessage>> GetRecentCoachMessagesAsync(Guid userId, int limit, CancellationToken ct = default) => inner.GetRecentCoachMessagesAsync(userId, limit, default);

        public Task UpsertCoachMessageAsync(Guid userId, DateTimeOffset at, string role, string text, CancellationToken ct = default) => inner.UpsertCoachMessageAsync(userId, at, role, text, default);

        public Task DeleteCoachMessagesAsync(Guid userId, CancellationToken ct = default) => inner.DeleteCoachMessagesAsync(userId, default);

        public Task UpsertScanSessionAsync(ScanSessionRecord session, CancellationToken ct = default) => inner.UpsertScanSessionAsync(session, default);

        public Task<ScanSessionRecord?> GetScanSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => inner.GetScanSessionAsync(userId, sessionId, default);

        public Task DeleteScanSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => inner.DeleteScanSessionAsync(userId, sessionId, default);

        public Task<WebNutritionResult?> GetWebNutritionCacheAsync(string normalizedName, CancellationToken ct = default) => inner.GetWebNutritionCacheAsync(normalizedName, default);

        public Task UpsertWebNutritionCacheAsync(WebNutritionResult result, CancellationToken ct = default) => inner.UpsertWebNutritionCacheAsync(result, default);
    }
}
