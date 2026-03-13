using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class OpenFoodFactsClient : IFoodApiService
{
    public string SourceName => DataSources.OpenFoodFacts;

    private readonly HttpClient _http;
    private readonly ILogger<OpenFoodFactsClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // OpenFoodFacts production base URL for barcode lookups (v2 product endpoint).
    private const string BaseUrl = "https://world.openfoodfacts.org";

    // Search-a-licious is the Elasticsearch-backed search service.
    // The legacy v2 /api/v2/search endpoint is a Perl database scan that takes 15-55s.
    // Search-a-licious returns results in 2-3s consistently.
    private const string SearchUrl = "https://search.openfoodfacts.org/search";

    // Only request the fields we actually use — keeps response small and fast.
    private const string BarcodeFields = "product_name,brands,code,nutriments,serving_size,serving_quantity,nova_group,nutriscore_grade,allergens_tags,additives_tags,ingredients_text,image_url";
    private const string SearchFields = "product_name,brands,code,nutriments,nova_group,nutriscore_grade,allergens_tags,image_url,ingredients_tags";

    public OpenFoodFactsClient(HttpClient http, ILogger<OpenFoodFactsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<OpenFoodFactsResponse>(
                $"{BaseUrl}/api/v2/product/{barcode}?fields={BarcodeFields}", JsonOptions, ct);

            if (response?.Product is null || response.Status != 1)
                return null;

            return MapProduct(response.Product, barcode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to lookup barcode {Barcode} from OpenFoodFacts", barcode);
            return null;
        }
    }

    public async Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var escapedQuery = Uri.EscapeDataString(query);
            var url = $"{SearchUrl}?q={escapedQuery}&page_size=10&langs=en&fields={SearchFields}";

            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Search-a-licious returns results in a "hits" array (not "products")
            if (!root.TryGetProperty("hits", out var hitsArray))
                return [];

            var results = new List<FoodProductDto>();
            foreach (var element in hitsArray.EnumerateArray())
            {
                try
                {
                    var product = DeserializeSearchHit(element);
                    if (product is null) continue;

                    var dto = MapProduct(product, product.Code);
                    if (dto is not null)
                        results.Add(dto);
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Skipping product that failed to deserialize in search for '{Query}'", query);
                }
            }

            _logger.LogInformation("OpenFoodFacts search for '{Query}' returned {Count} products",
                query, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search OpenFoodFacts for '{Query}'", query);
            return [];
        }
    }

    /// <summary>
    /// Deserialize a search-a-licious hit, handling format differences from v2:
    /// - "brands" is an array of strings (v2 returns a comma-separated string)
    /// </summary>
    private static OffProduct? DeserializeSearchHit(JsonElement element)
    {
        var raw = element.GetRawText();
        var product = JsonSerializer.Deserialize<OffProduct>(raw, JsonOptions);
        if (product is null) return null;

        // Search-a-licious returns brands as a JSON array; join into comma-separated string
        if (string.IsNullOrEmpty(product.Brands) &&
            element.TryGetProperty("brands", out var brandsEl) &&
            brandsEl.ValueKind == JsonValueKind.Array)
        {
            var brandList = new List<string>();
            foreach (var b in brandsEl.EnumerateArray())
            {
                var val = b.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                    brandList.Add(val);
            }
            if (brandList.Count > 0)
                product = product with { Brands = string.Join(", ", brandList) };
        }

        // Search-a-licious has ingredients_tags (e.g. "en:palm-oil") but not ingredients_text.
        // Convert tags into a readable string so the DTO has ingredient info before barcode lookup.
        if (string.IsNullOrEmpty(product.IngredientsText) &&
            element.TryGetProperty("ingredients_tags", out var tagsEl) &&
            tagsEl.ValueKind == JsonValueKind.Array)
        {
            var ingredients = new List<string>();
            foreach (var tag in tagsEl.EnumerateArray())
            {
                var val = tag.GetString();
                if (string.IsNullOrWhiteSpace(val)) continue;
                // Strip language prefix (e.g. "en:palm-oil" → "palm-oil"), then humanise
                var name = val.Contains(':') ? val[(val.IndexOf(':') + 1)..] : val;
                name = name.Replace('-', ' ');
                ingredients.Add(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name));
            }
            if (ingredients.Count > 0)
                product = product with { IngredientsText = string.Join(", ", ingredients) };
        }

        return product;
    }

    private static FoodProductDto? MapProduct(OffProduct p, string? barcode)
    {
        var name = p.ProductName ?? "Unknown";
        if (!string.IsNullOrWhiteSpace(p.Brands))
        {
            var brand = p.Brands.Split(',')[0].Trim();
            if (!name.Contains(brand, StringComparison.OrdinalIgnoreCase))
                name = $"{brand} - {name}";
        }

        return new FoodProductDto
        {
            Name = name,
            Barcode = barcode ?? p.Code,
            Brand = p.Brands,
            Ingredients = p.IngredientsText,
            ImageUrl = p.ImageUrl,
            NovaGroup = p.NovaGroup,
            NutriScore = p.NutriscoreGrade,
            AllergensTags = p.AllergensTags ?? [],
            Calories100g = p.Nutriments?.EnergyKcal100g,
            Protein100g = p.Nutriments?.Proteins100g,
            Carbs100g = p.Nutriments?.Carbohydrates100g,
            Fat100g = p.Nutriments?.Fat100g,
            Fiber100g = p.Nutriments?.Fiber100g,
            Sugar100g = p.Nutriments?.Sugars100g,
            Sodium100g = p.Nutriments?.Sodium100g,
            ServingSize = p.ServingSize,
            ServingQuantity = p.ServingQuantity,
            DataSource = DataSources.OpenFoodFacts,
            FoodKind = GutAI.Domain.Enums.FoodKind.Branded,
            SourceUrl = barcode is not null ? $"https://world.openfoodfacts.org/product/{barcode}" : null,
            ExternalId = barcode ?? p.Code,
            AdditivesTags = p.AdditivesTags?.ToList() ?? []
        };
    }
}

// Response models for OpenFoodFacts API
public record OpenFoodFactsResponse
{
    public int Status { get; init; }
    public OffProduct? Product { get; init; }
}

public record OpenFoodFactsSearchResponse
{
    public List<OffProduct>? Products { get; init; }
}

public record OffProduct
{
    public string? Code { get; init; }
    public string? ProductName { get; init; }
    public string? Brands { get; init; }
    public string? IngredientsText { get; init; }
    public string? ImageUrl { get; init; }
    public int? NovaGroup { get; init; }
    public string? NutriscoreGrade { get; init; }
    public string[]? AllergensTags { get; init; }
    public string[]? AdditivesTags { get; init; }
    public OffNutriments? Nutriments { get; init; }
    public string? ServingSize { get; init; }
    public decimal? ServingQuantity { get; init; }
}

public record OffNutriments
{
    [JsonPropertyName("energy-kcal_100g")]
    public decimal? EnergyKcal100g { get; init; }
    [JsonPropertyName("proteins_100g")]
    public decimal? Proteins100g { get; init; }
    [JsonPropertyName("carbohydrates_100g")]
    public decimal? Carbohydrates100g { get; init; }
    [JsonPropertyName("fat_100g")]
    public decimal? Fat100g { get; init; }
    [JsonPropertyName("fiber_100g")]
    public decimal? Fiber100g { get; init; }
    [JsonPropertyName("sugars_100g")]
    public decimal? Sugars100g { get; init; }
    [JsonPropertyName("sodium_100g")]
    public decimal? Sodium100g { get; init; }
}
