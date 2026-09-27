using System.Diagnostics;
using System.Globalization;

namespace GutAI.Infrastructure.Services.Evaluation;

/// <summary>Captures token usage from per-request model chat activities for a named source.</summary>
public sealed class TokenUsageCapture : IDisposable
{
    private readonly object _gate = new();
    private readonly ActivityListener _listener;
    private long _input;
    private long _output;
    private bool _hasInput;
    private bool _hasOutput;

    public TokenUsageCapture(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Capture
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public long? InputTokens { get { lock (_gate) return _hasInput ? _input : null; } }
    public long? OutputTokens { get { lock (_gate) return _hasOutput ? _output : null; } }

    private void Capture(Activity activity)
    {
        if (!string.Equals(activity.GetTagItem("gen_ai.operation.name")?.ToString(), "chat", StringComparison.OrdinalIgnoreCase)) return;

        long? input = null, output = null;
        foreach (var tag in activity.TagObjects)
        {
            var name = tag.Key.ToLowerInvariant();
            if (name is "gen_ai.usage.input_tokens" or "gen_ai.request.input_tokens" or "ai.usage.input_tokens")
                input = AsTokenCount(tag.Value);
            else if (name is "gen_ai.usage.output_tokens" or "gen_ai.request.output_tokens" or "ai.usage.output_tokens")
                output = AsTokenCount(tag.Value);
        }
        lock (_gate)
        {
            if (input is not null) { _input += input.Value; _hasInput = true; }
            if (output is not null) { _output += output.Value; _hasOutput = true; }
        }
    }

    private static long? AsTokenCount(object? value) => value switch
    {
        byte number => number,
        short number => number,
        int number => number,
        long number => number,
        string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };

    public void Dispose() => _listener.Dispose();
}
