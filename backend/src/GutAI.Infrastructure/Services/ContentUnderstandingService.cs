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
            var primaryResult = await ParseWithAnalyzerAsync(memoryStream, contentType, "prebuilt-documentFields", ct);

            // Validate the result: if we got at least some nutritional data, consider it successful
            if (primaryResult != null && HasNutritionalData(primaryResult))
            {
                return primaryResult;
            }
            
            _logger?.LogWarning("Azure Content Understanding returned insufficient data, attempting LLM fallback");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Azure Content Understanding failed, attempting LLM fallback");
        }

        // Fallback to LLM Vision model
        if (_openAiClient != null && _config != null)
        {
            try
            {
                // Create new stream for LLM to avoid potential position issues
                await using var llmStream = new MemoryStream();
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(llmStream, ct);
                llmStream.Position = 0;
                
                var fallbackResult = await ParseWithLlmVisionAsync(llmStream, contentType, ct);
                if (fallbackResult != null && HasNutritionalData(fallbackResult))
                {
                    return fallbackResult;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "LLM Vision fallback also failed");
            }
        }

        return null;
    }
    
    /// <summary>
    /// Validates that the extracted data contains at least some nutritional information
    /// </summary>
    private static bool HasNutritionalData(CustomFoodDto dto)
    {
        return dto.Calories > 0 || 
               dto.ProteinG > 0 || 
               dto.FatG > 0 || 
               dto.CarbG > 0 ||
               dto.SodiumMg > 0;
    }

    private async Task<CustomFoodDto?> ParseWithLlmVisionAsync(Stream memoryStream, string contentType, CancellationToken ct)
    {
        var modelName = _config?["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
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
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "nutrition_label_extraction",
                BinaryData.FromString("""
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
                    "Ingredients": { "type": ["string", "null"] }
                  },
                  "required": ["Name", "ServingSize", "ServingSizeUnit", "Calories", "ProteinG", "CarbG", "FatG"],
                  "additionalProperties": false
                }
                """),
                jsonSchemaIsStrict: true
            )
        };

        var response = await chatClient.CompleteChatAsync(messages, options, ct);
        var jsonResponse = response.Value.Content?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(jsonResponse)) return null;

        var dto = JsonSerializer.Deserialize<CustomFoodDto>(jsonResponse, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return dto;
    }

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

        try
        {
            if (TryGetField(documentContent.Fields, out var nameField, "ProductName", "ProductTitle", "Name", "Title"))
            {
                dto.Name = ExtractString(nameField.Value) ?? "";
            }

            if (TryGetField(documentContent.Fields, out var caloriesField, "CaloriesPerServing", "Calories", "Energy"))
            {
                dto.Calories = Utilities.ExtractNumber(ExtractString(caloriesField.Value) ?? "");
            }

            if (TryGetField(documentContent.Fields, out var ingredientsField, "Ingredients", "IngredientsList"))
            {
                dto.Ingredients = ExtractString(ingredientsField.Value) ?? "";
            }

            if (TryGetField(documentContent.Fields, out var serveSizeField, "ServeSize", "ServingSize", "PortionSize", "Portion"))
            {
                var serveSizeStr = ExtractString(serveSizeField.Value);
                if (!string.IsNullOrWhiteSpace(serveSizeStr))
                {
                    // Use unit normalization service for better parsing
                    var (amount, rawUnit, normalizedUnit) = UnitNormalizationService.ParseServingSize(serveSizeStr);
                    dto.ServingSize = amount;
                    dto.ServingSizeUnit = normalizedUnit;
                }
            }

            if (TryGetField(documentContent.Fields, out var manufacturerField, "ManufacturerName", "Manufacturer", "Brand", "BrandName", "MadeBy"))
            {
                dto.BrandName = ExtractString(manufacturerField.Value);
            }
            else if (TryGetField(documentContent.Fields, out var barcodeField, "Barcode", "Upc", "Ean"))
            {
                // Fallback to barcode for BrandName if missing
                dto.BrandName = ExtractString(barcodeField.Value);
            }

            if (TryGetField(documentContent.Fields, out var barcodeValueField, "Barcode", "Upc", "Ean", "BarcodeValue"))
            {
                dto.Barcode = ExtractString(barcodeValueField.Value);
            }

            var fieldsJson = JsonSerializer.Serialize(documentContent.Fields);
            var flatFields = new Dictionary<string, string>();
            using (var doc = JsonDocument.Parse(fieldsJson))
            {
                FlattenJson(doc.RootElement, "", flatFields);
            }

            // Basic macronutrients
            dto.Calories = dto.Calories > 0 ? dto.Calories : ExtractNutrientFromFlat(flatFields, "calories", "energy");
            dto.ProteinG = dto.ProteinG > 0 ? dto.ProteinG : ExtractNutrientFromFlat(flatFields, "protein");
            dto.FatG = dto.FatG > 0 ? dto.FatG : ExtractNutrientFromFlat(flatFields, "totalfat", "fat");
            dto.CarbG = dto.CarbG > 0 ? dto.CarbG : ExtractNutrientFromFlat(flatFields, "carbohydrate", "carb");
            dto.SugarG = dto.SugarG > 0 ? dto.SugarG : ExtractNutrientFromFlat(flatFields, "sugar", "sugars");
            dto.FiberG = dto.FiberG > 0 ? dto.FiberG : ExtractNutrientFromFlat(flatFields, "dietaryfibre", "fibre", "fiber");
            dto.SodiumMg = dto.SodiumMg > 0 ? dto.SodiumMg : ExtractNutrientFromFlat(flatFields, "sodium");
            
            // Extended macronutrients
            dto.SaturatedFatG = ExtractNutrientFromFlat(flatFields, "saturatedfat", "satfat");
            dto.TransFatG = ExtractNutrientFromFlat(flatFields, "transfat", "transfatty");
            dto.CholesterolMg = ExtractNutrientFromFlat(flatFields, "cholesterol");
            dto.PotassiumMg = ExtractNutrientFromFlat(flatFields, "potassium", "k");
            
            // Minerals
            dto.CalciumMg = ExtractNutrientFromFlat(flatFields, "calcium", "ca");
            dto.IronMg = ExtractNutrientFromFlat(flatFields, "iron", "fe");
            dto.MagnesiumMg = ExtractNutrientFromFlat(flatFields, "magnesium", "mg");
            dto.ZincMg = ExtractNutrientFromFlat(flatFields, "zinc", "zn");
            
            // Vitamins
            dto.VitaminA_IU = ExtractNutrientFromFlat(flatFields, "vitamina", "vitamin a", "retinol");
            dto.VitaminC_Mg = ExtractNutrientFromFlat(flatFields, "vitaminc", "vitamin c", "ascorbic");
            dto.VitaminD_Mcg = ExtractNutrientFromFlat(flatFields, "vitamind", "vitamin d", "cholecalciferol");
            dto.VitaminB12_Mcg = ExtractNutrientFromFlat(flatFields, "vitaminb12", "vitamin b12", "cobalamin");
            
            // Special nutrients
            dto.Omega3G = ExtractNutrientFromFlat(flatFields, "omega3", "omega-3", "ala", "dha", "epa");
            dto.CaffeineMg = ExtractNutrientFromFlat(flatFields, "caffeine");

            if (string.IsNullOrWhiteSpace(dto.Ingredients))
            {
                var ingMatch = flatFields.FirstOrDefault(k => k.Key.Contains("ingredient", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(k.Value));
                if (ingMatch.Key != null) dto.Ingredients = ingMatch.Value;
            }
            
            // Set extraction confidence if available
            if (TryGetField(documentContent.Fields, out var confidenceField, "Confidence", "OverallConfidence"))
            {
                var confStr = ExtractString(confidenceField.Value);
                if (decimal.TryParse(confStr, out var conf))
                {
                    dto.ExtractionConfidence = conf;
                }
            }
        }
        catch (Exception)
        {
            // Catch-all to ensure we return whatever we have mapped so far, rather than crashing whole ingestion
        }

        return dto;
    }

    private static decimal ExtractNutrientFromFlat(Dictionary<string, string> flatFields, params string[] keywords)
    {
        var matches = flatFields
            .Where(kvp => keywords.Any(k => kvp.Key.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                          (kvp.Key.EndsWith(".Value", StringComparison.OrdinalIgnoreCase) ||
                           kvp.Key.EndsWith(".content", StringComparison.OrdinalIgnoreCase) ||
                           kvp.Key.EndsWith(".valueString", StringComparison.OrdinalIgnoreCase) ||
                           kvp.Key.EndsWith(".valueNumber", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count == 0) return 0m;

        decimal FindFirstValidMatch(IEnumerable<KeyValuePair<string, string>> candidates)
        {
            // Prefer keys that are explicitly at a top level or clearly defined
            var ordered = candidates.OrderBy(x => x.Key.Length);
            foreach (var match in ordered)
            {
                var val = Utilities.ExtractNumber(match.Value);
                if (val > 0) return val;
            }
            return 0m;
        }

        // 1. Try "per serve" explicitly
        var serveMatches = matches
            .Where(m => m.Key.Contains("serve", StringComparison.OrdinalIgnoreCase) ||
                        m.Key.Contains("serving", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (serveMatches.Any())
        {
            var val = FindFirstValidMatch(serveMatches);
            if (val > 0) return val;
        }

        // 2. Try matches that don't explicitly say 100g/100ml
        var non100gMatches = matches
            .Where(m => !m.Key.Contains("100g", StringComparison.OrdinalIgnoreCase) &&
                        !m.Key.Contains("100ml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (non100gMatches.Any())
        {
            var val = FindFirstValidMatch(non100gMatches);
            if (val > 0) return val;
        }

        // 3. Fallback to any valid match
        return FindFirstValidMatch(matches);
    }

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
