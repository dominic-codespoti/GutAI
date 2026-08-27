using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Services;

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
        string imagesDir,
        GoldenManifest manifest,
        bool confirm,
        int repeat,
        string apiUrl)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(5),
        };

        var scores = new List<GoldenMetrics.CaseScore>();
        var groundedItems = 0;
        var autoSelectedItems = 0;
        var estimatedItems = 0;
        var totalExpectedItems = 0;
        var nutritionBackedMatches = 0;
        var falsePositiveItems = 0;
        var confirmedMeals = 0;
        foreach (var goldenCase in manifest.Cases)
        {
            for (var run = 1; run <= repeat; run++)
            {
            var email = $"golden-e2e-{Guid.NewGuid():N}@example.com";
            var register = await client.PostAsJsonAsync("api/auth/register", new
            {
                email,
                password = "GoldenE2e123!",
                displayName = $"Golden E2E {goldenCase.Image}",
            });
            var auth = await ReadOrThrowAsync<AuthResponse>(register, $"register {goldenCase.Image}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            var imagePath = Path.Combine(imagesDir, goldenCase.Image);
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"⚠  {goldenCase.Image}: image missing, skipped.");
                continue;
            }

            var draft = await ScanAsync(client, imagePath);
            var scannedComponents = ToScannedComponents(draft);
            var score = GoldenMetrics.ScoreCase(goldenCase, scannedComponents);
            scores.Add(score);

            var matched = GoldenMetrics.MatchComponents(goldenCase.Expected, scannedComponents);
            var matchedScannedIndexes = matched.Select(pair => pair.ScannedIdx).ToHashSet();
            var nutritionBacked = matched.Count(pair =>
            {
                var item = draft.Items[pair.ScannedIdx];
                return item.Calories is not null
                       && item.Source != "ai"
                       && item.FoodProductId is { } productId
                       && productId != Guid.Empty;
            });

            var grounded = draft.Items.Count(i => i.Source != "ai" && i.FoodProductId is { } id && id != Guid.Empty);
            var autoSelected = draft.Items.Count(i => i.Grounding?.AutoSelected == true);
            var estimated = draft.Items.Count(i => i.Source == "ai");
            groundedItems += grounded;
            autoSelectedItems += autoSelected;
            estimatedItems += estimated;
            totalExpectedItems += goldenCase.Expected.Count;
            nutritionBackedMatches += nutritionBacked;
            falsePositiveItems += draft.Items.Count - matchedScannedIndexes.Count;

            Console.WriteLine(
                $"✓  {goldenCase.Image} run {run}/{repeat}: recall {score.MatchedCount}/{score.ExpectedCount}, " +
                $"nutrition {nutritionBacked}/{goldenCase.Expected.Count}, extras {draft.Items.Count - matchedScannedIndexes.Count}, " +
                $"grounded {grounded}/{draft.Items.Count}, auto {autoSelected}, estimated {estimated}, " +
                $"kcal {draft.Items.Sum(i => i.Calories ?? 0)}");

            if (confirm)
            {
                var mealId = await ConfirmAsync(client, draft);
                await VerifyMealReadbackAsync(client, mealId, draft.Items.Count);
                confirmedMeals++;
            }
            }
        }

        if (scores.Count == 0)
        {
            Console.Error.WriteLine("No E2E cases produced results.");
            return 2;
        }

        var recall = scores.Average(s => s.Recall);
        var errors = scores.SelectMany(s => s.PerComponent)
            .Where(p => p.Item3 >= 0)
            .Select(p => p.Item3)
            .OrderBy(e => e)
            .ToList();
        var medianError = errors.Count == 0 ? double.NaN : Percentile(errors, 50);
        var nutritionBackedRate = totalExpectedItems == 0
            ? 0
            : (double)nutritionBackedMatches / totalExpectedItems;
        var falsePositiveRate = groundedItems + estimatedItems == 0
            ? 0
            : (double)falsePositiveItems / (groundedItems + estimatedItems);

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════");
        Console.WriteLine($" E2E API:              {apiUrl}");
        Console.WriteLine($" Repeat runs:          {repeat}");
        Console.WriteLine($" Cases scored:         {scores.Count}");
        Console.WriteLine($" Mean component recall: {recall:P1}");
        Console.WriteLine($" Median gram error:     {(double.IsNaN(medianError) ? "n/a" : $"{medianError:F1}%")}");
        Console.WriteLine($" Nutrition-backed rate: {nutritionBackedRate:P1}");
        Console.WriteLine($" False-positive rate:   {falsePositiveRate:P1}");
        Console.WriteLine($" Grounded items:        {groundedItems}");
        Console.WriteLine($" Auto-selected items:   {autoSelectedItems}");
        Console.WriteLine($" Estimated items:       {estimatedItems}");
        Console.WriteLine($" Confirmed meals:       {confirmedMeals}");
        Console.WriteLine("════════════════════════════════════════════");

        foreach (var score in scores)
        {
            Console.WriteLine($"\n— {score.Image}: recall {score.MatchedCount}/{score.ExpectedCount}");
            foreach (var (expected, matched, error) in score.PerComponent)
            {
                Console.WriteLine(error < 0
                    ? $"     MISS  '{expected}'"
                    : $"     MATCH '{expected}' ↔ '{matched}' ({error:F1}% error)");
            }
        }

        var gatePass = recall >= manifest.Gate.MinRecall
                       && (double.IsNaN(medianError) || medianError <= manifest.Gate.MaxMedianGramErrorPercent)
                       && nutritionBackedRate >= manifest.Gate.MinNutritionBackedRate
                       && falsePositiveRate <= manifest.Gate.MaxFalsePositiveRate;
        Console.WriteLine(confirm
            ? "\nProduction-like confirmation and readback completed."
            : "\nScan drafts persisted; rerun with --confirm to exercise meal confirmation/readback.");

        return gatePass ? 0 : 1;
    }

    private static async Task<MealScanDraftDto> ScanAsync(HttpClient client, string imagePath)
    {
        await using var stream = File.OpenRead(imagePath);
        using var content = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(ContentType(imagePath));
        content.Add(file, "file", Path.GetFileName(imagePath));

        var response = await client.PostAsync("api/meals/scan/image", content);
        return await ReadOrThrowAsync<MealScanDraftDto>(response, $"scan {Path.GetFileName(imagePath)}");
    }

    private static async Task<Guid> ConfirmAsync(HttpClient client, MealScanDraftDto draft)
    {
        var body = new
        {
            mealType = "Snack",
            loggedAt = DateTimeOffset.UtcNow,
            items = draft.Items.Select(item => new
            {
                itemId = item.ItemId,
                name = item.CanonicalName ?? item.Name,
                grams = item.Grams,
                foodProductId = item.FoodProductId,
                source = item.Source,
                sourceUrl = item.SourceUrl,
                matchConfidence = item.MatchConfidence,
                visionConfidence = item.VisionConfidence,
                calories = item.Calories,
                proteinG = item.ProteinG,
                carbsG = item.CarbsG,
                fatG = item.FatG,
                fiberG = item.FiberG,
                sugarG = item.SugarG,
                sodiumMg = item.SodiumMg,
            }),
        };

        var response = await client.PutAsJsonAsync($"api/meals/scan/{draft.ScanSessionId}/confirm", body);
        var result = await ReadOrThrowAsync<MealConfirmResponse>(response, $"confirm {draft.ScanSessionId}");
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

    private static List<ScannedComponent> ToScannedComponents(MealScanDraftDto draft) =>
        draft.Items.Select(item => new ScannedComponent
        {
            Name = item.Name,
            EstimatedGramsLow = item.Grams,
            EstimatedGramsMidpoint = item.Grams,
            EstimatedGramsHigh = item.Grams,
            Confidence = item.VisionConfidence,
        }).ToList();

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

    private static double Percentile(List<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken);
    private sealed record MealConfirmResponse(Guid MealId);
}
