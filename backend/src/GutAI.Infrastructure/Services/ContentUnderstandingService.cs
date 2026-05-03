using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.AI.OpenAI;
using Azure.Identity;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace GutAI.Infrastructure.Services;

public class ContentUnderstandingService : IContentUnderstandingService
{
    private readonly ContentUnderstandingClient _client;
    private readonly AzureOpenAIClient? _openAiClient;
    private readonly IConfiguration? _config;
    private readonly ILogger<ContentUnderstandingService>? _logger;

    public ContentUnderstandingService(
        ContentUnderstandingClient client,
        AzureOpenAIClient? openAiClient = null,
        IConfiguration? config = null,
        ILogger<ContentUnderstandingService>? logger = null)
    {
        _client = client;
        _openAiClient = openAiClient;
        _config = config;
        _logger = logger;
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
            if (primaryResult != null && HasMeaningfulExtraction(primaryResult))
            {
                return primaryResult;
            }
            
            if (primaryResult != null)
            {
                _logger?.LogWarning(
                    "Analyzer returned incomplete nutrition data (cal={Calories}, protein={ProteinG}, carbs={CarbG}, fat={FatG}, sodium={SodiumMg}); attempting LLM fallback.",
                    primaryResult.Calories,
                    primaryResult.ProteinG,
                    primaryResult.CarbG,
                    primaryResult.FatG,
                    primaryResult.SodiumMg);
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

        // Fallback to LLM Vision model
        if (_openAiClient != null && _config != null)
        {
            try
            {
                var modelName = _config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
                _logger?.LogInformation("LLM fallback starting with deployment {DeploymentName}.", modelName);

                // Create new stream for LLM to avoid potential position issues
                await using var llmStream = new MemoryStream();
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(llmStream, ct);
                llmStream.Position = 0;
                
                var fallbackResult = await ParseWithLlmVisionAsync(llmStream, contentType, ct);
                if (fallbackResult != null && HasMeaningfulExtraction(fallbackResult))
                {
                    _logger?.LogInformation("LLM fallback succeeded.");
                    return fallbackResult;
                }

                _logger?.LogWarning("LLM fallback returned no usable nutrition data.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "LLM fallback failed. {Details}", DescribeException(ex));
            }
        }
        else
        {
            _logger?.LogWarning("LLM fallback unavailable because Azure OpenAI is not configured.");
        }

        return null;
    }

    public async Task<CustomFoodDto?> DescribeFoodFromTextAsync(string description, CancellationToken ct)
    {
        var trimmedDescription = description?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedDescription))
        {
            return null;
        }

        if (_openAiClient == null || _config == null)
        {
            _logger?.LogWarning("Text food description is unavailable because Azure OpenAI is not configured.");
            return null;
        }

        try
        {
            var modelName = ResolveTextDeploymentName(_config);
            var chatClient = _openAiClient.GetChatClient(modelName);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("""
                    You are a nutrition estimation AI for a food logging app.
                    Convert a user's plain-language food description into a single reusable custom food entry.

                    Return one JSON object matching the schema.
                    Rules:
                    - Infer a concise food name from the description.
                    - Only include a brand when the user explicitly mentioned one.
                    - Estimate nutrition for one typical serving of the described food.
                    - Use grams for serving size when no better unit is obvious.
                    - Ingredients must be a concise comma-separated ingredient list for the described dish or product, not a transcript of the prompt.
                    - Do not invent a barcode.
                    - ExtractionConfidence must be a number between 0 and 1 representing how confident you are in the estimate.
                    - Prefer realistic, internally consistent nutrition values.
                    """),
                new UserChatMessage($"Describe this food: {trimmedDescription}")
            };

            var options = new ChatCompletionOptions
            {
                ResponseFormat = CreateFallbackResponseFormat()
            };

            var response = await chatClient.CompleteChatAsync(messages, options, ct);
            var textResponse = string.Concat(response.Value.Content?.Select(part => part.Text) ?? Enumerable.Empty<string>());

            if (!TryParseFallbackResponse(textResponse, out var dto) || dto is null)
            {
                _logger?.LogWarning("Text food description returned unparseable JSON for prompt '{Prompt}'.", Truncate(trimmedDescription, 120));
                return null;
            }

            FinalizeGeneratedFood(dto, trimmedDescription);
            return dto;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Text food description failed. {Details}", DescribeException(ex));
            return null;
        }
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

