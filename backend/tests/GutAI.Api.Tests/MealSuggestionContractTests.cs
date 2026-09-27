using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public sealed class MealSuggestionContractTests(GutAiWebFactory factory)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Status_ReportsFeatureAndServiceAvailability(bool enabled, bool expected)
    {
        var host = enabled ? EnabledHost() : DisabledHost();
        (enabled ? Service() : DisabledService()).SetBehavior(Result());
        using var client = await AuthenticatedClient(host);

        var response = await client.GetAsync("/api/meals/suggestions/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<MealSuggestionStatusDto>();
        Assert.NotNull(status);
        Assert.Equal(expected, status.Enabled);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
        Assert.Equal(expected, json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Post_WhenFeatureOff_Returns404Error()
    {
        DisabledService().SetBehavior(Result());
        using var client = await AuthenticatedClient(DisabledHost());

        var response = await client.PostAsJsonAsync("/api/meals/suggestions?timezoneId=Australia%2FSydney", new { mealType = "Lunch" });


        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("error").ValueKind);
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("Brunch", null)]
    [InlineData("Dinner", 201)]
    public async Task Post_InvalidMealTypeOrPreferences_Returns400(string mealType, int? preferenceLength)
    {
        Service().SetBehavior(Result());
        using var client = await AuthenticatedClient(EnabledHost());
        var response = await client.PostAsJsonAsync("/api/meals/suggestions", new
        {
            mealType,
            preferences = preferenceLength is null ? null : new string('x', preferenceLength.Value)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task Post_WhenEnabledButServiceUnavailable_Returns503Error()
    {
        using var client = await AuthenticatedClient(UnavailableHost());

        var response = await client.PostAsJsonAsync("/api/meals/suggestions", new { mealType = "Lunch" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("error").ValueKind);
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Post_WhenServiceRejectsRequest_Returns400Error()
    {
        Service().SetBehavior(Result(), throwArgumentException: true);
        using var client = await AuthenticatedClient(EnabledHost());

        var response = await client.PostAsJsonAsync("/api/meals/suggestions", new { mealType = "Lunch" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Service rejected input", json.RootElement.GetProperty("error").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Post_ReturnsEverySuggestionBudgetAndNestedDraftField_AndPassesTimezone()
    {
        var expected = Result();
        var fake = Service();
        fake.SetBehavior(expected);
        using var client = await AuthenticatedClient(EnabledHost());
        var response = await client.PostAsJsonAsync("/api/meals/suggestions?timezoneId=Australia%2FSydney",
            new MealSuggestionRequest { MealType = "Lunch", Preferences = "vegetarian" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actualJson = await response.Content.ReadAsStringAsync();
        var expectedJson = JsonSerializer.Serialize(expected, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), JsonNode.Parse(actualJson)), actualJson);
        Assert.Equal("Australia/Sydney", fake.TimezoneId);
        Assert.Equal("Lunch", fake.Request?.MealType);
        Assert.Equal("vegetarian", fake.Request?.Preferences);
        Assert.NotEqual(Guid.Empty, fake.UserId);
    }

    private FakeSuggestionService Service() =>
        (FakeSuggestionService)EnabledHost().Services.GetRequiredService<IMealSuggestionService>();

    private FakeSuggestionService DisabledService() =>
        (FakeSuggestionService)DisabledHost().Services.GetRequiredService<IMealSuggestionService>();

    private WebApplicationFactory<Program> EnabledHost() =>
        factory.DerivedHost("meal-suggestions-enabled", builder =>
        {
            builder.UseSetting("Features:MealSuggestions", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMealSuggestionService>();
                services.AddSingleton<IMealSuggestionService>(new FakeSuggestionService());
            });
        });

    private WebApplicationFactory<Program> DisabledHost() =>
        factory.DerivedHost("meal-suggestions-disabled", builder =>
        {
            builder.UseSetting("Features:MealSuggestions", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMealSuggestionService>();
                services.AddSingleton<IMealSuggestionService>(new FakeSuggestionService());
            });
        });

    private WebApplicationFactory<Program> UnavailableHost() =>
        factory.DerivedHost("meal-suggestions-unavailable", builder =>
        {
            builder.UseSetting("Features:MealSuggestions", "true");
            builder.ConfigureTestServices(services => services.RemoveAll<IMealSuggestionService>());
        });

    private static async Task<HttpClient> AuthenticatedClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"suggestion-{Guid.NewGuid():N}@test.com",
            password = "TestPass123",
            displayName = "Suggestion Test"
        });
        registration.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static MealSuggestionResultDto Result()
    {
        var itemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var draft = new MealDraftDto
        {
            DraftId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Origin = MealDraftOrigins.Suggestion,
            Status = MealDraftStatuses.PendingReview,
            MealType = "Lunch",
            LoggedAt = null,
            Items = [new MealDraftItemDto
            {
                ItemId = itemId,
                Name = "Brown rice",
                CanonicalName = "Brown rice",
                FoodProductId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Source = "usda",
                SourceUrl = null,
                Grams = 150,
                PortionLowGrams = 120,
                PortionHighGrams = 180,
                PortionMethod = "suggestion_grams",
                ServingHintUnit = null,
                ServingHintUnitPlural = null,
                ServingHintUnitGrams = null,
                PortionConfidence = 1,
                IsGarnish = false,
                IsInferred = false,
                IncludedByDefault = true,
                NeedsChoice = false,
                Per100g = new NutritionPer100gDto { CaloriesKcal = 123, ProteinG = 2.7m, CarbsG = 25.6m, FatG = 1m, FiberG = 1.6m },
                NutritionProvenance = "Sourced",
                Calories = 184.5m,
                ProteinG = 4.1m,
                CarbsG = 38.4m,
                FatG = 1.5m,
                FiberG = 2.4m,
                SugarG = 0.3m,
                SodiumMg = 3,
                MatchConfidence = 0.99m,
                VisionConfidence = null,
                CandidateNames = ["Brown rice"],
                Grounding = null,
                FodmapStatus = "NoKnownTriggersDetected",
                FodmapTriggers = [],
                GutRating = "Good"
            }],
            Warnings = ["Review portions before saving."],
            ReferenceObjectVisible = false,
            OverallConfidence = 0.9m,
            Totals = new MealDraftTotalsDto { Calories = 184.5m, ProteinG = 4.1m, CarbsG = 38.4m, FatG = 1.5m, ItemsWithoutNutrition = 0 },
            CreatedAt = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 9, 25, 11, 0, 0, TimeSpan.Zero)
        };
        var goals = new NutritionTargetsDto { Calories = 2000, ProteinG = 100, CarbsG = 250, FatG = 70, FiberG = 30 };
        var consumed = new NutritionTargetsDto { Calories = 1100, ProteinG = 60, CarbsG = 120, FatG = 40, FiberG = 12 };
        return new MealSuggestionResultDto
        {
            Budget = new NutritionBudgetDto
            {
                Date = new DateOnly(2026, 9, 25),
                Goals = goals,
                Consumed = consumed,
                Remaining = new NutritionTargetsDto { Calories = 900, ProteinG = 40, CarbsG = 130, FatG = 30, FiberG = 18 },
                MealCount = 2,
                ItemsWithoutNutrition = 1,
                MealType = "Lunch",
                MealTarget = new NutritionTargetsDto { Calories = 450, ProteinG = 20, CarbsG = 65, FatG = 15, FiberG = 9 }
            },
            Suggestions = [new MealSuggestionDto { Title = "Brown rice bowl", Rationale = "Uses a verified food and fits your remaining budget.", Draft = draft }],
            PromptVersion = "2026-09-25.test",
            RejectedCount = 1
        };
    }

    private sealed class FakeSuggestionService : IMealSuggestionService
    {
        private MealSuggestionResultDto _result = null!;
        private bool _throwArgumentException;

        public Guid UserId { get; private set; }
        public string? TimezoneId { get; private set; }
        public MealSuggestionRequest? Request { get; private set; }

        public void SetBehavior(MealSuggestionResultDto result, bool throwArgumentException = false)
        {
            _result = result;
            _throwArgumentException = throwArgumentException;
            UserId = Guid.Empty;
            TimezoneId = null;
            Request = null;
        }

        public Task<MealSuggestionResultDto> SuggestAsync(Guid userId, MealSuggestionRequest request, string? timezoneId = null, CancellationToken ct = default)
        {
            if (_throwArgumentException) throw new ArgumentException("Service rejected input");
            UserId = userId;
            Request = request;
            TimezoneId = timezoneId;
            return Task.FromResult(_result);
        }
    }
}
