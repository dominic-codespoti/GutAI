using System.Net.Http.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class UsdaFoodDataClient : IFoodApiService
{
    public string SourceName => DataSources.UsdaBranded;

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

    public async Task<List<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
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
            var url = $"https://api.nal.usda.gov/fdc/v1/foods/search?query={Uri.EscapeDataString(query)}&dataType={Uri.EscapeDataString(dataType)}&pageSize={pageSize}&api_key={_apiKey}";
            var response = await _http.GetFromJsonAsync<UsdaSearchResponse>(url, ct);

            return response?.Foods?.Select(f =>
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
                    Calories100g = Nutrient(f, 1008),
                    Protein100g = Nutrient(f, 1003),
                    Carbs100g = Nutrient(f, 1005),
                    Fat100g = Nutrient(f, 1004),
                    Fiber100g = Nutrient(f, 1079),
                    Sugar100g = Nutrient(f, 2000),
                    Sodium100g = Nutrient(f, 1093) is decimal mg ? mg / 1000m : null,
                    DataSource = isWhole ? DataSources.UsdaWhole : DataSources.UsdaBranded,
                    SourceUrl = $"https://fdc.nal.usda.gov/fdc-app.html#/food-details/{f.FdcId}/nutrients",
                    ExternalId = f.FdcId.ToString()
                };
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USDA {DataType} search failed for '{Query}'", dataType, query);
            return [];
        }
    }

    private static decimal? Nutrient(UsdaFood f, int id) =>
        f.FoodNutrients?.FirstOrDefault(n => n.NutrientId == id)?.Value;
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
    public List<UsdaNutrient>? FoodNutrients { get; init; }
}

public record UsdaNutrient
{
    public int NutrientId { get; init; }
    public string? NutrientName { get; init; }
    public decimal? Value { get; init; }
}
