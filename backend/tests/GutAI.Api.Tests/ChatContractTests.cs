using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class ChatContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task GetHistory_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/chat/history?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var msg in json.EnumerateArray())
        {
            msg.AssertHasStringProperty("id");
            msg.AssertHasStringProperty("role");
            msg.AssertHasStringProperty("content");
            msg.AssertHasStringProperty("createdAt");
        }
    }

    [Fact]
    public async Task ClearHistory_Returns204()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.DeleteAsync("/api/chat/history");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Stream_OversizedTimezoneId_Returns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            message = "How's my nutrition today?",
            timezoneId = new string('x', 101),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHistory_Unauthenticated_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/chat/history");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ClearHistory_Unauthenticated_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.DeleteAsync("/api/chat/history");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
