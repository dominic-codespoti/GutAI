using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class SymptomContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task LogSymptom_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/symptoms", new
        {
            symptomTypeId = 1,
            severity = 5,
            notes = "Test symptom"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("id");
        json.AssertHasNumberProperty("symptomTypeId");
        json.AssertHasStringProperty("symptomName");
        json.AssertHasStringProperty("category");
        json.AssertHasStringProperty("icon");
        json.AssertHasNumberProperty("severity");
        json.AssertHasStringProperty("occurredAt");
    }

    [Fact]
    public async Task LogSymptom_InvalidSeverity_Returns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/symptoms", new
        {
            symptomTypeId = 1,
            severity = 11
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LogSymptom_InvalidType_Returns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/symptoms", new
        {
            symptomTypeId = 99999,
            severity = 3
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSymptomTypes_ReturnsArrayWithCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/symptoms/types");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        if (json.GetArrayLength() > 0)
        {
            var first = json[0];
            first.AssertHasNumberProperty("id");
            first.AssertHasStringProperty("name");
            first.AssertHasStringProperty("category");
            first.AssertHasStringProperty("icon");
        }
    }

    [Fact]
    public async Task GetSymptomsByDate_ReturnsArray()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var response = await client.GetAsync($"/api/symptoms?date={today}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetSymptomHistory_ReturnsArray()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/symptoms/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetSymptom_NotFound_Returns404()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/symptoms/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SymptomRoundtrip_PreservesData()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/symptoms", new
        {
            symptomTypeId = 1,
            severity = 7,
            notes = "Roundtrip test"
        });

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var getResp = await client.GetAsync($"/api/symptoms/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        fetched.GetProperty("severity").GetInt32().Should().Be(7);
        fetched.AssertHasStringProperty("notes");
        fetched.GetProperty("notes").GetString().Should().Be("Roundtrip test");
    }

    [Fact]
    public async Task DeleteSymptom_ReturnsNoContent()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/symptoms", new
        {
            symptomTypeId = 1,
            severity = 3
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var deleteResp = await client.DeleteAsync($"/api/symptoms/{id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
