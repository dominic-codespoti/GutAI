using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class MealImportContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task ImportMeals_Unauthenticated_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/meals/import", new
        {
            source = "health-connect",
            items = new[]
            {
                new
                {
                    loggedAt = DateTime.UtcNow.ToString("O"),
                    calories = 200.0,
                    servings = 1.0
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportMeals_HappyPath_ImportsAndDerivesMealTypesAndMultipliesServings()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var targetDate = new DateOnly(2025, 5, 10);
        var breakfastTime = targetDate.ToDateTime(new TimeOnly(8, 0, 0), DateTimeKind.Utc);
        var lunchTime = targetDate.ToDateTime(new TimeOnly(13, 0, 0), DateTimeKind.Utc);

        var importResponse = await client.PostAsJsonAsync("/api/meals/import", new
        {
            source = "health-connect",
            items = new object[]
            {
                new
                {
                    loggedAt = breakfastTime.ToString("O"),
                    mealType = "Breakfast",
                    name = "Oatmeal with berries",
                    servings = 1.5,
                    calories = 100.0,
                    proteinG = 10.0,
                    carbsG = 20.0,
                    fatG = 2.0,
                    fiberG = 4.0,
                    sugarG = 6.0,
                    sodiumMg = 50.0
                },
                new
                {
                    loggedAt = lunchTime.ToString("O"),
                    // mealType omitted, 13:00 UTC should resolve to Lunch
                    name = "Chicken salad",
                    servings = 2.0,
                    calories = 250.0,
                    proteinG = 20.0,
                    carbsG = 15.0,
                    fatG = 5.0
                }
            }
        });

        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var importJson = await importResponse.Content.ReadFromJsonAsync<JsonElement>();

        importJson.AssertHasNumberProperty("imported");
        importJson.AssertHasNumberProperty("skippedDuplicates");
        importJson.AssertHasNumberProperty("failed");
        importJson.AssertHasProperty("errors", JsonValueKind.Array);

        importJson.GetProperty("imported").GetInt32().Should().Be(2);
        importJson.GetProperty("skippedDuplicates").GetInt32().Should().Be(0);
        importJson.GetProperty("failed").GetInt32().Should().Be(0);
        importJson.GetProperty("errors").GetArrayLength().Should().Be(0);

        // Verify via GET /api/meals?date=<date>&tzOffsetMinutes=0
        var getResponse = await client.GetAsync($"/api/meals?date={targetDate:yyyy-MM-dd}&tzOffsetMinutes=0");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var meals = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        meals.ValueKind.Should().Be(JsonValueKind.Array);
        meals.GetArrayLength().Should().Be(2);

        var breakfastMeal = meals[0];
        breakfastMeal.GetProperty("mealType").GetString().Should().Be("Breakfast");
        breakfastMeal.GetProperty("totalCalories").GetDecimal().Should().Be(150m); // 100 * 1.5
        breakfastMeal.GetProperty("totalProteinG").GetDecimal().Should().Be(15m);  // 10 * 1.5
        breakfastMeal.GetProperty("totalCarbsG").GetDecimal().Should().Be(30m);    // 20 * 1.5
        breakfastMeal.GetProperty("totalFatG").GetDecimal().Should().Be(3m);       // 2 * 1.5

        var breakfastItems = breakfastMeal.GetProperty("items");
        breakfastItems.GetArrayLength().Should().Be(1);
        var breakfastItem = breakfastItems[0];
        breakfastItem.GetProperty("foodName").GetString().Should().Be("Oatmeal with berries");
        breakfastItem.GetProperty("nutritionProvenance").GetString().Should().Be("Estimated");
        breakfastItem.GetProperty("fiberG").GetDecimal().Should().Be(6m);          // 4 * 1.5
        breakfastItem.GetProperty("sugarG").GetDecimal().Should().Be(9m);          // 6 * 1.5
        breakfastItem.GetProperty("sodiumMg").GetDecimal().Should().Be(75m);       // 50 * 1.5

        var lunchMeal = meals[1];
        lunchMeal.GetProperty("mealType").GetString().Should().Be("Lunch");
        lunchMeal.GetProperty("totalCalories").GetDecimal().Should().Be(500m);    // 250 * 2.0
        lunchMeal.GetProperty("totalProteinG").GetDecimal().Should().Be(40m);     // 20 * 2.0
        lunchMeal.GetProperty("totalCarbsG").GetDecimal().Should().Be(30m);       // 15 * 2.0
        lunchMeal.GetProperty("totalFatG").GetDecimal().Should().Be(10m);         // 5 * 2.0
    }

    [Theory]
    [InlineData("Bad Source!")]
    [InlineData("SourceWithUpperCase")]
    [InlineData("spaces not allowed")]
    [InlineData("source_with_underscores")]
    [InlineData("this-slug-is-way-too-long-because-it-exceeds-thirty-two-characters-in-length")]
    [InlineData("")]
    public async Task ImportMeals_InvalidSource_Returns400(string invalidSource)
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals/import", new
        {
            source = invalidSource,
            items = new[]
            {
                new
                {
                    loggedAt = DateTime.UtcNow.ToString("O"),
                    calories = 100.0,
                    servings = 1.0
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("error");
    }

    [Fact]
    public async Task ImportMeals_EmptyItems_Returns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals/import", new
        {
            source = "health-connect",
            items = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("error");
    }

    [Fact]
    public async Task ImportMeals_MoreThan2000Items_Returns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var now = DateTime.UtcNow.ToString("O");
        var items = Enumerable.Range(0, 2001).Select(i => new
        {
            loggedAt = now,
            calories = 100.0,
            servings = 1.0
        }).ToArray();

        var response = await client.PostAsJsonAsync("/api/meals/import", new
        {
            source = "health-connect",
            items
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("error");
    }

    [Fact]
    public async Task ImportMeals_IdempotentReimport_SkipsDuplicates()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var now = DateTime.UtcNow;

        var payload = new
        {
            source = "health-connect",
            items = new[]
            {
                new
                {
                    loggedAt = now.AddHours(-2).ToString("O"),
                    mealType = "Breakfast",
                    externalId = "hc-rec-1",
                    name = "Toast",
                    calories = 150.0,
                    servings = 1.0
                },
                new
                {
                    loggedAt = now.AddHours(-1).ToString("O"),
                    mealType = "Lunch",
                    externalId = "hc-rec-2",
                    name = "Soup",
                    calories = 200.0,
                    servings = 1.0
                }
            }
        };

        // First import
        var firstResponse = await client.PostAsJsonAsync("/api/meals/import", payload);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        firstJson.GetProperty("imported").GetInt32().Should().Be(2);
        firstJson.GetProperty("skippedDuplicates").GetInt32().Should().Be(0);
        firstJson.GetProperty("failed").GetInt32().Should().Be(0);

        // Second import with identical (source, externalId) items
        var secondResponse = await client.PostAsJsonAsync("/api/meals/import", payload);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondJson = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        secondJson.GetProperty("imported").GetInt32().Should().Be(0);
        secondJson.GetProperty("skippedDuplicates").GetInt32().Should().Be(2);
        secondJson.GetProperty("failed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ImportMeals_InvalidLoggedAt_CountsTowardFailedWithErrors()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/meals/import", new
        {
            source = "myfitnesspal",
            items = new object[]
            {
                new
                {
                    loggedAt = default(DateTime).ToString("O"), // default DateTime
                    calories = 100.0,
                    servings = 1.0
                },
                new
                {
                    loggedAt = DateTime.UtcNow.AddDays(5).ToString("O"), // far future > 1 day
                    calories = 200.0,
                    servings = 1.0
                },
                new
                {
                    loggedAt = DateTime.UtcNow.ToString("O"),
                    calories = 300.0,
                    servings = 1.0
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("imported").GetInt32().Should().Be(1);
        json.GetProperty("failed").GetInt32().Should().Be(2);
        json.GetProperty("skippedDuplicates").GetInt32().Should().Be(0);

        var errors = json.GetProperty("errors");
        errors.GetArrayLength().Should().Be(2);
        errors[0].GetString().Should().Contain("invalid loggedAt");
        errors[1].GetString().Should().Contain("invalid loggedAt");
    }
}
