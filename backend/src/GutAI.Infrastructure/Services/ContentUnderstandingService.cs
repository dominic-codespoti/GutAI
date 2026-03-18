using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace GutAI.Infrastructure.Services;

public class ContentUnderstandingService : IContentUnderstandingService
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deploymentName;

    public ContentUnderstandingService(AzureOpenAIClient client, IConfiguration config)
    {
        _client = client;
        _deploymentName = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
    }

    public async Task<CustomFoodDto?> ParseNutritionLabelAsync(Stream imageStream, string contentType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, ct);
        var base64 = Convert.ToBase64String(ms.ToArray());
        var dataUri = $"data:{contentType};base64,{base64}";

        var chatClient = _client.GetChatClient(_deploymentName);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a specialized nutrition label parser. Extract the food name, macros, serving size and ingredients into structured JSON. Infer logical zero values if not found."),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart("Extract the nutritional info from this label:"),
                ChatMessageContentPart.CreateImagePart(new Uri(dataUri))
            )
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "NutritionLabel",
                BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        { "Name", new { type = "string" } },
                        { "BrandName", new { type = new[] { "string", "null" } } },
                        { "ServingSize", new { type = "number" } },
                        { "ServingSizeUnit", new { type = "string" } },
                        { "Calories", new { type = "number" } },
                        { "ProteinG", new { type = "number" } },
                        { "CarbG", new { type = "number" } },
                        { "FatG", new { type = "number" } },
                        { "FiberG", new { type = new[] { "number", "null" } } },
                        { "SugarG", new { type = new[] { "number", "null" } } },
                        { "SodiumMg", new { type = new[] { "number", "null" } } },
                        { "Ingredients", new { type = new[] { "string", "null" } } }
                    },
                    required = new[] { "Name", "ServingSize", "ServingSizeUnit", "Calories", "ProteinG", "CarbG", "FatG" }
                }),
                "NutritionLabel details schema"
            )
        };

        var response = await chatClient.CompleteChatAsync(messages, options, ct);
        var content = response.Value.Content[0].Text;

        return JsonSerializer.Deserialize<CustomFoodDto>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
