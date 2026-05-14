using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GutAI.Api.Mcp;

[McpServerToolType]
public class FoodTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IFoodApiService _foodApi;
    private readonly ITableStore _store;
    private readonly FodmapService _fodmapService;
    private readonly GutRiskService _gutRiskService;
    private readonly PersonalizedScoringService _scoringService;
    private readonly ILogger<FoodTools> _logger;

    public FoodTools(
        IFoodApiService foodApi,
        ITableStore store,
        FodmapService fodmapService,
        GutRiskService gutRiskService,
        PersonalizedScoringService scoringService,
        ILogger<FoodTools> logger)
    {
        _foodApi = foodApi;
        _store = store;
        _fodmapService = fodmapService;
        _gutRiskService = gutRiskService;
        _scoringService = scoringService;
        _logger = logger;
    }

    [McpServerTool(Name = "gutai_search_foods", ReadOnly = true)]
    [Description("Search the food database by name for matching food products. Call this first before any food-related operation to find the right food product ID. Returns up to 10 results with nutrition per 100g, brand, data source, and match confidence.")]
    public async Task<string> SearchFoods(
        ClaimsPrincipal? user,
        [Description("Food name to search for (required). e.g. 'greek yogurt', 'chicken salad', 'coca cola'")] string query,
        CancellationToken ct)
    {
        try
        {
            var sanitized = QuerySanitizer.Sanitize(query);
            var results = await _foodApi.SearchAsync(sanitized, ct);
            var summary = results.Take(10).Select((f, i) => new
            {
                index = i + 1,
                id = f.Id,
                name = f.Name,
                brand = f.Brand,
                dataSource = f.DataSource,
                calories100g = f.Calories100g,
                protein100g = f.Protein100g,
                carbs100g = f.Carbs100g,
                fat100g = f.Fat100g,
                fiber100g = f.Fiber100g,
                servingSize = f.ServingSize,
                matchConfidence = f.MatchConfidence,
                ingredients = f.Ingredients?.Length > 120 ? f.Ingredients[..120] + "..." : f.Ingredients
            });
            return JsonSerializer.Serialize(new { results = summary }, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SearchFoods failed");
            throw new McpException($"Error searching foods: {ex.Message}");
        }
    }

    [McpServerTool(Name = "gutai_get_fodmap_assessment", ReadOnly = true)]
    [Description("Get the FODMAP assessment for a food product (score, rating, triggers, summary). This is a subset of gutai_get_food_safety. Use when you only need FODMAP-specific info, or use gutai_get_food_safety for the complete safety picture including gut risk and personalized scoring.")]
    public async Task<string> GetFodmapAssessment(
        ClaimsPrincipal? user,
        [Description("The food product ID (GUID) from gutai_search_foods results (required)")] string foodProductId,
        CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(foodProductId, out var id))
                throw new McpException("Invalid food product ID format. Provide a valid GUID.");

            var product = await _store.GetFoodProductAsync(id, ct);
            if (product is null)
                throw new McpException("Food product not found.");

            var dto = await FoodDtoHelper.BuildFoodProductDto(product, _store, ct);
            var fodmap = _fodmapService.Assess(dto);
            return JsonSerializer.Serialize(new
            {
                fodmap.FodmapScore,
                fodmap.FodmapRating,
                fodmap.TriggerCount,
                triggers = fodmap.Triggers.Select(t => new { t.Name, t.Category, t.Severity, t.Explanation }),
                fodmap.Summary
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetFodmapAssessment failed");
            throw new McpException($"Error assessing FODMAP: {ex.Message}");
        }
    }

    [McpServerTool(Name = "gutai_get_food_safety", ReadOnly = true)]
    [Description("Get a comprehensive personalized safety report for a food product. Combines FODMAP assessment, gut risk analysis (additives, NOVA, sodium), and a personalized score factoring in the user's allergies, conditions, and meal history. Prefer this over gutai_get_fodmap_assessment when you need the full picture.")]
    public async Task<string> GetFoodSafety(
        HttpContext httpContext,
        ClaimsPrincipal? user,
        [Description("The food product ID (GUID) from gutai_search_foods results (required)")] string foodProductId,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);

            if (!Guid.TryParse(foodProductId, out var id))
                throw new McpException("Invalid food product ID format. Provide a valid GUID.");

            var product = await _store.GetFoodProductAsync(id, ct);
            if (product is null)
                throw new McpException("Food product not found.");

            var dto = await FoodDtoHelper.BuildFoodProductDto(product, _store, ct);
            var fodmap = _fodmapService.Assess(dto);
            var gutRisk = _gutRiskService.Assess(dto);
            var score = await _scoringService.ScoreAsync(dto, userId, _store);

            return JsonSerializer.Serialize(new
            {
                product = new { product.Name, product.Brand, product.Ingredients },
                fodmap = new { fodmap.FodmapScore, fodmap.FodmapRating, fodmap.Summary },
                gutRisk = new { gutRisk.GutScore, gutRisk.GutRating, gutRisk.Summary },
                personalizedScore = new { score.CompositeScore, score.Rating, score.Summary }
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetFoodSafety failed");
            throw new McpException($"Error getting food safety: {ex.Message}");
        }
    }

    private static Guid GetUserId(HttpContext httpContext) =>
        Guid.Parse(httpContext.User.FindFirstValue("sub")!);
}
