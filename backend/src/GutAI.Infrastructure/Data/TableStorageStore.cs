using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Domain.ValueObjects;

namespace GutAI.Infrastructure.Data;

public class TableStorageStore : ITableStore
{
    private readonly TableClient _table;
    private volatile Task? _ensureCreatedTask;
    private readonly object _ensureLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public TableStorageStore(TableServiceClient serviceClient)
    {
        _table = serviceClient.GetTableClient("gutai");
    }

    private async Task EnsureTableAsync()
    {
        if (_ensureCreatedTask is { IsCompletedSuccessfully: true })
            return;

        Task task;
        lock (_ensureLock)
        {
            if (_ensureCreatedTask is not { IsCompletedSuccessfully: true })
                _ensureCreatedTask = _table.CreateIfNotExistsAsync();
            task = _ensureCreatedTask;
        }
        await task;
    }

    private async Task<TableEntity?> GetEntityOrNullAsync(string pk, string rk, CancellationToken ct)
    {
        await EnsureTableAsync();
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(pk, rk, cancellationToken: ct);
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task UpsertAsync(TableEntity entity, CancellationToken ct)
    {
        await EnsureTableAsync();
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    private async Task DeleteAsync(string pk, string rk, CancellationToken ct)
    {
        await EnsureTableAsync();
        try { await _table.DeleteEntityAsync(pk, rk, cancellationToken: ct); }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404) { }
    }

    private async Task<List<TableEntity>> QueryAsync(string filter, CancellationToken ct)
    {
        await EnsureTableAsync();
        var results = new List<TableEntity>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(filter, cancellationToken: ct))
            results.Add(entity);
        return results;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string Str(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? Str(decimal? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static decimal Dec(TableEntity e, string key)
    {
        var s = e.GetString(key);
        return !string.IsNullOrEmpty(s) &&
               decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : 0m;
    }

    private static decimal? DecN(TableEntity e, string key)
    {
        var s = e.GetString(key);
        return !string.IsNullOrEmpty(s) &&
               decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    private static string[] JsonToStringArray(string? json) =>
        string.IsNullOrEmpty(json) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(json, JsonOpts) ?? Array.Empty<string>();

    private static string StringArrayToJson(string[] arr) =>
        JsonSerializer.Serialize(arr, JsonOpts);

    // ═══════════════════════════════════════════════════════════
    //  Users
    // ═══════════════════════════════════════════════════════════

    public async Task<User?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync("USER", userId.ToString(), ct);
        return e == null ? null : MapToUser(e);
    }

    public async Task UpsertUserAsync(User user, CancellationToken ct)
    {
        user.UpdatedAt = DateTime.UtcNow;
        var e = new TableEntity("USER", user.Id.ToString())
        {
            { "Email", user.Email },
            { "DisplayName", user.DisplayName },
            { "CreatedAt", user.CreatedAt },
            { "UpdatedAt", user.UpdatedAt },
            { "DailyCalorieGoal", user.DailyCalorieGoal },
            { "DailyProteinGoalG", user.DailyProteinGoalG },
            { "DailyCarbGoalG", user.DailyCarbGoalG },
            { "DailyFatGoalG", user.DailyFatGoalG },
            { "DailyFiberGoalG", user.DailyFiberGoalG },
            { "Allergies", StringArrayToJson(user.Allergies) },
            { "DietaryPreferences", StringArrayToJson(user.DietaryPreferences) },
            { "GutConditions", StringArrayToJson(user.GutConditions) },
            { "OnboardingCompleted", user.OnboardingCompleted },
            { "TimezoneId", user.TimezoneId }
        };
        await UpsertAsync(e, ct);
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        await DeleteAsync("USER", userId.ToString(), ct);
    }

    private static User MapToUser(TableEntity e) => new()
    {
        Id = Guid.Parse(e.RowKey),
        Email = e.GetString("Email") ?? "",
        DisplayName = e.GetString("DisplayName"),
        CreatedAt = e.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow,
        UpdatedAt = e.GetDateTimeOffset("UpdatedAt")?.UtcDateTime ?? DateTime.UtcNow,
        DailyCalorieGoal = e.GetInt32("DailyCalorieGoal") ?? 2000,
        DailyProteinGoalG = e.GetInt32("DailyProteinGoalG") ?? 50,
        DailyCarbGoalG = e.GetInt32("DailyCarbGoalG") ?? 250,
        DailyFatGoalG = e.GetInt32("DailyFatGoalG") ?? 65,
        DailyFiberGoalG = e.GetInt32("DailyFiberGoalG") ?? 25,
        Allergies = JsonToStringArray(e.GetString("Allergies")),
        DietaryPreferences = JsonToStringArray(e.GetString("DietaryPreferences")),
        GutConditions = JsonToStringArray(e.GetString("GutConditions")),
        OnboardingCompleted = e.GetBoolean("OnboardingCompleted") ?? false,
        TimezoneId = e.GetString("TimezoneId")
    };

    // ═══════════════════════════════════════════════════════════
    //  Identity
    // ═══════════════════════════════════════════════════════════

    public async Task<IdentityRecord?> GetIdentityByIdAsync(Guid userId, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync("IDENTITY", userId.ToString(), ct);
        return e == null ? null : MapToIdentity(e);
    }

    public async Task<IdentityRecord?> GetIdentityByEmailAsync(string email, CancellationToken ct)
    {
        var normalizedEmail = email.ToUpperInvariant();
        var lookup = await GetEntityOrNullAsync("IDEMAIL", normalizedEmail, ct);
        if (lookup == null) return null;

        var userId = lookup.GetString("UserId");
        if (string.IsNullOrEmpty(userId)) return null;

        return await GetIdentityByIdAsync(Guid.Parse(userId), ct);
    }

    public async Task UpsertIdentityAsync(IdentityRecord identity, CancellationToken ct)
    {
        var e = new TableEntity("IDENTITY", identity.UserId.ToString())
        {
            { "Email", identity.Email },
            { "NormalizedEmail", identity.NormalizedEmail },
            { "PasswordHash", identity.PasswordHash },
            { "SecurityStamp", identity.SecurityStamp },
            { "CreatedAt", identity.CreatedAt }
        };
        await UpsertAsync(e, ct);

        var lookup = new TableEntity("IDEMAIL", identity.NormalizedEmail.ToUpperInvariant())
        {
            { "UserId", identity.UserId.ToString() }
        };
        await UpsertAsync(lookup, ct);
    }

    public async Task DeleteIdentityAsync(Guid userId, CancellationToken ct)
    {
        var existing = await GetIdentityByIdAsync(userId, ct);
        if (existing != null)
            await DeleteAsync("IDEMAIL", existing.NormalizedEmail.ToUpperInvariant(), ct);
        await DeleteAsync("IDENTITY", userId.ToString(), ct);
    }

    private static IdentityRecord MapToIdentity(TableEntity e) => new()
    {
        UserId = Guid.Parse(e.RowKey),
        Email = e.GetString("Email") ?? "",
        NormalizedEmail = e.GetString("NormalizedEmail") ?? "",
        PasswordHash = e.GetString("PasswordHash") ?? "",
        SecurityStamp = e.GetString("SecurityStamp"),
        CreatedAt = e.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow
    };

    // ═══════════════════════════════════════════════════════════
    //  MealLogs
    // ═══════════════════════════════════════════════════════════

    public async Task<MealLog?> GetMealLogAsync(Guid userId, Guid mealId, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync(userId.ToString(), $"MEAL|{mealId}", ct);
        if (e == null) return null;

        var meal = MapToMealLog(e);
        meal.Items = await GetMealItemsAsync(userId, mealId, ct);
        return meal;
    }

    public async Task<List<MealLog>> GetMealLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        return await GetMealLogsByDateRangeAsync(userId, date, date, ct);
    }

    public async Task<List<MealLog>> GetMealLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var pk = userId.ToString();
        var filter = $"PartitionKey eq '{pk}' and RowKey ge 'MEAL|' and RowKey lt 'MEAL|~'";
        var entities = await QueryAsync(filter, ct);

        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var meals = new List<MealLog>();
        foreach (var e in entities)
        {
            var meal = MapToMealLog(e);
            if (meal.IsDeleted) continue;
            if (meal.LoggedAt >= fromDt && meal.LoggedAt <= toDt)
                meals.Add(meal);
        }

        return meals.OrderBy(m => m.LoggedAt).ToList();
    }

    public async Task UpsertMealLogAsync(MealLog meal, CancellationToken ct)
    {
        meal.UpdatedAt = DateTime.UtcNow;
        var e = new TableEntity(meal.UserId.ToString(), $"MEAL|{meal.Id}")
        {
            { "MealType", (int)meal.MealType },
            { "LoggedAt", meal.LoggedAt },
            { "Notes", meal.Notes },
            { "PhotoUrl", meal.PhotoUrl },
            { "TotalCalories", Str(meal.TotalCalories) },
            { "TotalProteinG", Str(meal.TotalProteinG) },
            { "TotalCarbsG", Str(meal.TotalCarbsG) },
            { "TotalFatG", Str(meal.TotalFatG) },
            { "OriginalText", meal.OriginalText },
            { "CreatedAt", meal.CreatedAt },
            { "UpdatedAt", meal.UpdatedAt },
            { "IsDeleted", meal.IsDeleted }
        };
        await UpsertAsync(e, ct);
    }

    private static MealLog MapToMealLog(TableEntity e)
    {
        var mealId = Guid.Parse(e.RowKey.Substring("MEAL|".Length));
        return new MealLog
        {
            Id = mealId,
            UserId = Guid.Parse(e.PartitionKey),
            MealType = (MealType)(e.GetInt32("MealType") ?? 0),
            LoggedAt = e.GetDateTimeOffset("LoggedAt")?.UtcDateTime ?? DateTime.UtcNow,
            Notes = e.GetString("Notes"),
            PhotoUrl = e.GetString("PhotoUrl"),
            TotalCalories = Dec(e, "TotalCalories"),
            TotalProteinG = Dec(e, "TotalProteinG"),
            TotalCarbsG = Dec(e, "TotalCarbsG"),
            TotalFatG = Dec(e, "TotalFatG"),
            OriginalText = e.GetString("OriginalText"),
            CreatedAt = e.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow,
            UpdatedAt = e.GetDateTimeOffset("UpdatedAt")?.UtcDateTime ?? DateTime.UtcNow,
            IsDeleted = e.GetBoolean("IsDeleted") ?? false
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  MealItems
    // ═══════════════════════════════════════════════════════════

    public async Task<List<MealItem>> GetMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct)
    {
        var pk = userId.ToString();
        var prefix = $"MEALITEM|{mealLogId}|";
        var filter = $"PartitionKey eq '{pk}' and RowKey ge '{prefix}' and RowKey lt '{prefix}~'";
        var entities = await QueryAsync(filter, ct);
        return entities.Select(MapToMealItem).ToList();
    }

    public async Task UpsertMealItemsAsync(Guid userId, Guid mealLogId, List<MealItem> items, CancellationToken ct)
    {
        await DeleteMealItemsAsync(userId, mealLogId, ct);

        var pk = userId.ToString();
        foreach (var item in items)
        {
            item.MealLogId = mealLogId;
            var e = new TableEntity(pk, $"MEALITEM|{mealLogId}|{item.Id}")
            {
                { "FoodProductId", item.FoodProductId?.ToString() },
                { "FoodName", item.FoodName },
                { "Barcode", item.Barcode },
                { "Servings", Str(item.Servings) },
                { "ServingUnit", item.ServingUnit },
                { "ServingWeightG", Str(item.ServingWeightG) },
                { "Calories", Str(item.Calories) },
                { "ProteinG", Str(item.ProteinG) },
                { "CarbsG", Str(item.CarbsG) },
                { "FatG", Str(item.FatG) },
                { "FiberG", Str(item.FiberG) },
                { "SugarG", Str(item.SugarG) },
                { "SodiumMg", Str(item.SodiumMg) },
                { "CholesterolMg", Str(item.CholesterolMg) },
                { "SaturatedFatG", Str(item.SaturatedFatG) },
                { "PotassiumMg", Str(item.PotassiumMg) }
            };
            await UpsertAsync(e, ct);
        }
    }

    public async Task DeleteMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct)
    {
        var existing = await GetMealItemsAsync(userId, mealLogId, ct);
        var pk = userId.ToString();
        foreach (var item in existing)
            await DeleteAsync(pk, $"MEALITEM|{mealLogId}|{item.Id}", ct);
    }

    private static MealItem MapToMealItem(TableEntity e)
    {
        var rkParts = e.RowKey.Split('|');
        var mealLogId = Guid.Parse(rkParts[1]);
        var itemId = Guid.Parse(rkParts[2]);
        var fpId = e.GetString("FoodProductId");
        return new MealItem
        {
            Id = itemId,
            MealLogId = mealLogId,
            FoodProductId = string.IsNullOrEmpty(fpId) ? null : Guid.Parse(fpId),
            FoodName = e.GetString("FoodName") ?? "",
            Barcode = e.GetString("Barcode"),
            Servings = Dec(e, "Servings"),
            ServingUnit = e.GetString("ServingUnit") ?? "serving",
            ServingWeightG = DecN(e, "ServingWeightG"),
            Calories = Dec(e, "Calories"),
            ProteinG = Dec(e, "ProteinG"),
            CarbsG = Dec(e, "CarbsG"),
            FatG = Dec(e, "FatG"),
            FiberG = Dec(e, "FiberG"),
            SugarG = Dec(e, "SugarG"),
            SodiumMg = Dec(e, "SodiumMg"),
            CholesterolMg = Dec(e, "CholesterolMg"),
            SaturatedFatG = Dec(e, "SaturatedFatG"),
            PotassiumMg = Dec(e, "PotassiumMg")
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  SymptomLogs
    // ═══════════════════════════════════════════════════════════

    public async Task<SymptomLog?> GetSymptomLogAsync(Guid userId, Guid symptomId, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync(userId.ToString(), $"SYMPTOM|{symptomId}", ct);
        return e == null ? null : MapToSymptomLog(e);
    }

    public async Task<List<SymptomLog>> GetSymptomLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        return await GetSymptomLogsByDateRangeAsync(userId, date, date, ct);
    }

    public async Task<List<SymptomLog>> GetSymptomLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var pk = userId.ToString();
        var filter = $"PartitionKey eq '{pk}' and RowKey ge 'SYMPTOM|' and RowKey lt 'SYMPTOM|~'";
        var entities = await QueryAsync(filter, ct);

        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var logs = new List<SymptomLog>();
        foreach (var e in entities)
        {
            var log = MapToSymptomLog(e);
            if (log.IsDeleted) continue;
            if (log.OccurredAt >= fromDt && log.OccurredAt <= toDt)
                logs.Add(log);
        }

        return logs.OrderBy(s => s.OccurredAt).ToList();
    }

    public async Task UpsertSymptomLogAsync(SymptomLog symptom, CancellationToken ct)
    {
        symptom.UpdatedAt = DateTime.UtcNow;
        var e = new TableEntity(symptom.UserId.ToString(), $"SYMPTOM|{symptom.Id}")
        {
            { "SymptomTypeId", symptom.SymptomTypeId },
            { "Severity", symptom.Severity },
            { "OccurredAt", symptom.OccurredAt },
            { "RelatedMealLogId", symptom.RelatedMealLogId?.ToString() },
            { "Notes", symptom.Notes },
            { "DurationTicks", symptom.Duration?.Ticks },
            { "CreatedAt", symptom.CreatedAt },
            { "UpdatedAt", symptom.UpdatedAt },
            { "IsDeleted", symptom.IsDeleted }
        };
        await UpsertAsync(e, ct);
    }

    private static SymptomLog MapToSymptomLog(TableEntity e)
    {
        var symptomId = Guid.Parse(e.RowKey.Substring("SYMPTOM|".Length));
        var relatedId = e.GetString("RelatedMealLogId");
        var durationTicks = e.GetInt64("DurationTicks");
        return new SymptomLog
        {
            Id = symptomId,
            UserId = Guid.Parse(e.PartitionKey),
            SymptomTypeId = e.GetInt32("SymptomTypeId") ?? 0,
            Severity = e.GetInt32("Severity") ?? 0,
            OccurredAt = e.GetDateTimeOffset("OccurredAt")?.UtcDateTime ?? DateTime.UtcNow,
            RelatedMealLogId = string.IsNullOrEmpty(relatedId) ? null : Guid.Parse(relatedId),
            Notes = e.GetString("Notes"),
            Duration = durationTicks.HasValue ? TimeSpan.FromTicks(durationTicks.Value) : null,
            CreatedAt = e.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow,
            UpdatedAt = e.GetDateTimeOffset("UpdatedAt")?.UtcDateTime ?? DateTime.UtcNow,
            IsDeleted = e.GetBoolean("IsDeleted") ?? false
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  SymptomTypes
    // ═══════════════════════════════════════════════════════════

    public async Task<List<SymptomType>> GetAllSymptomTypesAsync(CancellationToken ct)
    {
        var filter = "PartitionKey eq 'SYMPTOMTYPE'";
        var entities = await QueryAsync(filter, ct);
        return entities.Select(MapToSymptomType).ToList();
    }

    public async Task<SymptomType?> GetSymptomTypeAsync(int id, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync("SYMPTOMTYPE", id.ToString(), ct);
        return e == null ? null : MapToSymptomType(e);
    }

    public async Task UpsertSymptomTypeAsync(SymptomType type, CancellationToken ct)
    {
        var e = new TableEntity("SYMPTOMTYPE", type.Id.ToString())
        {
            { "Name", type.Name },
            { "Category", type.Category },
            { "Icon", type.Icon }
        };
        await UpsertAsync(e, ct);
    }

    public async Task<bool> SymptomTypeExistsAsync(int id, CancellationToken ct)
    {
        return await GetEntityOrNullAsync("SYMPTOMTYPE", id.ToString(), ct) != null;
    }

    private static SymptomType MapToSymptomType(TableEntity e) => new()
    {
        Id = int.Parse(e.RowKey),
        Name = e.GetString("Name") ?? "",
        Category = e.GetString("Category") ?? "",
        Icon = e.GetString("Icon") ?? "🩺"
    };

    // ═══════════════════════════════════════════════════════════
    //  FoodProducts
    // ═══════════════════════════════════════════════════════════

    public async Task<FoodProduct?> GetFoodProductAsync(Guid id, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync("FOOD", id.ToString(), ct);
        return e == null ? null : MapToFoodProduct(e);
    }

    public async Task<FoodProduct?> GetFoodProductByBarcodeAsync(string barcode, CancellationToken ct)
    {
        var lookup = await GetEntityOrNullAsync("BARCODE", barcode, ct);
        if (lookup == null) return null;

        var foodProductId = lookup.GetString("FoodProductId");
        if (string.IsNullOrEmpty(foodProductId)) return null;

        return await GetFoodProductAsync(Guid.Parse(foodProductId), ct);
    }

    public async Task<List<FoodProduct>> SearchFoodProductsAsync(string query, int maxResults, CancellationToken ct)
    {
        var filter = "PartitionKey eq 'FOOD'";
        var results = new List<FoodProduct>();

        await foreach (var entity in _table.QueryAsync<TableEntity>(filter, cancellationToken: ct))
        {
            var name = entity.GetString("Name") ?? "";
            var brand = entity.GetString("Brand");
            var barcode = entity.GetString("Barcode");

            if (name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (brand != null && brand.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (barcode != null && barcode.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(MapToFoodProduct(entity));
                if (results.Count >= maxResults)
                    break;
            }
        }

        return results;
    }

    public async Task UpsertFoodProductAsync(FoodProduct product, CancellationToken ct)
    {
        var e = new TableEntity("FOOD", product.Id.ToString())
        {
            { "Barcode", product.Barcode },
            { "Name", product.Name },
            { "Brand", product.Brand },
            { "Ingredients", product.Ingredients },
            { "ImageUrl", product.ImageUrl },
            { "NovaGroup", product.NovaGroup },
            { "NutriScore", product.NutriScore },
            { "AllergensTags", StringArrayToJson(product.AllergensTags) },
            { "Calories100g", Str(product.Calories100g) },
            { "Protein100g", Str(product.Protein100g) },
            { "Carbs100g", Str(product.Carbs100g) },
            { "Fat100g", Str(product.Fat100g) },
            { "Fiber100g", Str(product.Fiber100g) },
            { "Sugar100g", Str(product.Sugar100g) },
            { "Sodium100g", Str(product.Sodium100g) },
            { "ServingSize", product.ServingSize },
            { "ServingQuantity", Str(product.ServingQuantity) },
            { "DataSource", product.DataSource },
            { "ExternalId", product.ExternalId },
            { "CachedAt", product.CachedAt },
            { "CacheTtlHours", product.CacheTtlHours },
            { "SafetyScore", product.SafetyScore },
            { "SafetyRating", product.SafetyRating.HasValue ? (int)product.SafetyRating.Value : null },
            { "IsDeleted", product.IsDeleted },
            { "AdditiveIds", JsonSerializer.Serialize(product.FoodProductAdditiveIds ?? [], JsonOpts) },
            { "NutritionInfo", product.NutritionInfo != null ? JsonSerializer.Serialize(product.NutritionInfo, JsonOpts) : null }
        };
        await UpsertAsync(e, ct);

        if (!string.IsNullOrEmpty(product.Barcode))
        {
            var barcodeLookup = new TableEntity("BARCODE", product.Barcode)
            {
                { "FoodProductId", product.Id.ToString() }
            };
            await UpsertAsync(barcodeLookup, ct);
        }
    }

    private static FoodProduct MapToFoodProduct(TableEntity e)
    {
        var safetyRating = e.GetInt32("SafetyRating");
        var additiveIdsJson = e.GetString("AdditiveIds");
        var nutritionInfoJson = e.GetString("NutritionInfo");
        return new FoodProduct
        {
            Id = Guid.Parse(e.RowKey),
            Barcode = e.GetString("Barcode"),
            Name = e.GetString("Name") ?? "",
            Brand = e.GetString("Brand"),
            Ingredients = e.GetString("Ingredients"),
            ImageUrl = e.GetString("ImageUrl"),
            NovaGroup = e.GetInt32("NovaGroup"),
            NutriScore = e.GetString("NutriScore"),
            AllergensTags = JsonToStringArray(e.GetString("AllergensTags")),
            Calories100g = DecN(e, "Calories100g"),
            Protein100g = DecN(e, "Protein100g"),
            Carbs100g = DecN(e, "Carbs100g"),
            Fat100g = DecN(e, "Fat100g"),
            Fiber100g = DecN(e, "Fiber100g"),
            Sugar100g = DecN(e, "Sugar100g"),
            Sodium100g = DecN(e, "Sodium100g"),
            ServingSize = e.GetString("ServingSize"),
            ServingQuantity = DecN(e, "ServingQuantity"),
            DataSource = e.GetString("DataSource") ?? "Manual",
            ExternalId = e.GetString("ExternalId"),
            CachedAt = e.GetDateTimeOffset("CachedAt")?.UtcDateTime ?? DateTime.UtcNow,
            CacheTtlHours = e.GetInt32("CacheTtlHours") ?? 24,
            SafetyScore = e.GetInt32("SafetyScore"),
            SafetyRating = safetyRating.HasValue ? (SafetyRating)safetyRating.Value : null,
            IsDeleted = e.GetBoolean("IsDeleted") ?? false,
            FoodProductAdditiveIds = !string.IsNullOrEmpty(additiveIdsJson) ? JsonSerializer.Deserialize<List<int>>(additiveIdsJson, JsonOpts) ?? [] : [],
            NutritionInfo = !string.IsNullOrEmpty(nutritionInfoJson) ? JsonSerializer.Deserialize<NutritionInfo>(nutritionInfoJson, JsonOpts) : null
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  FoodAdditives
    // ═══════════════════════════════════════════════════════════

    public async Task<List<FoodAdditive>> GetAllFoodAdditivesAsync(CancellationToken ct)
    {
        var filter = "PartitionKey eq 'ADDITIVE'";
        var entities = await QueryAsync(filter, ct);
        return entities.Select(MapToFoodAdditive).ToList();
    }

    public async Task<FoodAdditive?> GetFoodAdditiveAsync(int id, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync("ADDITIVE", id.ToString(), ct);
        return e == null ? null : MapToFoodAdditive(e);
    }

    public async Task UpsertFoodAdditiveAsync(FoodAdditive additive, CancellationToken ct)
    {
        var e = new TableEntity("ADDITIVE", additive.Id.ToString())
        {
            { "ENumber", additive.ENumber },
            { "Name", additive.Name },
            { "AlternateNames", StringArrayToJson(additive.AlternateNames) },
            { "Category", additive.Category },
            { "CspiRating", (int)additive.CspiRating },
            { "UsRegulatoryStatus", (int)additive.UsRegulatoryStatus },
            { "EuRegulatoryStatus", (int)additive.EuRegulatoryStatus },
            { "SafetyRating", (int)additive.SafetyRating },
            { "EfsaAdiMgPerKgBw", Str(additive.EfsaAdiMgPerKgBw) },
            { "EfsaLastReviewDate", additive.EfsaLastReviewDate },
            { "EpaCancerClass", additive.EpaCancerClass },
            { "HealthConcerns", additive.HealthConcerns },
            { "Description", additive.Description },
            { "FdaAdverseEventCount", additive.FdaAdverseEventCount },
            { "FdaRecallCount", additive.FdaRecallCount },
            { "BannedInCountries", StringArrayToJson(additive.BannedInCountries) },
            { "LastUpdated", additive.LastUpdated }
        };
        await UpsertAsync(e, ct);
    }

    private static FoodAdditive MapToFoodAdditive(TableEntity e) => new()
    {
        Id = int.Parse(e.RowKey),
        ENumber = e.GetString("ENumber"),
        Name = e.GetString("Name") ?? "",
        AlternateNames = JsonToStringArray(e.GetString("AlternateNames")),
        Category = e.GetString("Category") ?? "",
        CspiRating = (CspiRating)(e.GetInt32("CspiRating") ?? 0),
        UsRegulatoryStatus = (UsRegulatoryStatus)(e.GetInt32("UsRegulatoryStatus") ?? 0),
        EuRegulatoryStatus = (EuRegulatoryStatus)(e.GetInt32("EuRegulatoryStatus") ?? 0),
        SafetyRating = (SafetyRating)(e.GetInt32("SafetyRating") ?? 0),
        EfsaAdiMgPerKgBw = DecN(e, "EfsaAdiMgPerKgBw"),
        EfsaLastReviewDate = e.GetDateTimeOffset("EfsaLastReviewDate")?.UtcDateTime,
        EpaCancerClass = e.GetString("EpaCancerClass"),
        HealthConcerns = e.GetString("HealthConcerns") ?? "",
        Description = e.GetString("Description") ?? "",
        FdaAdverseEventCount = e.GetInt32("FdaAdverseEventCount") ?? 0,
        FdaRecallCount = e.GetInt32("FdaRecallCount") ?? 0,
        BannedInCountries = JsonToStringArray(e.GetString("BannedInCountries")),
        LastUpdated = e.GetDateTimeOffset("LastUpdated")?.UtcDateTime ?? DateTime.UtcNow
    };

    // ═══════════════════════════════════════════════════════════
    //  FoodProductAdditives
    // ═══════════════════════════════════════════════════════════

    public async Task<List<int>> GetAdditiveIdsForProductAsync(Guid foodProductId, CancellationToken ct)
    {
        var pk = $"FOODADDITIVE|{foodProductId}";
        var filter = $"PartitionKey eq '{pk}'";
        var entities = await QueryAsync(filter, ct);
        return entities.Select(e => int.Parse(e.RowKey)).ToList();
    }

    public async Task SetAdditiveIdsForProductAsync(Guid foodProductId, List<int> additiveIds, CancellationToken ct)
    {
        var pk = $"FOODADDITIVE|{foodProductId}";

        var existing = await GetAdditiveIdsForProductAsync(foodProductId, ct);
        foreach (var id in existing)
            await DeleteAsync(pk, id.ToString(), ct);

        foreach (var id in additiveIds)
        {
            var e = new TableEntity(pk, id.ToString());
            await UpsertAsync(e, ct);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  RefreshTokens
    // ═══════════════════════════════════════════════════════════

    public async Task<RefreshToken?> GetRefreshTokenByValueAsync(string token, CancellationToken ct)
    {
        var hash = HashToken(token);
        var lookup = await GetEntityOrNullAsync("RTLOOKUP", hash, ct);
        if (lookup == null) return null;

        var userId = lookup.GetString("UserId");
        var tokenId = lookup.GetString("TokenId");
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tokenId)) return null;

        var e = await GetEntityOrNullAsync(userId, $"RTOKEN|{tokenId}", ct);
        return e == null ? null : MapToRefreshToken(e);
    }

    public async Task<List<RefreshToken>> GetActiveRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var pk = userId.ToString();
        var filter = $"PartitionKey eq '{pk}' and RowKey ge 'RTOKEN|' and RowKey lt 'RTOKEN|~'";
        var entities = await QueryAsync(filter, ct);
        return entities
            .Select(MapToRefreshToken)
            .Where(t => t.IsActive)
            .ToList();
    }

    public async Task UpsertRefreshTokenAsync(RefreshToken token, CancellationToken ct)
    {
        var pk = token.UserId.ToString();
        var rk = $"RTOKEN|{token.Id}";
        var e = new TableEntity(pk, rk)
        {
            { "Token", token.Token },
            { "ExpiresAt", token.ExpiresAt },
            { "CreatedAt", token.CreatedAt },
        };
        if (token.RevokedAt.HasValue) e["RevokedAt"] = token.RevokedAt.Value;
        if (token.ReplacedByToken != null) e["ReplacedByToken"] = token.ReplacedByToken;
        await UpsertAsync(e, ct);

        var hash = HashToken(token.Token);
        var lookup = new TableEntity("RTLOOKUP", hash)
        {
            { "UserId", pk },
            { "TokenId", token.Id.ToString() }
        };
        await UpsertAsync(lookup, ct);
    }

    public async Task DeleteRefreshTokensForUserAsync(Guid userId, CancellationToken ct)
    {
        var pk = userId.ToString();
        var filter = $"PartitionKey eq '{pk}' and RowKey ge 'RTOKEN|' and RowKey lt 'RTOKEN|~'";
        var entities = await QueryAsync(filter, ct);

        foreach (var e in entities)
        {
            var tokenValue = e.GetString("Token");
            if (!string.IsNullOrEmpty(tokenValue))
            {
                var hash = HashToken(tokenValue);
                await DeleteAsync("RTLOOKUP", hash, ct);
            }

            await DeleteAsync(pk, e.RowKey, ct);
        }
    }

    private static RefreshToken MapToRefreshToken(TableEntity e)
    {
        var tokenId = Guid.Parse(e.RowKey.Substring("RTOKEN|".Length));
        return new RefreshToken
        {
            Id = tokenId,
            UserId = Guid.Parse(e.PartitionKey),
            Token = e.GetString("Token") ?? "",
            ExpiresAt = e.GetDateTimeOffset("ExpiresAt")?.UtcDateTime ?? DateTime.UtcNow,
            CreatedAt = e.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow,
            RevokedAt = e.GetDateTimeOffset("RevokedAt")?.UtcDateTime,
            ReplacedByToken = e.GetString("ReplacedByToken")
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  DailyNutritionSummary
    // ═══════════════════════════════════════════════════════════

    public async Task<DailyNutritionSummary?> GetDailyNutritionSummaryAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        var rk = $"NUTSUM|{date:yyyy-MM-dd}";
        var e = await GetEntityOrNullAsync(userId.ToString(), rk, ct);
        return e == null ? null : MapToNutritionSummary(e);
    }

    public async Task UpsertDailyNutritionSummaryAsync(DailyNutritionSummary summary, CancellationToken ct)
    {
        var rk = $"NUTSUM|{summary.Date:yyyy-MM-dd}";
        var e = new TableEntity(summary.UserId.ToString(), rk)
        {
            { "Id", summary.Id.ToString() },
            { "TotalCalories", Str(summary.TotalCalories) },
            { "TotalProteinG", Str(summary.TotalProteinG) },
            { "TotalCarbsG", Str(summary.TotalCarbsG) },
            { "TotalFatG", Str(summary.TotalFatG) },
            { "TotalFiberG", Str(summary.TotalFiberG) },
            { "TotalSugarG", Str(summary.TotalSugarG) },
            { "TotalSodiumMg", Str(summary.TotalSodiumMg) },
            { "MealCount", summary.MealCount },
            { "CalorieGoal", summary.CalorieGoal }
        };
        await UpsertAsync(e, ct);
    }

    private static DailyNutritionSummary MapToNutritionSummary(TableEntity e)
    {
        var dateStr = e.RowKey.Substring("NUTSUM|".Length);
        var idStr = e.GetString("Id");
        return new DailyNutritionSummary
        {
            Id = string.IsNullOrEmpty(idStr) ? Guid.NewGuid() : Guid.Parse(idStr),
            UserId = Guid.Parse(e.PartitionKey),
            Date = DateOnly.ParseExact(dateStr, "yyyy-MM-dd"),
            TotalCalories = Dec(e, "TotalCalories"),
            TotalProteinG = Dec(e, "TotalProteinG"),
            TotalCarbsG = Dec(e, "TotalCarbsG"),
            TotalFatG = Dec(e, "TotalFatG"),
            TotalFiberG = Dec(e, "TotalFiberG"),
            TotalSugarG = Dec(e, "TotalSugarG"),
            TotalSodiumMg = Dec(e, "TotalSodiumMg"),
            MealCount = e.GetInt32("MealCount") ?? 0,
            CalorieGoal = e.GetInt32("CalorieGoal") ?? 0
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  UserFoodAlerts
    // ═══════════════════════════════════════════════════════════

    public async Task<List<UserFoodAlert>> GetUserFoodAlertsAsync(Guid userId, CancellationToken ct)
    {
        var pk = userId.ToString();
        var filter = $"PartitionKey eq '{pk}' and RowKey ge 'ALERT|' and RowKey lt 'ALERT|~'";
        var entities = await QueryAsync(filter, ct);
        return entities.Select(MapToUserFoodAlert).ToList();
    }

    public async Task<UserFoodAlert?> GetUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync(userId.ToString(), $"ALERT|{additiveId}", ct);
        return e == null ? null : MapToUserFoodAlert(e);
    }

    public async Task UpsertUserFoodAlertAsync(UserFoodAlert alert, CancellationToken ct)
    {
        var e = new TableEntity(alert.UserId.ToString(), $"ALERT|{alert.FoodAdditiveId}")
        {
            { "Id", alert.Id.ToString() },
            { "AlertEnabled", alert.AlertEnabled },
            { "CreatedAt", alert.CreatedAt }
        };
        await UpsertAsync(e, ct);
    }

    public async Task DeleteUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct)
    {
        await DeleteAsync(userId.ToString(), $"ALERT|{additiveId}", ct);
    }

    private static UserFoodAlert MapToUserFoodAlert(TableEntity e)
    {
        var additiveId = int.Parse(e.RowKey.Substring("ALERT|".Length));
        var idStr = e.GetString("Id");
        return new UserFoodAlert
        {
            Id = string.IsNullOrEmpty(idStr) ? Guid.NewGuid() : Guid.Parse(idStr),
            UserId = Guid.Parse(e.PartitionKey),
            FoodAdditiveId = additiveId,
            AlertEnabled = e.GetBoolean("AlertEnabled") ?? true,
            CreatedAt = e.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  InsightReports
    // ═══════════════════════════════════════════════════════════

    public async Task<InsightReport?> GetInsightReportAsync(Guid userId, Guid reportId, CancellationToken ct)
    {
        var e = await GetEntityOrNullAsync(userId.ToString(), $"INSIGHT|{reportId}", ct);
        return e == null ? null : MapToInsightReport(e);
    }

    public async Task<List<InsightReport>> GetInsightReportsAsync(Guid userId, CancellationToken ct)
    {
        var pk = userId.ToString();
        var filter = $"PartitionKey eq '{pk}' and RowKey ge 'INSIGHT|' and RowKey lt 'INSIGHT|~'";
        var entities = await QueryAsync(filter, ct);
        return entities.Select(MapToInsightReport).OrderByDescending(r => r.GeneratedAt).ToList();
    }

    public async Task UpsertInsightReportAsync(InsightReport report, CancellationToken ct)
    {
        var e = new TableEntity(report.UserId.ToString(), $"INSIGHT|{report.Id}")
        {
            { "GeneratedAt", report.GeneratedAt },
            { "PeriodStart", report.PeriodStart.ToString("yyyy-MM-dd") },
            { "PeriodEnd", report.PeriodEnd.ToString("yyyy-MM-dd") },
            { "ReportType", (int)report.ReportType },
            { "CorrelationsJson", report.CorrelationsJson },
            { "SummaryText", report.SummaryText },
            { "AdditiveExposureJson", report.AdditiveExposureJson },
            { "TopTriggersJson", report.TopTriggersJson }
        };
        await UpsertAsync(e, ct);
    }

    private static InsightReport MapToInsightReport(TableEntity e)
    {
        var reportId = Guid.Parse(e.RowKey.Substring("INSIGHT|".Length));
        return new InsightReport
        {
            Id = reportId,
            UserId = Guid.Parse(e.PartitionKey),
            GeneratedAt = e.GetDateTimeOffset("GeneratedAt")?.UtcDateTime ?? DateTime.UtcNow,
            PeriodStart = DateOnly.ParseExact(e.GetString("PeriodStart") ?? "2000-01-01", "yyyy-MM-dd"),
            PeriodEnd = DateOnly.ParseExact(e.GetString("PeriodEnd") ?? "2000-01-01", "yyyy-MM-dd"),
            ReportType = (ReportType)(e.GetInt32("ReportType") ?? 0),
            CorrelationsJson = e.GetString("CorrelationsJson") ?? "[]",
            SummaryText = e.GetString("SummaryText") ?? "",
            AdditiveExposureJson = e.GetString("AdditiveExposureJson") ?? "{}",
            TopTriggersJson = e.GetString("TopTriggersJson") ?? "[]"
        };
    }
}
