using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GutAI.Domain.Entities;


namespace GutAI.Api.Tests;

[Collection("WebApi")]
public sealed class MealDraftContractTests(GutAiWebFactory factory)
{
    private static NutritionPer100gDto Basis(decimal kcal) => new() { CaloriesKcal = kcal, ProteinG = 10, CarbsG = 20, FatG = 5 };
    private static MealDraftItemDto Item(string name, decimal kcal, bool included = true) => new()
    {
        ItemId = Guid.NewGuid(),
        Name = name,
        Source = "usda",
        Grams = 100,
        Per100g = Basis(kcal),
        MatchConfidence = .9m,
        IncludedByDefault = included
    };

    private static MealDraftItemDto NeedsChoiceItem(string name) => Item(name, 900) with
    {
        NeedsChoice = true,
        Grounding = new GroundingAttemptDto
        {
            Query = name,
            ResolutionStatus = "ambiguous",
            AutoSelected = false,
            MatchConfidence = .5m,
            Method = "resolve_async",
            Candidates = [new GroundingCandidateDto("candidate food", Guid.NewGuid(), "usda", .8m, Calories100g: 200, Protein100g: 10, Carbs100g: 20, Fat100g: 5, CandidateKey: "previewed-candidate")]
        }
    };

    private async Task<(HttpClient Client, Guid UserId, IServiceProvider Services, MealDraftDto Draft)> CreateDraft(params MealDraftItemDto[] items)
    {
        var (client, userId, services, _) = await factory.CreateAuthenticatedRealStoreClientAsync();
        var service = services.GetRequiredService<IMealDraftService>();
        var draft = await service.CreateAsync(userId, new MealDraftCreateRequest { Origin = "photo", MealType = "Lunch", Items = items, OverallConfidence = .9m });
        return (client, userId, services, draft);
    }

    [Fact]
    public async Task ListAndGet_ReturnDraftContractShape()
    {
        var (client, _, _, draft) = await CreateDraft(Item("rice", 130));
        var listResponse = await client.GetAsync("/api/meals/drafts");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, list.RootElement.ValueKind);
        Assert.Equal(draft.DraftId, list.RootElement[0].GetProperty("draftId").GetGuid());

