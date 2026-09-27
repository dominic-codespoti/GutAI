using GutAI.Application.Common.Helpers;
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

using GutAI.Domain.Enums;

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
/// Results are cached per (image hash + prompt version + deployment + reasoning effort)
/// in golden-images/.cache/ so each model configuration is benchmarked independently.
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
        string? reportPathArg = null;
        var gate = false;
        var refresh = false;
        var mode = "stage-a";
        var confirm = false;
        var liveSelection = false;
        var repeat = 1;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--images" when i + 1 < args.Length: imagesDir = args[++i]; break;
                case "--mode" when i + 1 < args.Length: mode = args[++i]; break;
                case "--report" when i + 1 < args.Length: reportPathArg = args[++i]; break;
                case "--gate": gate = true; break;
                case "--refresh": refresh = true; break;
                case "--confirm": confirm = true; break;
                case "--live-selection": liveSelection = true; break;
                case "--repeat" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out repeat) || repeat < 1)
                    {
                        Console.Error.WriteLine("--repeat must be a positive integer.");
                        return 2;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete option: {args[i]}");
                    return 2;
            }
        }

        if (mode is not ("stage-a" or "in-process" or "e2e"))
        {
            Console.Error.WriteLine("--mode must be stage-a, in-process, or e2e.");
            return 2;
        }
        if (confirm && mode != "e2e")
        {
            Console.Error.WriteLine("--confirm is only valid with --mode e2e.");
            return 2;
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

        if (mode == "e2e")
            return await ProductionGoldenE2e.RunAsync(imagesDir, manifest, confirm, repeat,
                Environment.GetEnvironmentVariable("GUTAI_GOLDEN_API_URL") ?? "http://localhost:5000",
                gate, reportPathArg ?? Path.Combine(imagesDir, ".cache", "last-report.json"));
        if (mode == "in-process")
            return await RunInProcessAsync(imagesDir, manifest, refresh, repeat, gate, liveSelection,
                reportPathArg ?? Path.Combine(imagesDir, ".cache", "last-report.json"));
        var outputReportPath = reportPathArg ?? Path.Combine(imagesDir, ".cache", "last-report.json");
        // ── Build the production Stage-A pipeline ──
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.harness.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var deployment = AiWorkloads.ResolveDeployment(configuration, AiWorkloads.Vision);
        var reasoningEffort = AiWorkloads.ResolveReasoningEffort(configuration, AiWorkloads.Vision) ?? "default";
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        IMealVisionStage? stage = null;

        var cacheDir = Path.Combine(imagesDir, ".cache");
        Directory.CreateDirectory(cacheDir);
        var cachePath = Path.Combine(cacheDir, "results.json");
        var cache = File.Exists(cachePath)
            ? JsonSerializer.Deserialize<Dictionary<string, CachedResult>>(File.ReadAllText(cachePath), JsonOpts) ?? []
            : [];
        if (!refresh &&
            string.IsNullOrWhiteSpace(configuration["AzureOpenAI:Workloads:vision:Deployment"]) &&
            string.IsNullOrWhiteSpace(configuration["AzureOpenAI:DeploymentName"]))
        {
            foreach (var item in manifest.Cases)
            {
                var image = Path.Combine(imagesDir, item.Image);
                if (!File.Exists(image)) continue;
                var imageHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image)))[..24];
                var key = cache.Keys.FirstOrDefault(k => k.StartsWith(
                    $"{imageHash}|{MealScanService.EffectiveVisionPromptVersion(configuration)}|",
                    StringComparison.Ordinal));
                if (key is null) continue;
                var pieces = key.Split('|');
                if (pieces.Length == 4)
                {
                    deployment = pieces[2];
                    reasoningEffort = pieces[3];
                }
                break;
            }
        }


        var scores = new List<GoldenMetrics.CaseScore>();
        var evaluations = new List<GoldenMetrics.CaseEvaluation>();
        var usageMeter = new AiUsageMeter(configuration, loggerFactory.CreateLogger("GoldenScanHarness"));
        var repeatedRecallCvByCase = new Dictionary<string, double>();
        var stageObservations = new List<GoldenMetrics.StageObservation>();
        string? latestObservedModelId = null;
        var reportedModelIds = new List<string?>();
        foreach (var c in manifest.Cases)
        {
            var imagePath = Path.Combine(imagesDir, c.Image);
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"⚠  {c.Image}: image file missing, skipped.");
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)));
            var cacheKey = $"{hash[..24]}|{MealScanService.EffectiveVisionPromptVersion(configuration)}|{deployment}|{reasoningEffort}";

            var runRecalls = new List<double>();
            VisionDecomposition decomp = null!;
            for (var run = 0; run < repeat; run++)
            {
                if (!refresh && run == 0 && cache.TryGetValue(cacheKey, out var cached) && cached.FailedReason is null)
                {
                    decomp = cached.ToDecomposition();
                    reportedModelIds.Add(cached.ModelId);
                    runRecalls.Add(GoldenMetrics.ScoreCase(c, decomp.Components).Recall);
                    Console.WriteLine($"·  {c.Image}: cached ({decomp.Components.Count} components)");
                    continue;
                }
                var sw = Stopwatch.StartNew();
                latestObservedModelId = null;
                try
                {
                    stage ??= CreateLiveStage(configuration, loggerFactory, id => latestObservedModelId = id);
                    await using var fs = File.OpenRead(imagePath);
                    decomp = await stage.DecomposeAsync(fs, GetContentType(c.Image), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗  {c.Image}: FAILED — {ex.Message}");
                    cache[cacheKey] = CachedResult.Failed(ex.Message);
                    await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, JsonOpts));
                    decomp = null!;
                    break;
                }
                sw.Stop();
                var modelId = latestObservedModelId;
                runRecalls.Add(GoldenMetrics.ScoreCase(c, decomp.Components).Recall);
                reportedModelIds.Add(modelId);
                usageMeter.Record("vision", deployment, decomp.InputTokens, decomp.OutputTokens, sw.Elapsed);
                stageObservations.Add(new GoldenMetrics.StageObservation("vision",
                    sw.Elapsed.TotalSeconds,
                    decomp.InputTokens is { } inputTokens ? inputTokens : double.NaN,
                    decomp.OutputTokens is { } outputTokens ? outputTokens : double.NaN,
                    CostOfCall(configuration, deployment, decomp.InputTokens, decomp.OutputTokens), modelId));
                Console.WriteLine($"✓  {c.Image}: {decomp.Components.Count} components in {sw.ElapsedMilliseconds} ms " +
                                  $"(in={decomp.InputTokens ?? 0}/out={decomp.OutputTokens ?? 0} tok)");
                cache[cacheKey] = CachedResult.From(decomp, modelId);
                await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, JsonOpts));
            }
            if (decomp is null) continue;

            repeatedRecallCvByCase[c.Image] = CoefficientVariationPercent(runRecalls);
            var predicted = decomp.Components.Select(component => new GoldenMetrics.GoldenPredictedItem(
                component.Name, component.EstimatedGramsMidpoint, component.EstimatedGramsLow,
                component.EstimatedGramsHigh, null, null, component.Confidence, false,
                null, null, null, null)).ToList();
            scores.Add(GoldenMetrics.ScoreCase(c, [.. decomp.Components]));
            evaluations.Add(GoldenMetrics.EvaluateCase(c, predicted));
        }

        if (scores.Count == 0)
        {
            Console.Error.WriteLine("No cases produced results.");
            return 2;
        }

        var aggregate = GoldenMetrics.Aggregate(evaluations);
        var usageSummary = usageMeter.Summarize();
        var stageMetrics = GoldenMetrics.AggregateStages(stageObservations);
        var p95Latency = stageObservations.Count == 0 ? (double?)null :
            GoldenMetrics.Percentile(stageObservations.Select(x => x.LatencySeconds), 95);
        var costSamples = stageObservations.Select(x => x.CostUsd).Where(double.IsFinite).ToArray();
        double? p95Cost = costSamples.Length == 0 ? null : GoldenMetrics.Percentile(costSamples, 95);
        var gateResult = GoldenMetrics.EvaluateGate(manifest.Gate, aggregate, p95Latency, p95Cost,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "min_nutrition_backed_rate", "max_median_kcal_error_percent", "max_abs_kcal_bias_percent",
                "min_identity_precision", "max_abstention_rate", "max_identity_ece", "max_kcal_cv_percent",
            });

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════");
        Console.WriteLine($" Cases scored:          {scores.Count}");
        Console.WriteLine($" Mean component recall: {aggregate.MeanRecall:P1}");
        Console.WriteLine($" Median gram error:     {(double.IsFinite(aggregate.MedianGramErrorPercent) ? $"{aggregate.MedianGramErrorPercent:F1}%" : "n/a")}");
        Console.WriteLine($" Reasoning effort:      {reasoningEffort}");
        Console.WriteLine("════════════════════════════════════════════");

        foreach (var s in scores)
        {
            Console.WriteLine($"\n— {s.Image}: recall {s.MatchedCount}/{s.ExpectedCount}");
            foreach (var (exp, match, err) in s.PerComponent)
                Console.WriteLine(err < 0 ? $"     MISS  '{exp}'" : $"     MATCH '{exp}' ↔ '{match}' ({err:F1}% error)");
        }

        var recallCvValues = repeatedRecallCvByCase.Values.Where(double.IsFinite).ToArray();

        var report = JsonSerializer.Serialize(new
        {
            schema_version = 2,
            mode,
            generated_at = DateTimeOffset.UtcNow,
            prompt_version = MealScanService.EffectiveVisionPromptVersion(configuration),
            deployment,
            reasoning_effort = reasoningEffort,
            grounding_policy_version = GroundingPolicy.PolicyVersion,
            portion_calibrator_version = new PortionCalibrator(configuration).Version,
            active_flags = new
            {
                hidden_calories = configuration.GetValue("Features:HiddenCalories", false),
                portion_calibration = configuration.GetValue("Features:PortionCalibration", false),
                web_grounding = configuration.GetValue("Features:WebGrounding", false),
                live_selection = false,
            },
            model_ids = new { vision = DistinctModelIds(reportedModelIds) },
            metrics = aggregate,
            usage = usageSummary,
            stability = new
            {
                repeatedRecallCvPercentByCase = repeatedRecallCvByCase,
                meanRepeatedRecallCvPercent = recallCvValues.Length == 0 ? (double?)null : recallCvValues.Average(),
            },
            per_stage = stageMetrics,
            gate = gateResult,
            cases = evaluations,
            legacy_stage_a_scores = scores,
        }, new JsonSerializerOptions(JsonOpts) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputReportPath))!);
        await File.WriteAllTextAsync(outputReportPath, report);
        Console.WriteLine($"\nReport: {outputReportPath}");

        if (!gate) return 0;
        Console.WriteLine(gateResult.Passed ? "\nGATE PASS" :
            $"\nGATE FAIL — {string.Join(", ", gateResult.Failed.Select(f => $"{f.Name}={f.Actual:F2} (threshold {f.Threshold:F2})"))}");
        return gateResult.Passed ? 0 : 1;
    }

    private static async Task<int> RunInProcessAsync(string imagesDir, GoldenManifest manifest,
        bool refresh, int repeat, bool gate, bool liveSelection, string reportPath)
    {
        try
        {
            var baseConfig = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.harness.json"), optional: true)
                .AddEnvironmentVariables().Build();
            var effectiveConfig = new ConfigurationBuilder().AddConfiguration(baseConfig)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MealScan:MinSecondsForSelection"] = liveSelection ? "0" : "3600",
                    ["MealScan:MinSecondsForWeb"] = "3600",
                    ["MealScan:MaxWebQueriesPerScan"] = "0",
                    ["Features:WebGrounding"] = "false",
                }).Build();
            var deployment = AiWorkloads.ResolveDeployment(effectiveConfig, AiWorkloads.Vision);
            var effort = AiWorkloads.ResolveReasoningEffort(effectiveConfig, AiWorkloads.Vision) ?? "default";
            var prompt = MealScanService.EffectiveVisionPromptVersion(effectiveConfig);
            var cachePath = Path.Combine(imagesDir, ".cache", "results.json");
            var cache = File.Exists(cachePath)
                ? JsonSerializer.Deserialize<Dictionary<string, CachedResult>>(File.ReadAllText(cachePath), JsonOpts) ?? []
                : [];
            if (!refresh &&
                string.IsNullOrWhiteSpace(effectiveConfig["AzureOpenAI:Workloads:vision:Deployment"]) &&
                string.IsNullOrWhiteSpace(effectiveConfig["AzureOpenAI:DeploymentName"]))
            {
                foreach (var goldenCase in manifest.Cases)
                {
                    var imagePath = Path.Combine(imagesDir, goldenCase.Image);
                    if (!File.Exists(imagePath)) continue;
                    var imageHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)))[..24];
                    var prefix = $"{imageHash}|{prompt}|";
                    var key = cache.Keys.FirstOrDefault(k => k.StartsWith(prefix, StringComparison.Ordinal));
                    if (key is null) continue;
                    var keyParts = key.Split('|');
                    if (keyParts.Length == 4)
                    {
                        deployment = keyParts[2];
                        effort = keyParts[3];
                    }
                    break;
                }
            }
            effectiveConfig = new ConfigurationBuilder().AddConfiguration(effectiveConfig)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureOpenAI:Workloads:vision:Deployment"] = deployment,
                    ["AzureOpenAI:Workloads:vision:ReasoningEffort"] = effort,
                }).Build();

            var userId = Guid.Parse("d55a97bd-9792-4ad3-9d8e-2bd73e4c5047");
            var visionCache = new InProcessCache();
            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
            var usageMeter = new AiUsageMeter(effectiveConfig, loggerFactory.CreateLogger("GoldenScanHarness"));
            var stageObservations = new System.Collections.Concurrent.ConcurrentBag<GoldenMetrics.StageObservation>();
            var visionClient = (IChatClient?)null;
            var selectionClient = (IChatClient?)null;
            var store = new NullTableStore();
            var providers = new GutAI.Infrastructure.ExternalApis.ExternalFoodProviderAggregator(
                [new GutAI.Infrastructure.ExternalApis.WholeFoodApiService(),
                    new GutAI.Infrastructure.ExternalApis.BrandedFoodApiService(),
                    new GutAI.Infrastructure.ExternalApis.AustralianFoodApiService()],
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GutAI.Infrastructure.ExternalApis.ExternalFoodProviderAggregator>.Instance);
            var search = new GutAI.Infrastructure.ExternalApis.FoodSearchService(store, providers,
                new GutAI.Infrastructure.Data.FoodRanker(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GutAI.Infrastructure.ExternalApis.FoodSearchService>.Instance);
            var drafts = new StageOnlyMealDraftService();
            MealScanService CreateScan() => new(visionClient!, selectionClient!, store, effectiveConfig, search,
                new NoopWebLookup(), new FodmapService(), new GutRiskService(), drafts,
                new VisionResultCache(visionCache, effectiveConfig), new PortionCalibrator(effectiveConfig),
                loggerFactory.CreateLogger<MealScanService>());
            var scan = CreateScan();

            var repeatedRecall = new Dictionary<string, IReadOnlyList<double>>();
            var repeatedKcal = new Dictionary<string, IReadOnlyList<double>>();
            var repeatedCost = new List<double>();
            var reportedVisionModelIds = new List<string?>();
            var evaluations = new List<GoldenMetrics.CaseEvaluation>();
            foreach (var item in manifest.Cases)
            {
                var image = Path.Combine(imagesDir, item.Image);
                if (!File.Exists(image)) continue;
                var bytes = await File.ReadAllBytesAsync(image);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                var outerKey = $"{hash[..24]}|{prompt}|{deployment}|{effort}";
                var innerKey = VisionResultCache.BuildKey(userId, bytes, prompt, deployment, effort, "");
                var hasCacheEntry = cache.TryGetValue(outerKey, out var entry);
                var caseCacheHit = !refresh && hasCacheEntry && entry!.FailedReason is null;
                if (caseCacheHit)
                {
                    reportedVisionModelIds.Add(entry!.ModelId);
                    await visionCache.SetAsync(innerKey, entry.ToDecomposition());
                }
                if (liveSelection && selectionClient is null)
                {
                    selectionClient = CreateLiveClient(effectiveConfig, AiWorkloads.Selection, loggerFactory,
                        usageMeter, stageObservations);
                    scan = CreateScan();
                }
                var calorieRuns = new List<double>();
                var recallRuns = new List<double>();
                var costRuns = new List<double>();
                MealDraftDto? draft = null;
                string? latestVisionModelId = caseCacheHit ? entry!.ModelId : null;
                for (var run = 0; run < repeat; run++)
                {
                    var previousObservations = stageObservations.ToHashSet(
                        System.Collections.Generic.ReferenceEqualityComparer.Instance);
                    if (refresh && run > 0) await visionCache.RemoveAsync(innerKey);
                    if ((refresh || run == 0 && !caseCacheHit) && visionClient is null)
                    {
                        if (string.IsNullOrWhiteSpace(effectiveConfig["AzureOpenAI:Endpoint"]))
                            throw new InvalidOperationException(
                                $"Cache miss for key '{outerKey}' (deployment '{deployment}', effort '{effort}'); " +
                                "set AzureOpenAI__Endpoint to run the cold vision stage.");
                        visionClient = CreateLiveClient(effectiveConfig, AiWorkloads.Vision, loggerFactory,
                            usageMeter, stageObservations);
                        scan = CreateScan();
                    }
                    using var imageStream = new MemoryStream(bytes, writable: false);
                    var runTimer = Stopwatch.StartNew();
                    draft = await scan.ScanMealImageAsync(userId, imageStream, GetContentType(item.Image));
                    runTimer.Stop();
                    stageObservations.Add(new GoldenMetrics.StageObservation("scan", runTimer.Elapsed.TotalSeconds,
                        double.NaN, double.NaN, double.NaN));
                    var runPredicted = draft.Items.Select(i =>
                    {
                        var selected = i.Grounding?.Candidates.FirstOrDefault(c => c.FoodProductId == i.FoodProductId);
                        var identity = selected?.ExternalId is { Length: > 0 } externalId
                            ? $"{selected.Source}:{externalId}" : null;
                        return new GoldenMetrics.GoldenPredictedItem(i.Name, i.Grams, i.PortionLowGrams,
                            i.PortionHighGrams, identity, i.Grounding?.Method, i.Grounding?.MatchConfidence,
                            i.Grounding?.AutoSelected ?? false, i.Calories, i.ProteinG, i.CarbsG, i.FatG);
                    }).ToList();
                    recallRuns.Add(GoldenMetrics.EvaluateCase(item, runPredicted).Recall);
                    var runObservations = stageObservations
                        .Where(o => !previousObservations.Contains(o) &&
                            !string.Equals(o.Stage, "scan", StringComparison.OrdinalIgnoreCase)).ToArray();
                    var visionObservations = runObservations.Where(o =>
                        string.Equals(o.Stage, "vision", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (visionObservations.Length > 0)
                        latestVisionModelId = visionObservations[^1].ModelId;
                    costRuns.Add(runObservations.Any(o => !double.IsFinite(o.CostUsd))
                        ? double.NaN
                        : runObservations.Sum(o => o.CostUsd));
                    calorieRuns.Add((double)draft.Totals.Calories);
                }
                var cachedDecomposition = await visionCache.GetAsync<VisionDecomposition>(innerKey);
                if (cachedDecomposition is not null)
                {
                    cache[outerKey] = CachedResult.From(cachedDecomposition, latestVisionModelId);
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, JsonOpts));
                }
                if (draft is null) continue;
                repeatedRecall[item.Image] = recallRuns;
                repeatedCost.AddRange(costRuns);
                repeatedKcal[item.Image] = calorieRuns;
                var predicted = draft.Items.Select(i =>
                {
                    var selected = i.Grounding?.Candidates.FirstOrDefault(c => c.FoodProductId == i.FoodProductId);
                    var identity = selected?.ExternalId is { Length: > 0 } externalId
                        ? $"{selected.Source}:{externalId}" : null;
                    return new GoldenMetrics.GoldenPredictedItem(i.Name, i.Grams, i.PortionLowGrams,
                        i.PortionHighGrams, identity, i.Grounding?.Method, i.Grounding?.MatchConfidence,
                        i.Grounding?.AutoSelected ?? false, i.Calories, i.ProteinG, i.CarbsG, i.FatG);
                }).ToList();
                evaluations.Add(GoldenMetrics.EvaluateCase(item, predicted));
            }
            if (evaluations.Count == 0) return 2;
            var aggregate = GoldenMetrics.Aggregate(evaluations, repeatedKcal, repeatedRecall);
            var perStage = GoldenMetrics.AggregateStages(stageObservations);
            var scanLatencies = stageObservations.Where(x =>
                string.Equals(x.Stage, "scan", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.LatencySeconds).ToArray();
            var p95Latency = scanLatencies.Length == 0 ? (double?)null :
                GoldenMetrics.Percentile(scanLatencies, 95);
            var measuredCosts = repeatedCost.ToArray();
            var p95Cost = measuredCosts.Length == 0 || measuredCosts.Any(x => !double.IsFinite(x))
                ? (double?)null
                : GoldenMetrics.Percentile(measuredCosts, 95);
            var gateResult = GoldenMetrics.EvaluateGate(manifest.Gate, aggregate, p95Latency, p95Cost);
            var usage = usageMeter.Summarize();
            var report = JsonSerializer.Serialize(new
            {
                schema_version = 2,
                mode = "in-process",
                generated_at = DateTimeOffset.UtcNow,
                prompt_version = prompt,
                deployment,
                reasoning_effort = effort,
                grounding_policy_version = GroundingPolicy.PolicyVersion,
                portion_calibrator_version = new PortionCalibrator(effectiveConfig).Version,
                active_flags = new
                {
                    hidden_calories = effectiveConfig.GetValue("Features:HiddenCalories", false),
                    portion_calibration = effectiveConfig.GetValue("Features:PortionCalibration", false),
                    web_grounding = false,
                    live_selection = liveSelection,
                },
                usage = new
                {
                    modelCalls = usage.ModelCalls,
                    inputTokens = usage.InputTokens,
                    outputTokens = usage.OutputTokens,
                    estimatedCostUsd = usage.EstimatedCostUsd,
                },
                metrics = aggregate,
                per_stage = perStage,
                model_ids = new
                {
                    vision = DistinctModelIds(reportedVisionModelIds.Concat(stageObservations
                        .Where(o => string.Equals(o.Stage, "vision", StringComparison.OrdinalIgnoreCase))
                        .Select(o => o.ModelId))),
                    selection = DistinctModelIds(stageObservations
                        .Where(o => string.Equals(o.Stage, "selection", StringComparison.OrdinalIgnoreCase))
                        .Select(o => o.ModelId)),
                },
                gate = gateResult,
                cases = evaluations,
            }, new JsonSerializerOptions(JsonOpts) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath, report);
            Console.WriteLine($"Report: {reportPath}");
            return !gate || gateResult.Passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"In-process golden run failed: {ex.Message}");
            return 2;
        }
    }

    private static IChatClient CreateLiveClient(IConfiguration configuration, string workload, ILoggerFactory loggerFactory,
        AiUsageMeter? usageMeter = null,
        System.Collections.Concurrent.ConcurrentBag<GoldenMetrics.StageObservation>? stageObservations = null)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("A cold cache or live selection requires AzureOpenAI__Endpoint.");
        var client = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint),
            new Azure.Identity.AzureCliCredential(),
            new Azure.AI.OpenAI.AzureOpenAIClientOptions { NetworkTimeout = TimeSpan.FromMinutes(10) });
