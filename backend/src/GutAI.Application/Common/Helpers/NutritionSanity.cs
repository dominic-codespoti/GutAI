using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Helpers;

public sealed record NutritionSanityProfile(
    decimal AtwaterTolerance,
    decimal SugarCarbsTolerance,
    decimal FiberCarbsTolerance,
    decimal AtwaterMinimumDifference = 0m)
{
    public bool UseWebLimits { get; init; }

    public static NutritionSanityProfile Catalog { get; } = new(0.4m, 5m, 5m, 40m);
    public static NutritionSanityProfile Web { get; } = new(0.4m, 5m, 5m) { UseWebLimits = true };
}

public enum NutritionIssueSeverity
{
    Soft,
    Hard,
}

public sealed record NutritionIssue(string Code, NutritionIssueSeverity Severity, string Message);

public sealed record NutritionSanityResult(IReadOnlyList<NutritionIssue> Issues)
{
    public static readonly NutritionSanityResult Ok = new([]);

    public bool IsPlausible => Issues.Count == 0;
    public bool HasHardViolation => Issues.Any(issue => issue.Severity == NutritionIssueSeverity.Hard);
    public IReadOnlyList<string> Codes => Issues.Select(issue => issue.Code).ToArray();
}

public static class NutritionSanity
{
    public static class Codes
    {
        public const string NegativeValue = "negative_value";
        public const string EnergyOutOfRange = "energy_out_of_range";
        public const string MacroOutOfRange = "macro_out_of_range";
        public const string MacroMassExceedsTotal = "macro_mass_exceeds_total";
        public const string FiberOutOfRange = "fiber_out_of_range";
        public const string FiberExceedsCarbs = "fiber_exceeds_carbs";
        public const string SugarExceedsCarbs = "sugar_exceeds_carbs";
        public const string SodiumOutOfRange = "sodium_out_of_range";
        public const string EnergyLooksLikeKj = "energy_looks_like_kj";
        public const string AtwaterMismatch = "atwater_mismatch";
    }

    private static readonly string[] AtwaterExemptionCues =
    [
        "beer", "wine", "cider", "sake", "vodka", "whisky", "whiskey", "rum", "gin",
        "tequila", "brandy", "liqueur", "spirit", "spirits", "vermouth", "port", "sherry",
        "mead", "erythritol", "xylitol", "sorbitol", "maltitol", "isomalt", "sugar-free", "sugar free",
    ];

    public static NutritionSanityResult Check(
        NutritionPer100gDto per100g,
        NutritionSanityProfile? profile = null,
        string? foodName = null)
    {
        profile ??= NutritionSanityProfile.Catalog;
        ArgumentNullException.ThrowIfNull(per100g);

        var issues = new List<NutritionIssue>();
        AddNegativeIssue(issues, per100g.CaloriesKcal, per100g.ProteinG, per100g.CarbsG, per100g.FatG,
            per100g.FiberG, per100g.SugarG, per100g.SodiumMg);

        if (profile.UseWebLimits)
        {
            CheckWeb(per100g, profile, foodName, issues);
            return Result(issues);
        }

        if (per100g.CaloriesKcal > 900m)
            Add(issues, Codes.EnergyOutOfRange, NutritionIssueSeverity.Hard,
                $"Energy {per100g.CaloriesKcal} kcal per 100 g exceeds 900 kcal.");

        if (per100g.ProteinG > 100m || per100g.CarbsG > 100m || per100g.FatG > 100m)
            Add(issues, Codes.MacroOutOfRange, NutritionIssueSeverity.Hard,
                $"A macro value (protein {per100g.ProteinG} g, carbohydrates {per100g.CarbsG} g, fat {per100g.FatG} g) exceeds 100 g per 100 g.");

        var macroMass = per100g.ProteinG + per100g.CarbsG + per100g.FatG;
        if (macroMass > 102m)
            Add(issues, Codes.MacroMassExceedsTotal, NutritionIssueSeverity.Hard,
                $"Protein, carbohydrates, and fat total {macroMass} g per 100 g, above 102 g.");

        if (per100g.FiberG > 90m)
            Add(issues, Codes.FiberOutOfRange, NutritionIssueSeverity.Hard,
                $"Fiber {per100g.FiberG} g per 100 g exceeds 90 g.");

        CheckSugarAndFiber(per100g, profile, NutritionIssueSeverity.Soft, issues);

        if (per100g.SodiumMg > 40000m)
            Add(issues, Codes.SodiumOutOfRange, NutritionIssueSeverity.Hard,
                $"Sodium {per100g.SodiumMg} mg per 100 g exceeds 40,000 mg.");

        CheckEnergyRatios(per100g, foodName, profile, issues);
        return Result(issues);
    }