    private static bool IsRecognizedExtractionProperty(string name)
        => name.Equals("Name", StringComparison.OrdinalIgnoreCase)
           || name.Equals("BrandName", StringComparison.OrdinalIgnoreCase)
           || name.Equals("ServingSize", StringComparison.OrdinalIgnoreCase)
           || name.Equals("ServingSizeUnit", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Calories", StringComparison.OrdinalIgnoreCase)
           || name.Equals("ProteinG", StringComparison.OrdinalIgnoreCase)
           || name.Equals("CarbG", StringComparison.OrdinalIgnoreCase)
           || name.Equals("FatG", StringComparison.OrdinalIgnoreCase)
           || name.Equals("FiberG", StringComparison.OrdinalIgnoreCase)
           || name.Equals("SugarG", StringComparison.OrdinalIgnoreCase)
           || name.Equals("SodiumMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("SaturatedFatG", StringComparison.OrdinalIgnoreCase)
           || name.Equals("TransFatG", StringComparison.OrdinalIgnoreCase)
           || name.Equals("CholesterolMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("PotassiumMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("CalciumMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("IronMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("MagnesiumMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("ZincMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("VitaminA_IU", StringComparison.OrdinalIgnoreCase)
           || name.Equals("VitaminC_Mg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("VitaminD_Mcg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("VitaminB12_Mcg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Omega3G", StringComparison.OrdinalIgnoreCase)
           || name.Equals("CaffeineMg", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Ingredients", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Barcode", StringComparison.OrdinalIgnoreCase)
           || name.Equals("ExtractionConfidence", StringComparison.OrdinalIgnoreCase);

    private async Task<CustomFoodDto?> ParseWithLlmVisionAsync(Stream memoryStream, string contentType, CancellationToken ct)
    {
        var modelName = ResolveVisionDeploymentName(_config);
        var chatClient = _openAiClient!.GetChatClient(modelName);
        var imageBytes = BinaryData.FromStream(memoryStream);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("""
                You are a nutrition extraction AI. Analyze the image of a food label or product and extract the nutritional information and ingredients.
                If energy is explicitly stated in kJ without Calories, convert it to calories (kcal) by dividing by 4.184.
                Extract all available nutrients including vitamins and minerals when present.
                """),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart("Extract the nutritional label data."),
                ChatMessageContentPart.CreateImagePart(imageBytes, contentType))
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = CreateFallbackResponseFormat()
        };

        var response = await chatClient.CompleteChatAsync(messages, options, ct);
        var textResponse = string.Concat(response.Value.Content?.Select(part => part.Text) ?? Enumerable.Empty<string>());

        if (!TryParseFallbackResponse(textResponse, out var dto) || dto is null)
        {
            return null;
        }

        FinalizeGeneratedFood(dto);
        return dto;
    }

    private static ChatResponseFormat CreateFallbackResponseFormat()
        => ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: "custom_food_extraction",
            jsonSchema: BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "Name": { "type": "string" },
                "BrandName": { "type": ["string", "null"] },
                "ServingSize": { "type": "number" },
                "ServingSizeUnit": { "type": "string" },
                "Calories": { "type": "number" },
                "ProteinG": { "type": "number" },
                "CarbG": { "type": "number" },
                "FatG": { "type": "number" },
                "FiberG": { "type": ["number", "null"] },
                "SugarG": { "type": ["number", "null"] },
                "SodiumMg": { "type": ["number", "null"] },
                "SaturatedFatG": { "type": ["number", "null"] },
                "TransFatG": { "type": ["number", "null"] },
                "CholesterolMg": { "type": ["number", "null"] },
                "PotassiumMg": { "type": ["number", "null"] },
                "CalciumMg": { "type": ["number", "null"] },
                "IronMg": { "type": ["number", "null"] },
                "MagnesiumMg": { "type": ["number", "null"] },
                "ZincMg": { "type": ["number", "null"] },
                "VitaminA_IU": { "type": ["number", "null"] },
                "VitaminC_Mg": { "type": ["number", "null"] },
                "VitaminD_Mcg": { "type": ["number", "null"] },
                "VitaminB12_Mcg": { "type": ["number", "null"] },
                "Omega3G": { "type": ["number", "null"] },
                "CaffeineMg": { "type": ["number", "null"] },
                "Ingredients": { "type": ["string", "null"] },
                "Barcode": { "type": ["string", "null"] },
                "ExtractionConfidence": { "type": ["number", "null"] }
              },
              "required": ["Name", "ServingSize", "ServingSizeUnit", "Calories", "ProteinG", "CarbG", "FatG", "ExtractionConfidence"],
              "additionalProperties": false
            }
            """),
            jsonSchemaIsStrict: false);

    internal static string ResolveTextDeploymentName(IConfiguration? config)
        => config?["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

    internal static string ResolveVisionDeploymentName(IConfiguration? config)
        => config?["AzureOpenAI:VisionDeploymentName"] ?? "gpt-4o";

    internal static void FinalizeGeneratedFood(CustomFoodDto dto, string? fallbackName = null)
    {
        dto.Name = string.IsNullOrWhiteSpace(dto.Name)
            ? (fallbackName ?? string.Empty)
            : dto.Name.Trim();
        dto.BrandName = string.IsNullOrWhiteSpace(dto.BrandName) ? null : dto.BrandName.Trim();
        dto.ServingSizeUnit = string.IsNullOrWhiteSpace(dto.ServingSizeUnit) ? "g" : dto.ServingSizeUnit.Trim();
        dto.Ingredients = string.IsNullOrWhiteSpace(dto.Ingredients) ? null : dto.Ingredients.Trim();
        dto.Barcode = string.IsNullOrWhiteSpace(dto.Barcode) ? null : dto.Barcode.Trim();

        if (dto.ExtractionConfidence.HasValue)
        {
            dto.ExtractionConfidence = Math.Clamp(dto.ExtractionConfidence.Value, 0m, 1m);
        }
    }

    internal static bool TryParseFallbackResponse(string? responseText, out CustomFoodDto? dto)
    {
        dto = null;

        var jsonText = ExtractJsonObject(responseText);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.EnumerateObject().Any())
            {
                return false;
            }

            dto = JsonSerializer.Deserialize<CustomFoodDto>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto != null)
            {
                dto.ExtractionConfidence ??= 0m;
            }

            return dto != null;
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

        return MapDocumentContentToDto(documentContent);
    }

    internal static CustomFoodDto MapDocumentContentToDto(DocumentContent documentContent)
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
        catch (Exception)
        {
            // Catch-all to ensure we return whatever we have mapped so far, rather than crashing whole ingestion
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
