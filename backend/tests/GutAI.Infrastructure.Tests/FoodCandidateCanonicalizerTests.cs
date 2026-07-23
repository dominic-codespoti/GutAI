using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Domain.Constants;
using GutAI.Domain.Enums;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodCandidateCanonicalizerTests
{
    private static FoodProductDto MakeFood(
        string name, string source, string? barcode = null, string? brand = null,
        string? externalId = null, FoodKind kind = FoodKind.Unknown) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataSource = source,
            Barcode = barcode,
            Brand = brand,
            ExternalId = externalId,
            FoodKind = kind,
        };

    [Fact]
    public void Canonicalize_SameBarcode_CollapsesToOne()
    {
        var a = MakeFood("Diet Coke", DataSources.OpenFoodFacts, barcode: "049000028911");
        var b = MakeFood("Diet Coke Soft Drink", DataSources.Usda, barcode: "049000028911");

        var result = FoodCandidateCanonicalizer.Canonicalize([a, b]);

        result.Should().ContainSingle();
    }

    [Fact]
    public void Canonicalize_SameNameDifferentBrand_PreservesBoth()
    {
        var a = MakeFood("Eggs", "Mars", brand: "Mars Chocolate");
        var b = MakeFood("Eggs", DataSources.Usda, brand: null);

        var result = FoodCandidateCanonicalizer.Canonicalize([a, b]);

        result.Should().HaveCount(2, "different brands are different products even with the same display name");
    }

    [Fact]
    public void Canonicalize_SameNameDifferentBarcode_PreservesBoth()
    {
        var a = MakeFood("Chicken Breast", DataSources.OpenFoodFacts, barcode: "111");
        var b = MakeFood("Chicken Breast", DataSources.OpenFoodFacts, barcode: "222");

        var result = FoodCandidateCanonicalizer.Canonicalize([a, b]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Canonicalize_SameNameNoIdentifiers_HighestSourcePriorityWins()
    {
        // No barcode/externalId on either candidate — falls back to name+brand identity,
        // which is the realistic case for two whole-food databases returning the same
        // generic entry with no stable cross-source id.
        var wholeFoodUsda = MakeFood("Banana, raw", DataSources.Usda, kind: FoodKind.WholeFood);
        var wholeFoodAusnut = MakeFood("Banana, raw", DataSources.Ausnut, kind: FoodKind.WholeFood);

        var result = FoodCandidateCanonicalizer.Canonicalize([wholeFoodAusnut, wholeFoodUsda], FoodRegion.Default);

        result.Should().ContainSingle();
        result[0].DataSource.Should().Be(DataSources.Usda, "USDA outranks AUSNUT for whole foods outside AU");
    }

    [Fact]
    public void Canonicalize_AuRegion_PrefersAusnutForWholeFoods()
    {
        var wholeFoodUsda = MakeFood("Banana, raw", DataSources.Usda, kind: FoodKind.WholeFood);
        var wholeFoodAusnut = MakeFood("Banana, raw", DataSources.Ausnut, kind: FoodKind.WholeFood);

        var result = FoodCandidateCanonicalizer.Canonicalize([wholeFoodUsda, wholeFoodAusnut], FoodRegion.Au);

        result.Should().ContainSingle();
        result[0].DataSource.Should().Be(DataSources.Ausnut, "AU region should prefer AUSNUT for whole foods");
    }

    [Fact]
    public void Canonicalize_NoIdentityOverlap_KeepsEveryCandidate()
    {
        var candidates = new[]
        {
            MakeFood("Apple", DataSources.Usda),
            MakeFood("Banana", DataSources.Usda),
            MakeFood("Carrot", DataSources.OpenFoodFacts),
        };

        var result = FoodCandidateCanonicalizer.Canonicalize(candidates);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void Canonicalize_EmptyInput_ReturnsEmpty()
    {
        var result = FoodCandidateCanonicalizer.Canonicalize([]);

        result.Should().BeEmpty();
    }
}
