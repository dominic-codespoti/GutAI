using System.Net;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Stage B3 — free web-results cascade (zero recurring cost).
///
///   cache hit → DuckDuckGo HTML search → top page via Jina Reader →
///   LLM extraction (strict JSON) → plausibility gate → cache write.
///
/// Fail-soft by contract: every failure mode returns null and the caller keeps
/// the ai-source item. Hard 8 s timeout per network stage; max queries per scan
/// capped by the caller. Never used for FODMAP flags.
/// </summary>
public class WebNutritionCascade : IWebNutritionLookup
{
    private const string SearchUrl = "https://html.duckduckgo.com/html/?q={0}";
    private const string JinaReaderUrl = "https://r.jina.ai/{0}";
    private static readonly string[] PreferredDomains =
        ["fdc.nal.usda.gov", "www.nutritionvalue.org", "openfoodfacts.org"];

    /// <summary>
    /// Structured extraction options for the configured reasoning deployment.
    /// Temperature remains null so reasoning models omit the unsupported field.
    /// </summary>
    private static readonly ChatOptions ExtractionOptions = new();

    private static readonly ChatRole DeveloperRole = new("developer");

    private readonly IChatClient _chatClient;
    private readonly ITableStore _store;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _searchHttp;
    private readonly HttpClient _readerHttp;
    private readonly bool _enabled;
    private readonly int _cacheTtlDays;
    private readonly int _negativeCacheTtlDays;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebNutritionCascade> _logger;