#pragma warning disable OPENAI001
        var inner = client.GetResponsesClient().AsIChatClient(AiWorkloads.ResolveDeployment(configuration, workload));
#pragma warning restore OPENAI001
        IChatClient chat = new ChatClientBuilder(inner).UseLogging(loggerFactory).Build();
        return usageMeter is null || stageObservations is null
            ? chat
            : new MeteredChatClient(chat, workload, AiWorkloads.ResolveDeployment(configuration, workload), configuration,
                usageMeter, stageObservations);
    }

    private sealed class MeteredChatClient(
        IChatClient inner,
        string workload,
        string deployment,
        IConfiguration configuration,
        AiUsageMeter meter,
        System.Collections.Concurrent.ConcurrentBag<GoldenMetrics.StageObservation> observations)
        : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var timer = Stopwatch.StartNew();
            ChatResponse response;
            try
            {
                response = await base.GetResponseAsync(messages, options, cancellationToken);
            }
            catch
            {
                timer.Stop();
                meter.Record(workload, deployment, null, null, timer.Elapsed);
                throw;
            }

            timer.Stop();
            var inputTokens = response.Usage?.InputTokenCount;
            var outputTokens = response.Usage?.OutputTokenCount;
            meter.Record(workload, deployment, inputTokens, outputTokens, timer.Elapsed);
            observations.Add(new GoldenMetrics.StageObservation(workload, timer.Elapsed.TotalSeconds,
                inputTokens ?? double.NaN, outputTokens ?? double.NaN,
                CostOfCall(configuration, deployment, inputTokens, outputTokens), response.ModelId));
            return response;
        }
    }

    private static IMealVisionStage CreateLiveStage(IConfiguration configuration, ILoggerFactory loggerFactory,
        Action<string?> observeModelId)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("A cold Stage-A cache requires AzureOpenAI__Endpoint.");
        var deployment = AiWorkloads.ResolveDeployment(configuration, AiWorkloads.Vision);
        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint),
            new Azure.Identity.AzureCliCredential(),
            new Azure.AI.OpenAI.AzureOpenAIClientOptions { NetworkTimeout = TimeSpan.FromMinutes(10) });
