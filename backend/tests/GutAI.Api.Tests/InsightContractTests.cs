using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public class InsightContractTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task GetCorrelations_ReturnsArray()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/insights/correlations?from=2024-01-01&to=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetNutritionTrends_ReturnsArrayWithCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Lunch",
            items = new[] { new { foodName = "Rice", servings = 1.0, servingUnit = "cup", calories = 200.0, proteinG = 4.0, carbsG = 45.0, fatG = 0.5 } }
        });

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var response = await client.GetAsync($"/api/insights/nutrition-trends?from={today}&to={today}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);

        if (json.GetArrayLength() > 0)
        {
            var first = json[0];
            first.AssertHasStringProperty("date");
            first.AssertHasNumberProperty("calories");
            first.AssertHasNumberProperty("protein");
            first.AssertHasNumberProperty("carbs");
            first.AssertHasNumberProperty("fat");
            first.AssertHasNumberProperty("fiber");
            first.AssertHasNumberProperty("sugar");
            first.AssertHasNumberProperty("sodium");
            first.AssertHasNumberProperty("mealCount");
        }
    }

    [Fact]
    public async Task GetAdditiveExposure_ReturnsArray()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/insights/additive-exposure?from=2024-01-01&to=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        // When items exist, verify shape matches AdditiveExposure interface
        if (json.GetArrayLength() > 0)
        {
            var first = json[0];
            first.AssertHasStringProperty("additive");
            first.AssertHasStringProperty("cspiRating");
            first.AssertHasNumberProperty("count");
        }
    }

    [Fact]
    public async Task GetTriggerFoods_ReturnsArrayWithCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/insights/trigger-foods?from=2024-01-01&to=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        // When items exist, verify shape matches TriggerFood interface
        if (json.GetArrayLength() > 0)
        {
            var first = json[0];
            first.AssertHasStringProperty("food");
            first.AssertHasProperty("symptoms", JsonValueKind.Array);
            first.AssertHasNumberProperty("totalOccurrences");
            first.AssertHasNumberProperty("avgSeverity");
            first.AssertHasStringProperty("worstConfidence");
        }
    }

    [Fact]
    public async Task GetFoodDiaryAnalysis_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/insights/food-diary-analysis?from=2024-01-01&to=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasNumberProperty("totalMealsAnalyzed");
        json.AssertHasNumberProperty("totalSymptomsAnalyzed");
        json.AssertHasNumberProperty("patternsFound");
        json.AssertHasStringProperty("fromDate");
        json.AssertHasStringProperty("toDate");
        json.AssertHasProperty("patterns", JsonValueKind.Array);
        json.AssertHasProperty("timingInsights", JsonValueKind.Array);
        json.AssertHasProperty("recommendations", JsonValueKind.Array);
        json.AssertHasStringProperty("summary");
    }

    [Fact]
    public async Task GetEliminationDietStatus_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/insights/elimination-diet/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.AssertHasStringProperty("phase");
        json.AssertHasProperty("foodsToEliminate", JsonValueKind.Array);
        json.AssertHasProperty("foodsToReintroduce", JsonValueKind.Array);
        json.AssertHasProperty("safeFoods", JsonValueKind.Array);
        json.AssertHasProperty("reintroductionResults", JsonValueKind.Array);
        json.AssertHasProperty("recommendations", JsonValueKind.Array);
        json.AssertHasStringProperty("summary");
    }
}
