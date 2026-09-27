using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace GoldenScanHarness;

/// <summary>
/// Production-like golden runner. Exercises the deployed API over HTTP, which
/// drives the real scan pipeline and its configured Table Storage, providers,
/// confirmation endpoint, and persisted meal readback.
/// </summary>
internal static class ProductionGoldenE2e
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<int> RunAsync(
        string imagesDir, GoldenManifest manifest, bool confirm, int repeat, string apiUrl, bool gate, string reportPath)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(5),
        };
        var evaluations = new List<GoldenMetrics.CaseEvaluation>();
        var repeatedKcal = new Dictionary<string, IReadOnlyList<double>>();
        var repeatedRecall = new Dictionary<string, IReadOnlyList<double>>();
        var stageObservations = new List<GoldenMetrics.StageObservation>();
        var confirmedMeals = 0;
        foreach (var goldenCase in manifest.Cases)
        {
            var imagePath = Path.Combine(imagesDir, goldenCase.Image);
            if (!File.Exists(imagePath)) continue;
            var recallRuns = new List<double>();
            var kcalRuns = new List<double>();
            for (var run = 1; run <= repeat; run++)
            {
                var email = $"golden-e2e-{Guid.NewGuid():N}@example.com";
                var register = await client.PostAsJsonAsync("api/auth/register", new
                {
                    email, password = "GoldenE2e123!", displayName = $"Golden E2E {goldenCase.Image}",
                });
                var auth = await ReadOrThrowAsync<AuthResponse>(register, $"register {goldenCase.Image}");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
                var scanStarted = System.Diagnostics.Stopwatch.StartNew();
                var draft = await ScanAsync(client, imagePath);
                var scanSeconds = scanStarted.Elapsed.TotalSeconds;
                scanStarted.Stop();
                stageObservations.Add(new GoldenMetrics.StageObservation("scan", scanSeconds,
                    double.NaN, double.NaN, double.NaN));
                var predicted = draft.Items.Select(item =>
                {
                    var selected = item.Grounding?.Candidates.FirstOrDefault(c => c.FoodProductId == item.FoodProductId);
                    var identity = selected?.ExternalId is { Length: > 0 } externalId ? $"{selected.Source}:{externalId}" : null;
                    return new GoldenMetrics.GoldenPredictedItem(item.Name, item.Grams, item.PortionLowGrams,
                        item.PortionHighGrams, identity, item.Grounding?.Method, item.Grounding?.MatchConfidence,
                        item.Grounding?.AutoSelected ?? false, item.Calories, item.ProteinG, item.CarbsG, item.FatG);
                }).ToList();
                evaluations.Add(GoldenMetrics.EvaluateCase(goldenCase, predicted));
                recallRuns.Add(evaluations[^1].Recall);
                if (confirm)
                {
                    var mealId = await ConfirmAsync(client, draft);
                    await VerifyMealReadbackAsync(client, mealId, draft.Items.Count(i => i.IncludedByDefault));
                    confirmedMeals++;
                }
                var selectedRun = draft.Items.Where(i => i.IncludedByDefault).Sum(i => i.Calories ?? 0);
                kcalRuns.Add((double)selectedRun);
                Console.WriteLine($"✓  {goldenCase.Image} run {run}/{repeat}: recall {evaluations[^1].Recall:P1}, kcal {selectedRun}");
            }
            repeatedRecall[goldenCase.Image] = recallRuns;
            repeatedKcal[goldenCase.Image] = kcalRuns;
        }
        if (evaluations.Count == 0)
        {
            Console.Error.WriteLine("No E2E cases produced results.");
            return 2;
        }
        var aggregate = GoldenMetrics.Aggregate(evaluations, repeatedKcal, repeatedRecall);
        var perStage = GoldenMetrics.AggregateStages(stageObservations);
        var p95Latency = GoldenMetrics.Percentile(stageObservations
            .Where(s => string.Equals(s.Stage, "scan", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.LatencySeconds), 95);
        var gateResult = GoldenMetrics.EvaluateGate(manifest.Gate, aggregate, p95Latency, null);
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var report = JsonSerializer.Serialize(new
        {
            schema_version = 2, mode = "e2e", api_url = apiUrl, generated_at = DateTimeOffset.UtcNow,
            prompt_version = MealScanService.EffectiveVisionPromptVersion(config),
            deployment = AiWorkloads.ResolveDeployment(config, AiWorkloads.Vision),
            reasoning_effort = AiWorkloads.ResolveReasoningEffort(config, AiWorkloads.Vision) ?? "default",
            grounding_policy_version = GroundingPolicy.PolicyVersion,
            portion_calibrator_version = new PortionCalibrator(config).Version,
            active_flags = new
            {
                hidden_calories = config.GetValue("Features:HiddenCalories", false),
                portion_calibration = config.GetValue("Features:PortionCalibration", false),
                web_grounding = config.GetValue("Features:WebGrounding", false),
            },
            metrics = aggregate, per_stage = perStage, gate = gateResult,
            cases = evaluations, confirmed_meals = confirmedMeals,
        }, new JsonSerializerOptions(JsonOptions) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine($"Report: {reportPath}; confirmation readbacks: {confirmedMeals}");
        return !gate || gateResult.Passed ? 0 : 1;
    }

    private static async Task<MealDraftDto> ScanAsync(HttpClient client, string imagePath)
    {
        await using var stream = File.OpenRead(imagePath);
        using var content = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(ContentType(imagePath));
        content.Add(file, "file", Path.GetFileName(imagePath));

        var response = await client.PostAsync("api/meals/scan/image", content);
        return await ReadOrThrowAsync<MealDraftDto>(response, $"scan {Path.GetFileName(imagePath)}");
    }

    private static async Task<Guid> ConfirmAsync(HttpClient client, MealDraftDto draft)
    {
        var body = new MealDraftCommitRequest
        {
            MealType = "Snack",
            LoggedAt = DateTimeOffset.UtcNow,
        };
        var response = await client.PutAsJsonAsync($"api/meals/drafts/{draft.DraftId}/commit", body);
        var result = await ReadOrThrowAsync<MealDraftCommitResult>(response, $"commit {draft.DraftId}");
        return result.MealId;
    }

    private static async Task VerifyMealReadbackAsync(HttpClient client, Guid mealId, int expectedItems)
    {
        var response = await client.GetAsync($"api/meals/{mealId}");
        using var document = await ReadJsonOrThrowAsync(response, $"readback {mealId}");
        var items = document.RootElement.TryGetProperty("items", out var itemsElement)
            ? itemsElement.GetArrayLength()
            : 0;
        if (items != expectedItems)
            throw new InvalidOperationException($"Meal {mealId} read back {items} items; expected {expectedItems}.");
    }


    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{operation} failed ({(int)response.StatusCode}): {detail}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))
            ?? throw new InvalidOperationException($"{operation} returned an empty response.");
    }

    private static async Task<JsonDocument> ReadJsonOrThrowAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{operation} failed ({(int)response.StatusCode}): {detail}");
        }

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };


    private sealed record AuthResponse(string AccessToken, string RefreshToken);
}
