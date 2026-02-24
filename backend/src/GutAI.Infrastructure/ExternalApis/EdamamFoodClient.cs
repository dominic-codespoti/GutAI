using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class EdamamFoodClient : INutritionApiService, IFoodApiService
{
    private readonly HttpClient _http;
    private readonly ILogger<EdamamFoodClient> _logger;
    private readonly string _appId;
    private readonly string _appKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public EdamamFoodClient(HttpClient http, IConfiguration config, ILogger<EdamamFoodClient> logger)
    {
        _http = http;
        _logger = logger;
        _appId = config["ExternalApis:EdamamAppId"] ?? "";
        _appKey = config["ExternalApis:EdamamAppKey"] ?? "";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_appId) && !string.IsNullOrWhiteSpace(_appKey);

    public async Task<List<ParsedFoodItemDto>> ParseNaturalLanguageAsync(string text, CancellationToken ct = default)
    {
        if (!IsConfigured) return [];

        try
        {
            var url = $"https://api.edamam.com/api/food-database/v2/parser?app_id={_appId}&app_key={_appKey}&ingr={Uri.EscapeDataString(text)}&nutrition-type=logging";
            var response = await _http.GetFromJsonAsync<EdamamParserResponse>(url, JsonOptions, ct);

            if (response is null) return [];

            var results = new List<ParsedFoodItemDto>();

            // Use parsed items first (NLP-parsed results)
            if (response.Parsed is { Count: > 0 })
            {
                foreach (var parsed in response.Parsed)
                {
                    var food = parsed.Food;
                    if (food?.Nutrients is null) continue;
                    results.Add(MapToNutrition(food, parsed.Quantity, parsed.Measure));
                }
            }

            // Fall back to hints if no parsed results
            if (results.Count == 0 && response.Hints is { Count: > 0 })
            {
                var food = response.Hints[0].Food;
                if (food?.Nutrients is not null)
                    results.Add(MapToNutrition(food, 1, null));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Edamam NLP parse failed for '{Text}'", text);
            return [];
        }
    }

    public async Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        try
        {
            var url = $"https://api.edamam.com/api/food-database/v2/parser?app_id={_appId}&app_key={_appKey}&upc={barcode}&nutrition-type=logging";
            var response = await _http.GetFromJsonAsync<EdamamParserResponse>(url, JsonOptions, ct);

            if (response?.Hints is not { Count: > 0 }) return null;

            var food = response.Hints[0].Food;
            if (food is null) return null;

            return MapToProduct(food, barcode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Edamam barcode lookup failed for '{Barcode}'", barcode);
            return null;
        }
    }

    public async Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (!IsConfigured) return [];

        try
        {
            var url = $"https://api.edamam.com/api/food-database/v2/parser?app_id={_appId}&app_key={_appKey}&ingr={Uri.EscapeDataString(query)}&nutrition-type=logging";
            var response = await _http.GetFromJsonAsync<EdamamParserResponse>(url, JsonOptions, ct);

            if (response?.Hints is null) return [];

            var results = new List<FoodProductDto>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hint in response.Hints.Take(15))
            {
                if (hint.Food is null) continue;
                var name = hint.Food.Label ?? "Unknown";
                if (!seen.Add(name)) continue;

                var dto = MapToProduct(hint.Food, null);
                if (dto is not null)
                    results.Add(dto);
            }

            _logger.LogInformation("Edamam search for '{Query}' returned {Count} results", query, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Edamam search failed for '{Query}'", query);
            return [];
        }
    }

    public async Task<List<string>> GetHealthLabelsAsync(string foodId, CancellationToken ct = default)
    {
        if (!IsConfigured) return [];

        try
        {
            var url = $"https://api.edamam.com/api/food-database/v2/parser?app_id={_appId}&app_key={_appKey}&ingr={Uri.EscapeDataString(foodId)}&nutrition-type=logging&health=FODMAP_FREE";
            var response = await _http.GetFromJsonAsync<EdamamParserResponse>(url, JsonOptions, ct);

            // If the FODMAP_FREE filter returns results, the food is FODMAP-free
            if (response?.Hints is { Count: > 0 })
                return ["FODMAP_FREE"];

            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Edamam health label check failed for '{FoodId}'", foodId);
            return [];
        }
    }

    public async Task<bool> IsFodmapFreeAsync(string foodName, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        try
        {
            var url = $"https://api.edamam.com/api/food-database/v2/parser?app_id={_appId}&app_key={_appKey}&ingr={Uri.EscapeDataString(foodName)}&nutrition-type=logging&health=FODMAP_FREE";
            var response = await _http.GetFromJsonAsync<EdamamParserResponse>(url, JsonOptions, ct);
            return response?.Hints is { Count: > 0 };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Edamam FODMAP check failed for '{Food}'", foodName);
            return false;
        }
    }

    static ParsedFoodItemDto MapToNutrition(EdamamFood food, double? quantity, EdamamMeasure? measure)
    {
        var n = food.Nutrients!;
        var servingWeight = quantity ?? 100;
        var scale = servingWeight / 100.0;

        return new ParsedFoodItemDto
        {
            Name = food.Label ?? "Unknown",
            Calories = (decimal)(n.ENERC_KCAL * scale),
            ProteinG = (decimal)(n.PROCNT * scale),
            CarbsG = (decimal)(n.CHOCDF * scale),
            FatG = (decimal)(n.FAT * scale),
            FiberG = (decimal)(n.FIBTG * scale),
            SugarG = 0, // Edamam parser doesn't always return sugar
            SodiumMg = 0,
            CholesterolMg = 0,
            SaturatedFatG = 0,
            PotassiumMg = 0,
            ServingWeightG = (decimal)servingWeight,
            ServingSize = measure?.Label,
        };
    }

    static FoodProductDto? MapToProduct(EdamamFood food, string? barcode)
    {
        if (food.Nutrients is null) return null;

        var n = food.Nutrients;
        return new FoodProductDto
        {
            Name = food.Label ?? "Unknown",
            Barcode = barcode,
            Brand = food.Brand,
            ImageUrl = food.Image,
            Calories100g = (decimal?)n.ENERC_KCAL,
            Protein100g = (decimal?)n.PROCNT,
            Carbs100g = (decimal?)n.CHOCDF,
            Fat100g = (decimal?)n.FAT,
            Fiber100g = (decimal?)n.FIBTG,
            DataSource = "Edamam",
            ExternalId = food.FoodId,
        };
    }
}

