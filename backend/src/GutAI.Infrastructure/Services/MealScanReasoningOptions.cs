using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace GutAI.Infrastructure.Services;

/// <summary>Maps the configured meal-scan reasoning checkpoint to Responses options.</summary>
internal static class MealScanReasoningOptions
{
    public static ChatOptions Create(string? configured)
    {
        var normalized = Normalize(configured);
        var options = new ChatOptions();

        if (normalized == "max")
        {
#pragma warning disable OPENAI001 // raw Responses option required for max effort
            options.RawRepresentationFactory = _ => new CreateResponseOptions
            {
                ReasoningOptions = new ResponseReasoningOptions
                {
                    ReasoningEffortLevel = new ResponseReasoningEffortLevel("max"),
                },
            };
#pragma warning restore OPENAI001
            return options;
        }
        var enumValue = normalized == "xhigh" ? "ExtraHigh" : normalized;
        if (Enum.TryParse<ReasoningEffort>(enumValue, ignoreCase: true, out var effort))
            options.Reasoning = new ReasoningOptions { Effort = effort };

        return options;
    }

    public static bool TryNormalize(string? configured, out string normalized)
    {
        normalized = Normalize(configured);
        return normalized is "none" or "low" or "medium" or "high" or "xhigh" or "max";
    }

    public static int Rank(string normalized) => normalized switch
    {
        "none" => 0,
        "low" => 1,
        "medium" => 2,
        "high" => 3,
        "xhigh" => 4,
        "max" => 5,
        _ => -1,
    };

    private static string Normalize(string? configured)
    {
        var value = configured?.Trim().ToLowerInvariant();
        return value switch
        {
            "extrahigh" => "xhigh",
            "x-high" => "xhigh",
            _ => value ?? string.Empty,
        };
    }
}
