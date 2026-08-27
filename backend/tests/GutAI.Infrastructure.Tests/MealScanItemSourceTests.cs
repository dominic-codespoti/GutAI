using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

// Health-signal isolation depends on the Source taxonomy: the meal-scan web cascade treats
// Source == "ai" as ungrounded and REPLACES such items. A grounded catalogue product whose
// DataSource is unknown must therefore never map to "ai", or a web replacement could strip
// the catalog match while retaining its FODMAP/gut signals.
public class MealScanItemSourceTests
{
    private static ScannedComponent Component(decimal grams = 200m) => new()
    {
        Name = "rice",
        EstimatedGramsLow = grams * 0.8m,
        EstimatedGramsMidpoint = grams,
        EstimatedGramsHigh = grams * 1.2m,
    };

    private static GroundingAttemptDto Attempt() => new()
    {
        Query = "rice",
        ResolutionStatus = "exact",
        AutoSelected = true,
        MatchConfidence = 0.95m,
        Method = "resolve_async",
        Candidates = [],
    };

    [Fact]
    public void GroundedProduct_WithUnknownDataSource_MapsToDb_NotAi()
    {
        var product = new FoodProductDto { Id = Guid.NewGuid(), Name = "Rice", DataSource = null };

        var item = new GroundedItem(Component(), product, Attempt(), []).ToItem();

        Assert.Equal("db", item.Source);
        Assert.Equal(product.Id, item.FoodProductId);
    }

    [Fact]
    public void UngroundedComponent_MapsToAi_WithoutProductId()
    {
        var item = new GroundedItem(Component(), null, Attempt(), []).ToItem();

        Assert.Equal("ai", item.Source);
        Assert.Null(item.FoodProductId);
    }
}
