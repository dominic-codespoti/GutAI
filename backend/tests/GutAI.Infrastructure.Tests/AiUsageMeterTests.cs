using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class AiUsageMeterTests
{
    [Fact]
    public void Summarize_aggregates_stage_and_deployment_and_computes_configured_cost()
    {
        var meter = CreateMeter(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Pricing:model-a:InputPer1M"] = "2",
            ["AzureOpenAI:Pricing:model-a:OutputPer1M"] = "8",
            ["AzureOpenAI:Pricing:model-b:InputPer1M"] = "1",
            ["AzureOpenAI:Pricing:model-b:OutputPer1M"] = "4"
        });
        meter.Record("vision", "model-a", 1000, 100, TimeSpan.FromMilliseconds(25));
        meter.Record("vision", "model-a", 500, 50, TimeSpan.FromMilliseconds(35));
        meter.Record("selection", "model-a", 200, 20, TimeSpan.FromMilliseconds(10));
        meter.Record("vision", "model-b", 100, 10, TimeSpan.FromMilliseconds(5));

        var summary = meter.Summarize();
        Assert.Equal(4, summary.ModelCalls);
        Assert.Equal(1800, summary.InputTokens);
        Assert.Equal(180, summary.OutputTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(75), summary.TotalModelTime);
        Assert.Equal(0.0049m, summary.EstimatedCostUsd);
        var vision = Assert.Single(summary.Stages, stage => stage.Stage == "vision" && stage.Deployment == "model-a");
        Assert.Equal(2, vision.Calls);
        Assert.Equal(1500, vision.InputTokens);
        Assert.Equal(150, vision.OutputTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(60), vision.TotalElapsed);
        var otherDeployment = Assert.Single(summary.Stages, stage => stage.Stage == "vision" && stage.Deployment == "model-b");
        Assert.Equal(1, otherDeployment.Calls);
        Assert.Equal(100, otherDeployment.InputTokens);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Summarize_returns_unknown_cost_when_price_or_tokens_are_missing(bool hasTokens, bool hasPrice)
    {
        var configuration = new Dictionary<string, string?>();
        if (hasPrice)
        {
            configuration["AzureOpenAI:Pricing:model-a:InputPer1M"] = "2";
            configuration["AzureOpenAI:Pricing:model-a:OutputPer1M"] = "8";
        }

        var meter = CreateMeter(configuration);
        meter.Record("vision", "model-a", hasTokens ? 10 : null, hasTokens ? 5 : null, TimeSpan.Zero);

        Assert.Null(meter.Summarize().EstimatedCostUsd);
    }

    [Fact]
    public void LogSummary_with_no_calls_logs_without_throwing()
    {
        var logger = new Mock<ILogger<AiUsageMeter>>();
        var meter = CreateMeter(null, logger);

        var exception = Record.Exception(() => meter.LogSummary("scan", Guid.NewGuid()));

        Assert.Null(exception);
        logger.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.StartsWith("AI usage scan ", StringComparison.Ordinal)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static AiUsageMeter CreateMeter(
        IDictionary<string, string?>? values,
        Mock<ILogger<AiUsageMeter>>? logger = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();
        return new AiUsageMeter(config, (logger ?? new Mock<ILogger<AiUsageMeter>>()).Object);
    }
}
