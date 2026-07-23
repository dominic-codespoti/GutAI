using FluentAssertions;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class FoodSynonymsTests
{
    [Fact]
    public void Expand_AlwaysIncludesOriginalTokens()
    {
        var result = FoodSynonyms.Expand("banana", ["banana"]);
        result.Should().Contain("banana");
    }

    [Fact]
    public void Expand_RegionalSingleWordSynonym_AddsCanonicalForm()
    {
        var result = FoodSynonyms.Expand("capsicum", ["capsicum"]);
        result.Should().Contain("peppers");
    }

    [Theory]
    [InlineData("prawns", "shrimp")]
    [InlineData("mince", "ground")]
    [InlineData("rocket", "arugula")]
    [InlineData("coriander", "cilantro")]
    [InlineData("courgette", "zucchini")]
    [InlineData("yoghurt", "yogurt")]
    public void Expand_KnownRegionalTerm_ResolvesToUsEquivalent(string colloquial, string expected)
    {
        FoodSynonyms.Expand(colloquial, [colloquial]).Should().Contain(expected);
    }

    [Fact]
    public void Expand_MultiWordPhrase_AddsFullExpansion()
    {
        var result = FoodSynonyms.Expand("orange juice", ["orange", "juice"]);
        result.Should().Contain("raw");
    }

    [Fact]
    public void Expand_UnknownWord_ReturnsOnlyOriginalToken()
    {
        var result = FoodSynonyms.Expand("xyznonexistentfood", ["xyznonexistentfood"]);
        result.Should().BeEquivalentTo(["xyznonexistentfood"]);
    }

    [Fact]
    public void Expand_DoesNotDuplicateTokens()
    {
        // "ground" also appears verbatim as an input token, and as a synonym of "mince"
        var result = FoodSynonyms.Expand("mince ground beef", ["mince", "ground", "beef"]);
        result.Count(t => t.Equals("ground", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }
}
