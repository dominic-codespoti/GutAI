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

    // OpenFoodFacts production base URL (.org).
    // The .net domain is the staging environment which requires HTTP Basic Auth
    // and is frequently unreliable. See: https://openfoodfacts.github.io/openfoodfacts-server/api/
    private const string BaseUrl = "https://world.openfoodfacts.org";

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
                $"{BaseUrl}/api/v2/product/{barcode}", JsonOptions, ct);

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
            var fields = "product_name,brands,code,nutriments,serving_size,serving_quantity,nova_group,nutriscore_grade,allergens_tags,additives_tags,ingredients_text,image_url";
            var escapedQuery = Uri.EscapeDataString(query);

            // Use the v2 API with both a text search and a brand-targeted search in parallel.
            // The CGI search.pl endpoint is extremely slow (~25s) for niche brand queries,
            // while the v2 API responds in ~2-3s.
            // Rate limit: 10 req/min for search queries (enforced per IP by OFF).
            var textUrl = $"{BaseUrl}/api/v2/search?search_terms={escapedQuery}&fields={fields}&page_size=10&sort_by=unique_scans_n";
            var brandUrl = $"{BaseUrl}/api/v2/search?brands_tags={escapedQuery}&fields={fields}&page_size=10&sort_by=unique_scans_n";

            var textTask = SafeFetch(textUrl, query, ct);
            var brandTask = SafeFetch(brandUrl, query, ct);

            await Task.WhenAll(textTask, brandTask);

            var textResults = textTask.Result;
            var brandResults = brandTask.Result;

            // Merge: brand results first (higher confidence for brand queries), then text results
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<FoodProductDto>();

            foreach (var dto in brandResults.Concat(textResults))
            {
                if (seen.Add(dto.Barcode ?? dto.Name))
                    merged.Add(dto);
            }

            _logger.LogInformation("OpenFoodFacts search for '{Query}' returned {Count} products (text={TextCount}, brand={BrandCount})",
                query, merged.Count, textResults.Count, brandResults.Count);
            return merged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search OpenFoodFacts for '{Query}'", query);
            return [];
        }
    }

    private async Task<List<FoodProductDto>> SafeFetch(string url, string query, CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("products", out var productsArray))
                return [];

            var results = new List<FoodProductDto>();
            foreach (var element in productsArray.EnumerateArray())
            {
                try
                {
                    var product = JsonSerializer.Deserialize<OffProduct>(element.GetRawText(), JsonOptions);
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
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OpenFoodFacts fetch failed for URL segment of query '{Query}'", query);
            return [];
        }
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
