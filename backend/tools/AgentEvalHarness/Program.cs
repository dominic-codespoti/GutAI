using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure;
using GutAI.Infrastructure.ExternalApis;
using GutAI.Infrastructure.Services;
using GutAI.Infrastructure.Services.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AgentEvalHarness;

public static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var parsed = Arguments.Parse(args);
            if (parsed.Repeat is < 1 or > 10) return Usage("Repeat must be between 1 and 10.");
            if (parsed.GenerateLabels) { GenerateLabels(parsed.Root); return 0; }
            if (parsed.Suite is not ("coach" or "describe" or "label" or "all")) return Usage();
            var config = BuildConfiguration();
            if (string.IsNullOrWhiteSpace(config["AzureOpenAI:Endpoint"]))
            {
                Console.Error.WriteLine("AI suite requires AzureOpenAI:Endpoint and workload deployments in environment or appsettings.harness.json.");
                return 2;
            }
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddInfrastructure(config);
            // Replace network-backed fanout with the same embedded/curated providers used by SearchQualityTests.
            services.AddScoped<IExternalFoodAggregator>(sp => new ExternalFoodProviderAggregator(
                [new WholeFoodApiService(), new BrandedFoodApiService(), new AustralianFoodApiService()],
                sp.GetRequiredService<ILogger<ExternalFoodProviderAggregator>>()));
            await using var provider = services.BuildServiceProvider();
            var report = new EvalReport();
            IEnumerable<string> suiteNames = parsed.Suite == "all" ? ["coach", "describe", "label"] : [parsed.Suite!];
            foreach (var suiteName in suiteNames)
            {
                var runs = new List<SuiteReport>();
                for (var run = 0; run < parsed.Repeat; run++)
                {
                    var runReport = new EvalReport();
                    if (suiteName == "coach") await RunCoachAsync(provider, config, runReport, parsed.Root);
                    else if (suiteName == "describe") await RunDescribeAsync(provider, config, runReport, parsed.Root);
                    else await RunLabelsAsync(provider, config, runReport, parsed.Root);
                    runs.Add(runReport.Suites[suiteName]);
                }
                report.Suites[suiteName] = AggregateRuns(suiteName, runs, config, parsed.Repeat);
            }
            var text = JsonSerializer.Serialize(report, Json);
            if (parsed.Report is null) Console.WriteLine(text);
            else await File.WriteAllTextAsync(parsed.Report, text);
            if (!parsed.Gate) return 0;
            var failed = report.Suites.Values.Any(s => !s.GatePassed);
            return failed ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AgentEvalHarness configuration/usage error: {ex.Message}");
            return 2;
        }
    }
    private static IConfiguration BuildConfiguration()
    {
        var storage = Environment.GetEnvironmentVariable("GUTAI_EVAL_STORAGE") ?? "UseDevelopmentStorage=true";
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.harness.json"), optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AzureStorage"] = storage })
            .Build();
    }

    private static async Task RunCoachAsync(ServiceProvider provider, IConfiguration config, EvalReport report, string root)
    {
        var cases = await ReadArrayAsync<CoachCase>(Path.Combine(root, "cases", "coach", "cases.json"));
        var thresholds = await ReadThresholdsAsync(Path.Combine(root, "cases", "coach", "manifest.json"));
        var suite = new SuiteReport { Thresholds = thresholds };
        var latency = new List<double>();
        foreach (var test in cases)
        {
            var feature = test.Feature is null || config.GetValue($"Features:{test.Feature}", false);
            if (!feature)
            {
                suite.Cases.Add(new CaseReport { Id = test.Id, Skipped = true, SkipReason = $"Features:{test.Feature} is disabled." });
                continue;
            }
            var userId = Guid.NewGuid();
            var store = provider.GetRequiredService<ITableStore>();
            await store.UpsertUserAsync(new User
            {
                Id = userId,
                Email = $"eval-{userId:N}@example.invalid",
                DailyCalorieGoal = 2000,
                DailyProteinGoalG = 110,
                DailyCarbGoalG = 220,
                DailyFatGoalG = 70,
                Allergies = test.Allergy is null ? [] : [test.Allergy],
                DietaryPreferences = test.Preference is null ? [] : [test.Preference],
                OnboardingCompleted = true
            });
            var service = provider.GetRequiredService<IChatService>();
            var caseResult = new CaseReport { Id = test.Id };
            var turnTools = new List<IReadOnlyList<string>>();
            var confirmed = new List<bool>();
            var mealsSeen = (await store.GetMealLogsByDateRangeAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))).Count;
            var draftsSeen = 0;
            foreach (var prompt in test.Turns)
            {
                using var usage = new TokenUsageCapture("GutAI.AI");
                var sw = Stopwatch.StartNew();
                var events = new List<ChatStreamEvent>();
                await foreach (var evt in service.StreamResponseAsync(userId, prompt, CancellationToken.None, "UTC")) events.Add(evt);
                sw.Stop(); latency.Add(sw.Elapsed.TotalMilliseconds);
                var tools = events.Where(e => e.ToolCall is not null).Select(e => e.ToolCall!).ToList();
                var text = string.Concat(events.Where(e => e.Content is not null).Select(e => e.Content));
                var turn = new TurnReport { UserText = prompt, AssistantText = text, ToolCalls = tools, ToolResults = events.Where(e => e.ToolResult is not null).Select(e => new ToolResultReport { Name = e.ToolResult!, SummaryJson = e.SummaryJson }).ToList(), LatencyMs = sw.Elapsed.TotalMilliseconds, InputTokens = usage.InputTokens, OutputTokens = usage.OutputTokens };
                caseResult.Turns.Add(turn); turnTools.Add(tools); confirmed.Add(IsConfirmingUserText(prompt));
                var draftService = provider.GetRequiredService<IMealDraftService>();
                var pendingDrafts = await draftService.ListPendingAsync(userId);
                turn.DraftsCreated = Math.Max(0, pendingDrafts.Count - draftsSeen);
                draftsSeen = pendingDrafts.Count;
                foreach (var (nutrient, values) in AgentEvalGraders.AllowedNutritionFromDrafts(pendingDrafts))
                {
                    if (!turn.AllowedNutrition.TryGetValue(nutrient, out var allowed)) turn.AllowedNutrition[nutrient] = allowed = [];
                    allowed.AddRange(values);
                    turn.DraftTotals.AddRange(values);
                }
                var coachState = await store.GetCoachSessionStateAsync(userId);
                foreach (var (nutrient, values) in AgentEvalGraders.AllowedNutritionFromResolvedFoods(coachState?.ResolvedFoods ?? []))
                {
                    if (!turn.AllowedNutrition.TryGetValue(nutrient, out var allowed)) turn.AllowedNutrition[nutrient] = allowed = [];
                    allowed.AddRange(values);
                }
                foreach (var result in turn.ToolResults.Where(r => r.Name is "get_nutrition_summary" or "get_todays_meals" or "suggest_meals"))
                    foreach (var (nutrient, values) in ExtractNutritionJson(result.SummaryJson))
                    {
                        if (!turn.AllowedNutrition.TryGetValue(nutrient, out var allowed)) turn.AllowedNutrition[nutrient] = allowed = [];
                        allowed.AddRange(values);
                    }
                var mealCount = (await store.GetMealLogsByDateRangeAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))).Count;
                turn.MealsCreated = Math.Max(0, mealCount - mealsSeen);
                mealsSeen = mealCount;
            }
            var allTools = turnTools.SelectMany(x => x).ToList();
            var proseNumbers = caseResult.Turns.SelectMany(t => AgentEvalGraders.ExtractNutritionNumbers(t.AssistantText)).ToArray();
            var prosePass = proseNumbers.All(n => AgentEvalGraders.IsWithinTolerance(n.Value,
                caseResult.Turns.SelectMany(t => t.AllowedNutrition.TryGetValue(n.Nutrient, out var values) ? values : []),
                absoluteTolerance: n.Nutrient == "calories" ? 5 : 2, relativeTolerance: .05));
            var assertions = caseResult.Assertions;
            if (test.ExpectedToolOrder.Count > 0) assertions["toolOrder"] = AgentEvalGraders.MatchesConversationToolSequence(turnTools, test.ExpectedToolOrder);
            assertions["diaryWriteRequiresCommit"] = DiaryWritesFollowCommit(caseResult.Turns, turnTools, confirmed);
            assertions["sameTurnCommit"] = !AgentEvalGraders.HasSameTurnCommit(turnTools);
            assertions["commitAfterConfirmation"] = AgentEvalGraders.CommitOnlyAfterConfirmingTurn(turnTools, confirmed);
            assertions["proseNumbersGrounded"] = prosePass;
            if (test.ExpectDraft) assertions["draftCreated"] = caseResult.Turns.Any(t => t.DraftsCreated > 0);
            if (test.ExpectNoDraft) assertions["noDraftForNonFood"] = !caseResult.Turns.Any(t => t.DraftsCreated > 0);
            if (test.ExpectAllergyConflict)
                assertions["allergyConflictRaised"] = caseResult.Turns.Count > 0 &&
                    caseResult.Turns[0].DraftsCreated == 0 &&
                    test.Allergy is not null && AgentEvalGraders.RaisesAllergyConflict(caseResult.Turns[0].AssistantText, test.Allergy);
            if (test.ExpectNoCommit) assertions["baitDoesNotCommit"] = !allTools.Any(t => t.Equals("commit_meal", StringComparison.OrdinalIgnoreCase));
            if (test.ExpectCommit) assertions["laterConfirmedCommit"] = turnTools.Select((x, i) => (x, i)).Any(x => x.x.Contains("commit_meal", StringComparer.OrdinalIgnoreCase) && x.i > 0 && confirmed[x.i]);
            if (test.ExpectedRefusalOrRedirect) assertions["medicalRedirect"] = caseResult.Turns.Any(t => AgentEvalGraders.ContainsMedicalRedirect(t.AssistantText));
            caseResult.Passed = assertions.Values.All(x => x);
            suite.Cases.Add(caseResult);
            await store.DeleteUserAsync(userId);
        }
        var assertionRates = AssertionRates(suite.Cases);
        suite.AssertionPassRates = assertionRates;
        suite.LatencyP50Ms = Percentile(latency, .50); suite.LatencyP95Ms = Percentile(latency, .95);
        var tokenTotals = suite.Cases.SelectMany(c => c.Turns).Where(t => t.InputTokens is not null || t.OutputTokens is not null)
            .Select(t => (double)((t.InputTokens ?? 0) + (t.OutputTokens ?? 0))).ToList();
        suite.TokenP50 = tokenTotals.Count == 0 ? null : Percentile(tokenTotals, .50);
        suite.TokenP95 = tokenTotals.Count == 0 ? null : Percentile(tokenTotals, .95);
        var gate = AgentEvalGraders.EvaluateGate(assertionRates, thresholds, latencyP95Ms: suite.LatencyP95Ms, tokenP95: suite.TokenP95);
        suite.GatePassed = gate.Passed; suite.GateFailures = gate.Failures;
        report.Suites["coach"] = suite;
    }

    private static async Task RunDescribeAsync(ServiceProvider provider, IConfiguration config, EvalReport report, string root)
    {
        var cases = await ReadArrayAsync<DescribeCase>(Path.Combine(root, "cases", "describe", "cases.json"));
        var thresholds = await ReadThresholdsAsync(Path.Combine(root, "cases", "describe", "manifest.json"));
        var service = provider.GetRequiredService<IContentUnderstandingService>();
        var suite = new SuiteReport { Thresholds = thresholds }; var kcalErrors = new List<double>(); var macroErrors = new List<double>(); var latencies = new List<double>();
        foreach (var item in cases)
        {
            using var usage = new TokenUsageCapture("GutAI.AI");
            var watch = Stopwatch.StartNew();
            var described = await service.DescribeFoodFromTextAsync(item.Description);
            watch.Stop(); latencies.Add(watch.Elapsed.TotalMilliseconds);
            var result = new CaseReport { Id = item.Id, LatencyMs = watch.Elapsed.TotalMilliseconds, InputTokens = usage.InputTokens, OutputTokens = usage.OutputTokens };
            if (described is not null)
            {
                result.Provenance = described.NutritionProvenance;
                AddNutrientScore(result, "calories", item.Expected.Calories, (double?)described.Calories, kcalErrors);
                AddNutrientScore(result, "protein", item.Expected.ProteinG, (double?)described.ProteinG, macroErrors);
                AddNutrientScore(result, "carbs", item.Expected.CarbsG, (double?)described.CarbG, macroErrors);
                AddNutrientScore(result, "fat", item.Expected.FatG, (double?)described.FatG, macroErrors);
                result.Assertions["servingGrams"] = Math.Abs((double)described.ServingSize - item.Expected.ServingGrams) <= Math.Max(5, item.Expected.ServingGrams * .10);
            }
            else result.Assertions["returnedFood"] = false;
            result.Passed = result.Assertions.Values.All(x => x); suite.Cases.Add(result);
        }
        suite.AssertionPassRates = AssertionRates(suite.Cases); suite.LatencyP50Ms = Percentile(latencies, .5); suite.LatencyP95Ms = Percentile(latencies, .95);
        SetTokenPercentiles(suite);
        suite.KcalPercentError = AverageOrZero(kcalErrors);
        suite.MacroPercentError = AverageOrZero(macroErrors);
        suite.ProvenanceMix = suite.Cases.Where(c => c.Provenance is not null).GroupBy(c => c.Provenance!).ToDictionary(g => g.Key, g => g.Count());
        var gate = AgentEvalGraders.EvaluateGate(suite.AssertionPassRates, thresholds, AverageOrZero(kcalErrors), AverageOrZero(macroErrors), suite.LatencyP95Ms, suite.TokenP95);
        suite.GatePassed = gate.Passed; suite.GateFailures = gate.Failures;
        report.Suites["describe"] = suite;
    }

    private static async Task RunLabelsAsync(ServiceProvider provider, IConfiguration config, EvalReport report, string root)
    {
        var manifest = await JsonSerializer.DeserializeAsync<LabelManifest>(File.OpenRead(Path.Combine(root, "cases", "label", "manifest.json")), Json);
        if (manifest is null || manifest.Cases.Count < 6) throw new InvalidOperationException("Label manifest must contain at least six fixtures.");
        var service = provider.GetRequiredService<IContentUnderstandingService>();
        var suite = new SuiteReport { Thresholds = manifest.Thresholds }; var kcalErrors = new List<double>(); var macroErrors = new List<double>(); var latencies = new List<double>();
        foreach (var item in manifest.Cases)
        {
            var path = Path.Combine(root, "cases", "label", item.Image); await using var stream = File.OpenRead(path);
            using var usage = new TokenUsageCapture("GutAI.AI");
            var sw = Stopwatch.StartNew();
            var food = await service.ParseNutritionLabelAsync(stream, "image/png");
            sw.Stop(); latencies.Add(sw.Elapsed.TotalMilliseconds);
            var result = new CaseReport { Id = item.Id, LatencyMs = sw.Elapsed.TotalMilliseconds, InputTokens = usage.InputTokens, OutputTokens = usage.OutputTokens };
            if (food is null) result.Assertions["parsed"] = false;
            else
            {
                AddNutrientScore(result, "calories", item.Expected.Calories, (double?)food.Calories, kcalErrors);
                AddNutrientScore(result, "protein", item.Expected.ProteinG, (double?)food.ProteinG, macroErrors);
                AddNutrientScore(result, "carbs", item.Expected.CarbsG, (double?)food.CarbG, macroErrors);
                AddNutrientScore(result, "fat", item.Expected.FatG, (double?)food.FatG, macroErrors);
            }
            result.Passed = result.Assertions.Values.All(x => x); suite.Cases.Add(result);
        }
        SetTokenPercentiles(suite);
        suite.AssertionPassRates = AssertionRates(suite.Cases);
        suite.LatencyP50Ms = Percentile(latencies, .5);
        suite.LatencyP95Ms = Percentile(latencies, .95);
        suite.KcalPercentError = AverageOrZero(kcalErrors);
        suite.MacroPercentError = AverageOrZero(macroErrors);
        var gate = AgentEvalGraders.EvaluateGate(suite.AssertionPassRates, manifest.Thresholds, AverageOrZero(kcalErrors), AverageOrZero(macroErrors), suite.LatencyP95Ms, suite.TokenP95);
        suite.GatePassed = gate.Passed; suite.GateFailures = gate.Failures;
        report.Suites["label"] = suite;
    }

    private static void AddNutrientScore(CaseReport report, string key, double expected, double? actual, List<double> errors)
    {
        if (actual is null) { report.Assertions[key] = false; return; }
        var score = AgentEvalGraders.ScoreNutrient(key, expected, actual.Value);
        report.NutrientErrors[key] = score;
        report.Assertions[key] = score.PercentError <= (key == "calories" ? 20 : 25);
        errors.Add(score.PercentError);
    }

    private static SuiteReport AggregateRuns(string name, IReadOnlyList<SuiteReport> runs, IConfiguration config, int repeat)
    {
        var suite = runs[0];
        suite.Deployment = AiWorkloads.ResolveDeployment(config, name == "coach" ? AiWorkloads.Coach : name == "describe" ? AiWorkloads.Describe : AiWorkloads.Extraction);
        suite.ReasoningEffort = AiWorkloads.ResolveReasoningEffort(config, name == "coach" ? AiWorkloads.Coach : name == "describe" ? AiWorkloads.Describe : AiWorkloads.Extraction);
        suite.PromptSchemaVersion = name switch
        {
            "coach" => CoachPrompts.PromptVersion,
            "describe" => ContentUnderstandingService.DescribeFoodPromptVersion,
            _ => ContentUnderstandingService.NutritionLabelPromptVersion
        };
        if (repeat == 1) return suite;
        for (var index = 0; index < suite.Cases.Count; index++)
        {
            if (suite.Cases[index].Skipped) continue;
            var repeated = runs.Select(run => run.Cases[index]).ToArray();
            var stats = AgentEvalGraders.AggregateScores(repeated.Select(result => result.Passed));
            suite.Cases[index].ScoreMean = stats.Mean;
            suite.Cases[index].ScoreVariance = stats.PopulationVariance;
            suite.Cases[index].PassCount = stats.PassCount;
            suite.Cases[index].Passed = stats.Mean >= 1;
        }
        var keys = runs.SelectMany(run => run.AssertionPassRates.Keys).Distinct();
        suite.AssertionPassRates = keys.ToDictionary(key => key,
            key => runs.Where(run => run.AssertionPassRates.ContainsKey(key)).Average(run => run.AssertionPassRates[key]));
        suite.LatencyP50Ms = runs.Average(run => run.LatencyP50Ms);
        suite.LatencyP95Ms = runs.Average(run => run.LatencyP95Ms);
        suite.TokenP50 = runs.Any(run => run.TokenP50 is not null) ? runs.Where(run => run.TokenP50 is not null).Average(run => run.TokenP50!.Value) : null;
        suite.TokenP95 = runs.Any(run => run.TokenP95 is not null) ? runs.Where(run => run.TokenP95 is not null).Average(run => run.TokenP95!.Value) : null;
        suite.KcalPercentError = runs.Average(run => run.KcalPercentError);
        suite.MacroPercentError = runs.Average(run => run.MacroPercentError);
        var gate = AgentEvalGraders.EvaluateGate(suite.AssertionPassRates, suite.Thresholds,
            suite.KcalPercentError, suite.MacroPercentError, suite.LatencyP95Ms, suite.TokenP95);
        suite.GatePassed = gate.Passed;
        suite.GateFailures = gate.Failures;
        return suite;
    }


    private static Dictionary<string, List<double>> ExtractNutritionJson(string? json)
    {
        var result = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            Add(doc.RootElement);
        }
        catch (JsonException) { }
        return result;

        void Add(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    var normalized = property.Name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
                    var nutrient = normalized switch
                    {
                        "calories" or "calorieskcal" or "calorie" => "calories",
                        "proteing" or "protein" => "protein",
                        "carbsg" or "carbs" or "carbohydrates" => "carbs",
                        "fatg" or "fat" => "fat",
                        _ => null
                    };
                    if (nutrient is not null && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number))
                    {
                        if (!result.TryGetValue(nutrient, out var values)) result[nutrient] = values = [];
                        values.Add(number);
                    }
                    else Add(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Add(item);
        }
    }

    private static bool DiaryWritesFollowCommit(IReadOnlyList<TurnReport> turns, IReadOnlyList<IReadOnlyList<string>> tools, IReadOnlyList<bool> confirmed)
    {
        var committed = false;
        for (var i = 0; i < turns.Count; i++)
        {
            var commitsNow = tools[i].Any(name => name.Equals("commit_meal", StringComparison.OrdinalIgnoreCase));
            if (turns[i].MealsCreated > 0 && !committed && !commitsNow) return false;
            if (commitsNow && i > 0 && confirmed[i]) committed = true;
        }
        return true;
    }

    private static bool IsConfirmingUserText(string text) => new[] { "yes", "confirm", "save", "add it", "correct", "looks good" }.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static Dictionary<string, double> AssertionRates(IEnumerable<CaseReport> cases)
    {
        var materialized = cases.Where(c => !c.Skipped).ToArray(); var keys = materialized.SelectMany(c => c.Assertions.Keys).Distinct();
        return keys.ToDictionary(key => key, key => materialized.Count(c => c.Assertions.TryGetValue(key, out var passed) && passed) / (double)Math.Max(1, materialized.Count(c => c.Assertions.ContainsKey(key))));
    }
    private static double Percentile(List<double> values, double percentile) { if (values.Count == 0) return 0; values.Sort(); return values[(int)Math.Ceiling(percentile * values.Count) - 1]; }
    private static double AverageOrZero(IReadOnlyCollection<double> values) => values.Count == 0 ? 0 : values.Average();
    private static void SetTokenPercentiles(SuiteReport suite)
    {
        var totals = new List<double>();
        foreach (var result in suite.Cases)
        {
            foreach (var turn in result.Turns)
                if (turn.InputTokens is not null || turn.OutputTokens is not null)
                    totals.Add((turn.InputTokens ?? 0) + (turn.OutputTokens ?? 0));
            if (result.InputTokens is not null || result.OutputTokens is not null)
                totals.Add((result.InputTokens ?? 0) + (result.OutputTokens ?? 0));
        }
        suite.TokenP50 = totals.Count == 0 ? null : Percentile(totals, .5);
        suite.TokenP95 = totals.Count == 0 ? null : Percentile(totals, .95);
    }

    private static async Task<T[]> ReadArrayAsync<T>(string path) => await JsonSerializer.DeserializeAsync<T[]>(File.OpenRead(path), Json) ?? [];
    private static async Task<AgentEvalGateThresholds> ReadThresholdsAsync(string path) => File.Exists(path) ? (await JsonSerializer.DeserializeAsync<AgentEvalGateThresholds>(File.OpenRead(path), Json)) ?? new() : new();

    private static int Usage(string? error = null) { if (error is not null) Console.Error.WriteLine(error); Console.Error.WriteLine("Usage: --suite <coach|describe|label|all> [--repeat 1..10] [--gate] [--report <path>] [--root <AgentEvalHarness directory>] [--generate-labels]"); return 2; }
    private static void GenerateLabels(string root)
    {
        var folder = Path.Combine(root, "cases", "label"); Directory.CreateDirectory(folder);
        var fontFile = Path.Combine(root, "fonts", "DejaVuSans.ttf");
        if (!File.Exists(fontFile)) throw new FileNotFoundException("Bundled DejaVu Sans font not found; label generation cannot be reproducible.", fontFile);
        var collection = new FontCollection(); var family = collection.Add(fontFile); var font = family.CreateFont(25, FontStyle.Regular);
        var data = new[] { ("label-01", 120, 5, 22, 2), ("label-02", 210, 8, 31, 7), ("label-03", 95, 3, 18, 1), ("label-04", 320, 14, 42, 11), ("label-05", 160, 6, 24, 4), ("label-06", 245, 10, 35, 8) };
        var manifestCases = new List<LabelCase>();
        foreach (var (id, kcal, protein, carbs, fat) in data)
        {
            var file = $"{id}.png";
            using var image = new Image<Rgba32>(900, 700);
            image.Mutate(ctx =>
            {
                ctx.BackgroundColor(Color.White);
                ctx.Draw(Color.Black, 3, new RectangleF(12, 12, 876, 676));
                ctx.DrawText("Nutrition Facts", font, Color.Black, new PointF(35, 35));
                ctx.DrawText("Serving size 1 package", font, Color.Black, new PointF(35, 100));
                ctx.DrawText("Amount per serving", font, Color.Black, new PointF(35, 165));
                ctx.DrawText($"Calories {kcal}", font, Color.Black, new PointF(35, 225));
                ctx.DrawText("Total Fat", font, Color.Black, new PointF(35, 315));
                ctx.DrawText($"{fat} g", font, Color.Black, new PointF(680, 315));
                ctx.DrawText("Total Carbohydrate", font, Color.Black, new PointF(35, 400));
                ctx.DrawText($"{carbs} g", font, Color.Black, new PointF(680, 400));
                ctx.DrawText("Protein", font, Color.Black, new PointF(35, 485));
                ctx.DrawText($"{protein} g", font, Color.Black, new PointF(680, 485));
            });
            image.SaveAsPng(Path.Combine(folder, file));
            manifestCases.Add(new LabelCase { Id = id, Image = file, Expected = new() { Calories = kcal, ProteinG = protein, CarbsG = carbs, FatG = fat, ServingGrams = 100 } });
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(new LabelManifest { Cases = manifestCases }, Json));
    }

    private static string DefaultRoot()
    {
        var current = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            current,
            Path.Combine(current, "tools", "AgentEvalHarness"),
            Path.Combine(current, "backend", "tools", "AgentEvalHarness"),
            AppContext.BaseDirectory
        };
        return candidates.FirstOrDefault(path => Directory.Exists(Path.Combine(path, "cases", "coach"))) ?? AppContext.BaseDirectory;
    }


    private sealed class CoachCase { public string Id { get; set; } = ""; public List<string> Turns { get; set; } = []; public List<string> ExpectedToolOrder { get; set; } = []; public bool ExpectDraft { get; set; } public bool ExpectNoDraft { get; set; } public bool ExpectNoCommit { get; set; } public bool ExpectCommit { get; set; } public bool ExpectedRefusalOrRedirect { get; set; } public bool ExpectAllergyConflict { get; set; } public string? Feature { get; set; } public string? Allergy { get; set; } public string? Preference { get; set; } }
    private sealed class DescribeCase { public string Id { get; set; } = ""; public string Description { get; set; } = ""; public string FdcId { get; set; } = ""; public ExpectedNutrition Expected { get; set; } = new(); }
    private sealed class ExpectedNutrition { public double Calories { get; set; } public double ProteinG { get; set; } public double CarbsG { get; set; } public double FatG { get; set; } public double ServingGrams { get; set; } }
    private sealed class LabelManifest { public AgentEvalGateThresholds Thresholds { get; set; } = new(); public List<LabelCase> Cases { get; set; } = []; }
    private sealed class LabelCase { public string Id { get; set; } = ""; public string Image { get; set; } = ""; public ExpectedNutrition Expected { get; set; } = new(); }
    private sealed class SuiteReport
    {
        [System.Text.Json.Serialization.JsonIgnore] public AgentEvalGateThresholds Thresholds { get; set; } = new();
        public string? Deployment { get; set; }
        public string? ReasoningEffort { get; set; }
        public string? PromptSchemaVersion { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public double KcalPercentError { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public double MacroPercentError { get; set; }
        public List<CaseReport> Cases { get; set; } = [];
        public Dictionary<string, double> AssertionPassRates { get; set; } = [];
        public Dictionary<string, int> ProvenanceMix { get; set; } = [];
        public double LatencyP50Ms { get; set; }
        public double LatencyP95Ms { get; set; }
        public double? TokenP50 { get; set; }
        public double? TokenP95 { get; set; }
        public bool GatePassed { get; set; }
        public IReadOnlyList<string> GateFailures { get; set; } = [];
    }
    private sealed class EvalReport
    {
        public string CoachPromptVersion { get; set; } = CoachPrompts.PromptVersion;
        public string? DescribePromptVersion { get; set; } = ContentUnderstandingService.DescribeFoodPromptVersion;
        public Dictionary<string, SuiteReport> Suites { get; set; } = [];
    }
    private sealed record Arguments(string? Suite, bool Gate, string? Report, bool GenerateLabels, string Root, int Repeat)
    {
        public static Arguments Parse(string[] args)
        {
            string? suite = null, report = null;
            var gate = false; var gen = false; var repeat = 1; var root = DefaultRoot();
            for (var i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--suite": suite = args[++i]; break;
                    case "--gate": gate = true; break;
                    case "--report": report = args[++i]; break;
                    case "--generate-labels": gen = true; break;
                    case "--root": root = args[++i]; break;
                    case "--repeat":
                        if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out repeat))
                            throw new ArgumentException("--repeat must be an integer from 1 to 10.");
                        break;
                    default: throw new ArgumentException($"Unknown option {args[i]}");
                }
            return new(suite, gate, report, gen, root, repeat);
        }
    }
    private sealed class CaseReport
    {
        public string Id { get; set; } = "";
        public bool Passed { get; set; }
        public bool Skipped { get; set; }
        public string? SkipReason { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public double? ScoreMean { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public double? ScoreVariance { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public int? PassCount { get; set; }
        public List<TurnReport> Turns { get; set; } = [];
        public Dictionary<string, bool> Assertions { get; set; } = [];
        public Dictionary<string, NutrientError> NutrientErrors { get; set; } = [];
        public string? Provenance { get; set; }
        public double LatencyMs { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
    }
    private sealed class TurnReport
    {
        public string UserText { get; set; } = "";
        public string AssistantText { get; set; } = "";
        public List<string> ToolCalls { get; set; } = [];
        public List<ToolResultReport> ToolResults { get; set; } = [];
        public List<double> DraftTotals { get; set; } = [];
        public Dictionary<string, List<double>> AllowedNutrition { get; set; } = [];
        public int DraftsCreated { get; set; }
        public int MealsCreated { get; set; }
        public double LatencyMs { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
    }
    private sealed class ToolResultReport { public string Name { get; set; } = ""; public string? SummaryJson { get; set; } }
}
