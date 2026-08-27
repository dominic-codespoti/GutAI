using System.Text.Json.Serialization;

namespace GutAI.Infrastructure.Services;

// ── Manifest schema (golden-images/manifest.json) ──

public sealed class GoldenManifest
{
    [JsonPropertyName("prompt_version")]
    public string? PromptVersion { get; set; } // informational; cache keys use the code constant

    [JsonPropertyName("gate")]
    public GateThresholds Gate { get; set; } = new();

    [JsonPropertyName("cases")]
    public List<GoldenCase> Cases { get; set; } = [];
}

public sealed class GateThresholds
{
    /// <summary>Minimum fraction of expected components that must be matched.</summary>
    [JsonPropertyName("min_recall")]
    public double MinRecall { get; set; } = 0.80;

    /// <summary>Maximum allowed median gram error over matched components.</summary>
    [JsonPropertyName("max_median_gram_error_percent")]
    public double MaxMedianGramErrorPercent { get; set; } = 35.0;

    /// <summary>Minimum fraction of expected components with a real nutrition-backed product.</summary>
    [JsonPropertyName("min_nutrition_backed_rate")]
    public double MinNutritionBackedRate { get; set; } = 0.70;

    /// <summary>Maximum fraction of scanned items that are unmatched extras.</summary>
    [JsonPropertyName("max_false_positive_rate")]
    public double MaxFalsePositiveRate { get; set; } = 0.35;
}

public sealed class GoldenCase
{
    [JsonPropertyName("image")]
    public string Image { get; set; } = ""; // file name relative to the images directory

    /// <summary>"composite" expects one unified dish; "components" expects separate visible items.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "components";

    [JsonPropertyName("expected")]
    public List<GoldenExpected> Expected { get; set; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class GoldenExpected
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Hand-entered approximate weight in grams (the value you'd write in a food diary).</summary>
    [JsonPropertyName("grams")]
    public decimal Grams { get; set; }
}
