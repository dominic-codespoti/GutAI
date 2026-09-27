using System.Globalization;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using Microsoft.Extensions.Configuration;

namespace GutAI.Infrastructure.Services;

/// <summary>Applies versioned, opt-in portion corrections without changing Stage-A ranges.</summary>
public sealed class PortionCalibrator
{
    private const string CalibrationSection = "MealScan:PortionCalibration";
    private readonly bool _enabled;
    private readonly IReadOnlyDictionary<(string FoodClass, string Tier), decimal> _factors;

    public PortionCalibrator(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _enabled = config.GetValue<bool>("Features:PortionCalibration");

        var version = config[$"{CalibrationSection}:Version"]?.Trim();
        var factors = new Dictionary<(string FoodClass, string Tier), decimal>();
        if (_enabled && !string.IsNullOrEmpty(version))
        {
            foreach (var entry in config.GetSection($"{CalibrationSection}:Factors").GetChildren())
            {
                var foodClass = entry["FoodClass"]?.Trim().ToLowerInvariant();
                var tier = entry["Tier"]?.Trim().ToLowerInvariant();
                var rawFactor = entry["Factor"];
                if (string.IsNullOrEmpty(foodClass) || !IsKnownClass(foodClass) ||
                    tier is not ("low" or "medium" or "high" or "any") ||
                    !decimal.TryParse(rawFactor, NumberStyles.Number, CultureInfo.InvariantCulture, out var factor) ||
                    factor is < 0.5m or > 2.0m)
                    continue;

                factors.TryAdd((foodClass, tier), factor);
            }
        }

        _factors = factors;
        Version = _enabled && factors.Count > 0 ? version : null;
    }

    public string? Version { get; }

    public MealDraftItemDto Apply(MealDraftItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_enabled)
            return item;

        var foodClass = FoodClassClassifier.Classify(item.Name);
        var tier = FoodClassClassifier.ConfidenceTier(item.PortionConfidence);
        if (!_factors.TryGetValue((foodClass, tier), out var factor) &&
            !_factors.TryGetValue((foodClass, "any"), out factor))
            return item;

        var grams = NutritionCalculator.Round1(item.Grams * factor);
        if (item.PortionLowGrams is { } low)
            grams = Math.Max(grams, low);
        if (item.PortionHighGrams is { } high)
            grams = Math.Min(grams, high);
        if (grams == item.Grams)
            return item;

        var calibrated = item with
        {
            Grams = grams,
            PortionMethod = "vision_estimate_calibrated"
        };
        if (item.Per100g is { } basis)
        {
            var nutrition = NutritionCalculator.Compute(basis, grams);
            calibrated.Calories = nutrition.Calories;
            calibrated.ProteinG = nutrition.ProteinG;
            calibrated.CarbsG = nutrition.CarbsG;
            calibrated.FatG = nutrition.FatG;
            calibrated.FiberG = nutrition.FiberG;
            calibrated.SugarG = nutrition.SugarG;
            calibrated.SodiumMg = nutrition.SodiumMg;
        }
        else
        {
            calibrated.Calories = null;
            calibrated.ProteinG = null;
            calibrated.CarbsG = null;
            calibrated.FatG = null;
            calibrated.FiberG = null;
            calibrated.SugarG = null;
            calibrated.SodiumMg = null;
        }

        return calibrated;
    }

    private static bool IsKnownClass(string value) => value is
        FoodClassClassifier.Classes.Starch or
        FoodClassClassifier.Classes.Protein or
        FoodClassClassifier.Classes.Vegetable or
        FoodClassClassifier.Classes.Fruit or
        FoodClassClassifier.Classes.Dairy or
        FoodClassClassifier.Classes.FatSauce or
        FoodClassClassifier.Classes.Beverage or
        FoodClassClassifier.Classes.Dessert or
        FoodClassClassifier.Classes.MixedDish or
        FoodClassClassifier.Classes.Other;
}
