using System.Net.Http.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class UsdaFoodDataClient : IFoodProvider
{
    public string SourceName => DataSources.Usda;
    public FoodProviderCapabilities Capabilities => FoodProviderCapabilities.Search;

    private readonly HttpClient _http;
    private readonly ILogger<UsdaFoodDataClient> _logger;
    private readonly string _apiKey;

    public UsdaFoodDataClient(HttpClient http, IConfiguration config, ILogger<UsdaFoodDataClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["ExternalApis:UsdaApiKey"] ?? "";
    }

    public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        return Task.FromResult<FoodProductDto?>(null);
    }

    public async Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("USDA API key not configured, skipping search");
            return [];
        }

        try
        {
            var wholeTask = SearchByDataTypeAsync(query, "Foundation,SR Legacy", 10, ct);
            var brandedTask = SearchByDataTypeAsync(query, "Branded", 10, ct);

            await Task.WhenAll(wholeTask, brandedTask);

            var whole = await wholeTask;
            var branded = await brandedTask;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<FoodProductDto>();

            // Whole foods first
            foreach (var f in whole.Concat(branded))
                if (seen.Add(f.Name))
                    merged.Add(f);

            _logger.LogInformation("USDA search for '{Query}' returned {Whole} whole + {Branded} branded foods", query, whole.Count, branded.Count);
            return merged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search USDA for '{Query}'", query);
            return [];
        }
    }

    private async Task<List<FoodProductDto>> SearchByDataTypeAsync(string query, string dataType, int pageSize, CancellationToken ct)
    {
        try
        {
            var url = $"fdc/v1/foods/search?query={Uri.EscapeDataString(query)}&dataType={Uri.EscapeDataString(dataType)}&pageSize={pageSize}&api_key={_apiKey}";
            var response = await _http.GetFromJsonAsync<UsdaSearchResponse>(url, ct);

            return response?.Foods?.Select(UsdaFoodMapper.ToDto).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USDA {DataType} search failed for '{Query}'", dataType, query);
            return [];
        }
    }


}

public record UsdaSearchResponse
{
    public List<UsdaFood>? Foods { get; init; }
}

public record UsdaFood
{
    public int FdcId { get; init; }
    public string? Description { get; init; }
    public string? BrandOwner { get; init; }
    public string? DataType { get; init; }
    public string? FoodCategory { get; init; }
    public string? Ingredients { get; init; }
    public List<UsdaNutrient>? FoodNutrients { get; init; }
}

internal static class UsdaFoodMapper
{
    public static FoodProductDto ToDto(UsdaFood f)
    {
        var isWhole = f.DataType is "SR Legacy" or "Foundation";
        var name = f.Description ?? "Unknown";
        // Clean up USDA names - they're often ALL CAPS
        if (name == name.ToUpperInvariant() && name.Length > 3)
            name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());

        return new FoodProductDto
        {
            Name = name,
            Brand = isWhole ? null : f.BrandOwner,
            Ingredients = string.IsNullOrWhiteSpace(f.Ingredients) ? null : f.Ingredients.Trim(),
            Calories100g = Nutrient(f, 1008),
            Protein100g = Nutrient(f, 1003),
            Carbs100g = Nutrient(f, 1005),
            Fat100g = Nutrient(f, 1004),
            Fiber100g = Nutrient(f, 1079),
            Sugar100g = Nutrient(f, 2000),
            SodiumMg100g = Nutrient(f, 1093),
            DataSource = DataSources.Usda,
            SourceVersion = "live-api",
            LicenseType = "USDA FoodData Central terms",
            Attribution = "USDA FoodData Central",
            RetrievedAt = DateTime.UtcNow,
            SourceUrl = $"https://fdc.nal.usda.gov/fdc-app.html#/food-details/{f.FdcId}/nutrients",
            ExternalId = f.FdcId.ToString()
        };
    }

    private static decimal? Nutrient(UsdaFood f, int id) =>
        f.FoodNutrients?.FirstOrDefault(n => n.NutrientId == id)?.Value;
}

public record UsdaNutrient
{
    public int NutrientId { get; init; }
    public string? NutrientName { get; init; }
    public decimal? Value { get; init; }
}