// ─── Edamam API Response Models ─────────────────────────────────────────

public record EdamamParserResponse
{
    public string? Text { get; init; }
    public List<EdamamParsed>? Parsed { get; init; }
    public List<EdamamHint>? Hints { get; init; }
}

public record EdamamParsed
{
    public EdamamFood? Food { get; init; }
    public double? Quantity { get; init; }
    public EdamamMeasure? Measure { get; init; }
}

public record EdamamHint
{
    public EdamamFood? Food { get; init; }
    public List<EdamamMeasure>? Measures { get; init; }
}

public record EdamamFood
{
    public string? FoodId { get; init; }
    public string? Label { get; init; }
    public string? Brand { get; init; }
    public string? Category { get; init; }
    public string? CategoryLabel { get; init; }
    public string? Image { get; init; }
    public EdamamNutrients? Nutrients { get; init; }
    public List<string>? HealthLabels { get; init; }
}

public record EdamamNutrients
{
    public double ENERC_KCAL { get; init; }
    public double PROCNT { get; init; }
    public double FAT { get; init; }
    public double CHOCDF { get; init; }
    public double FIBTG { get; init; }
}

public record EdamamMeasure
{
    public string? Uri { get; init; }
    public string? Label { get; init; }
    public double? Weight { get; init; }
}
