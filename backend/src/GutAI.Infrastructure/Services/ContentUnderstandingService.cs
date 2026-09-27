using System.Text.Json;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using AITextContent = Microsoft.Extensions.AI.TextContent;
using AIDataContent = Microsoft.Extensions.AI.DataContent;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace GutAI.Infrastructure.Services;

public class ContentUnderstandingService : IContentUnderstandingService
{
    public const string DescribeFoodPromptVersion = "2026-09-25.v1";
    public const string NutritionLabelPromptVersion = "2026-09-25.v1";

    private readonly ContentUnderstandingClient _client;
    private readonly ILogger<ContentUnderstandingService>? _logger;
    private readonly AIProjectClient? _projectClient;
    private readonly IChatClient? _chatClient;
    private readonly IChatClient? _describeChatClient;
    private readonly IFoodSearchService? _foodSearch;
    private readonly string _agentName;

    /// <summary>
    /// Structured extraction options for the configured reasoning deployment.
    /// Temperature remains null so reasoning models omit the unsupported field.
    /// </summary>
    private static readonly ChatOptions ExtractionOptions = new();

    private static readonly AIChatRole DeveloperRole = new("developer");

    private const string NutritionLabelInstructions = """
        You extract the nutrition facts and ingredients visible on a food label.
        Return one JSON object matching the versioned nutrition label extraction schema.
        Extract values per stated serving; convert explicitly stated kJ to kcal by dividing by 4.184.
        Never add provenance or described-component fields.
        """;
    public ContentUnderstandingService(
        ContentUnderstandingClient client,
        IConfiguration? config = null,
        ILogger<ContentUnderstandingService>? logger = null,
        AIProjectClient? projectClient = null,
        IChatClient? chatClient = null,
        IChatClient? describeChatClient = null,
        IFoodSearchService? foodSearch = null)
    {
        _client = client;
        _logger = logger;
        _projectClient = projectClient;
        _chatClient = chatClient;
        _describeChatClient = describeChatClient;
        _foodSearch = foodSearch;
        _agentName = config?["Foundry:AgentName"] ?? "nutrition-estimation-agent";
    }

    public async Task<CustomFoodDto?> ParseNutritionLabelAsync(Stream imageStream, string contentType, CancellationToken ct)
    {
        // Use await using for proper async disposal
        await using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, ct);

        try
        {
            memoryStream.Position = 0;
            _logger?.LogInformation("Nutrition label parse starting with analyzer {AnalyzerId}.", "prebuilt-documentFields");
            var primaryResult = await ParseWithAnalyzerAsync(memoryStream, contentType, "prebuilt-documentFields", ct);

            // Accept partial extractions as long as we got any meaningful label data.
            if (primaryResult != null && HasMeaningfulExtraction(primaryResult) && FinalizeGeneratedFood(primaryResult))
                return primaryResult;

            if (primaryResult != null)
            {
                _logger?.LogWarning(
                    "Analyzer returned incomplete or implausible nutrition data (cal={Calories}, protein={ProteinG}, carbs={CarbG}, fat={FatG}, sodium={SodiumMg}); attempting LLM fallback.",
                    primaryResult.Calories, primaryResult.ProteinG, primaryResult.CarbG, primaryResult.FatG, primaryResult.SodiumMg);
            }
            else
            {
                _logger?.LogWarning("Analyzer returned no nutrition data; attempting LLM fallback.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Analyzer failed; attempting LLM fallback. {Details}", DescribeException(ex));
        }

        if (_chatClient is null)
        {
            _logger?.LogWarning("LLM label fallback unavailable because the Responses chat client is not configured.");
            return null;
        }

        try
        {
            _logger?.LogInformation("LLM label fallback starting through the Responses chat client using prompt {PromptVersion}.", NutritionLabelPromptVersion);
            await using var llmStream = new MemoryStream();
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(llmStream, ct);
            llmStream.Position = 0;

            var fallbackResult = await ParseWithLlmVisionAsync(llmStream, contentType, ct);
            if (fallbackResult != null && HasMeaningfulExtraction(fallbackResult) && FinalizeGeneratedFood(fallbackResult))
            {
                _logger?.LogInformation("LLM fallback succeeded.");
                return fallbackResult;
            }
            _logger?.LogWarning("LLM fallback returned no usable nutrition data.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "LLM label fallback failed. {Details}", DescribeException(ex));
        }

        return null;
    }

    public async Task<CustomFoodDto?> DescribeFoodFromTextAsync(string description, CancellationToken ct)
    {
        var trimmedDescription = description?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedDescription))
            return null;

        if ((_describeChatClient ?? _chatClient) is not null)
            return await DescribeFoodWithChatClientAsync(trimmedDescription, ct);
        if (_projectClient != null)
            return await DescribeFoodWithAgentAsync(trimmedDescription, ct);

