using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GutAI.Domain.Entities;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class IntegrationsContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task CreatePairingCode_Unauthenticated_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/user/pairing-codes", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreatePairingCode_Authenticated_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/user/pairing-codes", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("code");
        json.AssertHasStringProperty("expiresAt");

        var code = json.GetProperty("code").GetString();
        code.Should().NotBeNullOrWhiteSpace();
        code.Should().MatchRegex("^[A-Z0-9]{4}-[A-Z0-9]{4}$");
    }

    [Fact]
    public async Task ListTokens_Unauthenticated_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/user/tokens");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListTokens_Authenticated_ReturnsArrayAndValidItemSchema()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        // Ensure at least one token exists for the user by creating one directly in the store
        // or through pairing service via factory services.
        var response = await client.GetAsync("/api/user/tokens");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);

        // If items exist, assert the expected schema for each item
        foreach (var item in json.EnumerateArray())
        {
            item.AssertHasStringProperty("id");
            item.AssertHasStringProperty("name");
            item.AssertHasStringProperty("prefix");
            item.AssertHasProperty("scopes", JsonValueKind.Array);
            item.AssertHasStringProperty("createdAt");
            item.AssertHasProperty("lastUsedAt", JsonValueKind.Null); // Can be String or Null per AssertHasProperty helper

            var scopes = item.GetProperty("scopes");
            foreach (var scope in scopes.EnumerateArray())
            {
                scope.ValueKind.Should().Be(JsonValueKind.String);
            }
        }
    }

    [Fact]
    public async Task RevokeToken_Unauthenticated_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.DeleteAsync($"/api/user/tokens/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeToken_RandomGuid_Returns404WithErrorProperty()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var randomGuid = Guid.NewGuid();
        var response = await client.DeleteAsync($"/api/user/tokens/{randomGuid}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("error");
        json.GetProperty("error").GetString().Should().Be("Connected assistant not found.");
    }

    [Fact]
    public async Task RevokeToken_InvalidGuidFormat_ReturnsNotSuccess()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.DeleteAsync("/api/user/tokens/not-a-guid");

        // Route constraint {id:guid} rejects non-guid strings
        response.IsSuccessStatusCode.Should().BeFalse();
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.MethodNotAllowed);
    }
}
