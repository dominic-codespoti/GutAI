using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class FoodProductEndpointsTests(AzuriteFixture fx)
{
    [Fact]
    public async Task CreateAndRetrieveFoodProduct_ViaStore()
    {
        var id = Guid.NewGuid();
        var product = new FoodProduct
        {
            Id = id,
            Name = "Test Food",
            Barcode = $"EP-{id.ToString()[..8]}",
            Brand = "TestBrand",
            Ingredients = "water, salt",
            NovaGroup = 2,
            Calories100g = 50,
            Protein100g = 2,
            Carbs100g = 10,
            Fat100g = 0,
            SafetyRating = SafetyRating.Safe
        };

        await fx.Store.UpsertFoodProductAsync(product);

        var loaded = await fx.Store.GetFoodProductAsync(id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Test Food");
        loaded.Barcode.Should().Be(product.Barcode);

        var byBarcode = await fx.Store.GetFoodProductByBarcodeAsync(product.Barcode!);
        byBarcode.Should().NotBeNull();
        byBarcode!.Id.Should().Be(id);
    }

    [Fact]
    public async Task SearchFoodProducts_FindsByPartialName()
    {
        var tag = Guid.NewGuid().ToString()[..8];
        await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = Guid.NewGuid(), Name = $"Organic-{tag} Quinoa" });
        await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = Guid.NewGuid(), Name = $"Organic-{tag} Brown Rice" });
        await fx.Store.UpsertFoodProductAsync(new FoodProduct { Id = Guid.NewGuid(), Name = "Unrelated Item" });

        var results = await fx.Store.SearchFoodProductsAsync($"Organic-{tag}", 10);
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(p => p.Name.Should().Contain($"Organic-{tag}"));
    }
}
