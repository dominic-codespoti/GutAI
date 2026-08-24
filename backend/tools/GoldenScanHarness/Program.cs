using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoldenScanHarness;

/// <summary>
/// Golden-image regression harness for meal-scan Stage A.
///
/// Runs the PRODUCTION vision-decomposition path (IMealVisionStage → MealScanService)
/// against a directory of real meal photos with hand-entered ground truth, scores
/// component recall + gram error, and optionally gates (nonzero exit on regression).
///
/// Usage:
///   dotnet run --project tools/GoldenScanHarness -- --images ../golden-images
///   dotnet run --project tools/GoldenScanHarness -- --images ../golden-images --gate
///   dotnet run -- ... --refresh        (ignore cache, re-run all images)
///
/// Requires Azure OpenAI config via environment or appsettings:
///   AzureOpenAI__Endpoint, AzureOpenAI__VisionDeployment (or DeploymentName)
/// Results are cached per (image hash + prompt version) in golden-images/.cache/
/// so re-runs only bill for new or changed images/prompts.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> Main(string[] args)
    {
        string? imagesDir = null;
        var gate = false;
        var refresh = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--images": imagesDir = args[++i]; break;
                case "--gate": gate = true; break;
                case "--refresh": refresh = true; break;
            }
        }

        if (imagesDir is null)
        {
            Console.Error.WriteLine("Usage: --images <dir> [--gate] [--refresh] [--manifest <path>]");
            return 2;
        }

        var manifestPath = Path.Combine(imagesDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"No manifest at {manifestPath}. See README.md for the capture protocol.");
            return 2;
        }

        var manifest = JsonSerializer.Deserialize<GoldenManifest>(File.ReadAllText(manifestPath), JsonOpts);
        if (manifest is null || manifest.Cases.Count == 0)
        {
            Console.Error.WriteLine("Manifest has no cases. Add photos + expected components first.");
            return 2;
        }

        // ── Build the production Stage-A pipeline ──
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.harness.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var endpoint = configuration["AzureOpenAI:Endpoint"];
        var deployment = configuration["AzureOpenAI:VisionDeployment"] ?? configuration["AzureOpenAI:DeploymentName"];
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deployment))
        {
            Console.Error.WriteLine("Set AzureOpenAI__Endpoint and AzureOpenAI__VisionDeployment (env or appsettings.harness.json).");
            return 2;
        }

        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint),
            new Azure.Identity.AzureCliCredential());
#pragma warning disable OPENAI001 // experimental Responses surface
        var innerClient = configuration["AzureOpenAI:Transport"] == "chat"
            ? azureClient.GetChatClient(deployment).AsIChatClient()
            : azureClient.GetResponsesClient().AsIChatClient(deployment);
