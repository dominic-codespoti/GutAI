using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
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

    private readonly IFoodSearchService _foodApi;
    private readonly ITableStore _store;
    private readonly IOfflineFoodDatabase? _offlineDb;
    private readonly IExternalFoodAggregator? _externalFoodAggregator;
    private readonly FodmapService _fodmapService;
    private readonly GutRiskService _gutRiskService;
    private readonly PersonalizedScoringService _scoringService;
    private readonly ILogger<FoodTools> _logger;

    public FoodTools(
        IFoodSearchService foodApi,
        ITableStore store,
        FodmapService fodmapService,
        GutRiskService gutRiskService,
        PersonalizedScoringService scoringService,
        ILogger<FoodTools> logger,
        IOfflineFoodDatabase? offlineDb = null,
        IExternalFoodAggregator? externalFoodAggregator = null)
    {
        _foodApi = foodApi;
        _store = store;
        _offlineDb = offlineDb;
        _externalFoodAggregator = externalFoodAggregator;
        _fodmapService = fodmapService;
        _gutRiskService = gutRiskService;
        _scoringService = scoringService;
        _logger = logger;
    }

    [McpServerTool(Name = "gutai_search_foods")]
    [Authorize]
    [Description("Search the food database by name for matching food products. Call this first before any food-related operation to find the right food product ID. Returns up to 10 results with nutrition per 100g, brand, data source, and match confidence. IDs may be null for read-only connections when a result is not yet persisted.")]
    public async Task<string> SearchFoods(
        ClaimsPrincipal? user,
        [Description("Food name to search for (required). e.g. 'greek yogurt', 'chicken salad', 'coca cola'")] string query,
        CancellationToken ct)
    {
        try
        {
            var sanitized = QuerySanitizer.Sanitize(query);
            var results = (await _foodApi.SearchAsync(sanitized, ct)).Take(10).ToList();
            var summary = new List<object>(results.Count);
            for (var i = 0; i < results.Count; i++)
            {
                var food = results[i];
                Guid? id;
                if (food.Id == Guid.Empty && McpAccess.CanWrite(user))
                {
                    McpAccess.EnsureWrite(user!);
                    try
                    {
                        id = await FoodProductPersistence.ResolveOrPersistAsync(food, _store, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Could not persist MCP food search result '{FoodName}'", food.Name);
                        continue;
                    }
                }
                else
                {
                    id = food.Id == Guid.Empty ? null : food.Id;
                }
                summary.Add(new
                {
                    index = i + 1,
                    id,
                    name = food.Name,
                    brand = food.Brand,
                    dataSource = food.DataSource,
                    calories100g = food.Calories100g,
                    protein100g = food.Protein100g,
                    carbs100g = food.Carbs100g,
                    fat100g = food.Fat100g,
                    fiber100g = food.Fiber100g,
                    servingSize = food.ServingSize,
                    matchConfidence = food.MatchConfidence,
                    ingredients = food.Ingredients?.Length > 120 ? food.Ingredients[..120] + "..." : food.Ingredients
                });
            }
            return JsonSerializer.Serialize(new { results = summary }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SearchFoods failed");
            throw new McpException("Search failed. Please try again.");
        }
    }
    [McpServerTool(Name = "gutai_get_fodmap_assessment", ReadOnly = true)]
    [Authorize]
    [Description("Get the FODMAP ingredient-screening assessment for a food product: status (PotentialTriggersDetected / NoKnownTriggersDetected / InsufficientInformation), screening score 0-100 (higher = fewer triggers), confidence, trigger list with categories/severities, and summary. This is an ingredient screen, not a serving-size FODMAP classification.")]
    public async Task<string> GetFodmapAssessment(
        ClaimsPrincipal? user,
        [Description("The food product ID (GUID) from gutai_search_foods results (required)")] string foodProductId,
        CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(foodProductId, out var id))
                throw new McpException("Invalid food product ID format. Provide a valid GUID.");

            var product = await FoodProductResolver.GetEnrichedCatalogProductAsync(id, _store, _offlineDb, _externalFoodAggregator, ct, _logger);
            if (product is null)
                throw new McpException("Food product not found.");

            var dto = await FoodDtoHelper.BuildFoodProductDto(product, _store, ct);
            var fodmap = _fodmapService.Assess(dto);
            return JsonSerializer.Serialize(new
            {
                fodmap.Status,
                fodmap.IngredientScreeningScore,
                fodmap.Confidence,
                fodmap.MissingEvidence,
                fodmap.TriggerCount,
                fodmap.HighCount,
                fodmap.ModerateCount,
                fodmap.LowCount,
                fodmap.Categories,
                triggers = fodmap.Triggers.Select(t => new { t.Name, t.Category, t.SubCategory, t.Severity, t.Explanation }),
                fodmap.Summary
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetFodmapAssessment failed");
            throw new McpException("Could not assess FODMAP for that product. Please try again.");
        }
    }

    [McpServerTool(Name = "gutai_get_food_safety", ReadOnly = true)]
    [Authorize]
    [Description("Get a comprehensive personalized safety report for a food product. Combines FODMAP assessment, gut risk analysis (additives, NOVA, sodium), and a personalized score factoring in the user's allergies, conditions, and meal history. Prefer this over gutai_get_fodmap_assessment when you need the full picture.")]
    public async Task<string> GetFoodSafety(
        ClaimsPrincipal? user,
        [Description("The food product ID (GUID) from gutai_search_foods results (required)")] string foodProductId,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(user!);

            if (!Guid.TryParse(foodProductId, out var id))
                throw new McpException("Invalid food product ID format. Provide a valid GUID.");

            var product = await FoodProductResolver.GetEnrichedCatalogProductAsync(id, _store, _offlineDb, _externalFoodAggregator, ct, _logger);
            if (product is null)
                throw new McpException("Food product not found.");

            var dto = await FoodDtoHelper.BuildFoodProductDto(product, _store, ct);
            var fodmap = _fodmapService.Assess(dto);
            var gutRisk = _gutRiskService.Assess(dto);
            var score = await _scoringService.ScoreAsync(dto, userId, _store);

            return JsonSerializer.Serialize(new
            {
                fodmap = new { fodmap.Status, fodmap.Confidence, fodmap.MissingEvidence, fodmap.Summary },
                gutRisk = new { gutRisk.GutScore, gutRisk.GutRating, gutRisk.Summary },
                personalizedScore = new { score.CompositeScore, score.Rating, score.Summary }
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetFoodSafety failed");
            throw new McpException("Could not get the food safety report for that product. Please try again.");
        }
    }

    private static Guid GetUserId(ClaimsPrincipal? user) =>
        Guid.Parse(user!.FindFirstValue("sub")!);
}
