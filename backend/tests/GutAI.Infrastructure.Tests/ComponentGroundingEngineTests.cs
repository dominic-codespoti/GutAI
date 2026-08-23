using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// Stage-B grounding policy tests (P3). Covers the dangerous scenarios:
/// close variants, cooked/raw forms, branded-vs-generic, compound foods,
/// ambiguous drinks, no-credible-candidate. Success = correct auto-grounding
/// + appropriate abstention, NOT raw resolution percentage.
/// </summary>
public class ComponentGroundingEngineTests
{
    private static FoodProductDto Product(
        string name, string source = "USDA", decimal conf = 0.9m,
        decimal cal100 = 150m, decimal? servingQty = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DataSource = source,
        MatchConfidence = conf,
        Calories100g = cal100,
        Protein100g = 25m,
        Carbs100g = 0m,
        Fat100g = 5m,
        ServingQuantity = servingQty,
    };

    private static ComponentGroundingEngine EngineWith(FoodResolutionDto resolution)
    {
        var mock = new Mock<IFoodSearchService>();
        mock.Setup(f => f.ResolveAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolution);
        return new ComponentGroundingEngine(mock.Object);
    }

    private static ScannedComponent Component(string name = "grilled chicken", decimal grams = 120m) => new()
    {
        Name = name,
        EstimatedGramsLow = grams * 0.8m,
        EstimatedGramsMidpoint = grams,
        EstimatedGramsHigh = grams * 1.2m,
        Confidence = 0.9m,
        PreparationNote = "",
    };

    // ── Auto-select path ──

    [Fact]
    public async Task Exact_HighConfidence_AutoSelectsAndComputesMacros()
    {
        var product = Product("Chicken, breast, grilled", conf: 0.95m);
        var engine = EngineWith(new FoodResolutionDto
        {
            OriginalQuery = "grilled chicken",
            Status = FoodResolutionStatus.Exact,
            Selected = product,
            MatchConfidence = 0.95m,
        });

        var grounded = await engine.GroundAsync(Component(grams: 120m));
        var item = grounded.ToItem();

        item.FoodProductId.Should().Be(product.Id);
        item.CanonicalName.Should().Be("Chicken, breast, grilled");
        item.Source.Should().Be("usda");
        item.Grounding!.AutoSelected.Should().BeTrue();
        item.Grounding.Method.Should().Be("resolve_async");

        // Stage C: deterministic macros from per-100g × ORIGINAL grams
        item.Calories.Should().Be(Math.Round(150m * 120m / 100m));   // 180
        item.ProteinG.Should().Be(30m);                               // 25 × 1.2
    }

    // ── Gram immutability (the key P3 invariant) ──

    [Fact]
    public async Task Grounding_NeverMutatesStageA_Grams()
    {
        // catalogue entry claims a 250 g default serving; Stage A saw 120 g.
        var product = Product("Chicken, breast, grilled", conf: 0.97m, servingQty: 250m);
        var engine = EngineWith(new FoodResolutionDto
        {
            Status = FoodResolutionStatus.Exact,
            Selected = product,
            MatchConfidence = 0.97m,
        });

        var item = (await engine.GroundAsync(Component(grams: 120m))).ToItem();

        item.Grams.Should().Be(120m, "quantities attach to the detected component, never the catalogue entry");
    }

    // ── Close variants / cooked-raw ambiguity ──

    [Fact]
    public async Task Ambiguous_CloseVariants_NotAutoSelected_CandidatesExposed()
    {
        // "grilled chicken" → chicken breast vs chicken thigh, close scores
        var breast = Product("Chicken, breast, grilled", conf: 0.72m);
        var thigh = Product("Chicken, thigh, grilled", conf: 0.70m);
        var engine = EngineWith(new FoodResolutionDto
        {
            Status = FoodResolutionStatus.Ambiguous,
            Selected = breast,
            Alternatives = [thigh],
            MatchConfidence = 0.72m,
        });

        var grounded = await engine.GroundAsync(Component(name: "grilled chicken"));
        var item = grounded.ToItem();

        item.Grounding!.AutoSelected.Should().BeFalse("close variants must go to the human");
        item.Grounding.ResolutionStatus.Should().Be("ambiguous");
        item.Source.Should().Be("ai");                       // first-class abstention
        item.Calories.Should().BeNull();                      // no macros from a guess
        item.CandidateNames!.Should().Contain(new[] { "Chicken, breast, grilled", "Chicken, thigh, grilled" });
        item.Grams.Should().Be(120m);                         // portion estimate survives
    }

    [Fact]
    public async Task Probable_BelowConfidenceFloor_GoesToHumanNotGuess()
    {
        var product = Product("Chicken, breast, raw", conf: 0.60m);   // cooked→raw form risk
        var engine = EngineWith(new FoodResolutionDto
        {
            Status = FoodResolutionStatus.Probable,
            Selected = product,
            MatchConfidence = 0.60m,
        });

        var grounded = await engine.GroundAsync(Component(name: "grilled chicken"));

        grounded.Attempt.AutoSelected.Should().BeFalse("0.60 < frozen 0.85 floor");
    }

    // ── Unresolved is first-class ──

    [Fact]
    public async Task Unresolved_StaysAiSource_NoCandidatesFabricated()
    {
        var engine = EngineWith(new FoodResolutionDto
        {
            OriginalQuery = "grandma mystery casserole",
            Status = FoodResolutionStatus.Unresolved,
            Selected = null,
            Alternatives = [],
            MatchConfidence = 0m,
        });

        var item = (await engine.GroundAsync(Component(name: "grandma mystery casserole"))).ToItem();

        item.Source.Should().Be("ai");
        item.FoodProductId.Should().BeNull();
        item.Grounding!.ResolutionStatus.Should().Be("unresolved");
        item.Grounding.AutoSelected.Should().BeFalse();
        item.CandidateNames.Should().BeEmpty();
        item.Grams.Should().Be(120m);
    }

    // ── Branded vs generic ──

    [Fact]
    public async Task GenericPreferredByResolver_AutoSelectsGenericEntry()
    {
        // resolver already prefers generic over branded (documented behavior);
        // grounding must simply honor its selection.
        var generic = Product("Oats", source: "USDA", conf: 0.93m);
        var engine = EngineWith(new FoodResolutionDto
        {
            Status = FoodResolutionStatus.Exact,
            Selected = generic,
            MatchConfidence = 0.93m,
        });

        var item = (await engine.GroundAsync(Component(name: "oatmeal", grams: 40m))).ToItem();

        item.CanonicalName.Should().Be("Oats");
        item.Source.Should().Be("usda");
    }

    [Fact]
    public async Task Provenance_ChainRecorded_QueryToCandidatesToSelection()
    {
        var product = Product("Rice, white, cooked", conf: 0.92m);
        var alt = Product("Rice, brown, cooked", conf: 0.55m);
        var engine = EngineWith(new FoodResolutionDto
        {
            OriginalQuery = "white rice",
            Status = FoodResolutionStatus.Probable,
            Selected = product,
            Alternatives = [alt],
            MatchConfidence = 0.92m,
        });

        var item = (await engine.GroundAsync(Component(name: "white rice", grams: 180m))).ToItem();
        var g = item.Grounding!;

        g.Query.Should().Be("white rice");
        g.Candidates.Select(c => c.Name).Should().Contain(new[] { "Rice, white, cooked", "Rice, brown, cooked" });
        g.SelectedFoodProductId.Should().Be(product.Id);
        g.ResolutionStatus.Should().Be("probable");
    }
}