    public static NutritionSanityResult CheckPortion(NutritionAmountsDto amounts, decimal? grams, string? foodName = null)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        var issues = new List<NutritionIssue>();
        AddNegativeIssue(issues, amounts.Calories, amounts.ProteinG, amounts.CarbsG, amounts.FatG,
            amounts.FiberG, amounts.SugarG, amounts.SodiumMg);

        if (grams is > 0m)
        {
            var per100g = new NutritionPer100gDto
            {
                CaloriesKcal = Scale(amounts.Calories, grams.Value),
                ProteinG = Scale(amounts.ProteinG, grams.Value),
                CarbsG = Scale(amounts.CarbsG, grams.Value),
                FatG = Scale(amounts.FatG, grams.Value),
                FiberG = amounts.FiberG is { } fiber ? Scale(fiber, grams.Value) : null,
                SugarG = amounts.SugarG is { } sugar ? Scale(sugar, grams.Value) : null,
                SodiumMg = amounts.SodiumMg is { } sodium ? Scale(sodium, grams.Value) : null,
            };

            var checkedBasis = Check(per100g, NutritionSanityProfile.Catalog, foodName);
            issues.AddRange(checkedBasis.Issues.Where(issue => issue.Code != Codes.NegativeValue));
        }
        else
        {
            var ratioValues = new NutritionPer100gDto
            {
                CaloriesKcal = amounts.Calories,
                ProteinG = amounts.ProteinG,
                CarbsG = amounts.CarbsG,
                FatG = amounts.FatG,
                FiberG = amounts.FiberG,
                SugarG = amounts.SugarG,
                SodiumMg = amounts.SodiumMg,
            };
            CheckEnergyRatios(ratioValues, foodName, NutritionSanityProfile.Catalog, issues);
        }

