using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// Unit tests for Stage-B deterministic food-form and specificity safety policies (<see cref="FoodFormPolicy"/>).
/// Tests conservative product-form vetoes (raw fruit, oatmeal, smoothie) and specificity rules
/// (generic observed vs specific candidate veto; specific observed vs generic candidate allowance; exact matches).
/// </summary>
public sealed class FoodFormPolicyTests
{
    private static FoodProductDto Product(
        string name,
        string source = "USDA",
        decimal conf = 0.9m,
        decimal cal100 = 100m,
        FoodKind kind = FoodKind.WholeFood,
        string? brand = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DataSource = source,
        MatchConfidence = conf,
        Calories100g = cal100,
        FoodKind = kind,
        Brand = brand,
    };

    private static ComponentGroundingEngine EngineWith(FoodResolutionDto resolution)
    {
        var mock = new Mock<IFoodSearchService>();
        mock.Setup(f => f.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolution);
        return new ComponentGroundingEngine(mock.Object);
    }

    private static ScannedComponent Component(string name, string? prepNote = null, params string[] queries) => new()
    {
        Name = name,
        PreparationNote = prepNote ?? "",
        SearchQueries = queries.ToList(),
        EstimatedGramsLow = 50,
        EstimatedGramsMidpoint = 100,
        EstimatedGramsHigh = 150,
        Confidence = 0.85m,
    };

    // ── 1. Raw Fruit / Raw Berries Vetoes ──

    [Theory]
    [InlineData("raw blueberries", "Blueberry Juice", "juice")]
    [InlineData("fresh strawberries", "Strawberry Pops", "pop")]
    [InlineData("raw strawberries", "Strawberry Fruit Bar", "bar")]
    [InlineData("fresh blueberries", "Blueberry Puree Concentrate", "concentrate")]
    [InlineData("raw raspberries", "Raspberry Topping Sauce", "topping")]
    [InlineData("fresh blueberries", "Blueberry Lowfat Yogurt", "yogurt")]
    public void RawFruit_MismatchedProductForms_AreVetoed(string obsName, string candName, string expectedKeyword)
    {
        var obs = Component(obsName);
        var cand = Product(candName);

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().NotBeNull();
        reason.Should().Contain(expectedKeyword);
    }

    [Fact]
    public void RawFruit_PlausibleRawFruitCandidate_IsAllowed()
    {
        var obs = Component("raw blueberries");
        var cand = Product("Blueberries, wild, raw");

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().BeNull();
    }

    // ── 2. Oatmeal / Porridge Vetoes ──

    [Theory]
    [InlineData("oatmeal", "Oatmeal Bread")]
    [InlineData("oat porridge", "Dry Cereal Flakes")]
    [InlineData("rolled oats porridge", "Wheat Farina")]
    [InlineData("oatmeal", "Malted Barley Flour")]
    [InlineData("porridge", "Oatmeal Raisin Cookies")]
    public void Oatmeal_MismatchedProductForms_AreVetoed(string obsName, string candName)
    {
        var obs = Component(obsName);
        var cand = Product(candName);

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().NotBeNull();
    }

    [Theory]
    [InlineData("oatmeal", "Cereals, oats, regular and quick, not fortified, cooked with water")]
    [InlineData("oat porridge", "Oatmeal, cooked")]
    [InlineData("steel cut oatmeal", "Steel Cut Oats")]
    public void Oatmeal_PlausibleOatmealCandidate_IsAllowed(string obsName, string candName)
    {
        var obs = Component(obsName);
        var cand = Product(candName);

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().BeNull();
    }

    // ── 3. Smoothie Vetoes & Allowances ──

    [Theory]
    [InlineData("strawberry banana smoothie", "Strawberry Banana Protein Bar")]
    [InlineData("fruit smoothie", "Fruit Smoothie Ice Pops")]
    [InlineData("protein smoothie", "Protein Bar")]
    public void Smoothie_SolidSnackForms_AreVetoed(string obsName, string candName)
    {
        var obs = Component(obsName);
        var cand = Product(candName);

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().NotBeNull();
    }

