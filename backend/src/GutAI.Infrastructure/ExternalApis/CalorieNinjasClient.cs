using System.Net;
using System.Net.Http.Json;
using GutAI.Application.Common.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public class CalorieNinjasClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CalorieNinjasClient> _logger;
    private readonly string _apiKey;

    public CalorieNinjasClient(HttpClient http, IConfiguration config, ILogger<CalorieNinjasClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["ExternalApis:CalorieNinjasApiKey"] ?? "";
    }

    public virtual async Task<List<ParsedFoodItemDto>> ParseNaturalLanguageAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return [];

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.calorieninjas.com/v1/nutrition?query={Uri.EscapeDataString(text)}");
        request.Headers.Add("X-Api-Key", _apiKey);

        var response = await _http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests)
            throw new CalorieNinjasApiException(response.StatusCode,
                $"CalorieNinjas API returned {(int)response.StatusCode}");

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CalorieNinjasResponse>(ct);

        return result?.Items?.Select(i => new ParsedFoodItemDto
        {
            Name = i.Name ?? "Unknown",
            Calories = i.Calories,
            ProteinG = i.ProteinG,
            CarbsG = i.CarbohydratesTotalG,
            FatG = i.FatTotalG,
            FiberG = i.FiberG,
            SugarG = i.SugarG,
            SodiumMg = i.SodiumMg,
            CholesterolMg = i.CholesterolMg,
            SaturatedFatG = i.FatSaturatedG,
            PotassiumMg = i.PotassiumMg,
            ServingWeightG = i.ServingSizeG
        }).ToList() ?? [];
    }
}

public record CalorieNinjasResponse
{
    public List<CalorieNinjasItem>? Items { get; init; }
}

public record CalorieNinjasItem
{
    public string? Name { get; init; }
    public decimal Calories { get; init; }
    public decimal ServingSizeG { get; init; }
    public decimal FatTotalG { get; init; }
    public decimal FatSaturatedG { get; init; }
    public decimal ProteinG { get; init; }
    public decimal SodiumMg { get; init; }
    public decimal PotassiumMg { get; init; }
    public decimal CholesterolMg { get; init; }
    public decimal CarbohydratesTotalG { get; init; }
    public decimal FiberG { get; init; }
    public decimal SugarG { get; init; }
}
