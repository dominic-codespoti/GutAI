using GutAI.Domain.Entities;

namespace GutAI.Application.Common.Interfaces;

public interface ITableStore
{
    Task<User?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task UpsertUserAsync(User user, CancellationToken ct = default);
    Task DeleteUserAsync(Guid userId, CancellationToken ct = default);

    Task<IdentityRecord?> GetIdentityByIdAsync(Guid userId, CancellationToken ct = default);
    Task<IdentityRecord?> GetIdentityByEmailAsync(string email, CancellationToken ct = default);
    Task UpsertIdentityAsync(IdentityRecord identity, CancellationToken ct = default);
    Task DeleteIdentityAsync(Guid userId, CancellationToken ct = default);

    Task<MealLog?> GetMealLogAsync(Guid userId, Guid mealId, CancellationToken ct = default);
    Task<List<MealLog>> GetMealLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct = default);
    Task<List<MealLog>> GetMealLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task UpsertMealLogAsync(MealLog meal, CancellationToken ct = default);

    Task<List<MealItem>> GetMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct = default);
    Task<List<MealItem>> GetAllUserMealItemsAsync(Guid userId, int limit = 100, CancellationToken ct = default);
    Task UpsertMealItemsAsync(Guid userId, Guid mealLogId, List<MealItem> items, CancellationToken ct = default);
    Task DeleteMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct = default);

    Task<SymptomLog?> GetSymptomLogAsync(Guid userId, Guid symptomId, CancellationToken ct = default);
    Task<List<SymptomLog>> GetSymptomLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct = default);
    Task<List<SymptomLog>> GetSymptomLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task UpsertSymptomLogAsync(SymptomLog symptom, CancellationToken ct = default);

    Task<List<SymptomType>> GetAllSymptomTypesAsync(CancellationToken ct = default);
    Task<SymptomType?> GetSymptomTypeAsync(int id, CancellationToken ct = default);
    Task UpsertSymptomTypeAsync(SymptomType type, CancellationToken ct = default);
    Task<bool> SymptomTypeExistsAsync(int id, CancellationToken ct = default);

    Task<FoodProduct?> GetFoodProductAsync(Guid id, CancellationToken ct = default);
    Task<FoodProduct?> GetFoodProductByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<List<FoodProduct>> SearchFoodProductsAsync(string query, int maxResults, CancellationToken ct = default);
    Task UpsertFoodProductAsync(FoodProduct product, CancellationToken ct = default);

    Task<List<FoodAdditive>> GetAllFoodAdditivesAsync(CancellationToken ct = default);
    Task<FoodAdditive?> GetFoodAdditiveAsync(int id, CancellationToken ct = default);
    Task UpsertFoodAdditiveAsync(FoodAdditive additive, CancellationToken ct = default);

    Task<List<int>> GetAdditiveIdsForProductAsync(Guid foodProductId, CancellationToken ct = default);
    Task SetAdditiveIdsForProductAsync(Guid foodProductId, List<int> additiveIds, CancellationToken ct = default);

    Task<RefreshToken?> GetRefreshTokenByValueAsync(string token, CancellationToken ct = default);
    Task<List<RefreshToken>> GetActiveRefreshTokensAsync(Guid userId, CancellationToken ct = default);
    Task UpsertRefreshTokenAsync(RefreshToken token, CancellationToken ct = default);
    Task DeleteRefreshTokensForUserAsync(Guid userId, CancellationToken ct = default);

    Task<DailyNutritionSummary?> GetDailyNutritionSummaryAsync(Guid userId, DateOnly date, CancellationToken ct = default);
    Task UpsertDailyNutritionSummaryAsync(DailyNutritionSummary summary, CancellationToken ct = default);

    Task<List<UserFoodAlert>> GetUserFoodAlertsAsync(Guid userId, CancellationToken ct = default);
    Task<UserFoodAlert?> GetUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct = default);
    Task UpsertUserFoodAlertAsync(UserFoodAlert alert, CancellationToken ct = default);
    Task DeleteUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct = default);

    Task<InsightReport?> GetInsightReportAsync(Guid userId, Guid reportId, CancellationToken ct = default);
    Task<List<InsightReport>> GetInsightReportsAsync(Guid userId, CancellationToken ct = default);
    Task UpsertInsightReportAsync(InsightReport report, CancellationToken ct = default);

}