        return Result(issues);
    }

    private static void CheckWeb(NutritionPer100gDto n, NutritionSanityProfile profile, string? foodName, List<NutritionIssue> issues)
    {
        if (n.CaloriesKcal is < 1m or > 900m)
            Add(issues, Codes.EnergyOutOfRange, NutritionIssueSeverity.Hard,
                $"Energy {n.CaloriesKcal} kcal per 100 g is outside 1–900 kcal.");

        if (n.ProteinG > 90m || n.CarbsG > 100m || n.FatG > 100m)
            Add(issues, Codes.MacroOutOfRange, NutritionIssueSeverity.Hard,
                $"Protein {n.ProteinG} g, carbohydrates {n.CarbsG} g, or fat {n.FatG} g is outside the web limits.");

        if (n.FiberG is < 0m or > 60m)
            Add(issues, Codes.FiberOutOfRange, NutritionIssueSeverity.Hard,
                $"Fiber {n.FiberG} g per 100 g is outside 0–60 g.");

        if (n.SugarG is < 0m or > 100m)
            Add(issues, Codes.MacroOutOfRange, NutritionIssueSeverity.Hard,
                $"Sugar {n.SugarG} g per 100 g is outside 0–100 g.");

        if (n.SodiumMg is < 0m or > 6000m)
            Add(issues, Codes.SodiumOutOfRange, NutritionIssueSeverity.Hard,
                $"Sodium {n.SodiumMg} mg per 100 g is outside 0–6,000 mg.");

        CheckSugarAndFiber(n, profile, NutritionIssueSeverity.Hard, issues);
        CheckAtwaterMismatch(n, profile, foodName, NutritionIssueSeverity.Hard, issues);
    }

    private static void CheckEnergyRatios(
        NutritionPer100gDto n,
        string? foodName,
        NutritionSanityProfile profile,
        List<NutritionIssue> issues)
    {
        var macroKcal = MacroKcal(n);
        var looksLikeKj = macroKcal >= 25m && n.CaloriesKcal > 100m
            && n.CaloriesKcal >= 3.8m * macroKcal && n.CaloriesKcal <= 4.6m * macroKcal;

        if (looksLikeKj)
        {
            Add(issues, Codes.EnergyLooksLikeKj, NutritionIssueSeverity.Hard,
                $"Energy {n.CaloriesKcal} kcal looks like kJ for macro energy {macroKcal} kcal.");
            return;
        }

        CheckAtwaterMismatch(n, profile, foodName, NutritionIssueSeverity.Soft, issues);
    }

    private static void CheckAtwaterMismatch(
        NutritionPer100gDto n,
        NutritionSanityProfile profile,
        string? foodName,
        NutritionIssueSeverity severity,
        List<NutritionIssue> issues)
    {
        var macroKcal = MacroKcal(n);
        var difference = Math.Abs(macroKcal - n.CaloriesKcal);
        var tolerance = Math.Max(n.CaloriesKcal * profile.AtwaterTolerance, profile.AtwaterMinimumDifference);
        if (macroKcal > 0m && n.CaloriesKcal >= 20m
            && difference > tolerance
            && !HasAtwaterExemption(foodName))
        {
            Add(issues, Codes.AtwaterMismatch, severity,
                $"Macro energy {macroKcal} kcal differs from reported energy {n.CaloriesKcal} kcal.");
        }
    }

    private static void CheckSugarAndFiber(
        NutritionPer100gDto n,
        NutritionSanityProfile profile,
        NutritionIssueSeverity severity,
        List<NutritionIssue> issues)
    {
        if (n.SugarG is { } sugar && sugar > n.CarbsG + profile.SugarCarbsTolerance)
            Add(issues, Codes.SugarExceedsCarbs, severity,
                $"Sugar {sugar} g exceeds carbohydrates {n.CarbsG} g by more than {profile.SugarCarbsTolerance} g.");

        if (n.FiberG is { } fiber && fiber > n.CarbsG + profile.FiberCarbsTolerance)
            Add(issues, Codes.FiberExceedsCarbs, severity,
                $"Fiber {fiber} g exceeds carbohydrates {n.CarbsG} g by more than {profile.FiberCarbsTolerance} g.");
    }

    private static bool HasAtwaterExemption(string? foodName)
    {
        if (string.IsNullOrWhiteSpace(foodName)) return false;
        return AtwaterExemptionCues.Any(cue => Regex.IsMatch(
            foodName,
            $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(cue).Replace("\\ ", @"\s+")}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static decimal MacroKcal(NutritionPer100gDto n) => 4m * (n.ProteinG + n.CarbsG) + 9m * n.FatG;

    private static decimal Scale(decimal value, decimal grams) => value * 100m / grams;


    private static void AddNegativeIssue(List<NutritionIssue> issues, params decimal?[] values)
    {
        if (values.Any(value => value < 0m))
        {
            var offending = string.Join(", ", values.Where(value => value < 0m).Select(value => value!.Value));
            Add(issues, Codes.NegativeValue, NutritionIssueSeverity.Hard,
                $"Nutrition contains negative value(s): {offending}.");
        }
    }

    private static void Add(List<NutritionIssue> issues, string code, NutritionIssueSeverity severity, string message) =>
        issues.Add(new NutritionIssue(code, severity, message));

    private static NutritionSanityResult Result(List<NutritionIssue> issues) =>
        issues.Count == 0 ? NutritionSanityResult.Ok : new NutritionSanityResult(issues);
}
