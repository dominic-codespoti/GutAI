using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public sealed class LegacyMealScanCompatTests(GutAiWebFactory factory)
{
    private const string StubHostKey = "legacy-meal-scan-compat-stub";

    private static NutritionPer100gDto Basis(decimal calories, decimal protein = 10, decimal carbs = 20, decimal fat = 5) => new()
    {
        CaloriesKcal = calories,
        ProteinG = protein,
        CarbsG = carbs,
        FatG = fat,
        FiberG = 3,
        SugarG = 4,
        SodiumMg = 50
    };

    private static MealDraftItemDto Item(string name, decimal grams, NutritionPer100gDto? basis, Guid? productId = null) => new()
    {
        ItemId = Guid.NewGuid(),
        Name = name,
        Source = productId is null ? "web" : "usda",
        FoodProductId = productId,
        Grams = grams,
        Per100g = basis,
        NutritionProvenance = basis is null ? "Unknown" : productId is null ? "Web" : "Sourced",
        MatchConfidence = .9m
    };

    private async Task<(HttpClient Client, Guid UserId, IServiceProvider Services)> CreateClient(bool stubScan = false)
    {
        if (stubScan)
        {
            var (stubClient, stubUserId, stubServices, _) = await factory.CreateAuthenticatedRealStoreClientAsync(
                StubHostKey,
                builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMealScanService>();
                    services.AddSingleton<IMealScanService>(new LegacyScanStub());
                }));
            return (stubClient, stubUserId, stubServices);
        }

        var (client, userId, services, _) = await factory.CreateAuthenticatedRealStoreClientAsync();
        return (client, userId, services);
    }

    [Fact]
    public async Task Upload_ReturnsLegacyScanShapeWithSessionId()
    {
        var (client, _, _) = await CreateClient(stubScan: true);
        using var form = new MultipartFormDataContent();
        using var png = new MemoryStream();
        using (var pixel = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(8, 8))
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(pixel, png);
        var image = new ByteArrayContent(png.ToArray());
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "meal.png");

        var response = await client.PostAsync("/api/meals/scan/image", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(LegacyScanStub.DraftId, root.GetProperty("scanSessionId").GetGuid());
        foreach (var field in new[] { "draftId", "origin", "status", "mealType", "loggedAt", "items", "warnings", "referenceObjectVisible", "overallConfidence", "totals", "createdAt", "expiresAt" })
            Assert.True(root.TryGetProperty(field, out _), $"Expected legacy-compatible draft field '{field}'.");
        var item = root.GetProperty("items")[0];
        foreach (var field in new[] { "itemId", "name", "grams", "foodProductId", "source", "sourceUrl", "matchConfidence", "visionConfidence", "calories", "proteinG", "carbsG", "fatG", "fiberG", "sugarG", "sodiumMg" })
            Assert.True(item.TryGetProperty(field, out _), $"Expected legacy-compatible item field '{field}'.");
        root.AssertHasStringProperty("scanSessionId");
        root.AssertHasStringProperty("mealType");
        root.AssertHasProperty("items", JsonValueKind.Array);
        item.AssertHasStringProperty("name");
        item.AssertHasNumberProperty("grams");
        item.AssertHasNumberProperty("calories");
    }

    [Fact]
    public async Task Get_ReturnsLegacyShapeForPhotoDraftAndUnknownIsNotFound()
    {
        var (client, userId, services) = await CreateClient();
        var draft = await services.GetRequiredService<IMealDraftService>().CreateAsync(userId, new MealDraftCreateRequest
        {
            Origin = MealDraftOrigins.Photo,
            Items = [Item("rice", 100, Basis(130))]
        });

        var response = await client.GetAsync($"/api/meals/scan/{draft.DraftId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(draft.DraftId, json.RootElement.GetProperty("scanSessionId").GetGuid());
        json.RootElement.AssertHasStringProperty("draftId");
        json.RootElement.AssertHasProperty("items", JsonValueKind.Array);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/meals/scan/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Confirm_TranslatesLegacyChoicesAndRecomputesNutrition()
    {
        var (client, userId, services) = await CreateClient();
        var store = services.GetRequiredService<ITableStore>();
        var ordinaryProduct = Product("Catalog base", 200m, 10m, 30m, 8m);
        var candidateProduct = Product("Candidate choice", 300m, 15m, 40m, 12m);
        await store.UpsertFoodProductAsync(ordinaryProduct);
        await store.UpsertFoodProductAsync(candidateProduct);

        var ordinary = Item(ordinaryProduct.Name, 80m, Basis(200m, 10m, 30m, 8m), ordinaryProduct.Id);
        var picked = Item("Ambiguous item", 50m, null) with
        {
            NeedsChoice = true,
            Grounding = new GroundingAttemptDto
            {
                Query = "ambiguous item",
                ResolutionStatus = "ambiguous",
                AutoSelected = false,
                MatchConfidence = .5m,
                Method = "resolve_async",
                Candidates = [new GroundingCandidateDto(candidateProduct.Name, candidateProduct.Id, "usda", .8m, Calories100g: 300m, Protein100g: 15m, Carbs100g: 40m, Fat100g: 12m, CandidateKey: "candidate-picked")]
            }
        };
        var noMatch = Item("Unmatched item", 20m, null) with { NeedsChoice = true };
        var loggedAt = DateTimeOffset.UtcNow;
        var draft = await services.GetRequiredService<IMealDraftService>().CreateAsync(userId, new MealDraftCreateRequest
        {
            Origin = MealDraftOrigins.Photo,
            MealType = "Lunch",
            Items = [ordinary, picked, noMatch]
        });
        var oldBody = new
        {
            mealType = "Dinner",
            loggedAt,
            items = new[]
            {
                LegacyItem(draft.Items[0], draft.Items[0].Grams * 2, ordinaryProduct.Id, calories: 99999m),
                LegacyItem(draft.Items[1], draft.Items[1].Grams, candidateProduct.Id, calories: 99999m),
                LegacyItem(draft.Items[2], draft.Items[2].Grams, null, calories: 99999m)
            }
        };
        var response = await client.PutAsJsonAsync($"/api/meals/scan/{draft.DraftId}/confirm", oldBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        result.RootElement.AssertHasStringProperty("mealId");
        var mealId = result.RootElement.GetProperty("mealId").GetGuid();
        var loggedDate = DateOnly.FromDateTime(loggedAt.UtcDateTime);
        var mealsResponse = await client.GetAsync($"/api/meals?date={loggedDate:yyyy-MM-dd}&tzOffsetMinutes=0");
        Assert.Equal(HttpStatusCode.OK, mealsResponse.StatusCode);
        using var meals = JsonDocument.Parse(await mealsResponse.Content.ReadAsStringAsync());
        var meal = Assert.Single(meals.RootElement.EnumerateArray(), value => value.GetProperty("id").GetGuid() == mealId);
        var mealItems = meal.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, mealItems.Length);
        var baseSaved = Assert.Single(mealItems, value => ProductId(value) == ordinaryProduct.Id);
        AssertNutrition(baseSaved, ordinaryProduct, 160m, "Sourced");
        var pickedSaved = Assert.Single(mealItems, value => ProductId(value) == candidateProduct.Id);
        AssertNutrition(pickedSaved, candidateProduct, 50m, "Sourced");
        var unknownSaved = Assert.Single(mealItems, value => ProductId(value) is null);
        Assert.Equal("Unknown", unknownSaved.GetProperty("nutritionProvenance").GetString());
        Assert.Equal(470m, meal.GetProperty("totalCalories").GetDecimal());

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/meals/scan/{draft.DraftId}/confirm", oldBody)).StatusCode);
        var repeatDate = DateOnly.FromDateTime(loggedAt.UtcDateTime);
        using var afterRepeat = JsonDocument.Parse(await (await client.GetAsync($"/api/meals?date={repeatDate:yyyy-MM-dd}&tzOffsetMinutes=0")).Content.ReadAsStringAsync());
        var persistedMeals = afterRepeat.RootElement.EnumerateArray().ToArray();
        Assert.Single(persistedMeals);
        Assert.Equal(mealId, persistedMeals[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Delete_ReturnsNoContentAndRemovesPendingScan()
    {
        var (client, userId, services) = await CreateClient();
        var draft = await services.GetRequiredService<IMealDraftService>().CreateAsync(userId, new MealDraftCreateRequest
        {
            Origin = MealDraftOrigins.Photo,
            Items = [Item("rice", 100m, Basis(130m))]
        });

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/meals/scan/{draft.DraftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/meals/scan/{draft.DraftId}")).StatusCode);
    }

    private static FoodProduct Product(string name, decimal calories, decimal protein, decimal carbs, decimal fat) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{name} {Guid.NewGuid():N}",
        Calories100g = calories,
        Protein100g = protein,
        Carbs100g = carbs,
        Fat100g = fat,
        Fiber100g = 3m,
        Sugar100g = 4m,
        SodiumMg100g = 50m
    };

    private static object LegacyItem(MealDraftItemDto item, decimal grams, Guid? productId, decimal calories) => new
    {
        itemId = item.ItemId,
        name = item.Name,
        grams,
        foodProductId = productId,
        source = item.Source,
        sourceUrl = item.SourceUrl,
        matchConfidence = item.MatchConfidence,
        visionConfidence = item.VisionConfidence,
        calories,
        proteinG = 999m,
        carbsG = 999m,
        fatG = 999m,
        fiberG = 999m,
        sugarG = 999m,
        sodiumMg = 999m
    };

    private static Guid? ProductId(JsonElement item) =>
        item.GetProperty("foodProductId") is { ValueKind: JsonValueKind.String } id ? id.GetGuid() : null;

    private static void AssertNutrition(JsonElement item, FoodProduct product, decimal grams, string provenance)
    {
        Assert.Equal(product.Calories100g!.Value * grams / 100m, item.GetProperty("calories").GetDecimal());
        Assert.Equal(product.Protein100g!.Value * grams / 100m, item.GetProperty("proteinG").GetDecimal());
        Assert.Equal(product.Carbs100g!.Value * grams / 100m, item.GetProperty("carbsG").GetDecimal());
        Assert.Equal(product.Fat100g!.Value * grams / 100m, item.GetProperty("fatG").GetDecimal());
        Assert.Equal(provenance, item.GetProperty("nutritionProvenance").GetString());
        Assert.NotEqual(99999m, item.GetProperty("calories").GetDecimal());
    }

    private sealed class LegacyScanStub : IMealScanService
    {
        public static readonly Guid DraftId = Guid.Parse("17a5aeb5-f16d-4300-9aed-5fae216fe33a");

        public Task<MealDraftDto> ScanMealImageAsync(Guid userId, Stream imageStream, string contentType, string? note = null, CancellationToken ct = default)
        {
            var item = Item("stub meal", 100m, Basis(125m)) with { VisionConfidence = .91m, Calories = 125m };
            return Task.FromResult(new MealDraftDto
            {
                DraftId = DraftId,
                Origin = MealDraftOrigins.Photo,
                Status = MealDraftStatuses.PendingReview,
                MealType = "Snack",
                Items = [item],
                Warnings = [],
                ReferenceObjectVisible = true,
                OverallConfidence = .9m,
                Totals = new MealDraftTotalsDto { Calories = 125m, ProteinG = 10m, CarbsG = 20m, FatG = 5m },
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        }
    }
}
