using System.Text.Json.Serialization;

namespace GutAI.Application.Common.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
// Stage B3 — free web-results cascade (docs/meal-scan-detailed-design.md §4.4)
//
// Chain: cache → (resolver already tried) → DuckDuckGo HTML search →
// Jina Reader fetch → LLM extraction → plausibility gate → cache write.
// Zero recurring cost: DDG + Jina are keyless; only cheap extraction tokens.
// Values are PER 100 g. Never touches FODMAP flags (curated data stays
// authoritative). Failure at any stage ⇒ null ⇒ caller falls back to ai-source.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Authoritative nutrition values scraped from the web, per 100 g.</summary>
public sealed record WebNutritionResult
{
    public required decimal CaloriesKcal { get; init; }
    public required decimal ProteinG { get; init; }
    public required decimal CarbsG { get; init; }
    public required decimal FatG { get; init; }
    public decimal? FiberG { get; init; }
    public decimal? SugarG { get; init; }
    public decimal? SodiumMg { get; init; }
    public required string SourceName { get; init; }
    public required string SourceUrl { get; init; }

    /// <summary>The food name this entry was cached under (normalized).</summary>
    [JsonPropertyName("cache_key")]
    public string? CacheKey { get; init; }
}

public interface IWebNutritionLookup
{
    /// <summary>
    /// Look up per-100g nutrition for a food via the free web cascade.
    /// Returns null when nothing credible was found (caller keeps ai-source).
    /// Implementations must be fail-soft: never throw for "not found".
    /// </summary>
    Task<WebNutritionResult?> LookupAsync(string foodName, CancellationToken ct = default);
}

/// <summary>LLM extraction schema from fetched page markdown (strict JSON).</summary>
public sealed class WebNutritionExtraction
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("calories_kcal")]
    public decimal CaloriesKcal { get; set; }

    [JsonPropertyName("protein_g")]
    public decimal ProteinG { get; set; }

    [JsonPropertyName("carbs_g")]
    public decimal CarbsG { get; set; }

    [JsonPropertyName("fat_g")]
    public decimal FatG { get; set; }

    [JsonPropertyName("fiber_g")]
    public decimal? FiberG { get; set; }

    [JsonPropertyName("sugar_g")]
    public decimal? SugarG { get; set; }

    [JsonPropertyName("sodium_mg")]
    public decimal? SodiumMg { get; set; }

    [JsonPropertyName("source_name")]
    public string SourceName { get; set; } = "";

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = "";
}
