using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Azure.Data.Tables;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Data;

public class AzureTableOfflineDatabase : IOfflineFoodDatabase
{
    private readonly TableClient _table;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AzureTableOfflineDatabase> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string TableName = "offproducts";
    private const string OfflinePartitionKey = "off";

    private static string TruncateForTable(string? value, int maxChars) =>
        value is not null && value.Length > maxChars ? value[..maxChars] : value ?? "";

    private static readonly HashSet<int> RetryableStatuses = [400, 413, 429, 503];

    private async Task SubmitBatchSafeAsync(List<TableTransactionAction> batch, CancellationToken ct)
    {
        try
        {
            await _table.SubmitTransactionAsync(batch, ct);
        }
        catch (TableTransactionFailedException ex) when (RetryableStatuses.Contains(ex.Status))
        {
            // Throttling (429/503) — wait and retry the full batch
            if (ex.Status is 429 or 503)
            {
                _logger.LogWarning("Batch throttled ({Status}), waiting 5s before retry...", ex.Status);
                await Task.Delay(5000, ct);
                try
                {
                    await _table.SubmitTransactionAsync(batch, ct);
                    return;
                }
                catch (TableTransactionFailedException retryEx) when (RetryableStatuses.Contains(retryEx.Status))
                {
                    // fall through to per-entity retry
                }
            }

            _logger.LogWarning("Batch failed ({ErrorCode}), retrying individually...", ex.ErrorCode);
            foreach (var action in batch)
            {
                try
                {
                    await _table.SubmitTransactionAsync([action], ct);
                }
                catch (Exception inner)
                {
                    _logger.LogWarning("Skipping entity {RowKey}: {Message}", action.Entity.RowKey, inner.Message);
                }
            }
        }
    }

    public AzureTableOfflineDatabase(
        TableServiceClient serviceClient,
        IMemoryCache cache,
        ILogger<AzureTableOfflineDatabase> logger)
    {
        _table = serviceClient.GetTableClient(TableName);
        _cache = cache;
        _logger = logger;
    }

    public async Task<FoodProductDto?> LookupByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var cacheKey = $"offline:{barcode}";

        if (_cache.TryGetValue(cacheKey, out FoodProductDto? cached))
            return cached;

        try
        {
            var pk = OfflinePartitionKey;
            var response = await _table.GetEntityAsync<TableEntity>(pk, barcode, cancellationToken: ct);
            var entity = response.Value;

            var dto = MapToFoodProductDto(entity);
            if (dto is not null)
            {
                _cache.Set<FoodProductDto?>(cacheKey, dto, TimeSpan.FromHours(1));
                return dto;
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to look up barcode {Barcode} in offline database", barcode);
        }

        _cache.Set<FoodProductDto?>(cacheKey, null, TimeSpan.FromMinutes(10));
        return null;
    }

    public async Task ImportFromOffDumpAsync(
        Stream jsonlGzStream,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        await _table.CreateIfNotExistsAsync(ct);

        using var gzip = new GZipStream(jsonlGzStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        // Batching requires all entities in a batch to share the same partition key.
        // We use a static partition key for import so batches of 100 always work.
        var batch = new Dictionary<string, TableTransactionAction>();
        var count = 0;
        string? line;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
                var ingredientsText = root.TryGetProperty("ingredients_text", out var ingEl) ? ingEl.GetString() : null;

                if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(ingredientsText))
                    continue;

                var entity = new TableEntity(OfflinePartitionKey, code);
                AddStringProperty(entity, "ProductName", root, "product_name");
                AddStringProperty(entity, "Brands", root, "brands");
                entity.Add("IngredientsText", TruncateForTable(ingredientsText, 30000));
                AddStringArrayProperty(entity, "IngredientsTags", root, "ingredients_tags");
                AddStringArrayProperty(entity, "AdditivesTags", root, "additives_tags");
                AddStringArrayProperty(entity, "AllergensTags", root, "allergens_tags");

                if (root.TryGetProperty("nova_group", out var novaEl) && novaEl.ValueKind == JsonValueKind.Number)
                    entity.Add("NovaGroup", novaEl.GetInt32());

                AddStringProperty(entity, "NutritionGrades", root, "nutrition_grades");

                if (root.TryGetProperty("nutriments", out var nutEl) && nutEl.ValueKind == JsonValueKind.Object)
                {
                    var nutrRaw = nutEl.GetRawText();
                    entity.Add("Nutriments", TruncateForTable(nutrRaw, 30000));
                }

                AddStringProperty(entity, "ServingSize", root, "serving_size");
                AddStringProperty(entity, "ImageUrl", root, "image_url");

                batch[code] = new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity);
                count++;

