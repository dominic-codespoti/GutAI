using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public sealed class TimezoneParityContractTests(GutAiWebFactory factory)
{
    private const string Timezone = "America/New_York";
    private const string LocalDate = "2026-08-25";
    private const string BoundaryInstant = "2026-08-26T03:30:00.0000000Z";

    [Fact]
    public async Task LocalDayBoundary_IsConsistentAcrossMealsSymptomsInsightsAndExport()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var mealResponse = await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Lunch",
            loggedAt = BoundaryInstant,
            items = new[]
            {
                new
                {
                    foodName = "Boundary meal",
                    servings = 1.0,
                    servingUnit = "plate",
                    calories = 1422.0,
                    proteinG = 97.0,
                    carbsG = 134.0,
                    fatG = 60.0,
                    fiberG = 8.0,
                },
            },
        });
        mealResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var mealList = await GetJsonAsync(client,
            $"/api/meals?date={LocalDate}&timezoneId={Timezone}");
        mealList.ValueKind.Should().Be(JsonValueKind.Array);
        mealList.GetArrayLength().Should().Be(1);

        var dailySummary = await GetJsonAsync(client,
            $"/api/meals/daily-summary/{LocalDate}?timezoneId={Timezone}");
        dailySummary.GetProperty("totalCalories").GetDecimal().Should().Be(1422m);
        dailySummary.GetProperty("mealCount").GetInt32().Should().Be(1);

        var trends = await GetJsonAsync(client,
            $"/api/insights/nutrition-trends?from={LocalDate}&to={LocalDate}&timezoneId={Timezone}");
        trends.ValueKind.Should().Be(JsonValueKind.Array);
        trends.GetArrayLength().Should().Be(1);
        trends[0].GetProperty("date").GetString().Should().Be(LocalDate);
        trends[0].GetProperty("calories").GetDecimal().Should().Be(1422m);

        var symptomTypes = await GetJsonAsync(client, "/api/symptoms/types");
        var symptomTypeId = symptomTypes[0].GetProperty("id").GetInt32();
        var symptomResponse = await client.PostAsJsonAsync("/api/symptoms", new
        {
            symptomTypeId,
            severity = 5,
            occurredAt = BoundaryInstant,
        });
        symptomResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var symptomsForDay = await GetJsonAsync(client,
            $"/api/symptoms?date={LocalDate}&timezoneId={Timezone}");
        symptomsForDay.GetArrayLength().Should().Be(1);

        var symptomHistory = await GetJsonAsync(client,
            $"/api/symptoms/history?from={LocalDate}&to={LocalDate}&timezoneId={Timezone}");
        symptomHistory.GetArrayLength().Should().Be(1);

        var export = await GetJsonAsync(client,
            $"/api/meals/export?from={LocalDate}&to={LocalDate}&timezoneId={Timezone}");
        export.GetProperty("meals").GetArrayLength().Should().Be(1);
        export.GetProperty("symptoms").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ImportMealType_UsesRequestedTimezoneForFallbackHour()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(
            $"/api/meals/import?timezoneId={Timezone}",
            new
            {
                source = "health-connect",
                items = new[]
                {
                    new
                    {
                        loggedAt = "2026-08-26T12:30:00.0000000Z",
                        name = "Imported breakfast",
                        servings = 1.0,
                        calories = 300.0,
                    },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("imported").GetInt32().Should().Be(1);
        result.GetProperty("failed").GetInt32().Should().Be(0);

        var meals = await GetJsonAsync(client,
            $"/api/meals?date=2026-08-26&timezoneId={Timezone}");
        meals.GetArrayLength().Should().Be(1);
        meals[0].GetProperty("mealType").GetString().Should().Be("Breakfast");
    }

    [Fact]
    public async Task TimezoneOnlyProfileUpdate_PreservesProfileArrays()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var initial = await client.PutAsJsonAsync("/api/user/profile", new
        {
            allergies = new[] { "peanuts" },
            dietaryPreferences = new[] { "low-fodmap" },
            gutConditions = new[] { "IBS" },
        });
        initial.StatusCode.Should().Be(HttpStatusCode.OK);

        var timezoneOnly = await client.PutAsJsonAsync("/api/user/profile", new
        {
            timezoneId = Timezone,
        });
        timezoneOnly.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await timezoneOnly.Content.ReadFromJsonAsync<JsonElement>();

        profile.GetProperty("allergies")[0].GetString().Should().Be("peanuts");
        profile.GetProperty("dietaryPreferences")[0].GetString().Should().Be("low-fodmap");
        profile.GetProperty("gutConditions")[0].GetString().Should().Be("IBS");
        profile.GetProperty("timezoneId").GetString().Should().Be(Timezone);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