        var response = await client.GetAsync($"/api/meals/drafts/{draft.DraftId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.AssertHasStringProperty("origin");
        root.AssertHasStringProperty("status");
        root.AssertHasStringProperty("createdAt");
        root.AssertHasStringProperty("expiresAt");
        Assert.Equal(draft.DraftId, root.GetProperty("draftId").GetGuid());
        var item = root.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Object, item.GetProperty("per100g").ValueKind);
        item.AssertHasStringProperty("nutritionProvenance");
        var totals = root.GetProperty("totals");
        foreach (var field in new[] { "calories", "proteinG", "carbsG", "fatG", "itemsWithoutNutrition" }) totals.AssertHasNumberProperty(field);
    }

    [Fact]
    public async Task Commit_RecomputesCaloriesAndReturnsMealResult()
    {
        var (client, _, _, draft) = await CreateDraft(Item("chicken", 165), Item("rice", 130), Item("sauce", 120));
        var request = new MealDraftCommitRequest { Items = draft.Items.Select(i => new MealDraftCommitItem { ItemId = i.ItemId, Grams = i.Grams * 2 }).ToList() };
        var response = await client.PutAsJsonAsync($"/api/meals/drafts/{draft.DraftId}/commit", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.AssertHasStringProperty("mealId");
        foreach (var field in new[] { "totalCalories", "totalProteinG", "totalCarbsG", "totalFatG", "itemCount", "itemsWithoutNutrition" }) body.RootElement.AssertHasNumberProperty(field);
        Assert.Equal(830m, body.RootElement.GetProperty("totalCalories").GetDecimal());
        var mealId = body.RootElement.GetProperty("mealId").GetGuid();
        var mealResponse = await client.GetAsync($"/api/meals/{mealId}");
        Assert.Equal(HttpStatusCode.OK, mealResponse.StatusCode);
        using var meal = JsonDocument.Parse(await mealResponse.Content.ReadAsStringAsync());
        Assert.Equal(830m, meal.RootElement.GetProperty("totalCalories").GetDecimal());
    }

    [Fact]
    public async Task Commit_ScanShapedCatalogAndWebItemsRecomputeEditedGramsAndKeepProvenance()
    {
        var (client, userId, services, _) = await factory.CreateAuthenticatedRealStoreClientAsync();
        var store = services.GetRequiredService<ITableStore>();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Scanned catalog {Guid.NewGuid():N}",
            Calories100g = 200m,
            Protein100g = 10m,
            Carbs100g = 30m,
            Fat100g = 8m,
            Fiber100g = 4m,
            Sugar100g = 6m,
            SodiumMg100g = 50m
        };
        await store.UpsertFoodProductAsync(product);
        var loggedAt = DateTimeOffset.UtcNow;
        var catalogBasis = new NutritionPer100gDto
        {
            CaloriesKcal = product.Calories100g!.Value,
            ProteinG = product.Protein100g!.Value,
            CarbsG = product.Carbs100g!.Value,
            FatG = product.Fat100g!.Value,
            FiberG = product.Fiber100g,
            SugarG = product.Sugar100g,
            SodiumMg = product.SodiumMg100g
        };
        var webBasis = new NutritionPer100gDto { CaloriesKcal = 150m, ProteinG = 7m, CarbsG = 18m, FatG = 4m, FiberG = 2m, SugarG = 3m, SodiumMg = 20m };
        var draft = await services.GetRequiredService<IMealDraftService>().CreateAsync(userId, new MealDraftCreateRequest
        {
            Origin = "photo",
            MealType = "Lunch",
            LoggedAt = loggedAt,
            Items =
            [
                new MealDraftItemDto { ItemId = Guid.NewGuid(), Name = product.Name, Source = "db", FoodProductId = product.Id, Grams = 100m, Per100g = catalogBasis, NutritionProvenance = "Sourced", MatchConfidence = .95m },
                new MealDraftItemDto { ItemId = Guid.NewGuid(), Name = "Web-researched item", Source = "web", Grams = 100m, Per100g = webBasis, NutritionProvenance = "Web", MatchConfidence = .75m }
            ]
        });

        var commit = await client.PutAsJsonAsync($"/api/meals/drafts/{draft.DraftId}/commit", new MealDraftCommitRequest
        {
            Items = draft.Items.Select(i => new MealDraftCommitItem { ItemId = i.ItemId, Grams = i.Grams * 2 }).ToList()
        });
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        var commitBody = JsonDocument.Parse(await commit.Content.ReadAsStringAsync()).RootElement;
        var date = DateOnly.FromDateTime(loggedAt.UtcDateTime);
        var mealsResponse = await client.GetAsync($"/api/meals?date={date:yyyy-MM-dd}&tzOffsetMinutes=0");
        Assert.Equal(HttpStatusCode.OK, mealsResponse.StatusCode);
        using var mealsJson = JsonDocument.Parse(await mealsResponse.Content.ReadAsStringAsync());
        var meal = Assert.Single(mealsJson.RootElement.EnumerateArray(), m => m.GetProperty("id").GetGuid() == commitBody.GetProperty("mealId").GetGuid());
        var catalogItem = Assert.Single(meal.GetProperty("items").EnumerateArray(), i => i.GetProperty("foodProductId").ValueKind == JsonValueKind.String && i.GetProperty("foodProductId").GetGuid() == product.Id);
        AssertItemNutrition(catalogItem, catalogBasis, 200m, "Sourced");
        var webItem = Assert.Single(meal.GetProperty("items").EnumerateArray(), i => i.GetProperty("foodProductId").ValueKind == JsonValueKind.Null);
        AssertItemNutrition(webItem, webBasis, 200m, "Web");
    }

    [Fact]
    public async Task Commit_PersistsProvenanceForEachDraftSourceAndCountsUnknownNutrition()
    {
        var items = new[]
        {
            Item("catalog", 200) with { Source = "db", NutritionProvenance = "Sourced" },
            Item("web", 150) with { Source = "web", NutritionProvenance = "Web" },
            Item("model estimate", 120) with { Source = "ai", NutritionProvenance = "ModelEstimated" },
            new MealDraftItemDto { ItemId = Guid.NewGuid(), Name = "unknown", Source = "ai", Grams = 50m, MatchConfidence = 0m }
        };
        // Draft normalization cannot preserve UserEntered: with a basis it assigns provenance from Source,
        // and without a basis it forces Unknown unless the incoming provenance is an estimate-like value.
        var (client, _, _, draft) = await CreateDraft(items);
        var loggedAt = DateTimeOffset.UtcNow;
        var update = await client.PutAsJsonAsync($"/api/meals/drafts/{draft.DraftId}", new MealDraftUpdateRequest
        {
            LoggedAt = loggedAt,
            Items = draft.Items.Select(i => new MealDraftCommitItem { ItemId = i.ItemId, Grams = i.Grams }).ToList()
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var commit = await client.PutAsJsonAsync($"/api/meals/drafts/{draft.DraftId}/commit", new MealDraftCommitRequest
        {
            LoggedAt = loggedAt,
            Items = draft.Items.Select(i => new MealDraftCommitItem { ItemId = i.ItemId, Grams = i.Grams, LogWithoutCalories = i.Name == "unknown" }).ToList()
        });
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        using var commitBody = JsonDocument.Parse(await commit.Content.ReadAsStringAsync());
        var mealId = commitBody.RootElement.GetProperty("mealId").GetGuid();
        var mealsResponse = await client.GetAsync($"/api/meals?date={DateOnly.FromDateTime(loggedAt.UtcDateTime):yyyy-MM-dd}&tzOffsetMinutes=0");
        Assert.Equal(HttpStatusCode.OK, mealsResponse.StatusCode);
        using var meals = JsonDocument.Parse(await mealsResponse.Content.ReadAsStringAsync());
        var meal = Assert.Single(meals.RootElement.EnumerateArray(), m => m.GetProperty("id").GetGuid() == mealId);
        var persistedItems = meal.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("foodName").GetString()!);
        Assert.Equal(4, persistedItems.Count);
        var provenanceCases = new[]
        {
            ("Catalog", "Sourced"),
            ("Web", "Web"),
            ("Model Estimate", "ModelEstimated")
        };
        foreach (var (name, provenance) in provenanceCases)
            Assert.Equal(provenance, persistedItems[name].GetProperty("nutritionProvenance").GetString());
        // UserEntered is not representable by draft normalization; provenance derives from Source for items with a basis.
        var unknown = persistedItems["Unknown"];
        Assert.Equal("Unknown", unknown.GetProperty("nutritionProvenance").GetString());
        Assert.Equal(0m, unknown.GetProperty("calories").GetDecimal());
        Assert.Equal(0m, unknown.GetProperty("proteinG").GetDecimal());
        Assert.Equal(0m, unknown.GetProperty("carbsG").GetDecimal());
        Assert.Equal(0m, unknown.GetProperty("fatG").GetDecimal());
        Assert.Equal(1, commitBody.RootElement.GetProperty("itemsWithoutNutrition").GetInt32());

        var summaryResponse = await client.GetAsync($"/api/meals/daily-summary/{DateOnly.FromDateTime(loggedAt.UtcDateTime):yyyy-MM-dd}?tzOffsetMinutes=0");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        using var summary = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, summary.RootElement.GetProperty("itemsWithoutNutrition").GetInt32());
    }

    [Fact]
    public async Task UpdateAndCommit_InvalidMealTypeReturnBadRequestAndLeaveDraftPending()
    {
        foreach (var action in new[] { "update", "commit" })
        {
            var (client, _, _, draft) = await CreateDraft(Item($"meal type {action}", 100));
            var response = action == "update"
                ? await client.PutAsJsonAsync($"/api/meals/drafts/{draft.DraftId}", new MealDraftUpdateRequest
                {
                    MealType = "Brunch",
                    Items = draft.Items.Select(i => new MealDraftCommitItem { ItemId = i.ItemId, Grams = i.Grams }).ToList()
                })
                : await client.PutAsJsonAsync($"/api/meals/drafts/{draft.DraftId}/commit", new MealDraftCommitRequest { MealType = "Brunch" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var pending = await client.GetFromJsonAsync<MealDraftDto>($"/api/meals/drafts/{draft.DraftId}");
            Assert.Equal(MealDraftStatuses.PendingReview, pending?.Status);
        }
    }

    private static void AssertItemNutrition(JsonElement item, NutritionPer100gDto basis, decimal grams, string provenance)
    {
        Assert.Equal(basis.CaloriesKcal * grams / 100m, item.GetProperty("calories").GetDecimal());
        Assert.Equal(basis.ProteinG * grams / 100m, item.GetProperty("proteinG").GetDecimal());
        Assert.Equal(basis.CarbsG * grams / 100m, item.GetProperty("carbsG").GetDecimal());
        Assert.Equal(basis.FatG * grams / 100m, item.GetProperty("fatG").GetDecimal());
        Assert.Equal(provenance, item.GetProperty("nutritionProvenance").GetString());
    }

    [Fact]
    public async Task Commit_MapsUnresolvedAndUnknownItemsAndSecondCommit()
    {
        var unresolvedItem = new MealDraftItemDto { ItemId = Guid.NewGuid(), Name = "herbs", Source = "ai", Grams = 10, MatchConfidence = .2m };
        var (client, userId, services, unresolvedDraft) = await CreateDraft(unresolvedItem);
        var unresolved = await client.PutAsJsonAsync($"/api/meals/drafts/{unresolvedDraft.DraftId}/commit", new MealDraftCommitRequest { Items = [new() { ItemId = unresolvedDraft.Items[0].ItemId, Grams = 10 }] });
        Assert.Equal((HttpStatusCode)422, unresolved.StatusCode);
        using (var error = JsonDocument.Parse(await unresolved.Content.ReadAsStringAsync()))
        {
            error.RootElement.AssertHasStringProperty("error");
            Assert.Equal(JsonValueKind.Array, error.RootElement.GetProperty("itemIds").ValueKind);
            Assert.Equal(unresolvedDraft.Items[0].ItemId, error.RootElement.GetProperty("itemIds")[0].GetGuid());
        }
        var unknown = await client.PutAsJsonAsync($"/api/meals/drafts/{unresolvedDraft.DraftId}/commit", new MealDraftCommitRequest { Items = [new() { ItemId = Guid.NewGuid(), Grams = 10 }] });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var normal = await client.PutAsJsonAsync($"/api/meals/drafts/{unresolvedDraft.DraftId}/commit", new MealDraftCommitRequest { Items = [new() { ItemId = unresolvedDraft.Items[0].ItemId, Grams = 10, LogWithoutCalories = true }] });
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        var second = await client.PutAsJsonAsync($"/api/meals/drafts/{unresolvedDraft.DraftId}/commit", new MealDraftCommitRequest());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var delete = await client.DeleteAsync($"/api/meals/drafts/{unresolvedDraft.DraftId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var persisted = await services.GetRequiredService<ITableStore>().GetMealDraftAsync(userId, unresolvedDraft.DraftId);
        Assert.Equal(MealDraftStatuses.Committed, persisted?.Status);
    }

    [Fact]
    public async Task Commit_NeedsChoiceWithoutChoiceReturns422AndLeavesDraftPending()
    {
        var item = NeedsChoiceItem("ambiguous meal");
        var (client, _, _, draft) = await CreateDraft(item);
        var itemId = draft.Items[0].ItemId;
        var response = await client.PutAsJsonAsync($"/api/meals/drafts/{draft.DraftId}/commit", new MealDraftCommitRequest
        {
            Items = [new() { ItemId = itemId, Grams = 100 }]
        });
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        using (var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Contains(error.RootElement.GetProperty("itemIds").EnumerateArray(), id => id.GetGuid() == itemId);
        }

        var get = await client.GetAsync($"/api/meals/drafts/{draft.DraftId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var body = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(MealDraftStatuses.PendingReview, body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DraftOwnershipAndDeleteAreEnforced()
    {
        var (client, _, _, draft) = await CreateDraft(Item("rice", 130));
        var (other, _, _, _) = await factory.CreateAuthenticatedRealStoreClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/meals/drafts/{draft.DraftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync($"/api/meals/drafts/{draft.DraftId}")).StatusCode);
        var ownerGet = await client.GetAsync($"/api/meals/drafts/{draft.DraftId}");
        Assert.Equal(HttpStatusCode.OK, ownerGet.StatusCode);
        using var ownerBody = JsonDocument.Parse(await ownerGet.Content.ReadAsStringAsync());
        Assert.Equal(MealDraftStatuses.PendingReview, ownerBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/meals/drafts/{draft.DraftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/meals/drafts/{draft.DraftId}")).StatusCode);

        var list = await client.GetFromJsonAsync<List<MealDraftDto>>("/api/meals/drafts");
        Assert.DoesNotContain(list!, item => item.DraftId == draft.DraftId);
    }
}
