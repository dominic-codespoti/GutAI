using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodProductPersistenceTests
{
    private readonly Mock<ITableStore> _store = new();

    private static FoodProductDto MakeDto(string name, string? barcode = null, string? externalId = null,
        string dataSource = "USDA", string? brand = null) => new()
    {
        Name = name,
        Barcode = barcode,
        ExternalId = externalId,
        DataSource = dataSource,
        Brand = brand,
        Calories100g = 100,
    };

    private static FoodProduct MakeExisting(Guid id, string name, string? barcode = null, string? externalId = null,
        string dataSource = "USDA", string? brand = null, int? safetyScore = null,
        SafetyRating? safetyRating = null, List<int>? additiveIds = null, bool isDeleted = false) => new()
    {
        Id = id,
        Name = name,
        Barcode = barcode,
        ExternalId = externalId,
        DataSource = dataSource,
        Brand = brand,
        SafetyScore = safetyScore,
        SafetyRating = safetyRating,
        FoodProductAdditiveIds = additiveIds ?? [],
        IsDeleted = isDeleted,
    };

    [Fact]
    public async Task ResolveOrPersistAsync_NoExistingIdentity_CreatesNewProduct()
    {
        _store.Setup(s => s.SearchFoodProductsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var dto = MakeDto("Bananas, raw");
        var id = await FoodProductPersistence.ResolveOrPersistAsync(dto, _store.Object);

        id.Should().NotBe(Guid.Empty);
        _store.Verify(s => s.UpsertFoodProductAsync(
            It.Is<FoodProduct>(p => p.Id == id && p.Name == "Bananas, raw"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveOrPersistAsync_ExistingBarcodeMatch_ReusesId()
    {
        var existingId = Guid.NewGuid();
        var existing = MakeExisting(existingId, "Diet Coke", barcode: "049000028911");
        _store.Setup(s => s.GetFoodProductByBarcodeAsync("049000028911", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var dto = MakeDto("Diet Coke", barcode: "049000028911");
        var id = await FoodProductPersistence.ResolveOrPersistAsync(dto, _store.Object);

        id.Should().Be(existingId);
        _store.Verify(s => s.UpsertFoodProductAsync(
            It.Is<FoodProduct>(p => p.Id == existingId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveOrPersistAsync_ExistingSourceMatch_ReusesId_WithoutBarcode()
    {
        var existingId = Guid.NewGuid();
        var existing = MakeExisting(existingId, "Grilled chicken breast", externalId: "12345", dataSource: "OpenFoodFacts");
        _store.Setup(s => s.GetFoodProductBySourceAsync("OpenFoodFacts", "12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var dto = MakeDto("Grilled chicken breast", externalId: "12345", dataSource: "OpenFoodFacts");
        var id = await FoodProductPersistence.ResolveOrPersistAsync(dto, _store.Object);

        id.Should().Be(existingId);
    }

    [Fact]
    public async Task ResolveOrPersistAsync_ExistingNameAndBrandMatch_ReusesId_WhenNoStableIdentity()
    {
        // Embedded whole-food/branded/Australian databases never set Barcode/ExternalId —
        // this is the fallback path that must catch them.
        var existingId = Guid.NewGuid();
        var existing = MakeExisting(existingId, "Bananas, raw", dataSource: "USDA");
        _store.Setup(s => s.SearchFoodProductsAsync("Bananas, raw", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var dto = MakeDto("Bananas, raw", dataSource: "USDA");
        var id = await FoodProductPersistence.ResolveOrPersistAsync(dto, _store.Object);

        id.Should().Be(existingId);
    }

    [Fact]
    public async Task ResolveOrPersistAsync_NameMatchesButBrandDiffers_DoesNotReuse()
    {
        var existing = MakeExisting(Guid.NewGuid(), "Eggs", brand: "Mars Chocolate");
        _store.Setup(s => s.SearchFoodProductsAsync("Eggs", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var dto = MakeDto("Eggs", brand: null);
        var id = await FoodProductPersistence.ResolveOrPersistAsync(dto, _store.Object);

        id.Should().NotBe(existing.Id);
    }

    [Fact]
    public async Task ResolveOrPersistAsync_RefreshingExisting_PreservesSafetyScoringAndAdditiveLinks()
    {
        // The external DTO never carries SafetyScore/SafetyRating/additive links (those are
        // computed by separate pipelines) — a refresh must not silently wipe them.
        var existingId = Guid.NewGuid();
        var existing = MakeExisting(existingId, "White Bread", barcode: "123", safetyScore: 42,
            safetyRating: SafetyRating.Caution, additiveIds: [7, 9], isDeleted: false);
        _store.Setup(s => s.GetFoodProductByBarcodeAsync("123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        FoodProduct? persisted = null;
        _store.Setup(s => s.UpsertFoodProductAsync(It.IsAny<FoodProduct>(), It.IsAny<CancellationToken>()))
            .Callback<FoodProduct, CancellationToken>((p, _) => persisted = p)
            .Returns(Task.CompletedTask);

        var dto = MakeDto("White Bread", barcode: "123");
        await FoodProductPersistence.ResolveOrPersistAsync(dto, _store.Object);

        persisted.Should().NotBeNull();
        persisted!.Id.Should().Be(existingId);
        persisted.SafetyScore.Should().Be(42);
        persisted.SafetyRating.Should().Be(SafetyRating.Caution);
        persisted.FoodProductAdditiveIds.Should().BeEquivalentTo([7, 9]);
    }

    [Fact]
    public async Task ResolveOrPersistAsync_BarcodeTakesPrecedenceOverSourceId()
    {
        var byBarcodeId = Guid.NewGuid();
        var bySourceId = Guid.NewGuid();
        _store.Setup(s => s.GetFoodProductByBarcodeAsync("999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeExisting(byBarcodeId, "Product", barcode: "999"));
        _store.Setup(s => s.GetFoodProductBySourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeExisting(bySourceId, "Product", externalId: "42"));

        var dto = MakeDto("Product", barcode: "999", externalId: "42");
        var id = await FoodProductPersistence.ResolveOrPersistAsync(dto, _store.Object);

        id.Should().Be(byBarcodeId);
    }
}