#pragma warning restore OPENAI001
        IChatClient chatClient = new ChatClientBuilder(innerClient)
            .UseLogging(loggerFactory)
            .Build();

        IMealVisionStage stage = new MealScanService(
            chatClient, new NullTableStore(), configuration,
            new NoopFoodSearch(), new NoopWebLookup(),
            new FodmapService(), new GutRiskService(),
            loggerFactory.CreateLogger<MealScanService>());

        // ── Cache ──
        var cacheDir = Path.Combine(imagesDir, ".cache");
        Directory.CreateDirectory(cacheDir);
        var cachePath = Path.Combine(cacheDir, "results.json");
        var cache = File.Exists(cachePath)
            ? JsonSerializer.Deserialize<Dictionary<string, CachedResult>>(File.ReadAllText(cachePath), JsonOpts) ?? []
            : [];

        // ── Run cases ──
        var scores = new List<GoldenMetrics.CaseScore>();
        foreach (var c in manifest.Cases)
        {
            var imagePath = Path.Combine(imagesDir, c.Image);
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"⚠  {c.Image}: image file missing, skipped.");
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)));
            var cacheKey = $"{hash[..24]}|{MealScanService.VisionPromptVersion}";

            VisionDecomposition decomp;
            if (!refresh && cache.TryGetValue(cacheKey, out var cached) && cached.FailedReason is null)
            {
                decomp = cached.ToDecomposition();
                Console.WriteLine($"·  {c.Image}: cached ({decomp.Components.Count} components)");
            }
            else
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await using var fs = File.OpenRead(imagePath);
                    decomp = await stage.DecomposeAsync(fs, GetContentType(c.Image), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗  {c.Image}: FAILED — {ex.Message}");
                    cache[cacheKey] = CachedResult.Failed(ex.Message);
                    await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, JsonOpts));
                    continue;
                }
                sw.Stop();
                Console.WriteLine($"✓  {c.Image}: {decomp.Components.Count} components in {sw.ElapsedMilliseconds} ms " +
                                  $"(in={decomp.InputTokens ?? 0}/out={decomp.OutputTokens ?? 0} tok)");
                cache[cacheKey] = CachedResult.From(decomp);
                await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, JsonOpts));
            }

            scores.Add(GoldenMetrics.ScoreCase(c, [.. decomp.Components]));
        }

        if (scores.Count == 0)
        {
            Console.Error.WriteLine("No cases produced results.");
            return 2;
        }

        // ── Aggregate report ──
        var recall = scores.Average(s => s.Recall);
        var allErrors = scores.SelectMany(s => s.PerComponent)
            .Where(p => p.Item3 >= 0)
            .Select(p => p.Item3)
            .OrderBy(e => e)
            .ToList();
        var medianError = allErrors.Count == 0 ? double.NaN : Percentile(allErrors, 50);

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════");
        Console.WriteLine($" Cases scored:        {scores.Count}");
        Console.WriteLine($" Mean component recall:   {recall:P1}");
        Console.WriteLine($" Median gram error:       {(double.IsNaN(medianError) ? "n/a" : $"{medianError:F1}%")}");
        Console.WriteLine($" Prompt version:      {MealScanService.VisionPromptVersion}");
        Console.WriteLine("════════════════════════════════════════════");

        foreach (var s in scores)
        {
            Console.WriteLine($"\n— {s.Image}: recall {s.MatchedCount}/{s.ExpectedCount}");
            foreach (var (exp, match, err) in s.PerComponent)
                Console.WriteLine(err < 0
                    ? $"     MISS  '{exp}'"
                    : $"     MATCH '{exp}' ↔ '{match}' ({err:F1}% error)");
        }

        var reportPath = Path.Combine(cacheDir, "last-report.json");
        var report = JsonSerializer.Serialize(new
        {
            prompt_version = MealScanService.VisionPromptVersion,
            generated_at = DateTimeOffset.UtcNow,
            recall,
            median_gram_error_percent = double.IsNaN(medianError) ? (double?)null : medianError,
            cases = scores,
        }, new JsonSerializerOptions(JsonOpts) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine($"\nReport: {reportPath}");

        if (!gate) return 0;

        var pass = recall >= manifest.Gate.MinRecall
                   && (double.IsNaN(medianError) || medianError <= manifest.Gate.MaxMedianGramErrorPercent);
        Console.WriteLine(pass
            ? $"\nGATE PASS (recall ≥ {manifest.Gate.MinRecall:P0}, median error ≤ {manifest.Gate.MaxMedianGramErrorPercent:F0}%)"
            : $"\nGATE FAIL — thresholds: recall ≥ {manifest.Gate.MinRecall:P0}, median error ≤ {manifest.Gate.MaxMedianGramErrorPercent:F0}%");
        return pass ? 0 : 1;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        var idx = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }

    private static string GetContentType(string fileName) =>
        fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

    private sealed record CachedResult(
        [property: JsonPropertyName("components")] List<ScannedComponent> Components,
        [property: JsonPropertyName("ref_visible")] bool RefVisible,
        [property: JsonPropertyName("scale_notes")] string ScaleNotes,
        [property: JsonPropertyName("overall_confidence")] decimal OverallConfidence,
        [property: JsonPropertyName("dropped")] List<string> Dropped,
        [property: JsonPropertyName("raw")] string Raw,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("failed")] string? FailedReason)
    {
        public VisionDecomposition ToDecomposition() => new(
            Components, RefVisible, ScaleNotes, OverallConfidence, Dropped, Raw, Prompt, null, null);

        public static CachedResult From(VisionDecomposition d) => new(
            [.. d.Components], d.ReferenceObjectVisible, d.ScaleNotes, d.OverallConfidence,
            [.. d.DroppedNotes], d.RawJson, d.PromptVersion, null);

        public static CachedResult Failed(string reason) => new([], false, "", 0, [], "", "", reason);
    }

    /// <summary>Web cascade not exercised by the harness.</summary>
    private sealed class NoopWebLookup : IWebNutritionLookup
    {
        public Task<WebNutritionResult?> LookupAsync(string foodName, CancellationToken ct = default)
            => Task.FromResult<WebNutritionResult?>(null);
    }

    /// <summary>Grounding is not exercised by the harness (Stage B is deterministic and unit-tested separately).</summary>
    private sealed class NoopFoodSearch : IFoodSearchService
    {
        public Task<IReadOnlyList<FoodProductDto>> SearchAsync(string query, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<FoodProductDto>> SearchPersonalizedAsync(string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<FoodResolutionDto> ResolveAsync(string query, IReadOnlyCollection<Guid> boostIds, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<FoodProductDto?> LookupBarcodeAsync(string barcode, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>The harness never persists scan sessions — no-op store.</summary>
    private sealed class NullTableStore : ITableStore
    {
        public Task UpsertScanSessionAsync(ScanSessionRecord session, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ScanSessionRecord?> GetScanSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
            => Task.FromResult<ScanSessionRecord?>(null);
        public Task DeleteScanSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WebNutritionResult?> GetWebNutritionCacheAsync(string normalizedName, CancellationToken ct = default)
            => Task.FromResult<WebNutritionResult?>(null);
        public Task UpsertWebNutritionCacheAsync(WebNutritionResult result, CancellationToken ct = default) => Task.CompletedTask;

        // Everything below is unreachable for the harness but required by the interface.
    public Task<User?> GetUserAsync(Guid userId, CancellationToken ct ) => Task.FromResult<User?>(null);

    public Task UpsertUserAsync(User user, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteUserAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<IdentityRecord?> GetIdentityByIdAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<IdentityRecord?> GetIdentityByEmailAsync(string email, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertIdentityAsync(IdentityRecord identity, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteIdentityAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<MealLog?> GetMealLogAsync(Guid userId, Guid mealId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<MealLog>> GetMealLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<MealLog>> GetMealLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertMealLogAsync(MealLog meal, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<MealItem>> GetMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<MealItem>> GetAllUserMealItemsAsync(Guid userId, int limit , CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertMealItemsAsync(Guid userId, Guid mealLogId, List<MealItem> items, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<SymptomLog?> GetSymptomLogAsync(Guid userId, Guid symptomId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<SymptomLog>> GetSymptomLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<SymptomLog>> GetSymptomLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertSymptomLogAsync(SymptomLog symptom, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<SymptomType>> GetAllSymptomTypesAsync(CancellationToken ct ) => throw new NotSupportedException();

    public Task<SymptomType?> GetSymptomTypeAsync(int id, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertSymptomTypeAsync(SymptomType type, CancellationToken ct ) => throw new NotSupportedException();

    public Task<bool> SymptomTypeExistsAsync(int id, CancellationToken ct ) => throw new NotSupportedException();

    public Task<FoodProduct?> GetFoodProductAsync(Guid id, CancellationToken ct ) => throw new NotSupportedException();

    public Task<FoodProduct?> GetFoodProductByBarcodeAsync(string barcode, CancellationToken ct ) => throw new NotSupportedException();

    public Task<FoodProduct?> GetFoodProductBySourceAsync(string dataSource, string externalId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<FoodProduct>> SearchFoodProductsAsync(string query, int maxResults, CancellationToken ct ) => throw new NotSupportedException();

    public Task<Dictionary<Guid, string?>> GetFoodProductSafetyRatingsAsync(IEnumerable<Guid> ids, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertFoodProductAsync(FoodProduct product, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<FoodAdditive>> GetAllFoodAdditivesAsync(CancellationToken ct ) => throw new NotSupportedException();

    public Task<FoodAdditive?> GetFoodAdditiveAsync(int id, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertFoodAdditiveAsync(FoodAdditive additive, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<int>> GetAdditiveIdsForProductAsync(Guid foodProductId, CancellationToken ct ) => throw new NotSupportedException();

    public Task SetAdditiveIdsForProductAsync(Guid foodProductId, List<int> additiveIds, CancellationToken ct ) => throw new NotSupportedException();

    public Task<RefreshToken?> GetRefreshTokenByValueAsync(string token, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<RefreshToken>> GetActiveRefreshTokensAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertRefreshTokenAsync(RefreshToken token, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteRefreshTokensForUserAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<DailyNutritionSummary?> GetDailyNutritionSummaryAsync(Guid userId, DateOnly date, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertDailyNutritionSummaryAsync(DailyNutritionSummary summary, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<UserFoodAlert>> GetUserFoodAlertsAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<UserFoodAlert?> GetUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertUserFoodAlertAsync(UserFoodAlert alert, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<FavoriteFoodProduct>> GetUserFavoriteFoodsAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<FavoriteFoodProduct?> GetUserFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertFavoriteFoodAsync(FavoriteFoodProduct favorite, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<InsightReport?> GetInsightReportAsync(Guid userId, Guid reportId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<InsightReport>> GetInsightReportsAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertInsightReportAsync(InsightReport report, CancellationToken ct ) => throw new NotSupportedException();

    public Task<CustomFood?> GetCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<CustomFood>> GetCustomFoodsAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    public Task UpsertCustomFoodAsync(CustomFood food, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct ) => throw new NotSupportedException();

    public Task<List<CoachChatMessage>> GetRecentCoachMessagesAsync(Guid userId, int limit, CancellationToken ct ) => Task.FromResult<List<CoachChatMessage>>([]);

    public Task UpsertCoachMessageAsync(Guid userId, DateTimeOffset at, string role, string text, CancellationToken ct ) => throw new NotSupportedException();

    public Task DeleteCoachMessagesAsync(Guid userId, CancellationToken ct ) => throw new NotSupportedException();

    }
}
