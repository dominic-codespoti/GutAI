using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class FoodContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task SearchFoodProducts_ReturnsArray()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/food/search?q=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetFoodAdditives_ReturnsArrayWithCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/food/additives");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        if (json.GetArrayLength() > 0)
        {
            var first = json[0];
            first.AssertHasNumberProperty("id");
            first.AssertHasStringProperty("eNumber");
            first.AssertHasStringProperty("name");
            first.AssertHasStringProperty("category");
            first.AssertHasStringProperty("cspiRating");
            first.AssertHasStringProperty("safetyRating");
            first.AssertHasStringProperty("usStatus");
            first.AssertHasStringProperty("euStatus");
            first.AssertHasStringProperty("healthConcerns");
            first.AssertHasProperty("bannedInCountries", JsonValueKind.Array);
            first.AssertHasStringProperty("description");
            first.AssertHasProperty("alternateNames", JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task GetFoodAdditive_NotFound_Returns404()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/food/additives/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFoodProduct_NotFound_Returns404()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/food/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateFoodProduct_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var response = await client.PostAsJsonAsync("/api/food", new
        {
            name = "Test Product"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("id");
        json.AssertHasStringProperty("name");
        json.AssertHasStringProperty("dataSource");
        json.AssertHasProperty("additives", JsonValueKind.Array);
        json.AssertHasProperty("allergensTags", JsonValueKind.Array);
        json.AssertHasProperty("additivesTags", JsonValueKind.Array);
        json.AssertHasBoolProperty("isDeleted");
    }

    [Fact]
    public async Task FoodProduct_Roundtrip_PreservesData()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new
        {
            name = "Roundtrip Food",
            brand = "TestBrand",
            ingredients = "Water, salt",
            servingSize = "100g"
        });

        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var getResp = await client.GetAsync($"/api/food/{productId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        fetched.GetProperty("name").GetString().Should().Be("Roundtrip Food");
        fetched.GetProperty("brand").GetString().Should().Be("TestBrand");
        fetched.GetProperty("ingredients").GetString().Should().Be("Water, salt");
    }

    [Fact]
    public async Task GetSafetyReport_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new { name = "Safety Test" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/api/food/{productId}/safety-report");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasProperty("product", JsonValueKind.Object);
        json.AssertHasProperty("additives", JsonValueKind.Array);
        json.AssertHasNumberProperty("safetyScore");
        json.AssertHasStringProperty("safetyRating");
        json.AssertHasNumberProperty("novaGroup");
        json.AssertHasStringProperty("nutriScore");
        json.AssertHasProperty("gutRisk", JsonValueKind.Object);
        json.AssertHasProperty("fodmap", JsonValueKind.Object);
        json.AssertHasProperty("substitutions", JsonValueKind.Object);
        json.AssertHasProperty("glycemic", JsonValueKind.Object);
    }

    [Fact]
    public async Task GetGutRisk_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new { name = "GutRisk Test" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/api/food/{productId}/gut-risk");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasNumberProperty("gutScore");
        json.AssertHasStringProperty("gutRating");
        json.AssertHasStringProperty("confidence");
        json.AssertHasNumberProperty("flagCount");
        json.AssertHasNumberProperty("highRiskCount");
        json.AssertHasNumberProperty("mediumRiskCount");
        json.AssertHasNumberProperty("lowRiskCount");
        json.AssertHasNumberProperty("doseSensitiveFlagsCount");
        json.AssertHasProperty("flags", JsonValueKind.Array);
        json.AssertHasStringProperty("summary");
    }

    [Fact]
    public async Task GetFodmap_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new { name = "Fodmap Test" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/api/food/{productId}/fodmap");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasNumberProperty("fodmapScore");
        json.AssertHasStringProperty("fodmapRating");
        json.AssertHasStringProperty("confidence");
        json.AssertHasNumberProperty("triggerCount");
        json.AssertHasNumberProperty("highCount");
        json.AssertHasNumberProperty("moderateCount");
        json.AssertHasNumberProperty("lowCount");
        json.AssertHasProperty("categories", JsonValueKind.Array);
        json.AssertHasProperty("triggers", JsonValueKind.Array);
        json.AssertHasStringProperty("summary");
    }

    [Fact]
    public async Task GetSubstitutions_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new { name = "Sub Test" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/api/food/{productId}/substitutions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("productName");
        json.AssertHasNumberProperty("suggestionCount");
        json.AssertHasProperty("suggestions", JsonValueKind.Array);
        json.AssertHasStringProperty("summary");
    }

    [Fact]
    public async Task GetGlycemic_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new { name = "Glycemic Test" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/api/food/{productId}/glycemic");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("giCategory");
        json.AssertHasStringProperty("glCategory");
        json.AssertHasNumberProperty("matchCount");
        json.AssertHasProperty("matches", JsonValueKind.Array);
        json.AssertHasStringProperty("gutImpactSummary");
        json.AssertHasProperty("recommendations", JsonValueKind.Array);
        json.AssertHasStringProperty("confidence");
    }

    [Fact]
    public async Task GetPersonalizedScore_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new { name = "Score Test" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/api/food/{productId}/personalized-score");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasNumberProperty("compositeScore");
        json.AssertHasStringProperty("rating");
        json.AssertHasNumberProperty("fodmapComponent");
        json.AssertHasNumberProperty("additiveRiskComponent");
        json.AssertHasNumberProperty("novaComponent");
        json.AssertHasProperty("explanations", JsonValueKind.Array);
        json.AssertHasProperty("personalWarnings", JsonValueKind.Array);
        json.AssertHasStringProperty("summary");
    }

    [Fact]
    public async Task DeleteFoodProduct_ReturnsNoContent()
    {
        var (client, _) = await factory.CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/food", new { name = "DeleteMe" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var productId = created.GetProperty("id").GetString();

        var deleteResp = await client.DeleteAsync($"/api/food/{productId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