#pragma warning disable OPENAI001
        var inner = azureClient.GetResponsesClient().AsIChatClient(deployment);
#pragma warning restore OPENAI001
        IChatClient chatClient = new ModelIdObservingChatClient(
            new ChatClientBuilder(inner).UseLogging(loggerFactory).Build(), observeModelId);
        return new MealScanService(chatClient, chatClient, new NullTableStore(), configuration,
            new NoopFoodSearch(), new NoopWebLookup(), new FodmapService(), new GutRiskService(),
            new StageOnlyMealDraftService(), new VisionResultCache(new NoopCacheService(), configuration),
            new PortionCalibrator(configuration), loggerFactory.CreateLogger<MealScanService>());
    }

    private sealed class ModelIdObservingChatClient(IChatClient inner, Action<string?> observeModelId)
        : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            observeModelId(response.ModelId);
            return response;
        }
    }


    private static double CostOfCall(IConfiguration configuration, string deployment, long? inputTokens, long? outputTokens)
    {
        if (inputTokens is null || outputTokens is null ||
            !decimal.TryParse(configuration[$"AzureOpenAI:Pricing:{deployment}:InputPer1M"],
                System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var inputRate) ||
            !decimal.TryParse(configuration[$"AzureOpenAI:Pricing:{deployment}:OutputPer1M"],
                System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var outputRate))
            return double.NaN;
        return (double)(inputTokens.Value * inputRate / 1_000_000m + outputTokens.Value * outputRate / 1_000_000m);
    }
    private static double CoefficientVariationPercent(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return double.NaN;
        var mean = values.Average();
        if (mean == 0) return double.NaN;
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / values.Count) / Math.Abs(mean) * 100d;
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
        [property: JsonPropertyName("failed")] string? FailedReason,
        [property: JsonPropertyName("model_id")] string? ModelId = null)
    {
        [JsonPropertyName("inferred_components")]
        public List<ScannedComponent> InferredComponents { get; init; } = [];

        public VisionDecomposition ToDecomposition() => new(
            Components, RefVisible, ScaleNotes, OverallConfidence, Dropped, Raw, Prompt, null, null)
        { InferredComponents = InferredComponents };

        public static CachedResult From(VisionDecomposition d, string? modelId) => new(
            [.. d.Components], d.ReferenceObjectVisible, d.ScaleNotes, d.OverallConfidence,
            [.. d.DroppedNotes], d.RawJson, d.PromptVersion, null, modelId)
        { InferredComponents = [.. d.InferredComponents] };

        public static CachedResult Failed(string reason) => new([], false, "", 0, [], "", "", reason);
    }
    private static string?[] DistinctModelIds(IEnumerable<string?> modelIds) =>
        modelIds.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Web cascade not exercised by the harness.</summary>
    private sealed class NoopWebLookup : IWebNutritionLookup
    {
        public Task<WebNutritionResult?> LookupAsync(string foodName, FoodRegion region, CancellationToken ct = default)
            => Task.FromResult<WebNutritionResult?>(null);
    }

    /// <summary>In-memory draft sink; it computes authoritative totals and never persists.</summary>
    private sealed class StageOnlyMealDraftService : IMealDraftService
    {
        public Task<MealDraftDto> CreateAsync(Guid userId, MealDraftCreateRequest request, CancellationToken ct = default)
        {
            var items = request.Items.Select(item =>
            {
                var amounts = item.Per100g is null ? null : NutritionCalculator.Compute(item.Per100g, item.Grams);
                return item with
                {
                    Calories = amounts?.Calories,
                    ProteinG = amounts?.ProteinG,
                    CarbsG = amounts?.CarbsG,
                    FatG = amounts?.FatG,
                    FiberG = amounts?.FiberG,
                    SugarG = amounts?.SugarG,
                    SodiumMg = amounts?.SodiumMg,
                };
            }).ToList();
            var included = items.Where(i => i.IncludedByDefault).ToList();
            var dto = new MealDraftDto
            {
                DraftId = Guid.NewGuid(),
                Origin = request.Origin,
                Status = MealDraftStatuses.PendingReview,
                MealType = request.MealType,
                LoggedAt = request.LoggedAt,
                Items = items,
                Warnings = request.Warnings,
                ReferenceObjectVisible = request.ReferenceObjectVisible,
                OverallConfidence = request.OverallConfidence,
                Totals = new MealDraftTotalsDto
                {
                    Calories = included.Sum(i => i.Calories ?? 0),
                    ProteinG = included.Sum(i => i.ProteinG ?? 0),
                    CarbsG = included.Sum(i => i.CarbsG ?? 0),
                    FatG = included.Sum(i => i.FatG ?? 0),
                    ItemsWithoutNutrition = included.Count(i => i.Calories is null),
                },
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            };
            return Task.FromResult(dto);
        }
        public Task<MealDraftDto?> GetAsync(Guid userId, Guid draftId, CancellationToken ct = default) => throw Unsupported();
        public Task<IReadOnlyList<MealDraftDto>> ListPendingAsync(Guid userId, CancellationToken ct = default) => throw Unsupported();
        public Task<MealDraftDto> UpdateAsync(Guid userId, Guid draftId, MealDraftUpdateRequest request, CancellationToken ct = default) => throw Unsupported();
        public Task<MealDraftCommitResult> CommitAsync(Guid userId, Guid draftId, MealDraftCommitRequest? request, MealDraftCommitGuard? guard = null, CancellationToken ct = default) => throw Unsupported();
        public Task DiscardAsync(Guid userId, Guid draftId, CancellationToken ct = default) => throw Unsupported();
        private static NotSupportedException Unsupported() => new("Golden harness draft sink supports create only.");
    }

    private sealed class InProcessCache : ICacheService
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _values = new();
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => Task.FromResult(_values.TryGetValue(key, out var value) && value is T typed ? typed : default);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        {
            if (value is not null) _values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _values.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }

    /// <summary>Stage-A mode measures the model on every run; nothing is cached in-process.</summary>
    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
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

    /// <summary>The harness never persists drafts or meals — no-op store.</summary>
    private sealed class NullTableStore : ITableStore
    {
        private readonly Dictionary<Guid, FoodProduct> _products = [];
        public Task UpsertMealDraftAsync(MealDraftRecord draft, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> TryReplaceMealDraftAsync(MealDraftRecord draft, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<MealDraftRecord?> GetMealDraftAsync(Guid userId, Guid draftId, CancellationToken ct = default)
            => Task.FromResult<MealDraftRecord?>(null);
        public Task<List<MealDraftRecord>> GetMealDraftsByStatusAsync(Guid userId, string status, CancellationToken ct = default)
            => Task.FromResult<List<MealDraftRecord>>([]);
        public Task<int> PurgeMealDraftsAsync(DateTimeOffset pendingExpiredBefore, DateTimeOffset closedCreatedBefore, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<CoachSessionState?> GetCoachSessionStateAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<CoachSessionState?>(null);
        public Task UpsertCoachSessionStateAsync(Guid userId, CoachSessionState state, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteCoachSessionStateAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WebNutritionCacheEntry?> GetWebNutritionCacheEntryAsync(string cacheKey, CancellationToken ct = default)
            => Task.FromResult<WebNutritionCacheEntry?>(null);
        public Task UpsertWebNutritionNegativeCacheAsync(string cacheKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertWebNutritionCacheAsync(WebNutritionResult result, CancellationToken ct = default) => Task.CompletedTask;

        // Everything below is unreachable for the harness but required by the interface.
        public Task<User?> GetUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<User?>(null);

        public Task UpsertUserAsync(User user, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IdentityRecord?> GetIdentityByIdAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IdentityRecord?> GetIdentityByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertIdentityAsync(IdentityRecord identity, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteIdentityAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<MealLog?> GetMealLogAsync(Guid userId, Guid mealId, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<MealLog>> GetMealLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<MealLog>> GetMealLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertMealLogAsync(MealLog meal, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<MealItem>> GetMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<MealItem>> GetAllUserMealItemsAsync(Guid userId, int limit, CancellationToken ct) => Task.FromResult<List<MealItem>>([]);

        public Task UpsertMealItemsAsync(Guid userId, Guid mealLogId, List<MealItem> items, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteMealItemsAsync(Guid userId, Guid mealLogId, CancellationToken ct) => throw new NotSupportedException();

        public Task<SymptomLog?> GetSymptomLogAsync(Guid userId, Guid symptomId, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<SymptomLog>> GetSymptomLogsByDateAsync(Guid userId, DateOnly date, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<SymptomLog>> GetSymptomLogsByDateRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertSymptomLogAsync(SymptomLog symptom, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<SymptomType>> GetAllSymptomTypesAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<SymptomType?> GetSymptomTypeAsync(int id, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertSymptomTypeAsync(SymptomType type, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> SymptomTypeExistsAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<FoodProduct?> GetFoodProductAsync(Guid id, CancellationToken ct) => Task.FromResult(_products.GetValueOrDefault(id));

        public Task<FoodProduct?> GetFoodProductByBarcodeAsync(string barcode, CancellationToken ct)
            => Task.FromResult(_products.Values.FirstOrDefault(p => p.Barcode == barcode));

        public Task<FoodProduct?> GetFoodProductBySourceAsync(string dataSource, string externalId, CancellationToken ct)
            => Task.FromResult(_products.Values.FirstOrDefault(p => p.DataSource == dataSource && p.ExternalId == externalId));

        public Task<List<FoodProduct>> SearchFoodProductsAsync(string query, int maxResults, CancellationToken ct)
            => Task.FromResult(_products.Values.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(maxResults).ToList());

        public Task<Dictionary<Guid, string?>> GetFoodProductSafetyRatingsAsync(IEnumerable<Guid> ids, CancellationToken ct)
            => Task.FromResult(new Dictionary<Guid, string?>());

        public Task UpsertFoodProductAsync(FoodProduct product, CancellationToken ct)
        {
            _products[product.Id] = product;
            return Task.CompletedTask;
        }

        public Task<List<FoodAdditive>> GetAllFoodAdditivesAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<FoodAdditive?> GetFoodAdditiveAsync(int id, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertFoodAdditiveAsync(FoodAdditive additive, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<int>> GetAdditiveIdsForProductAsync(Guid foodProductId, CancellationToken ct) => throw new NotSupportedException();

        public Task SetAdditiveIdsForProductAsync(Guid foodProductId, List<int> additiveIds, CancellationToken ct) => throw new NotSupportedException();

        public Task<RefreshToken?> GetRefreshTokenByValueAsync(string token, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<RefreshToken>> GetActiveRefreshTokensAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertRefreshTokenAsync(RefreshToken token, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteRefreshTokensForUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<DailyNutritionSummary?> GetDailyNutritionSummaryAsync(Guid userId, DateOnly date, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertDailyNutritionSummaryAsync(DailyNutritionSummary summary, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<UserFoodAlert>> GetUserFoodAlertsAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<UserFoodAlert?> GetUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertUserFoodAlertAsync(UserFoodAlert alert, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteUserFoodAlertAsync(Guid userId, int additiveId, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<FavoriteFoodProduct>> GetUserFavoriteFoodsAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<FavoriteFoodProduct?> GetUserFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertFavoriteFoodAsync(FavoriteFoodProduct favorite, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteFavoriteFoodAsync(Guid userId, Guid foodProductId, CancellationToken ct) => throw new NotSupportedException();

        public Task<InsightReport?> GetInsightReportAsync(Guid userId, Guid reportId, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<InsightReport>> GetInsightReportsAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertInsightReportAsync(InsightReport report, CancellationToken ct) => throw new NotSupportedException();

        public Task<CustomFood?> GetCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<CustomFood>> GetCustomFoodsAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertCustomFoodAsync(CustomFood food, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteCustomFoodAsync(Guid userId, Guid foodId, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<CoachChatMessage>> GetRecentCoachMessagesAsync(Guid userId, int limit, CancellationToken ct) => Task.FromResult<List<CoachChatMessage>>([]);

        public Task UpsertCoachMessageAsync(Guid userId, DateTimeOffset at, string role, string text, CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteCoachMessagesAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<GutAI.Domain.Entities.PairingCode?> GetPairingCodeByHashAsync(string codeHash, CancellationToken ct) => Task.FromResult<GutAI.Domain.Entities.PairingCode?>(null);

        public Task UpsertPairingCodeAsync(GutAI.Domain.Entities.PairingCode code, CancellationToken ct) => throw new NotSupportedException();

        public Task DeletePairingCodesForUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<GutAI.Domain.Entities.PersonalAccessToken?> GetPersonalAccessTokenByHashAsync(string tokenHash, CancellationToken ct) => Task.FromResult<GutAI.Domain.Entities.PersonalAccessToken?>(null);

        public Task<List<GutAI.Domain.Entities.PersonalAccessToken>> GetActivePersonalAccessTokensAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task UpsertPersonalAccessTokenAsync(GutAI.Domain.Entities.PersonalAccessToken token, CancellationToken ct) => throw new NotSupportedException();

        public Task DeletePersonalAccessTokensForUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<MealLog?> GetMealLogByExternalRefAsync(Guid userId, string source, string externalId, CancellationToken ct) => Task.FromResult<MealLog?>(null);

    }
}
