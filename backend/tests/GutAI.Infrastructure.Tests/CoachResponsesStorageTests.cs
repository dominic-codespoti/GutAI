using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class CoachResponsesStorageTests
{
    [Fact]
    public async Task ToolLoop_DisablesStorageAndReplaysFullHistoryWithoutResponseId()
    {
        var handler = new ResponsesHandler(
            """
            {"id":"resp_tool","object":"response","created_at":0,"status":"completed","model":"coach-deployment","output":[{"id":"rs_1","type":"reasoning","summary":[],"encrypted_content":"encrypted-reasoning-token"},{"id":"fc_item","type":"function_call","call_id":"call_1","name":"lookup","arguments":"{\"query\":\"oats\"}","status":"completed"}]}
            """,
            """
            {"id":"resp_final","object":"response","created_at":0,"status":"completed","model":"coach-deployment","output":[{"id":"msg_item","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"Found oats."}]}]}
            """);
        var client = CreateCoachClient(handler);
        var options = MealScanReasoningOptions.Create("medium", storeOutput: false);
        options.Tools = [AIFunctionFactory.Create((string query) => $"result for {query}", "lookup", "Looks up food")];

        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Find oats")], options);

        Assert.Equal("Found oats.", result.Text);
        Assert.Equal(2, handler.RequestBodies.Count);

        using var second = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.All(handler.RequestBodies, body =>
        {
            using var request = JsonDocument.Parse(body);
            Assert.False(request.RootElement.GetProperty("store").GetBoolean());
            Assert.Contains(
                request.RootElement.GetProperty("include").EnumerateArray(),
                item => item.GetString() == "reasoning.encrypted_content");
            Assert.True(request.RootElement.GetProperty("reasoning").GetProperty("effort").GetString() == "medium");
        });

        var secondRoot = second.RootElement;
        Assert.False(secondRoot.TryGetProperty("previous_response_id", out _));
        var input = secondRoot.GetProperty("input");
        Assert.Contains(input.EnumerateArray(), item =>
            item.GetProperty("type").GetString() == "message" &&
            item.GetProperty("content").ToString().Contains("Find oats", StringComparison.Ordinal));
        Assert.Contains(input.EnumerateArray(), item =>
            item.GetProperty("type").GetString() == "function_call" &&
            item.GetProperty("call_id").GetString() == "call_1");
        Assert.Contains(input.EnumerateArray(), item =>
            item.GetProperty("type").GetString() == "function_call_output" &&
            item.GetProperty("call_id").GetString() == "call_1");
        Assert.DoesNotContain(input.EnumerateArray(), item =>
            item.GetProperty("type").GetString() == "reasoning" &&
            !item.TryGetProperty("encrypted_content", out _));
    }

    [Fact]
    public async Task DefaultStorageOption_DoesNotForceStoreFalse()
    {
        var handler = new ResponsesHandler(
            """
            {"id":"resp_final","object":"response","created_at":0,"status":"completed","model":"coach-deployment","output":[{"id":"msg_item","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"Done."}]}]}
            """);
        var client = CreateCoachClient(handler);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            MealScanReasoningOptions.Create("medium"));

        Assert.Single(handler.RequestBodies);
        using var request = JsonDocument.Parse(handler.RequestBodies[0]);
        if (request.RootElement.TryGetProperty("store", out var store))
            Assert.True(store.GetBoolean());
    }

    private static IChatClient CreateCoachClient(ResponsesHandler handler)
    {
        var azure = new AzureOpenAIClient(
            new Uri("https://example.openai.azure.com/"),
            new ApiKeyCredential("test"),
            new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            });
#pragma warning disable OPENAI001
        var inner = azure.GetResponsesClient().AsIChatClient("coach-deployment");
#pragma warning restore OPENAI001
        return new ChatClientBuilder(inner).UseFunctionInvocation().Build();
    }

    private sealed class ResponsesHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (!_responses.TryDequeue(out var response))
                throw new InvalidOperationException("Unexpected extra Responses API request.");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
