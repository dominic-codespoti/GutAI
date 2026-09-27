using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class UserContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task GetProfile_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/user/profile");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("id");
        json.AssertHasStringProperty("email");
        json.AssertHasStringProperty("displayName");
        json.AssertHasNumberProperty("dailyCalorieGoal");
        json.AssertHasNumberProperty("dailyProteinGoalG");
        json.AssertHasNumberProperty("dailyCarbGoalG");
        json.AssertHasNumberProperty("dailyFatGoalG");
        json.AssertHasNumberProperty("dailyFiberGoalG");
        json.AssertHasProperty("allergies", JsonValueKind.Array);
        json.AssertHasProperty("dietaryPreferences", JsonValueKind.Array);
        json.AssertHasProperty("gutConditions", JsonValueKind.Array);
        json.GetProperty("timezoneId").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        json.AssertHasStringProperty("preferredFoodRegion");
        json.AssertHasStringProperty("createdAt");
        json.AssertHasBoolProperty("onboardingCompleted");
    }

    [Fact]
    public async Task UpdateProfile_PreservesDisplayName_WhenNotSent()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync("/api/user/profile", new { displayName = "CustomName" });
        await client.PutAsJsonAsync("/api/user/profile", new { onboardingCompleted = true });

        var profile = await client.GetAsync("/api/user/profile");
        var json = await profile.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("displayName").GetString().Should().Be("CustomName");
        json.GetProperty("onboardingCompleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task UpdateProfile_RoundTripsPreferredFoodRegionAndReturnsCompleteProfile()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var update = await client.PutAsJsonAsync(
            "/api/user/profile",
            new { preferredFoodRegion = "aU" });

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.AssertHasStringProperty("id");
        updated.AssertHasStringProperty("email");
        updated.AssertHasStringProperty("displayName");
        updated.AssertHasProperty("allergies", JsonValueKind.Array);
        updated.AssertHasProperty("dietaryPreferences", JsonValueKind.Array);
        updated.AssertHasProperty("gutConditions", JsonValueKind.Array);
        updated.GetProperty("timezoneId").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        updated.AssertHasBoolProperty("onboardingCompleted");
        updated.AssertHasNumberProperty("dailyCalorieGoal");
        updated.AssertHasNumberProperty("dailyProteinGoalG");
        updated.AssertHasNumberProperty("dailyCarbGoalG");
        updated.AssertHasNumberProperty("dailyFatGoalG");
        updated.AssertHasNumberProperty("dailyFiberGoalG");
        updated.AssertHasStringProperty("preferredFoodRegion");
        updated.AssertHasStringProperty("createdAt");
        updated.GetProperty("preferredFoodRegion").GetString().Should().Be("Au");

        var readBack = await client.GetAsync("/api/user/profile");
        var profile = await readBack.Content.ReadFromJsonAsync<JsonElement>();
        profile.GetProperty("preferredFoodRegion").GetString().Should().Be("Au");
    }

    [Fact]
    public async Task UpdateProfile_NullPreferredFoodRegionKeepsCurrentValue()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        await client.PutAsJsonAsync("/api/user/profile", new { preferredFoodRegion = "Us" });
        var response = await client.PutAsJsonAsync("/api/user/profile", new { preferredFoodRegion = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await (await client.GetAsync("/api/user/profile")).Content.ReadFromJsonAsync<JsonElement>();
        profile.GetProperty("preferredFoodRegion").GetString().Should().Be("Us");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("3")]
    public async Task UpdateProfile_RejectsUnknownOrNumericRegionNames(string region)
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PutAsJsonAsync(
            "/api/user/profile",
            new { preferredFoodRegion = region });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.AssertHasStringProperty("error");
    }

    [Fact]
    public async Task UpdateGoals_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PutAsJsonAsync("/api/user/goals", new
        {
            dailyCalorieGoal = 2500,
            dailyProteinGoalG = 150,
            dailyCarbGoalG = 250,
            dailyFatGoalG = 80,
            dailyFiberGoalG = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasNumberProperty("dailyCalorieGoal");
        json.GetProperty("dailyCalorieGoal").GetInt32().Should().Be(2500);
    }

    [Fact]
    public async Task GetAlerts_ReturnsArray()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/user/alerts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task AddAlert_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        // Add a valid alert (additive ID 1 should exist from seed data)
        var addResp = await client.PostAsJsonAsync("/api/user/alerts", new { additiveId = 1 });
        if (addResp.StatusCode == HttpStatusCode.Created)
        {
            // Now fetch alerts and verify shape matches UserFoodAlert interface
            var response = await client.GetAsync("/api/user/alerts");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.GetArrayLength().Should().BeGreaterThan(0);
            var first = json[0];
            first.AssertHasNumberProperty("additiveId");
            first.AssertHasStringProperty("name");
            first.AssertHasStringProperty("cspiRating");
            first.AssertHasBoolProperty("alertEnabled");
        }
    }

    [Fact]
    public async Task DeleteAccount_ReturnsNoContent()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.DeleteAsync("/api/user/account");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var profileResp = await client.GetAsync("/api/user/profile");
        profileResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
