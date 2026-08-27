using System.Diagnostics;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodMatchIndexTests
{
    private static FoodProductDto MakeFood(
        string name, string source = "USDA", FoodKind kind = FoodKind.WholeFood,
        string? brand = null, string? barcode = null, string? externalId = null,
        decimal? cal = null, decimal? protein = null, decimal? carbs = null, decimal? fat = null) =>
        new()
        {
            Id = Guid.NewGuid(), Name = name, DataSource = source, FoodKind = kind,
            Brand = brand, Barcode = barcode, ExternalId = externalId,
            Calories100g = cal, Protein100g = protein, Carbs100g = carbs, Fat100g = fat,
        };

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var index = new FoodMatchIndex([MakeFood("Banana, raw")]);
        index.Search("").Should().BeEmpty();
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        var index = new FoodMatchIndex();
        index.Search("banana").Should().BeEmpty();
    }

    [Fact]
    public void Search_ExactMatch_RanksFirst()
    {
        var foods = new[]
        {
            MakeFood("Banana bread"),
            MakeFood("Banana, raw"),
            MakeFood("Banana chips"),
        };
        var index = new FoodMatchIndex(foods);

        var results = index.Search("banana", 3);
        results[0].Name.Should().Be("Banana, raw");
    }

    [Fact]
    public void AddRange_DuplicateIdentity_KeepsOnlyOne()
    {
        var shared = Guid.NewGuid();
        var a = MakeFood("Diet Coke", barcode: "049000028911") with { Id = shared };
        var b = MakeFood("Diet Coke Soft Drink", source: "OpenFoodFacts", barcode: "049000028911");

        var index = new FoodMatchIndex([a, b]);
        index.Count.Should().Be(1);
    }

    [Fact]
    public void AddRange_SameNameDifferentBrand_KeepsBoth()
    {
        var a = MakeFood("Eggs", brand: "Mars Chocolate");
        var b = MakeFood("Eggs", brand: null);

        var index = new FoodMatchIndex([a, b]);
        index.Count.Should().Be(2);
    }

    [Fact]
    public void SearchPersonalized_BoostedItem_RanksAboveEquivalentUnboosted()
    {
        var boosted = MakeFood("Rice, brown, cooked");
        var other = MakeFood("Rice, white, cooked");
        var index = new FoodMatchIndex([boosted, other]);

        var results = index.SearchPersonalized("rice", [boosted.Id], 2);
        results[0].Id.Should().Be(boosted.Id);
    }

    [Fact]
    public void Search_BrandedProductWithoutBrandInQuery_RanksBelowWholeFood()
    {
        var wholeFood = MakeFood("Eggs, whole, raw, fresh", kind: FoodKind.WholeFood);
        var candy = MakeFood("Eggs", source: "OpenFoodFacts", kind: FoodKind.Branded, brand: "Mars Chocolate");
        var index = new FoodMatchIndex([wholeFood, candy]);

        var results = index.Search("eggs", 2);
        results[0].Id.Should().Be(wholeFood.Id);
    }

    [Fact]
    public void Search_ImplausibleMeatMacros_RanksBelowPlausibleAlternative()
    {
        // Reproduces the original bug: an exact-name-match OpenFoodFacts entry with
        // implausible carbs for plain chicken breast must lose to a nutritionally sane USDA entry.
        var implausible = MakeFood("Grilled chicken breast", source: "OpenFoodFacts", kind: FoodKind.Unknown,
            cal: 86, protein: 9.29m, carbs: 5.71m, fat: 2.86m);
        var plausible = MakeFood("Chicken, breast, raw", source: "USDA", kind: FoodKind.WholeFood,
            cal: 120, protein: 22.6m, carbs: 0m, fat: 2.6m);

        var index = new FoodMatchIndex([implausible, plausible]);
        var results = index.Search("grilled chicken breast", 2);

        results[0].Id.Should().Be(plausible.Id);
    }

    [Fact]
    public void Search_FriedEggQuery_PrefersFriedCandidateOverRawVariants()
    {
        // A real design gap this suite must guard: dropping Lucene's token-match score
        // means the generic "prefer raw/plain form" bonus could otherwise override an
        // explicit cooking-method request in the query.
        var fried = MakeFood("Egg, whole, cooked, fried");
        var raw1 = MakeFood("Egg, whole, raw, fresh");
        var raw2 = MakeFood("Egg, duck, whole, fresh, raw");

        var index = new FoodMatchIndex([raw1, raw2, fried]);
        var results = index.Search("fried egg", 3);

        results[0].Id.Should().Be(fried.Id);
    }

    [Fact]
    public void Search_LargeCatalog_CompletesQuickly()
    {
        var foods = Enumerable.Range(0, 10_000)
            .Select(i => MakeFood($"Synthetic food item number {i}, raw", cal: i % 500))
            .ToList();
        var index = new FoodMatchIndex(foods);

        var sw = Stopwatch.StartNew();
        var results = index.Search("synthetic food raw", 20);
        sw.Stop();

        results.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "a linear scan over a 10k-item catalog must stay well within a single request's budget");
    }

    // ════════════════════════════════════════════════════════
    //  Resolve — the single decision for auto-selecting a food match. These are the
    //  behaviors that used to live in NaturalLanguageFallbackService's now-deleted
    //  PickBestMatch/FilterImplausibleMeatMatches/ComputeConfidence.
    // ════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_ZeroOverlapQuery_ReturnsUnresolved_NotBestQualityGuess()
    {
        var foods = new[]
        {
            MakeFood("Banana, raw"),
            MakeFood("Apple, raw"),
            MakeFood("Rice, white, cooked"),
        };
        var index = new FoodMatchIndex(foods);

        var resolution = index.Resolve("xyznonexistentfood", []);

        resolution.Status.Should().Be(FoodResolutionStatus.Unresolved);
        resolution.Selected.Should().BeNull();
        resolution.Alternatives.Should().BeEmpty();
        resolution.MatchConfidence.Should().Be(0m);
    }

    [Fact]
    public void Resolve_EmptyQuery_ReturnsUnresolved()
    {
        var index = new FoodMatchIndex([MakeFood("Banana, raw")]);

        index.Resolve("", []).Status.Should().Be(FoodResolutionStatus.Unresolved);
    }

    [Fact]
    public void Resolve_ExactNameMatch_ReturnsExactWithFullConfidence()
    {
        var index = new FoodMatchIndex([MakeFood("Egg, whole, raw, fresh"), MakeFood("Egg")]);

        var resolution = index.Resolve("egg", []);

        resolution.Status.Should().Be(FoodResolutionStatus.Exact);
        resolution.Selected!.Name.Should().Be("Egg");
        resolution.MatchConfidence.Should().Be(1.0m);
    }

    [Fact]
    public void Resolve_SingleClearCandidate_ReturnsProbable()
    {
        var index = new FoodMatchIndex([MakeFood("Chicken, breast, raw")]);

        var resolution = index.Resolve("chicken breast", []);

        resolution.Status.Should().Be(FoodResolutionStatus.Probable);
        resolution.Selected!.Name.Should().Be("Chicken, breast, raw");
        resolution.MatchConfidence.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Resolve_NearlyTiedCandidates_ReturnsAmbiguous()
    {
        // Two equally-plausible, equally-covered candidates for the same query with no
        // decisive signal (brand, exact match, quality) separating them.
        var index = new FoodMatchIndex([
            MakeFood("Salmon, sockeye, raw", cal: 168, protein: 20, carbs: 0, fat: 9),
            MakeFood("Salmon, coho, raw", cal: 168, protein: 20, carbs: 0, fat: 9),
        ]);

        var resolution = index.Resolve("salmon", []);

        resolution.Status.Should().Be(FoodResolutionStatus.Ambiguous);
        resolution.Selected.Should().NotBeNull();
        resolution.Alternatives.Should().ContainSingle();
    }

    [Fact]
    public void Resolve_SingleIneligibleCandidate_ReturnsUnresolved()
    {
        // A lone candidate with zero lexical overlap must not be returned just because
        // it's the only thing in the index — the old count<=1 shortcut bypassed this.
        var index = new FoodMatchIndex([MakeFood("Banana, raw")]);

        index.Resolve("xyznonexistentfood", []).Status.Should().Be(FoodResolutionStatus.Unresolved);
    }

    [Fact]
    public void Resolve_PartialCoverage_ReturnsIntermediateConfidence()
    {
        var index = new FoodMatchIndex([MakeFood("Grilled chicken breast tenders")]);

        var resolution = index.Resolve("grilled chicken", []);

        resolution.MatchConfidence.Should().BeInRange(0.4m, 0.85m);
    }

    [Fact]
    public void Search_ZeroOverlapQuery_ReturnsEmpty_NotUnrelatedCandidates()
    {
        // Regression guard for the original design gap: a plain ranked-list caller
        // (Search/SearchPersonalized) must also abstain, not just Resolve.
        var foods = new[] { MakeFood("Banana, raw"), MakeFood("Apple, raw") };
        var index = new FoodMatchIndex(foods);

        index.Search("xyznonexistentfood", 20).Should().BeEmpty();
    }

    [Fact]
    public void Search_TiedCandidates_ReturnsDeterministicOrderAcrossPermutations()
    {
        // Two candidates with identical score/quality/relevance inputs for the query.
        // Equal scores must break ties deterministically by Name then ExternalId,
        // regardless of index insertion order / arrival order.
        var foodA = MakeFood("Apple, gala, raw", externalId: "usda-101", cal: 52, protein: 0.3m, carbs: 14, fat: 0.2m);
        var foodB = MakeFood("Apple, fuji, raw", externalId: "usda-102", cal: 52, protein: 0.3m, carbs: 14, fat: 0.2m);

        var index1 = new FoodMatchIndex([foodA, foodB]);
        var index2 = new FoodMatchIndex([foodB, foodA]);

        var results1 = index1.Search("apple", 10);
        var results2 = index2.Search("apple", 10);

        results1.Select(r => r.Name).Should().Equal(results2.Select(r => r.Name));
        results1.Select(r => r.ExternalId).Should().Equal(results2.Select(r => r.ExternalId));
        results1.Select(r => r.Name).Should().ContainInOrder("Apple, fuji, raw", "Apple, gala, raw");
    }
}
