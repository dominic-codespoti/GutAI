using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class AuthContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task Register_ReturnsCorrectShape()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"shape-{Guid.NewGuid():N}@test.com",
            password = "TestPass123",
            displayName = "Shape Test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("accessToken");
        json.AssertHasStringProperty("refreshToken");
        json.AssertHasStringProperty("expiresAt");

        var user = json.GetProperty("user");
        user.AssertHasStringProperty("id");
        user.AssertHasStringProperty("email");
        user.AssertHasStringProperty("displayName");
        user.AssertHasNumberProperty("dailyCalorieGoal");
        user.AssertHasNumberProperty("dailyProteinGoalG");
        user.AssertHasNumberProperty("dailyCarbGoalG");
        user.AssertHasNumberProperty("dailyFatGoalG");
        user.AssertHasNumberProperty("dailyFiberGoalG");
        user.AssertHasBoolProperty("onboardingCompleted");
        user.AssertHasProperty("allergies", JsonValueKind.Array);
        user.AssertHasProperty("dietaryPreferences", JsonValueKind.Array);
        user.AssertHasProperty("gutConditions", JsonValueKind.Array);
    }

    [Fact]
    public async Task Login_ReturnsCorrectShape()
    {
        var client = factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@test.com";
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "TestPass123",
            displayName = "Login Test"
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "TestPass123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("accessToken");
        json.AssertHasStringProperty("refreshToken");
        json.AssertHasStringProperty("expiresAt");
        json.GetProperty("user").AssertHasStringProperty("id");
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns422()
    {
        var client = factory.CreateClient();
        var email = $"dup-{Guid.NewGuid():N}@test.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "TestPass123", displayName = "Dup" });
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "TestPass123", displayName = "Dup2" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns422()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"weak-{Guid.NewGuid():N}@test.com",
            password = "short",
            displayName = "Weak"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns422()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "not-an-email",
            password = "TestPass123",
            displayName = "Bad"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_NullPassword_Returns422()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"nullpw-{Guid.NewGuid():N}@test.com",
            password = (string?)null,
            displayName = "NullPw"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = factory.CreateClient();
        var email = $"wrongpw-{Guid.NewGuid():N}@test.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "TestPass123", displayName = "WP" });
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPass1" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReturnsNewTokens()
    {
        var client = factory.CreateClient();
        var email = $"refresh-{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "TestPass123", displayName = "Ref" });
        var regJson = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = regJson.GetProperty("refreshToken").GetString();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("accessToken");
        json.AssertHasStringProperty("refreshToken");
        json.GetProperty("refreshToken").GetString().Should().NotBe(refreshToken);
    }
}