        _logger?.LogWarning("Text food description is unavailable because no Responses-backed AI client is configured.");
        return null;
    }

    private async Task<CustomFoodDto?> DescribeFoodWithChatClientAsync(string description, CancellationToken ct)
    {
        try
        {
            _logger?.LogInformation("Invoking IChatClient for structured food description using prompt {PromptVersion}.", DescribeFoodPromptVersion);
            var messages = new List<AIChatMessage>
            {
                new(DeveloperRole, DescribeFoodInstructions),
                new(AIChatRole.User, $"<food_description>\n{description}\n</food_description>")
            };
            var response = await (_describeChatClient ?? _chatClient!).GetResponseAsync<DescribedDish>(
                messages, options: ExtractionOptions, useJsonSchemaResponseFormat: true, cancellationToken: ct);
            return response.Result is { } dish ? await FinalizeDescribedDishAsync(dish, description, ct) : null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "IChatClient food description failed. {Details}", DescribeException(ex));
            return null;
        }
    }

    private const string DescribeFoodInstructions = """
        You decompose a described prepared food into its physical food components for nutrition logging.
        Return exactly one structured DescribedDish object matching the schema.
        Rules:
        - Output identity, serving unit and grams, component identities and grams, search queries, and confidence only; never provide dish-level nutrition totals.
        - Component grams are the amounts within one serving. Sum component grams should be plausible for that serving.
        - Give at most 3 short, distinct catalog search queries per component.
        - Include fallback_per_100g only as a plausible estimate for that component, in kcal and grams per 100 g; include fiber only when inferable.
        - Do not invent brands. Include all meaningful ingredients/components.
        - Confidence is 0..1 and reflects clarity of the description.
        """;

    private async Task<CustomFoodDto?> DescribeFoodWithAgentAsync(string description, CancellationToken ct)
    {
        try
        {
            _logger?.LogInformation("Invoking Foundry agent '{AgentName}' for food description using prompt {PromptVersion}.", _agentName, DescribeFoodPromptVersion);
            var agentRef = new AgentReference(name: _agentName);
            var responsesClient = _projectClient!.OpenAI.GetProjectResponsesClientForAgent(agentRef);
#pragma warning disable OPENAI001 // Experimental APIs
            var options = new CreateResponseOptions();
            options.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(DescribeFoodInstructions));
            options.InputItems.Add(ResponseItem.CreateUserMessageItem($"<food_description>\n{description}\n</food_description>"));
            var result = await responsesClient.CreateResponseAsync(options, ct);
#pragma warning restore OPENAI001
            var rawJson = result.GetRawResponse()?.Content?.ToString();
            var textResponse = rawJson is not null ? ExtractResponseText(rawJson) : null;
            if (string.IsNullOrWhiteSpace(textResponse) || !TryParseDescribedDish(textResponse, out var dish) || dish is null)
            {
                _logger?.LogWarning("Agent '{AgentName}' returned no valid structured food description.", _agentName);
                return null;
            }
            return await FinalizeDescribedDishAsync(dish, description, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent '{AgentName}' invocation failed. {Details}", _agentName, DescribeException(ex));
            return null;
        }
    }

    internal async Task<CustomFoodDto?> FinalizeDescribedDishAsync(DescribedDish dish, string fallbackName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dish.Name) || dish.Serving is null
            || dish.Serving.Grams is <= 0 or > 10000 || dish.Components is null
            || dish.Components.Count == 0 || dish.Components.Count > 50)
            return null;

        var described = new List<DescribedFoodComponentDto>(dish.Components.Count);
        var portions = new List<NutritionAmountsDto>(dish.Components.Count);
        foreach (var component in dish.Components)
        {
            if (string.IsNullOrWhiteSpace(component.Name) || component.Grams is <= 0 or > 10000)
            {
                _logger?.LogWarning("Dropping invalid described-food component '{ComponentName}'.", component.Name);
                continue;
            }

            FoodProductDto? selected = null;
            decimal matchConfidence = 0m;
            if (_foodSearch is not null)
            {
                foreach (var query in (component.SearchQueries ?? []).Where(q => !string.IsNullOrWhiteSpace(q)).Take(3))
                {
                    var resolution = await _foodSearch.ResolveAsync(query.Trim(), [], ct);
                    var decision = GroundingPolicy.Decide(resolution);
                    if (!decision.AutoSelected || decision.Selected is null)
                        continue;
                    selected = decision.Selected;
                    matchConfidence = resolution.MatchConfidence;
                    break;
                }
            }

            NutritionPer100gDto? basis = selected is null ? null : NutritionCalculator.BasisFrom(selected);
            var isGrounded = basis is not null;
            if (!isGrounded)
            {
                var estimate = component.FallbackPer100G;
                basis = estimate is null ? null : new NutritionPer100gDto
                {
                    CaloriesKcal = estimate.Kcal,
                    ProteinG = estimate.ProteinG,
                    CarbsG = estimate.CarbsG,
                    FatG = estimate.FatG,
                    FiberG = estimate.FiberG
                };
                if (basis is null || !NutritionSanity.Check(basis, NutritionSanityProfile.Web, component.Name).IsPlausible)
                {
                    _logger?.LogWarning("Dropping described-food component '{ComponentName}': no auto-selected catalog match and fallback estimate failed nutrition sanity.", component.Name);
                    continue;
                }
            }

            var amounts = NutritionCalculator.Compute(basis, component.Grams);
            portions.Add(amounts);
            described.Add(new DescribedFoodComponentDto
            {
                Name = component.Name.Trim(),
                Grams = component.Grams,
                FoodProductId = selected?.Id,
                CanonicalName = selected?.Name,
                Source = isGrounded ? NormalizeSource(selected!.DataSource) : "ai",
                NutritionProvenance = isGrounded ? "Sourced" : "ModelEstimated",
                MatchConfidence = matchConfidence,
                Calories = amounts.Calories,
                ProteinG = amounts.ProteinG,
                CarbsG = amounts.CarbsG,
                FatG = amounts.FatG
            });
        }

        if (described.Count == 0)
            return null;
        var totals = NutritionCalculator.Sum(portions);
        var provenance = described.All(c => c.NutritionProvenance == "Sourced") ? "Sourced" : "ModelEstimated";
        // Estimated components have match confidence 0, so this is the mean over surviving components.
        var meanMatchConfidence = described.Average(c => c.MatchConfidence);
        var result = new CustomFoodDto
        {
            Name = string.IsNullOrWhiteSpace(dish.Name) ? fallbackName : dish.Name.Trim(),
            ServingSize = dish.Serving.Grams,
            ServingSizeUnit = string.IsNullOrWhiteSpace(dish.Serving.Unit) ? "g" : dish.Serving.Unit.Trim(),
            Calories = totals.Calories,
            ProteinG = totals.ProteinG,
            CarbG = totals.CarbsG,
            FatG = totals.FatG,
            FiberG = totals.FiberG,
            SugarG = totals.SugarG,
            SodiumMg = totals.SodiumMg,
            ExtractionConfidence = Math.Clamp(dish.Confidence, 0m, 1m) * meanMatchConfidence,
            NutritionProvenance = provenance,
            DescribedComponents = described
        };
        return FinalizeGeneratedFood(result, fallbackName) ? result : null;
    }

    private static string NormalizeSource(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        "usda" => "usda",
        "openfoodfacts" or "off" => "off",
        "au" or "australian" or "ausnut" => "au",
        "db" or "database" or "manual" => "db",
        _ => "db"
    };

    internal static bool TryParseDescribedDish(string? responseText, out DescribedDish? dish)
    {
        dish = null;
        var json = ExtractJsonObject(responseText);
        if (json is null) return false;
        try
        {
            dish = JsonSerializer.Deserialize<DescribedDish>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dish is not null;
        }
        catch (JsonException) { return false; }
    }




    private static string? ExtractResponseText(object responseValue)
    {
        try
        {
            var json = JsonSerializer.Serialize(responseValue);
            return ExtractResponseText(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractResponseText(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return ExtractOutputText(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        // Try lowercase "output" (wire format) then "Output" / "OutputItems" (reflection serialization)
        var output = TryGetPropertyCaseInsensitive(root, "output");
        if (output is null || output.Value.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in output.Value.EnumerateArray())
        {
            var type = TryGetPropertyCaseInsensitive(item, "type");
            if (type is null || type.Value.GetString() != "message")
                continue;

            var content = TryGetPropertyCaseInsensitive(item, "content");
            if (content is null || content.Value.ValueKind != JsonValueKind.Array)
                continue;

            var texts = new List<string>();
            foreach (var block in content.Value.EnumerateArray())
            {
                var text = TryGetPropertyCaseInsensitive(block, "text");
                if (text is not null)
                    texts.Add(text.Value.GetString() ?? "");
            }
            if (texts.Count > 0)
                return string.Concat(texts);
        }

        return null;
    }

    private static JsonElement? TryGetPropertyCaseInsensitive(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (element.TryGetProperty(propertyName, out var value))
            return value;

        // Try the lowercase variant
        var lower = propertyName.ToLowerInvariant();
        if (lower != propertyName && element.TryGetProperty(lower, out value))
            return value;

        // Try the PascalCase variant
        var pascal = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        if (pascal != propertyName && element.TryGetProperty(pascal, out value))
            return value;

        return null;
    }

    /// <summary>
    /// Validates that the extracted data contains any meaningful label information
    /// </summary>
    internal static bool HasMeaningfulExtraction(CustomFoodDto dto)
    {
        return !string.IsNullOrWhiteSpace(dto.Name) ||
               !string.IsNullOrWhiteSpace(dto.BrandName) ||
               dto.ServingSize > 0 ||
               !string.IsNullOrWhiteSpace(dto.ServingSizeUnit) && dto.ServingSize > 0 ||
               !string.IsNullOrWhiteSpace(dto.Ingredients) ||
               !string.IsNullOrWhiteSpace(dto.Barcode) ||
               HasMeaningfulNumericValue(dto.Calories) ||
               HasMeaningfulNumericValue(dto.ProteinG) ||
               HasMeaningfulNumericValue(dto.FatG) ||
               HasMeaningfulNumericValue(dto.CarbG) ||
               HasMeaningfulNullableValue(dto.FiberG) ||
               HasMeaningfulNullableValue(dto.SugarG) ||
               HasMeaningfulNullableValue(dto.SodiumMg) ||
               HasMeaningfulNullableValue(dto.SaturatedFatG) ||
               HasMeaningfulNullableValue(dto.TransFatG) ||
               HasMeaningfulNullableValue(dto.CholesterolMg) ||
               HasMeaningfulNullableValue(dto.PotassiumMg) ||
               HasMeaningfulNullableValue(dto.CalciumMg) ||
               HasMeaningfulNullableValue(dto.IronMg) ||
               HasMeaningfulNullableValue(dto.MagnesiumMg) ||
               HasMeaningfulNullableValue(dto.ZincMg) ||
               HasMeaningfulNullableValue(dto.VitaminA_IU) ||
               HasMeaningfulNullableValue(dto.VitaminC_Mg) ||
               HasMeaningfulNullableValue(dto.VitaminD_Mcg) ||
               HasMeaningfulNullableValue(dto.VitaminB12_Mcg) ||
               HasMeaningfulNullableValue(dto.Omega3G) ||
               HasMeaningfulNullableValue(dto.CaffeineMg) ||
               dto.ExtractionConfidence.HasValue;
    }

    private static bool HasMeaningfulNumericValue(decimal value) => value > 0m;

    private static bool HasMeaningfulNullableValue(decimal? value) => value.HasValue;


    private async Task<CustomFoodDto?> ParseWithLlmVisionAsync(Stream memoryStream, string contentType, CancellationToken ct)
    {
        if (_chatClient is null)
            return null;
        try
        {
            using var mem = new MemoryStream();
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(mem, ct);
            var rawData = BinaryData.FromBytes(mem.ToArray(), contentType == "image/png" ? "image/png" : "image/jpeg");
            var aiMessages = new List<AIChatMessage>
            {
                new(DeveloperRole, NutritionLabelInstructions),
                new(AIChatRole.User, [
                    new AITextContent("Extract the nutritional label data."),
                    new AIDataContent(rawData.ToArray(), rawData.MediaType)
                ])
            };
            var aiResponse = await _chatClient.GetResponseAsync<NutritionLabelExtraction>(
                aiMessages, options: ExtractionOptions, useJsonSchemaResponseFormat: true, cancellationToken: ct);
            return aiResponse.Result is { } extracted ? MapLabelExtraction(extracted) : null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Responses vision label parse failed: {Message}", ex.Message);
            return null;
        }
    }

    internal static CustomFoodDto MapLabelExtraction(NutritionLabelExtraction source) => new()
    {
        Name = source.Name,
        BrandName = source.BrandName,
        ServingSize = source.ServingSize,
        ServingSizeUnit = source.ServingSizeUnit,
        Calories = source.Calories,
        ProteinG = source.ProteinG,
        CarbG = source.CarbG,
        FatG = source.FatG,
        FiberG = source.FiberG,
        SugarG = source.SugarG,
        SodiumMg = source.SodiumMg,
        SaturatedFatG = source.SaturatedFatG,
        TransFatG = source.TransFatG,
        CholesterolMg = source.CholesterolMg,
        PotassiumMg = source.PotassiumMg,
        CalciumMg = source.CalciumMg,
        IronMg = source.IronMg,
        MagnesiumMg = source.MagnesiumMg,
        ZincMg = source.ZincMg,
        VitaminA_IU = source.VitaminA_IU,
        VitaminC_Mg = source.VitaminC_Mg,
        VitaminD_Mcg = source.VitaminD_Mcg,
        VitaminB12_Mcg = source.VitaminB12_Mcg,
        Omega3G = source.Omega3G,
        CaffeineMg = source.CaffeineMg,
        Ingredients = source.Ingredients,
        Barcode = source.Barcode,
        ExtractionConfidence = source.ExtractionConfidence
    };


    internal static string ResolveTextDeploymentName(IConfiguration? config)
        => config?["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

    /// <summary>Falls back onto the same deployment used for text completions when no
    /// dedicated vision deployment is configured — a hardcoded model name here would
    /// silently point at a deployment that may not exist on a given Azure OpenAI resource,
    /// even though the configured text deployment (already proven reachable) is equally
    /// capable of multimodal (vision) input for the common single-deployment setup.</summary>
    internal static string ResolveVisionDeploymentName(IConfiguration? config)
        => config?["AzureOpenAI:VisionDeploymentName"] ?? ResolveTextDeploymentName(config);

    internal static bool FinalizeGeneratedFood(CustomFoodDto dto, string? fallbackName = null)
    {
        dto.Name = string.IsNullOrWhiteSpace(dto.Name) ? (fallbackName ?? string.Empty) : dto.Name.Trim();
        dto.BrandName = string.IsNullOrWhiteSpace(dto.BrandName) ? null : dto.BrandName.Trim();
        dto.ServingSizeUnit = string.IsNullOrWhiteSpace(dto.ServingSizeUnit) ? "g" : dto.ServingSizeUnit.Trim();
        dto.Ingredients = string.IsNullOrWhiteSpace(dto.Ingredients) ? null : dto.Ingredients.Trim();
        dto.Barcode = string.IsNullOrWhiteSpace(dto.Barcode) ? null : dto.Barcode.Trim();

        var servingGrams = dto.ServingSizeUnit.Equals("g", StringComparison.OrdinalIgnoreCase)
            || dto.ServingSizeUnit.Equals("gram", StringComparison.OrdinalIgnoreCase)
            || dto.ServingSizeUnit.Equals("grams", StringComparison.OrdinalIgnoreCase)
            ? dto.ServingSize : (decimal?)null;
        var amounts = new NutritionAmountsDto
        {
            Calories = dto.Calories,
            ProteinG = dto.ProteinG,
            CarbsG = dto.CarbG,
            FatG = dto.FatG,
            FiberG = dto.FiberG,
            SugarG = dto.SugarG,
            SodiumMg = dto.SodiumMg
        };
        var sanity = NutritionSanity.CheckPortion(amounts, servingGrams, dto.Name);
        if (!sanity.IsPlausible || dto.ServingSize < 0
            || dto.SugarG is { } sugar && sugar > dto.CarbG + 5m
            || dto.FiberG is { } fiber && fiber > dto.CarbG + 5m)
            return false;

        if (dto.ExtractionConfidence.HasValue)
            dto.ExtractionConfidence = Math.Clamp(dto.ExtractionConfidence.Value, 0m, 1m);
        return true;
    }

    internal static bool TryParseFallbackResponse(string? responseText, out CustomFoodDto? dto)
    {
        dto = null;
        var jsonText = ExtractJsonObject(responseText);
        if (string.IsNullOrWhiteSpace(jsonText))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.EnumerateObject().Any())
                return false;
            var extracted = JsonSerializer.Deserialize<NutritionLabelExtraction>(jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (extracted is null)
                return false;
            dto = MapLabelExtraction(extracted);
            dto.ExtractionConfidence ??= 0m;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }


    internal static string? ExtractJsonObject(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return null;

        var trimmed = responseText.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = StripMarkdownJsonFence(trimmed);
        }

        if (TryParseWholeJson(trimmed, out var json))
        {
            return json;
        }

        var balancedJson = ExtractBalancedJsonObject(trimmed);
        if (balancedJson != null)
        {
            return balancedJson;
        }

        if (TryUnwrapJsonString(trimmed, out var unwrapped) && unwrapped != null)
        {
            return ExtractJsonObject(unwrapped);
        }

        return null;
    }

    private static string StripMarkdownJsonFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        trimmed = trimmed[3..].TrimStart();
        if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..].TrimStart();
        }

        return trimmed.EndsWith("```", StringComparison.Ordinal)
            ? trimmed[..^3].Trim()
            : trimmed;
    }

    private static bool TryParseWholeJson(string text, out string? json)
    {
        json = null;

        if (!text.StartsWith('{') || !text.EndsWith('}'))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                json = text;
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryUnwrapJsonString(string text, out string? unwrapped)
    {
        unwrapped = null;

        try
        {
            if (text.Length > 1 && text.StartsWith('"') && text.EndsWith('"'))
            {
                unwrapped = JsonSerializer.Deserialize<string>(text);
                return !string.IsNullOrWhiteSpace(unwrapped);
            }
        }
        catch (JsonException)
        {
            // ignored
        }

        return false;
    }

    private static string? ExtractBalancedJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = inString;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }

        return null;
    }

    internal static string DescribeException(Exception ex)
    {
        var parts = new List<string> { ex.GetType().Name };

        var statusValue = ex.GetType().GetProperty("Status")?.GetValue(ex);
        if (statusValue is not null)
        {
            parts.Add($"status={statusValue}");
        }

        var message = ex.Message.ReplaceLineEndings(" ").Trim();
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add($"message={Truncate(message, 350)}");
        }

        return string.Join("; ", parts);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private async Task<CustomFoodDto?> ParseWithAnalyzerAsync(Stream imageStream, string contentType, string analyzerId, CancellationToken ct)
    {
        var operation = await _client.AnalyzeBinaryAsync(
            WaitUntil.Completed,
            analyzerId,
            BinaryData.FromStream(imageStream),
            null, contentType, null,
            cancellationToken: ct);

        var result = operation.Value;

        if (result.Contents?.FirstOrDefault() is not DocumentContent documentContent)
        {
            return null;
        }

        return MapDocumentContentToDto(documentContent, _logger);
    }

    internal static CustomFoodDto MapDocumentContentToDto(DocumentContent documentContent, ILogger<ContentUnderstandingService>? logger = null)
    {
        var dto = new CustomFoodDto();
        decimal? maxExtractionConfidence = null;
        var hasAnyMappedField = false;

        try
        {
            if (TryGetField(documentContent.Fields, out var nameField, "ProductName", "ProductTitle", "Name", "Title"))
            {
                hasAnyMappedField = true;
                dto.Name = ExtractString(nameField.Value) ?? "";
                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(nameField));
            }

            if (TryGetField(documentContent.Fields, out var caloriesField, "CaloriesPerServing", "Calories", "Energy"))
            {
                hasAnyMappedField = true;
                dto.Calories = Utilities.ExtractNumber(ExtractString(caloriesField.Value) ?? "");
                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(caloriesField));
            }

            if (TryGetField(documentContent.Fields, out var ingredientsField, "Ingredients", "IngredientsList"))
            {
                hasAnyMappedField = true;
                dto.Ingredients = ExtractString(ingredientsField.Value) ?? "";
                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(ingredientsField));
            }

            if (TryGetField(documentContent.Fields, out var serveSizeField, "ServeSize", "ServingSize", "PortionSize", "Portion"))
            {
                hasAnyMappedField = true;
                var serveSizeStr = ExtractString(serveSizeField.Value);
                if (!string.IsNullOrWhiteSpace(serveSizeStr))
                {
                    // Use unit normalization service for better parsing
                    var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize(serveSizeStr);
                    dto.ServingSize = amount;
                    dto.ServingSizeUnit = normalizedUnit;
                }

                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(serveSizeField));
            }

            if (TryGetField(documentContent.Fields, out var manufacturerField, "ManufacturerName", "Manufacturer", "Brand", "BrandName", "MadeBy"))
            {
                hasAnyMappedField = true;
                dto.BrandName = ExtractString(manufacturerField.Value);
                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(manufacturerField));
            }
            else if (TryGetField(documentContent.Fields, out var barcodeField, "Barcode", "Upc", "Ean"))
            {
                // Fallback to barcode for BrandName if missing
                hasAnyMappedField = true;
                dto.BrandName = ExtractString(barcodeField.Value);
                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(barcodeField));
            }

            if (TryGetField(documentContent.Fields, out var barcodeValueField, "Barcode", "Upc", "Ean", "BarcodeValue"))
            {
                hasAnyMappedField = true;
                dto.Barcode = ExtractString(barcodeValueField.Value);
                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(barcodeValueField));
            }

            var fieldsJson = JsonSerializer.Serialize(documentContent.Fields);
            var flatFields = new Dictionary<string, string>();
            using (var doc = JsonDocument.Parse(fieldsJson))
            {
                FlattenJson(doc.RootElement, "", flatFields);
            }

            // Basic macronutrients
            if (TryExtractNutrientFromFlat(flatFields, out var calories, "calories", "energy"))
            {
                hasAnyMappedField = true;
                dto.Calories = calories;
            }

            if (TryExtractNutrientFromFlat(flatFields, out var protein, "protein"))
            {
                hasAnyMappedField = true;
                dto.ProteinG = protein;
            }

            if (TryExtractNutrientFromFlat(flatFields, out var fat, "totalfat", "fat"))
            {
                hasAnyMappedField = true;
                dto.FatG = fat;
            }

            if (TryExtractNutrientFromFlat(flatFields, out var carbs, "carbohydrate", "carb"))
            {
                hasAnyMappedField = true;
                dto.CarbG = carbs;
            }

            if (TryExtractNutrientFromFlat(flatFields, out var sugar, "sugar", "sugars"))
            {
                hasAnyMappedField = true;
                dto.SugarG = sugar;
            }

            if (TryExtractNutrientFromFlat(flatFields, out var fiber, "dietaryfibre", "fibre", "fiber"))
            {
                hasAnyMappedField = true;
                dto.FiberG = fiber;
            }

            if (TryExtractNutrientFromFlat(flatFields, out var sodium, "sodium"))
            {
                hasAnyMappedField = true;
                dto.SodiumMg = sodium;
            }

            // Extended macronutrients
            if (TryExtractNutrientFromFlat(flatFields, out var saturatedFat, "saturatedfat", "satfat")) { hasAnyMappedField = true; dto.SaturatedFatG = saturatedFat; }
            if (TryExtractNutrientFromFlat(flatFields, out var transFat, "transfat", "transfatty")) { hasAnyMappedField = true; dto.TransFatG = transFat; }
            if (TryExtractNutrientFromFlat(flatFields, out var cholesterol, "cholesterol")) { hasAnyMappedField = true; dto.CholesterolMg = cholesterol; }
            if (TryExtractNutrientFromFlat(flatFields, out var potassium, "potassium", "k")) { hasAnyMappedField = true; dto.PotassiumMg = potassium; }

            // Minerals
            if (TryExtractNutrientFromFlat(flatFields, out var calcium, "calcium", "ca")) { hasAnyMappedField = true; dto.CalciumMg = calcium; }
            if (TryExtractNutrientFromFlat(flatFields, out var iron, "iron", "fe")) { hasAnyMappedField = true; dto.IronMg = iron; }
            if (TryExtractNutrientFromFlat(flatFields, out var magnesium, "magnesium", "mg")) { hasAnyMappedField = true; dto.MagnesiumMg = magnesium; }
            if (TryExtractNutrientFromFlat(flatFields, out var zinc, "zinc", "zn")) { hasAnyMappedField = true; dto.ZincMg = zinc; }

            // Vitamins
            if (TryExtractNutrientFromFlat(flatFields, out var vitaminA, "vitamina", "vitamin a", "retinol")) { hasAnyMappedField = true; dto.VitaminA_IU = vitaminA; }
            if (TryExtractNutrientFromFlat(flatFields, out var vitaminC, "vitaminc", "vitamin c", "ascorbic")) { hasAnyMappedField = true; dto.VitaminC_Mg = vitaminC; }
            if (TryExtractNutrientFromFlat(flatFields, out var vitaminD, "vitamind", "vitamin d", "cholecalciferol")) { hasAnyMappedField = true; dto.VitaminD_Mcg = vitaminD; }
            if (TryExtractNutrientFromFlat(flatFields, out var vitaminB12, "vitaminb12", "vitamin b12", "cobalamin")) { hasAnyMappedField = true; dto.VitaminB12_Mcg = vitaminB12; }

            // Special nutrients
            if (TryExtractNutrientFromFlat(flatFields, out var omega3, "omega3", "omega-3", "ala", "dha", "epa")) { hasAnyMappedField = true; dto.Omega3G = omega3; }
            if (TryExtractNutrientFromFlat(flatFields, out var caffeine, "caffeine")) { hasAnyMappedField = true; dto.CaffeineMg = caffeine; }

            if (string.IsNullOrWhiteSpace(dto.Ingredients))
            {
                var ingMatch = flatFields.FirstOrDefault(k => k.Key.Contains("ingredient", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(k.Value));
                if (ingMatch.Key != null)
                {
                    hasAnyMappedField = true;
                    dto.Ingredients = ingMatch.Value;
                }
            }

            // Set extraction confidence if available
            if (TryGetField(documentContent.Fields, out var confidenceField, "Confidence", "OverallConfidence"))
            {
                var confStr = ExtractString(confidenceField.Value);
                if (decimal.TryParse(confStr, out var conf))
                {
                    dto.ExtractionConfidence = conf;
                }

                maxExtractionConfidence = MaxConfidence(maxExtractionConfidence, GetConfidence(confidenceField));
                hasAnyMappedField = true;
            }

            if (hasAnyMappedField && dto.ExtractionConfidence is null && maxExtractionConfidence is null)
            {
                dto.ExtractionConfidence = 0m;
            }

            dto.ExtractionConfidence ??= maxExtractionConfidence;
        }
        catch (Exception ex)
        {
            // Catch-all to ensure we return whatever we have mapped so far, rather than crashing whole ingestion.
            logger?.LogWarning(ex, "Nutrition label field mapping failed partway through; returning partial extraction.");
        }

        return dto;
    }

    private static bool TryExtractNutrientFromFlat(Dictionary<string, string> flatFields, out decimal value, params string[] keywords)
    {
        value = 0m;
        var matches = flatFields
            .Where(kvp => keywords.Any(k => kvp.Key.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                          (kvp.Key.EndsWith(".Value", StringComparison.OrdinalIgnoreCase) ||
                           kvp.Key.EndsWith(".content", StringComparison.OrdinalIgnoreCase) ||
                           kvp.Key.EndsWith(".valueString", StringComparison.OrdinalIgnoreCase) ||
                           kvp.Key.EndsWith(".valueNumber", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count == 0) return false;

        bool TryFindFirstValidMatch(IEnumerable<KeyValuePair<string, string>> candidates, out decimal matched)
        {
            // Prefer keys that are explicitly at a top level or clearly defined
            var ordered = candidates.OrderBy(x => x.Key.Length);
            foreach (var match in ordered)
            {
                var val = Utilities.ExtractNumber(match.Value);
                if (!string.IsNullOrWhiteSpace(match.Value))
                {
                    matched = val;
                    return true;
                }
            }

            matched = 0m;
            return false;
        }

        // 1. Try "per serve" explicitly
        var serveMatches = matches
            .Where(m => m.Key.Contains("serve", StringComparison.OrdinalIgnoreCase) ||
                        m.Key.Contains("serving", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (serveMatches.Any())
        {
            if (TryFindFirstValidMatch(serveMatches, out value)) return true;
        }

        // 2. Try matches that don't explicitly say 100g/100ml
        var non100gMatches = matches
            .Where(m => !m.Key.Contains("100g", StringComparison.OrdinalIgnoreCase) &&
                        !m.Key.Contains("100ml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (non100gMatches.Any())
        {
            if (TryFindFirstValidMatch(non100gMatches, out value)) return true;
        }

        // 3. Fallback to any valid match
        return TryFindFirstValidMatch(matches, out value);
    }

    private static decimal? GetConfidence(ContentField field)
    {
        var confidenceProperty = field.GetType().GetProperty("Confidence");
        if (confidenceProperty?.GetValue(field) is null)
        {
            return null;
        }

        var value = confidenceProperty.GetValue(field);
        return value switch
        {
            decimal d => d,
            double db => (decimal)db,
            float f => (decimal)f,
            int i => i,
            long l => l,
            _ when decimal.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? MaxConfidence(decimal? current, decimal? candidate)
        => candidate is null ? current : current is null ? candidate : Math.Max(current.Value, candidate.Value);

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> dict)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var newPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenJson(prop.Value, newPrefix, dict);
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJson(item, $"{prefix}[{i}]", dict);
                    i++;
                }
                break;
            case JsonValueKind.String:
                dict[prefix] = element.GetString() ?? "";
                break;
            case JsonValueKind.Number:
                dict[prefix] = element.GetRawText();
                break;
            case JsonValueKind.True:
                dict[prefix] = "true";
                break;
            case JsonValueKind.False:
                dict[prefix] = "false";
                break;
        }
    }

    private static bool TryGetField(IDictionary<string, ContentField> fields, out ContentField field, params string[] possibleNames)
    {
        // Exact match case-insensitive
        foreach (var name in possibleNames)
        {
            var foundKey = fields.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (foundKey != null && fields.TryGetValue(foundKey, out field!))
            {
                return true;
            }
        }

        // Normalized match (ignore spaces and underscores)
        foreach (var name in possibleNames)
        {
            var normalizedName = name.Replace(" ", "").Replace("_", "").Replace("-", "");
            var foundKey = fields.Keys.FirstOrDefault(k => string.Equals(k.Replace(" ", "").Replace("_", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase));
            if (foundKey != null && fields.TryGetValue(foundKey, out field!))
            {
                return true;
            }
        }

        field = null!;
        return false;
    }

    private static string? ExtractString(object? obj)
    {
        if (obj == null) return null;
        if (obj is JsonElement je) return ExtractStringFromJson(je);
        return obj.ToString();
    }

    private static string? ExtractStringFromJson(JsonElement je)
    {
        if (je.ValueKind == JsonValueKind.String) return je.GetString();
        if (je.ValueKind == JsonValueKind.Number) return je.GetRawText();
        if (je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("valueString", out var vs) && vs.ValueKind == JsonValueKind.String) return vs.GetString();
            if (je.TryGetProperty("valueNumber", out var vn) && vn.ValueKind == JsonValueKind.Number) return vn.GetRawText();
            if (je.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
            if (je.TryGetProperty("Value", out var v)) return ExtractStringFromJson(v);
        }
        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return null;
        return je.GetRawText().Trim('"');
    }
}

public static class Utilities
{
    public static decimal ExtractNumber(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0m;

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var style = System.Globalization.NumberStyles.Any;

        // Handle variations like "1200kJ 287Cal" or "1200 kJ 287 kcal"
        if (input.Contains("cal", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d+(?:\.\d+)?)\s*(?:k)?cal", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, style, culture, out var num))
            {
                return num;
            }
        }

        // If it explicitly says kJ and no cal, convert kJ to kcal (divide by 4.184)
        if (input.Contains("kj", StringComparison.OrdinalIgnoreCase))
        {
            var kjMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+(?:\.\d+)?)\s*kj", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (kjMatch.Success && decimal.TryParse(kjMatch.Groups[1].Value, style, culture, out var kjNum))
            {
                return Math.Round(kjNum / 4.184m, 1);
            }
        }

        // Handle IU (International Units) for vitamins
        if (input.Contains("iu", StringComparison.OrdinalIgnoreCase))
        {
            var iuMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+(?:\.\d+)?)\s*iu", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (iuMatch.Success && decimal.TryParse(iuMatch.Groups[1].Value, style, culture, out var iuNum))
            {
                return iuNum;
            }
        }

        // Handle mcg/µg (micrograms)
        if (input.Contains("mcg", StringComparison.OrdinalIgnoreCase) || input.Contains("µg"))
        {
            var mcgMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+(?:\.\d+)?)\s*(?:mcg|µg)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mcgMatch.Success && decimal.TryParse(mcgMatch.Groups[1].Value, style, culture, out var mcgNum))
            {
                return mcgNum;
            }
        }

        // Just find the first standalone number in the entire string as a generic fallback.
        var fallbackMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+(?:\.\d+)?)");
        if (fallbackMatch.Success && decimal.TryParse(fallbackMatch.Groups[1].Value, style, culture, out var result))
        {
            return result;
        }

        return 0m;
    }
}
