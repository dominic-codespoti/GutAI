using System.Globalization;
using System.Text.Json;
using Azure.Data.Tables;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.Services;

static int Usage(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("Usage: dotnet run --project backend/tools/CorrectionAnalytics -- [--connection <storage connection string> | env GUTAI_STORAGE_CONNECTION] [--since <ISO date>] [--min-samples n] [--out <report.json>]");
    return 2;
}

var values = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is not ("--connection" or "--since" or "--min-samples" or "--out") || i + 1 >= args.Length)
        return Usage($"Invalid or incomplete argument: {args[i]}");
    var key = args[i];
    if (!values.TryAdd(key, args[++i])) return Usage($"Duplicate argument: {key}");
}

var connection = values.GetValueOrDefault("--connection") ?? Environment.GetEnvironmentVariable("GUTAI_STORAGE_CONNECTION");
if (string.IsNullOrWhiteSpace(connection)) return Usage("Provide --connection or set GUTAI_STORAGE_CONNECTION.");
var since = DateTimeOffset.UtcNow.AddDays(-90);
if (values.TryGetValue("--since", out var sinceValue) &&
    !DateTimeOffset.TryParse(sinceValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out since))
    return Usage("--since must be an ISO-8601 date or timestamp.");
var minSamples = 20;
if (values.TryGetValue("--min-samples", out var minValue) && (!int.TryParse(minValue, NumberStyles.None, CultureInfo.InvariantCulture, out minSamples) || minSamples < 1))
    return Usage("--min-samples must be a positive integer.");

try
{
    var store = new TableStorageStore(new TableServiceClient(connection));
    var drafts = await store.GetMealDraftsCreatedSinceAsync(since);
    var report = CorrectionAnalytics.Compute(drafts, new CorrectionAnalyticsOptions { MinSamples = minSamples });
    Console.WriteLine($"Correction analytics since {since:O}: {report.DraftCount} drafts; {report.MalformedRows} malformed committed rows skipped.");
    Console.WriteLine("Acceptance by origin (committed / committed + discarded + expired):");
    foreach (var rate in report.AcceptanceByOrigin)
        Console.WriteLine($"  {rate.Origin}: {rate.Committed}/{rate.Suggested} = {rate.AcceptanceRate:P1} (discarded {rate.Discarded}, expired {rate.Expired})");
    Console.WriteLine("Portion correction cells (median ratio; p25–p75; n):");
    foreach (var cell in report.Portion)
        Console.WriteLine($"  {cell.FoodClass} × {cell.Tier}: {cell.MedianRatio:F3}; {cell.P25:F3}–{cell.P75:F3}; n={cell.N}");
    Console.WriteLine("Swaps by grounding method:");
    foreach (var row in report.SwapsByMethod) Console.WriteLine($"  {row.Method}: {row.Swaps}/{row.N} = {row.Rate:P1}");
    Console.WriteLine($"Removals overall: {report.Removal.Overall.Removed}/{report.Removal.Overall.N} = {report.Removal.Overall.Rate:P1}");
    foreach (var row in report.Removal.ByOrigin)
        Console.WriteLine($"  Removed by origin {row.Name}: {row.Metrics.Removed}/{row.Metrics.N} = {row.Metrics.Rate:P1}");
    foreach (var row in report.Removal.ByMethod)
        Console.WriteLine($"  Removed by method {row.Name}: {row.Metrics.Removed}/{row.Metrics.N} = {row.Metrics.Rate:P1}");
    Console.WriteLine($"Calibration proposal: {report.Calibration.Factors.Count} cells meet n≥{minSamples}; {report.Calibration.BelowMinimum.Count} below minimum.");
    foreach (var cell in report.Calibration.BelowMinimum)
        Console.WriteLine($"  Below minimum: {cell.FoodClass} × {cell.Tier} n={cell.N} (not proposed)");
    Console.WriteLine(report.Calibration.ConfigJson);

    if (values.TryGetValue("--out", out var outputPath))
    {
        var json = JsonSerializer.Serialize(new { Since = since, Report = report }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"Report written to {outputPath}");
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Correction analytics failed: {ex.Message}");
    return 2;
}
