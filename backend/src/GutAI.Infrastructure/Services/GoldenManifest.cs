using System.Text.Json.Serialization;

namespace GutAI.Infrastructure.Services;

// ── Manifest schema (golden-images/manifest.json) ──

public sealed class GoldenManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

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

    /// <summary>Minimum component precision; null disables this gate.</summary>
    [JsonPropertyName("min_precision")]
    public double? MinPrecision { get; set; }

    /// <summary>Maximum median absolute kcal percentage error; null disables this gate.</summary>
    [JsonPropertyName("max_median_kcal_error_percent")]
    public double? MaxMedianKcalErrorPercent { get; set; }

    /// <summary>Maximum absolute mean signed kcal bias percentage; null disables this gate.</summary>
    [JsonPropertyName("max_abs_kcal_bias_percent")]
    public double? MaxAbsKcalBiasPercent { get; set; }

    /// <summary>Minimum precision for identity predictions; null disables this gate.</summary>
    [JsonPropertyName("min_identity_precision")]
    public double? MinIdentityPrecision { get; set; }

    /// <summary>Maximum fraction of predicted items abstaining from identity selection; null disables this gate.</summary>
    [JsonPropertyName("max_abstention_rate")]
    public double? MaxAbstentionRate { get; set; }

    /// <summary>Minimum coverage of expected grams by predicted intervals; null disables this gate.</summary>
    [JsonPropertyName("min_interval_coverage")]
    public double? MinIntervalCoverage { get; set; }

    /// <summary>Maximum expected calibration error; null disables this gate.</summary>
    [JsonPropertyName("max_identity_ece")]
    public double? MaxIdentityEce { get; set; }

    /// <summary>Maximum mean across-case kcal coefficient of variation percentage; null disables this gate.</summary>
    [JsonPropertyName("max_kcal_cv_percent")]
    public double? MaxKcalCvPercent { get; set; }

    /// <summary>Maximum p95 latency in seconds; null disables this gate.</summary>
    [JsonPropertyName("max_p95_latency_seconds")]
    public double? MaxP95LatencySeconds { get; set; }

    /// <summary>Maximum p95 cost in USD; null disables this gate.</summary>
    [JsonPropertyName("max_p95_cost_usd")]
    public double? MaxP95CostUsd { get; set; }
}

public sealed class GoldenCase
{
    [JsonPropertyName("image")]
    public string Image { get; set; } = ""; // file name relative to the images directory

    /// <summary>Tags may be no_reference, restaurant, homemade, mixed_dish, beverage, or hidden_fat.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>Whether a reference object is visible in the image; null means unspecified.</summary>
    [JsonPropertyName("reference_object")]
    public bool? ReferenceObject { get; set; }

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

    /// <summary>Whether this expected component was weighed rather than visually estimated.</summary>
    [JsonPropertyName("weighed")]
    public bool Weighed { get; set; }

    /// <summary>Expected component calories in kcal; only measured/catalog-backed values are supplied.</summary>
    [JsonPropertyName("kcal")]
    public decimal? Kcal { get; set; }

    /// <summary>Expected component protein in grams.</summary>
    [JsonPropertyName("protein_g")]
    public decimal? ProteinG { get; set; }

    /// <summary>Expected component carbohydrates in grams.</summary>
    [JsonPropertyName("carbs_g")]
    public decimal? CarbsG { get; set; }

    /// <summary>Expected component fat in grams.</summary>
    [JsonPropertyName("fat_g")]
    public decimal? FatG { get; set; }

    /// <summary>Allowed grounded identities, formatted as source:externalId or name:canonical name.</summary>
    [JsonPropertyName("acceptable_identities")]
    public List<string> AcceptableIdentities { get; set; } = [];
}
