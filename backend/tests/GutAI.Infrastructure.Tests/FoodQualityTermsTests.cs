using FluentAssertions;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodQualityTermsTests
{
    [Fact]
    public void ScoreConditionalPenalties_ImitationTerm_UnlessQueried_IsPenalized()
    {
        FoodQualityTerms.ScoreConditionalPenalties("meatless bacon", "bacon", 1).Should().BeLessThan(0f);
    }

    [Fact]
    public void ScoreConditionalPenalties_ImitationTerm_WhenQueried_IsNotPenalized()
    {
        FoodQualityTerms.ScoreConditionalPenalties("meatless bacon", "meatless bacon", 2).Should().Be(0f);
    }

    [Fact]
    public void ScoreConditionalPenalties_OrganMeat_UnlessQueried_IsPenalized()
    {
        FoodQualityTerms.ScoreConditionalPenalties("chicken liver", "chicken", 1).Should().BeLessThan(0f);
    }

    [Fact]
    public void ScoreConditionalPenalties_CuredTerm_OnlyAppliesForShortQueries()
    {
        var shortQuery = FoodQualityTerms.ScoreConditionalPenalties("smoked salmon", "salmon", 1);
        var longQuery = FoodQualityTerms.ScoreConditionalPenalties("smoked salmon", "smoked salmon fillet with capers", 5);

        shortQuery.Should().BeLessThan(0f);
        longQuery.Should().Be(0f);
    }

    [Fact]
    public void ScoreConditionalPenalties_MechanicallyProcessed_UnlessMechanicallyQueried()
    {
        FoodQualityTerms.ScoreConditionalPenalties("mechanically deboned chicken", "chicken", 1).Should().BeLessThan(0f);
        FoodQualityTerms.ScoreConditionalPenalties("mechanically deboned chicken", "mechanically deboned chicken", 3).Should().Be(0f);
    }

    [Fact]
    public void ScoreModifierRules_EggWhole_GetsBonus()
    {
        FoodQualityTerms.ScoreModifierRules("egg, whole, raw", "egg").Should().BeGreaterThan(0f);
    }

    [Fact]
    public void ScoreModifierRules_EggWhite_UnlessQueried_IsPenalized()
    {
        FoodQualityTerms.ScoreModifierRules("egg, white, raw", "egg").Should().BeLessThan(0f);
    }

    [Fact]
    public void ScoreModifierRules_EggWhite_WhenQueried_IsNotPenalized()
    {
        FoodQualityTerms.ScoreModifierRules("egg, white, raw", "egg white").Should().Be(0f);
    }

    [Fact]
    public void ScoreModifierRules_BaconTurkey_UnlessQueried_IsPenalized()
    {
        FoodQualityTerms.ScoreModifierRules("turkey bacon", "bacon").Should().BeLessThan(0f);
    }

    [Fact]
    public void ScoreModifierRules_CoconutMilkWithoutCoconut_IsPenalized()
    {
        FoodQualityTerms.ScoreModifierRules("almond beverage", "coconut milk").Should().BeLessThan(0f);
    }

    [Fact]
    public void ScoreModifierRules_CornedBeef_ExcludedFromCornRule_WhenQueryAsksForCorned()
    {
        FoodQualityTerms.ScoreModifierRules("corned beef", "corned beef").Should().Be(0f);
    }

    [Fact]
    public void ScoreModifierRules_CornedBeef_PenalizedForPlainCornQuery()
    {
        FoodQualityTerms.ScoreModifierRules("corned beef", "corn").Should().BeLessThan(0f);
    }

    [Fact]
    public void ScoreModifierRules_CrabApple_PenalizedForCrabQuery()
    {
        FoodQualityTerms.ScoreModifierRules("crabapple jelly", "crab").Should().BeLessThan(0f);
    }

    [Fact]
    public void ScoreModifierRules_UnrelatedQuery_ReturnsZero()
    {
        FoodQualityTerms.ScoreModifierRules("anything at all", "widget").Should().Be(0f);
    }

    [Fact]
    public void ScoreModifierRules_RawCitrus_NotJuice_GetsBonus()
    {
        FoodQualityTerms.ScoreModifierRules("orange, raw", "orange").Should().BeGreaterThan(0f);
    }

    [Fact]
    public void ScoreModifierRules_CitrusJuice_UnlessQueried_IsPenalized()
    {
        FoodQualityTerms.ScoreModifierRules("orange juice", "orange").Should().BeLessThan(0f);
    }
}
