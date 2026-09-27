using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
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
        json.AssertHasNumberProperty("correctionCount");
        json.AssertHasProperty("lastCorrectedAt", JsonValueKind.Null);
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
        item.AssertHasStringProperty("safetyRating");
    }
    [Fact]
    public async Task CreateMeal_CatalogItemRecomputesTamperedCalories()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Catalog {Guid.NewGuid():N}",
            Calories100g = 200m,
            Protein100g = 10m,
            Carbs100g = 20m,
            Fat100g = 8m,
            Fiber100g = 3m,
            Sugar100g = 4m,
            SodiumMg100g = 50m,
        };
        await factory.Services.GetRequiredService<ITableStore>().UpsertFoodProductAsync(product);

        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = product.Name, foodProductId = product.Id, servings = 1m, servingWeightG = 50m, calories = 9999m, proteinG = 499m, carbsG = 500m, fatG = 500m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("calories").GetDecimal().Should().Be(100m);
        item.GetProperty("proteinG").GetDecimal().Should().Be(5m);
        item.GetProperty("nutritionProvenance").GetString().Should().Be("Sourced");
    }
    [Fact]
    public async Task CreateMeal_CatalogItemInfersGramsFromCaloriesAndRecomputesNutrition()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Legacy catalog {Guid.NewGuid():N}",
            Calories100g = 200m,
            Protein100g = 10m,
            Carbs100g = 20m,
            Fat100g = 8m,
            Fiber100g = 3m,
            Sugar100g = 4m,
            SodiumMg100g = 50m,
        };
        await factory.Services.GetRequiredService<ITableStore>().UpsertFoodProductAsync(product);

        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = product.Name, foodProductId = product.Id, servings = 1m, servingWeightG = (decimal?)null, calories = 300m, proteinG = 499m, carbsG = 500m, fatG = 500m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("servingWeightG").GetDecimal().Should().Be(150m);
        item.GetProperty("calories").GetDecimal().Should().Be(300m);
        item.GetProperty("proteinG").GetDecimal().Should().Be(15m);
        item.GetProperty("carbsG").GetDecimal().Should().Be(30m);
        item.GetProperty("fatG").GetDecimal().Should().Be(12m);
        item.GetProperty("fiberG").GetDecimal().Should().Be(4.5m);
        item.GetProperty("sugarG").GetDecimal().Should().Be(6m);
        item.GetProperty("sodiumMg").GetDecimal().Should().Be(75m);
    }

    [Fact]
    public async Task CreateMeal_CatalogItemWithoutCaloriesStillRequiresServingWeight()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Legacy no-calorie catalog {Guid.NewGuid():N}",
            Calories100g = 200m,
            Protein100g = 10m,
        };
        await factory.Services.GetRequiredService<ITableStore>().UpsertFoodProductAsync(product);

        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = product.Name, foodProductId = product.Id, servings = 1m, servingWeightG = (decimal?)null, calories = 0m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("error").GetString().Should().Be("servingWeightG is required for catalog items");
    }

    [Fact]
    public async Task CreateMeal_CatalogItemInferredGramsAboveLimitReturns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Legacy oversized catalog {Guid.NewGuid():N}",
            Calories100g = 200m,
            Protein100g = 10m,
        };
        await factory.Services.GetRequiredService<ITableStore>().UpsertFoodProductAsync(product);

        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = product.Name, foodProductId = product.Id, servings = 1m, servingWeightG = (decimal?)null, calories = 10002m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMeal_CatalogItemInfersGramsFromCalories()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Initial item", servings = 1m, calories = 100m, proteinG = 5m, carbsG = 10m, fatG = 4m } }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var mealId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Legacy update catalog {Guid.NewGuid():N}",
            Calories100g = 200m,
            Protein100g = 10m,
            Carbs100g = 20m,
            Fat100g = 8m,
        };
        await factory.Services.GetRequiredService<ITableStore>().UpsertFoodProductAsync(product);

        var response = await client.PutAsJsonAsync($"/api/meals/{mealId}", new
        {
            items = new[] { new { foodName = product.Name, foodProductId = product.Id, servings = 1m, servingWeightG = (decimal?)null, calories = 300m, proteinG = 499m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("servingWeightG").GetDecimal().Should().Be(150m);
        item.GetProperty("calories").GetDecimal().Should().Be(300m);
        item.GetProperty("proteinG").GetDecimal().Should().Be(15m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public async Task CreateMeal_CatalogItemRejectsInvalidServingWeight(int grams)
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Gram limit {Guid.NewGuid():N}",
            Calories100g = 200m,
            Protein100g = 10m,
            Carbs100g = 20m,
            Fat100g = 8m,
        };
        await factory.Services.GetRequiredService<ITableStore>().UpsertFoodProductAsync(product);

        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = product.Name, foodProductId = product.Id, servings = 1m, servingWeightG = grams, calories = 100m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMeal_ManualMissingProvenanceStoresUserEntered()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Hand-entered lunch", servings = 1m, calories = 150m, proteinG = 5m, carbsG = 20m, fatG = 5m } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("nutritionProvenance").GetString().Should().Be("UserEntered");
    }

    [Fact]
    public async Task CreateMeal_UnknownProvenanceReturns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Unknown source", servings = 1m, calories = 100m, nutritionProvenance = "invented" } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMeal_UnknownProvenanceWithNutritionReturns400()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Unknown source", servings = 1m, calories = 100m, nutritionProvenance = "Unknown" } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAndUpdateMeal_UnknownProvenanceWithoutNutritionRoundtrips()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Name-only item", servings = 1m, nutritionProvenance = "Unknown" } }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdMeal = await created.Content.ReadFromJsonAsync<JsonElement>();
        var item = createdMeal.GetProperty("items")[0];
        item.GetProperty("calories").GetDecimal().Should().Be(0m);
        item.GetProperty("nutritionProvenance").GetString().Should().Be("Unknown");

        var mealId = createdMeal.GetProperty("id").GetString();
        var updated = await client.PutAsJsonAsync($"/api/meals/{mealId}", new
        {
            items = new[] { new { foodName = "Name-only item", servings = 1m, nutritionProvenance = "Unknown", calories = 0m } }
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedItem = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        updatedItem.GetProperty("calories").GetDecimal().Should().Be(0m);
        updatedItem.GetProperty("nutritionProvenance").GetString().Should().Be("Unknown");
    }

    [Fact]
    public async Task CreateMeal_EstimatedProvenanceWithNutritionIsAccepted()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Estimated food", servings = 1m, calories = 145m, nutritionProvenance = "Estimated" } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("calories").GetDecimal().Should().Be(145m);
        item.GetProperty("nutritionProvenance").GetString().Should().Be("Estimated");
    }

    [Fact]
    public async Task CreateMeal_SourcedWithoutProductStoresAsEstimated()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Unlinked food", servings = 1m, calories = 145m, proteinG = 5m, carbsG = 20m, fatG = 5m, nutritionProvenance = "Sourced" } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("nutritionProvenance").GetString().Should().Be("Estimated");
    }

    [Fact]
    public async Task CreateMeal_HardNutritionSanityViolationReturns422()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Impossible food", servings = 1m, servingWeightG = 100m, calories = 500m, proteinG = 60m, carbsG = 60m, fatG = 60m } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UpdateMeal_CatalogItemRecomputesTamperedCalories()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Update catalog {Guid.NewGuid():N}",
            Calories100g = 240m,
            Protein100g = 12m,
            Carbs100g = 24m,
            Fat100g = 8m,
            Fiber100g = 2m,
            Sugar100g = 4m,
            SodiumMg100g = 60m,
        };
        await factory.Services.GetRequiredService<ITableStore>().UpsertFoodProductAsync(product);
        var created = await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Initial", servings = 1m, calories = 150m, proteinG = 5m, carbsG = 20m, fatG = 5m } }
        });
        var mealId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await client.PutAsJsonAsync($"/api/meals/{mealId}", new
        {
            items = new[] { new { foodName = product.Name, foodProductId = product.Id, servings = 1m, servingWeightG = 50m, calories = 9999m, proteinG = 499m, carbsG = 500m, fatG = 500m } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("calories").GetDecimal().Should().Be(120m);
    }

    [Fact]
    public async Task LogNatural_ReturnsDraftAndParsedItemReferencesWithContractShape()
    {
        // The shared factory's FaultInjectionTableStore throws on SearchFoodProductsAsync,
        // so exercise NLP resolution with the factory's real-store client.
        var (client, _, _, token) = await factory.CreateAuthenticatedRealStoreClientAsync();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/meals/log-natural", new { text = $"1 serving of unknown-{Guid.NewGuid():N}", mealType = "lunch" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.AssertHasStringProperty("originalText");
        json.AssertHasStringProperty("mealType");
        json.AssertHasStringProperty("draftId");
        json.AssertHasProperty("parsedItems", JsonValueKind.Array);
        var parsedItem = json.GetProperty("parsedItems")[0];
        parsedItem.AssertHasStringProperty("draftItemId");
        parsedItem.AssertHasProperty("per100g", JsonValueKind.Object);
        parsedItem.GetProperty("needsChoice").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        var draftId = json.GetProperty("draftId").GetString();
        var draftResponse = await client.GetAsync($"/api/meals/drafts/{draftId}");
        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
        draft.GetProperty("mealType").GetString().Should().Be("Lunch");
    }

    [Fact]
    public async Task LogNatural_InvalidMealTypeReturns400()
    {
        var (client, _, _, token) = await factory.CreateAuthenticatedRealStoreClientAsync();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/meals/log-natural", new { text = "1 serving of oatmeal", mealType = "Brunch" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
    public async Task UpdateMeal_RecordsCorrectionMetadata()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/meals", new
        {
            mealType = "Breakfast",
            originalText = "two eggs",
            items = new[] { new { foodName = "Eggs", servings = 2.0, servingUnit = "piece", calories = 140.0 } }
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var mealId = created.GetProperty("id").GetString();

        var updateResp = await client.PutAsJsonAsync($"/api/meals/{mealId}", new
        {
            mealType = "Breakfast",
            items = new[] { new { foodName = "Eggs", servings = 1.0, servingUnit = "piece", calories = 70.0 } }
        });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("correctionCount").GetInt32().Should().Be(1);
        updated.GetProperty("lastCorrectedAt").ValueKind.Should().Be(JsonValueKind.String);
        updated.GetProperty("originalText").GetString().Should().Be("two eggs");
    }


    [Fact]
    public async Task DailySummary_ReturnsCorrectShape()
    {
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/meals", new
        {
            items = new[] { new { foodName = "Unknown nutrition item", servings = 1m, calories = 0m, nutritionProvenance = "Unknown" } }
        });
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
        json.AssertHasNumberProperty("itemsWithoutNutrition");
        json.GetProperty("itemsWithoutNutrition").GetInt32().Should().BeGreaterThan(0);


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
