using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class ComponentGroundingEngineQueryTests
{
    [Theory]
    [InlineData("katsu curry rice bowl", "katsu curry")]
    [InlineData("taco salad plate", "taco salad")]
    [InlineData("smoothie", "smoothie")]
    public void NormalizeRetrievalQuery_RemovesServingSuffixes(string raw, string expected)
        => Assert.Equal(expected, ComponentGroundingEngine.NormalizeRetrievalQuery(raw));

    [Fact]
    public void NormalizeRetrievalQuery_UsesCoreForLongComposite()
    {
        var actual = ComponentGroundingEngine.NormalizeRetrievalQuery(
            "loaded nachos with grilled meat, lettuce, diced salsa, and guacamole");

        Assert.Equal("loaded nachos", actual);
    }

    [Fact]
    public void BuildResolverQueries_PutsNormalizedQueryFirst()
    {
        var queries = ComponentGroundingEngine.BuildResolverQueries(new ScannedComponent
        {
            Name = "katsu curry rice bowl",
            SearchQueries = ["Japanese katsu curry rice"],
        });

        Assert.Equal("katsu curry", queries[0]);
        Assert.Contains("katsu curry rice bowl", queries);
        Assert.Contains("Japanese katsu curry rice", queries);
        Assert.True(queries.Count <= 3);
    }
}
