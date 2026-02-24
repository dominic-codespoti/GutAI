using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class MealContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task CreateMeal_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Breakfast",
            items = new[]
            {
                new
                {
                    foodName = "Oatmeal",
                    servings = 1.0,
                    servingUnit = "bowl",
                    calories = 300.0,
                    proteinG = 10.0,
                    carbsG = 50.0,
                    fatG = 5.0
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("id");
        json.AssertHasStringProperty("mealType");
        json.AssertHasStringProperty("loggedAt");
        json.AssertHasNumberProperty("totalCalories");
        json.AssertHasNumberProperty("totalProteinG");
        json.AssertHasNumberProperty("totalCarbsG");
        json.AssertHasNumberProperty("totalFatG");
        json.AssertHasProperty("items", JsonValueKind.Array);

        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        var item = items[0];
        item.AssertHasStringProperty("id");
        item.AssertHasStringProperty("foodName");
        item.AssertHasNumberProperty("servings");
        item.AssertHasStringProperty("servingUnit");
        item.AssertHasNumberProperty("calories");
        item.AssertHasNumberProperty("proteinG");
        item.AssertHasNumberProperty("carbsG");
        item.AssertHasNumberProperty("fatG");
        item.AssertHasNumberProperty("fiberG");
        item.AssertHasNumberProperty("sugarG");
        item.AssertHasNumberProperty("sodiumMg");
    }

    [Fact]
    public async Task CreateMeal_EmptyItems_Returns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Lunch",
            items = Array.Empty<object>()
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMeal_NegativeCalories_Returns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Dinner",
            items = new[]
            {
                new { foodName = "Bad", servings = 1.0, servingUnit = "x", calories = -100.0, proteinG = 0.0, carbsG = 0.0, fatG = 0.0 }
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMealsByDate_ReturnsArray()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/meals?date=" + DateTime.UtcNow.ToString("yyyy-MM-dd"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetMeal_NotFound_Returns404()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/meals/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMeal_Roundtrip_PreservesData()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Snack",
            notes = "Test roundtrip",
            items = new[]
            {
                new
                {
                    foodName = "Banana",
                    servings = 1.0,
                    servingUnit = "piece",
                    calories = 105.0,
                    proteinG = 1.3,
                    carbsG = 27.0,
                    fatG = 0.4,
                    fiberG = 3.1,
                    sugarG = 14.4,
                    sodiumMg = 1.0
                }
            }
        });

        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var mealId = created.GetProperty("id").GetString();

        var getResp = await client.GetAsync($"/api/meals/{mealId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        fetched.GetProperty("notes").GetString().Should().Be("Test roundtrip");
        fetched.GetProperty("items")[0].GetProperty("foodName").GetString().Should().Be("Banana");
        fetched.GetProperty("items")[0].GetProperty("fiberG").GetDecimal().Should().Be(3.1m);
    }

    [Fact]
    public async Task DailySummary_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var response = await client.GetAsync($"/api/meals/daily-summary/{today}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("date");
        json.AssertHasNumberProperty("totalCalories");
        json.AssertHasNumberProperty("totalProteinG");
        json.AssertHasNumberProperty("totalCarbsG");
        json.AssertHasNumberProperty("totalFatG");
        json.AssertHasNumberProperty("totalFiberG");
        json.AssertHasNumberProperty("totalSugarG");
        json.AssertHasNumberProperty("totalSodiumMg");
        json.AssertHasNumberProperty("mealCount");
        json.AssertHasNumberProperty("calorieGoal");
    }

    [Fact]
    public async Task Export_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/meals/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("exportedAt");
        json.AssertHasStringProperty("from");
        json.AssertHasStringProperty("to");
        json.AssertHasProperty("meals", JsonValueKind.Array);
        json.AssertHasProperty("symptoms", JsonValueKind.Array);
    }

    [Fact]
    public async Task DeleteMeal_ReturnsNoContent()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Lunch",
            items = new[] { new { foodName = "Salad", servings = 1.0, servingUnit = "bowl", calories = 200.0, proteinG = 5.0, carbsG = 10.0, fatG = 8.0 } }
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var mealId = created.GetProperty("id").GetString();

        var deleteResp = await client.DeleteAsync($"/api/meals/{mealId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/meals");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
