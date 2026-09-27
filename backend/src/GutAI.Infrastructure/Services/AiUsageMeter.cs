using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Services;

public sealed record AiStageUsage(
    string Stage,
    string Deployment,
    int Calls,
    long InputTokens,
    long OutputTokens,
    TimeSpan TotalElapsed);

public sealed record AiUsageSummary(
    IReadOnlyList<AiStageUsage> Stages,
    long InputTokens,
    long OutputTokens,
    int ModelCalls,
    TimeSpan TotalModelTime,
    decimal? EstimatedCostUsd);

public sealed class AiUsageMeter
{
    private static readonly Meter Meter = new("GutAI.AI");
    private static readonly Histogram<double> CostHistogram = Meter.CreateHistogram<double>("gutai.ai.operation.cost_usd");
    private static readonly Histogram<double> ModelTimeHistogram = Meter.CreateHistogram<double>("gutai.ai.operation.model_ms", "ms");
    private static readonly Counter<long> TokenCounter = Meter.CreateCounter<long>("gutai.ai.operation.tokens", "tokens");
    private static readonly AsyncLocal<AiUsageMeter?> Ambient = new();

    private readonly object _gate = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly List<Call> _calls = [];

    public static AiUsageMeter? Current => Ambient.Value;

    public IDisposable BeginScope()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new Scope(previous);
    }

    private sealed class Scope(AiUsageMeter? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            Ambient.Value = previous;
            _disposed = true;
        }
    }


    public AiUsageMeter(IConfiguration config, ILogger logger)
    {
        _configuration = config;
        _logger = logger;
    }

    public void Record(string stage, string deployment, long? inputTokens, long? outputTokens, TimeSpan elapsed)
    {
        lock (_gate)
            _calls.Add(new Call(stage, deployment, inputTokens, outputTokens, elapsed));
    }

    public AiUsageSummary Summarize()
    {
        Call[] calls;
        lock (_gate)
            calls = [.. _calls];

        var stages = calls
            .GroupBy(call => (call.Stage, call.Deployment))
            .Select(group => new AiStageUsage(
                group.Key.Stage,
                group.Key.Deployment,
                group.Count(),
                group.Sum(call => call.InputTokens ?? 0),
                group.Sum(call => call.OutputTokens ?? 0),
                TimeSpan.FromTicks(group.Sum(call => call.Elapsed.Ticks))))
            .ToArray();

        var inputTokens = calls.Sum(call => call.InputTokens ?? 0);
        var outputTokens = calls.Sum(call => call.OutputTokens ?? 0);
        decimal? cost = null;
        if (calls.All(call => call.InputTokens.HasValue && call.OutputTokens.HasValue))
        {
            decimal total = 0;
            var known = true;
            foreach (var call in calls)
            {
                var inputPrice = _configuration[$"AzureOpenAI:Pricing:{call.Deployment}:InputPer1M"];
                var outputPrice = _configuration[$"AzureOpenAI:Pricing:{call.Deployment}:OutputPer1M"];
                if (!decimal.TryParse(inputPrice, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var inputRate) ||
                    !decimal.TryParse(outputPrice, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var outputRate))
                {
                    known = false;
                    break;
                }

                total += call.InputTokens!.Value * inputRate / 1_000_000m;
                total += call.OutputTokens!.Value * outputRate / 1_000_000m;
            }

            if (known)
                cost = total;
        }

        return new AiUsageSummary(
            stages,
            inputTokens,
            outputTokens,
            calls.Length,
            TimeSpan.FromTicks(calls.Sum(call => call.Elapsed.Ticks)),
            cost);
    }

    public void LogSummary(string operation, Guid correlationId)
    {
        var summary = Summarize();
        var modelMs = summary.TotalModelTime.TotalMilliseconds;
        _logger.LogInformation(
            "AI usage {Operation} {CorrelationId}: calls={ModelCalls} in={InputTokens} out={OutputTokens} modelMs={ModelMs} costUsd={EstimatedCostUsd} stages={Stages}",
            operation,
            correlationId,
            summary.ModelCalls,
            summary.InputTokens,
            summary.OutputTokens,
            modelMs,
            summary.EstimatedCostUsd,
            summary.Stages);

        var tags = new TagList { { "operation", operation } };
        if (summary.EstimatedCostUsd is { } cost)
            CostHistogram.Record((double)cost, tags);
        ModelTimeHistogram.Record(modelMs, tags);
        TokenCounter.Add(summary.InputTokens, new TagList { { "operation", operation }, { "direction", "input" } });
        TokenCounter.Add(summary.OutputTokens, new TagList { { "operation", operation }, { "direction", "output" } });
    }

    private sealed record Call(string Stage, string Deployment, long? InputTokens, long? OutputTokens, TimeSpan Elapsed);
}