    [Theory]
    [InlineData("fruit smoothie", "Fruit Smoothie Beverage")]
    [InlineData("green smoothie", "Green Smoothie")]
    [InlineData("protein shake", "Protein Shake Ready-to-drink")]
    public void Smoothie_PlausibleBeverageCandidates_AreAllowed(string obsName, string candName)
    {
        var obs = Component(obsName);
        var cand = Product(candName);

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().BeNull();
    }

    // ── 4. Specificity Policy: Generic Acceptance & Over-Specificity Rejection ──

    [Fact]
    public void SpecificObservation_GenericCandidate_SameDish_IsAllowed()
    {
        // Observed specific "pork katsu curry" with candidate generic "Katsu curry"
        var obs = Component("pork katsu curry", queries: ["katsu curry"]);
        var cand = Product("Katsu curry");

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().BeNull();
    }

    [Fact]
    public void GenericObservation_SpecificProduct_BlueberryPops_IsVetoed()
    {
        // Observed generic "blueberry" with candidate specific "Blueberry Pops"
        var obs = Component("blueberry");
        var cand = Product("Blueberry Pops", kind: FoodKind.Branded, brand: "Example Brand");

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().NotBeNull();
    }

    [Fact]
    public void ExactMatch_IsAllowed()
    {
        var obs = Component("grilled chicken");
        var cand = Product("Grilled chicken");

        var reason = FoodFormPolicy.Evaluate(obs, cand);

        reason.Should().BeNull();
    }

    // ── 5. Integration in ComponentGroundingEngine (End-to-End GroundAsync) ──

    [Fact]
    public async Task GroundAsync_VetoedBlueberryPops_DoesNotAutoSelect_ExposedForReview()
    {
        var cand = Product("Blueberry Pops", source: "OpenFoodFacts", conf: 0.95m, kind: FoodKind.Branded, brand: "CoolPops");
        var engine = EngineWith(new FoodResolutionDto
        {
            OriginalQuery = "blueberry",
            Status = FoodResolutionStatus.Exact,
            Selected = cand,
            MatchConfidence = 0.95m,
        });

        var grounded = await engine.GroundAsync(Component("blueberry"));

        grounded.Attempt.AutoSelected.Should().BeFalse("generic blueberry must not auto-select Blueberry Pops");
        grounded.Attempt.ResolutionStatus.Should().Be("ambiguous");
        grounded.ResolvedProduct.Should().BeNull();
        grounded.CandidateProducts.Should().Contain(c => c.Name == "Blueberry Pops", "it must remain exposed as a candidate for review");
    }

    [Fact]
    public async Task GroundAsync_AllowedKatsuCurry_AutoSelectsWhenConfident()
    {
        var cand = Product("Katsu curry", source: "USDA", conf: 0.90m, cal100: 180m);
        var engine = EngineWith(new FoodResolutionDto
        {
            OriginalQuery = "pork katsu curry",
            Status = FoodResolutionStatus.Probable,
            Selected = cand,
            MatchConfidence = 0.90m,
        });

        var grounded = await engine.GroundAsync(Component("pork katsu curry"));

        grounded.Attempt.AutoSelected.Should().BeTrue("specific pork katsu curry can ground to generic katsu curry");
        grounded.ResolvedProduct.Should().NotBeNull();
        grounded.ResolvedProduct!.Name.Should().Be("Katsu curry");
    }

    [Fact]
    public async Task GroundAsync_VetoedOatmealBread_DoesNotAutoSelect()
    {
        var cand = Product("Oatmeal Bread", source: "USDA", conf: 0.90m, cal100: 260m);
        var engine = EngineWith(new FoodResolutionDto
        {
            OriginalQuery = "oatmeal",
            Status = FoodResolutionStatus.Probable,
            Selected = cand,
            MatchConfidence = 0.90m,
        });

        var grounded = await engine.GroundAsync(Component("oatmeal"));

        grounded.Attempt.AutoSelected.Should().BeFalse("oatmeal bowl must not auto-select oatmeal bread");
        grounded.Attempt.ResolutionStatus.Should().Be("ambiguous");
        grounded.ResolvedProduct.Should().BeNull();
    }
}
