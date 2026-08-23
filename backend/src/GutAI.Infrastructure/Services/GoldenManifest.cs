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
}

public sealed class GoldenCase
{
    [JsonPropertyName("image")]
    public string Image { get; set; } = ""; // file name relative to the images directory

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