                if (batch.Count >= 100)
                {
                    await SubmitBatchSafeAsync(batch.Values.ToList(), ct);
                    progress?.Report(count);
                    batch.Clear();
                }
            }
            catch (JsonException)
            {
            }
        }

        if (batch.Count > 0)
        {
            await SubmitBatchSafeAsync(batch.Values.ToList(), ct);
            progress?.Report(count);
        }

        _logger.LogInformation("Imported {Count} products into offline database", count);
    }

    private static FoodProductDto? MapToFoodProductDto(TableEntity e)
    {
        var name = e.GetString("ProductName") ?? "Unknown";
        var brand = e.GetString("Brands");

        if (!string.IsNullOrWhiteSpace(brand))
        {
            var firstBrand = brand.Split(',')[0].Trim();
            if (!name.Contains(firstBrand, StringComparison.OrdinalIgnoreCase))
                name = $"{firstBrand} - {name}";
        }

        var additivesTags = DeserializeStringArray(e.GetString("AdditivesTags"));
        var nutriments = DeserializeNutriments(e.GetString("Nutriments"));

        return new FoodProductDto
        {
            Name = name,
            Barcode = e.RowKey,
            Brand = brand,
            Ingredients = e.GetString("IngredientsText"),
            ImageUrl = e.GetString("ImageUrl"),
            NovaGroup = e.GetInt32("NovaGroup"),
            NutriScore = e.GetString("NutritionGrades"),
            AllergensTags = DeserializeStringArray(e.GetString("AllergensTags")),
            Calories100g = nutriments?.TryGetValue("energy-kcal_100g", out var kcal) == true ? kcal : null,
            Protein100g = nutriments?.TryGetValue("proteins_100g", out var prot) == true ? prot : null,
            Carbs100g = nutriments?.TryGetValue("carbohydrates_100g", out var carbs) == true ? carbs : null,
            Fat100g = nutriments?.TryGetValue("fat_100g", out var fat) == true ? fat : null,
            Fiber100g = nutriments?.TryGetValue("fiber_100g", out var fiber) == true ? fiber : null,
            Sugar100g = nutriments?.TryGetValue("sugars_100g", out var sugar) == true ? sugar : null,
            SodiumMg100g = nutriments?.TryGetValue("sodium_100g", out var sodium) == true ? sodium * 1000m : null,
            ServingSize = e.GetString("ServingSize"),
            DataSource = DataSources.OpenFoodFacts,
            FoodKind = FoodKind.Branded,
            ExternalId = e.RowKey,
            SourceUrl = $"https://world.openfoodfacts.org/product/{e.RowKey}",
            AdditivesTags = additivesTags.ToList(),
        };
    }

    private static void AddStringProperty(TableEntity entity, string columnName, JsonElement root, string jsonKey)
    {
        if (root.TryGetProperty(jsonKey, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var val = el.GetString();
            if (!string.IsNullOrEmpty(val))
                entity.Add(columnName, val);
        }
    }

    private static void AddStringArrayProperty(TableEntity entity, string columnName, JsonElement root, string jsonKey)
    {
        if (root.TryGetProperty(jsonKey, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                var val = item.GetString();
                if (!string.IsNullOrEmpty(val))
                    items.Add(val);
            }
            if (items.Count > 0)
                entity.Add(columnName, JsonSerializer.Serialize(items, JsonOpts));
        }
    }

    private static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, decimal>? DeserializeNutriments(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts);
            if (raw is null) return null;

            var result = new Dictionary<string, decimal>();
            foreach (var (key, val) in raw)
            {
                if (val.ValueKind == JsonValueKind.Number)
                {
                    if (val.TryGetDecimal(out var d))
                        result[key] = d;
                    else if (val.TryGetInt32(out var i))
                        result[key] = i;
                }
            }
            return result;
        }
        catch
        {
            return null;
        }
    }
}