    public WebNutritionCascade(
        [FromKeyedServices(AiWorkloads.Extraction)] IChatClient chatClient,
        ITableStore store,
        IConfiguration config,
        HttpClient httpClient,
        ILogger<WebNutritionCascade> logger,
        TimeProvider? timeProvider = null)
    {
        _configuration = config;
        _chatClient = chatClient;
        _store = store;
        _enabled = config.GetValue("Features:WebGrounding", false);
        _cacheTtlDays = config.GetValue("WebGrounding:CacheTtlDays", 180);
        _negativeCacheTtlDays = config.GetValue("WebGrounding:NegativeCacheTtlDays", 7);
        _timeProvider = timeProvider ?? TimeProvider.System;
        // One client, 8s hard cap per stage; reader gets a slightly longer one for slow pages.
        _searchHttp = httpClient;
        _searchHttp.Timeout = TimeSpan.FromSeconds(8);
        _readerHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        if (!_searchHttp.DefaultRequestHeaders.Contains("User-Agent"))
            _searchHttp.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (compatible; GutLens/1.0; +https://gutai.app)");
        _logger = logger;
    }

    public async Task<WebNutritionResult?> LookupAsync(string foodName, FoodRegion region, CancellationToken ct = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(foodName)) return null;

        var key = $"{NormalizeName(foodName)}|{region.ToString().ToLowerInvariant()}";
        var now = _timeProvider.GetUtcNow();

        // ── 0. Cache ──
        try
        {
            var cached = await _store.GetWebNutritionCacheEntryAsync(key, ct);
            if (cached is not null)
            {
                var isUsableEntry = cached.IsNegative || cached.Result is not null;
                var ttlDays = cached.IsNegative ? _negativeCacheTtlDays : _cacheTtlDays;
                if (isUsableEntry && now - cached.CachedAt < TimeSpan.FromDays(ttlDays))
                {
                    _logger.LogDebug("Web nutrition cache hit: {Key}", key);
                    return cached.IsNegative ? null : cached.Result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web nutrition cache read failed for {Key}; continuing to network.", key);
        }

        // ── 2. Free search (authoritative domains preferred in ranking) ──
        var query = $"{foodName} nutrition per 100g";
        List<(string Title, string Url)> results;
        try
        {
            results = await SearchDuckDuckGo(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web nutrition search failed for '{Food}'.", foodName);
            return null;
        }
        if (results.Count == 0)
        {
            if (ct.IsCancellationRequested) return null;
            await CacheNegativeAsync(key, ct);
            return null;
        }

        var ordered = results.OrderByDescending(u => DomainScore(u.Url)).Take(2).ToList();

        // ── 3+4. Fetch + extract per candidate until one passes plausibility ──
        foreach (var (_, url) in ordered)
        {
            string? markdown;
            try { markdown = await FetchViaJina(url, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web nutrition fetch failed for {Url}.", url);
                return null;
            }
            if (markdown is null) return null;

            WebNutritionExtraction? extraction;
            try { extraction = await ExtractAsync(foodName, url, markdown, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web nutrition extraction failed for '{Food}' from {Url}.", foodName, url);
                return null;
            }
            if (extraction is null) return null;
            if (!extraction.Found) continue;

            var result = ToResult(extraction, key, url);
            if (result is null || !IsPlausible(result, foodName))
            {
                _logger.LogInformation("Web extraction for '{Food}' from {Url} failed plausibility.", foodName, url);
                continue;
            }

            // ── 5. Cache write (fail-soft) ──
            try { await _store.UpsertWebNutritionCacheAsync(result, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Web nutrition cache write failed for {Key}.", key); }

            return result;
        }

        if (ct.IsCancellationRequested) return null;
        await CacheNegativeAsync(key, ct);
        return null;
    }

    private async Task CacheNegativeAsync(string key, CancellationToken ct)
    {
        try { await _store.UpsertWebNutritionNegativeCacheAsync(key, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Web nutrition negative-cache write failed for {Key}.", key); }
    }


    // ── DuckDuckGo HTML parsing ──

    internal virtual async Task<List<(string Title, string Url)>> SearchDuckDuckGo(string query, CancellationToken ct)
    {
        using var response = await _searchHttp.GetAsync(string.Format(SearchUrl, Uri.EscapeDataString(query)), ct);
        response.EnsureSuccessStatusCode();
        await using var stream = response.Content.ReadAsStream(ct);
        using var reader = new StreamReader(stream);
        return DuckDuckGoParser.ParseResults(await reader.ReadToEndAsync(ct));
    }

    internal static class DuckDuckGoParser // nested for internal test access
    {
        /// <summary>Parses DDG /html result links; decodes uddg redirects.</summary>
        public static List<(string Title, string Url)> ParseResults(string html)
        {
            var results = new List<(string, string)>();
            if (string.IsNullOrEmpty(html)) return results;

            var linkRegex = new System.Text.RegularExpressions.Regex(
                @"<a[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in linkRegex.Matches(html))
            {
                var href = WebUtility.HtmlDecode(m.Groups[1].Value);
                var title = System.Text.RegularExpressions.Regex.Replace(
                    WebUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", "").Trim();

                // DDG wraps outbound links in /l/?uddg=<urlencoded>
                if (href.Contains("uddg="))
                {
                    var start = href.IndexOf("uddg=") + 5;
                    var end = href.IndexOf('&', start);
                    var encoded = end > start ? href[start..end] : href[start..];
                    if (Uri.UnescapeDataString(encoded) is { Length: > 0 } real)
                        href = real;
                }

                if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) && title.Length > 0)
                    results.Add((title, href));
            }

            return results.DistinctBy(r => r.Item2).ToList();
        }
    }

    // ── Jina Reader fetch ──

    internal virtual async Task<string?> FetchViaJina(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _readerHttp.GetAsync(string.Format(JinaReaderUrl, url), ct);
            response.EnsureSuccessStatusCode();
            await using var stream = response.Content.ReadAsStream(ct);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync(ct);
            // Cap context fed to the extraction model.
            return text.Length <= 14_000 ? text : text[..14_000];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jina fetch failed for {Url}.", url);
            return null;
        }
    }

    // ── LLM extraction (cheap text call, strict JSON) ──

    internal virtual async Task<WebNutritionExtraction?> ExtractAsync(string foodName, string url, string markdown, CancellationToken ct)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new(DeveloperRole,
                    """
                    You extract nutrition facts from web pages. Given page content, find the
                    nutritional composition PER 100 g for the requested food. Prefer USDA /
                    FoodData Central figures when sources disagree. If the page has no usable
                    per-100 g composition table for this food, set found=false. Numbers are
                    per 100 g: kcal, protein/carbs/fat/fiber/sugar in grams, sodium in mg.
                    """),
                new(ChatRole.User,
                    $"Food: {foodName}\nPage URL: {url}\n\nPage content:\n{markdown}"),
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _chatClient.GetResponseAsync<WebNutritionExtraction>(
                messages, options: ExtractionOptions, useJsonSchemaResponseFormat: true, cancellationToken: ct);
            stopwatch.Stop();
            AiUsageMeter.Current?.Record(
                "web_extraction",
                AiWorkloads.ResolveDeployment(_configuration, AiWorkloads.Extraction),
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount,
                stopwatch.Elapsed);
            return response.Result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extraction failed for '{Food}' from {Url}.", foodName, url);
            return null;
        }
    }

    // ── Plausibility gate ──

    internal static WebNutritionResult? ToResult(WebNutritionExtraction e, string cacheKey, string fetchedUrl)
    {
        if (!e.Found) return null;
        return new WebNutritionResult
        {
            CaloriesKcal = e.CaloriesKcal,
            ProteinG = e.ProteinG,
            CarbsG = e.CarbsG,
            FatG = e.FatG,
            FiberG = e.FiberG,
            SugarG = e.SugarG,
            SodiumMg = e.SodiumMg,
            SourceName = string.IsNullOrWhiteSpace(e.SourceName) ? new Uri(fetchedUrl).Host : e.SourceName,
            SourceUrl = fetchedUrl,
            CacheKey = cacheKey,
        };
    }

    internal static bool IsPlausible(WebNutritionResult result, string? foodName = null) =>
        NutritionSanity.Check(NutritionCalculator.BasisFrom(result), NutritionSanityProfile.Web, foodName).IsPlausible;

    internal static string NormalizeName(string name)
    {
        var cleaned = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static int DomainScore(string url)
    {
        var host = new Uri(url).Host.ToLowerInvariant();
        for (var i = 0; i < PreferredDomains.Length; i++)
            if (host.EndsWith(PreferredDomains[i], StringComparison.Ordinal))
                return PreferredDomains.Length - i;
        return 0;
    }
}
