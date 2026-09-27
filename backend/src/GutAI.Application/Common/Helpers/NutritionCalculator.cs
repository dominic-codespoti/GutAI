using GutAI.Application.Common.DTOs;
using GutAI.Domain.Entities;

namespace GutAI.Application.Common.Helpers;

/// <summary>
/// The single nutrition calculator (AGENTS.md N1): absolute nutrition is always
/// basis-per-100g × grams / 100 with one rounding policy — calories and sodium to whole
/// numbers, macros to one decimal, midpoints rounded away from zero (matches JavaScript
/// <c>Math.round</c> for the non-negative values nutrition uses, so the frontend mirror in
/// <c>frontend/src/utils/nutrition.ts</c> displays exactly what the server persists).
/// </summary>
public static class NutritionCalculator
{
    public static NutritionAmountsDto Compute(NutritionPer100gDto basis, decimal grams)
    {
        ArgumentNullException.ThrowIfNull(basis);
        if (grams < 0)
            throw new ArgumentOutOfRangeException(nameof(grams), grams, "Grams cannot be negative.");

        var factor = grams / 100m;
        return new NutritionAmountsDto
        {
            Calories = Round0(basis.CaloriesKcal * factor),
            ProteinG = Round1(basis.ProteinG * factor),
            CarbsG = Round1(basis.CarbsG * factor),
            FatG = Round1(basis.FatG * factor),
            FiberG = basis.FiberG is { } fiber ? Round1(fiber * factor) : null,
            SugarG = basis.SugarG is { } sugar ? Round1(sugar * factor) : null,
            SodiumMg = basis.SodiumMg is { } sodium ? Round0(sodium * factor) : null,
        };
    }

    /// <summary>Sums portions; optional nutrients stay null only when every part is null.</summary>
    public static NutritionAmountsDto Sum(IEnumerable<NutritionAmountsDto> parts)
    {
        decimal calories = 0, protein = 0, carbs = 0, fat = 0;
        decimal? fiber = null, sugar = null, sodium = null;
        foreach (var p in parts)
        {
            calories += p.Calories;
            protein += p.ProteinG;
            carbs += p.CarbsG;
            fat += p.FatG;
            if (p.FiberG is { } f) fiber = (fiber ?? 0) + f;
            if (p.SugarG is { } s) sugar = (sugar ?? 0) + s;
            if (p.SodiumMg is { } n) sodium = (sodium ?? 0) + n;
        }

        return new NutritionAmountsDto
        {
            Calories = calories,
            ProteinG = protein,
            CarbsG = carbs,
            FatG = fat,
            FiberG = fiber,
            SugarG = sugar,
            SodiumMg = sodium,
        };
    }

    /// <summary>Basis from a catalog DTO; null when the product carries no calorie value.</summary>
    public static NutritionPer100gDto? BasisFrom(FoodProductDto product) =>
        product.Calories100g is { } kcal
            ? new NutritionPer100gDto
            {
                CaloriesKcal = kcal,
                ProteinG = product.Protein100g ?? 0,
                CarbsG = product.Carbs100g ?? 0,
                FatG = product.Fat100g ?? 0,
                FiberG = product.Fiber100g,
                SugarG = product.Sugar100g,
                SodiumMg = product.SodiumMg100g,
            }
            : null;

    /// <summary>Basis from a persisted catalog entity; null when it carries no calorie value.</summary>
    public static NutritionPer100gDto? BasisFrom(FoodProduct product) =>
        product.Calories100g is { } kcal
            ? new NutritionPer100gDto
            {
                CaloriesKcal = kcal,
                ProteinG = product.Protein100g ?? 0,
                CarbsG = product.Carbs100g ?? 0,
                FatG = product.Fat100g ?? 0,
                FiberG = product.Fiber100g,
                SugarG = product.Sugar100g,
                SodiumMg = product.SodiumMg100g,
            }
            : null;

    /// <summary>Basis from a grounding candidate snapshot; null when it carries no calorie value.</summary>
    public static NutritionPer100gDto? BasisFrom(GroundingCandidateDto candidate) =>
        candidate.Calories100g is { } kcal
            ? new NutritionPer100gDto
            {
                CaloriesKcal = kcal,
                ProteinG = candidate.Protein100g ?? 0,
                CarbsG = candidate.Carbs100g ?? 0,
                FatG = candidate.Fat100g ?? 0,
                FiberG = candidate.Fiber100g,
                SugarG = candidate.Sugar100g,
                SodiumMg = candidate.SodiumMg100g,
            }
            : null;

    /// <summary>Basis from a web-cascade result (already per 100 g).</summary>
    public static NutritionPer100gDto BasisFrom(WebNutritionResult web) => new()
    {
        CaloriesKcal = web.CaloriesKcal,
        ProteinG = web.ProteinG,
        CarbsG = web.CarbsG,
        FatG = web.FatG,
        FiberG = web.FiberG,
        SugarG = web.SugarG,
        SodiumMg = web.SodiumMg,
    };

    public static decimal Round0(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    public static decimal Round1(decimal value) => decimal.Round(value, 1, MidpointRounding.AwayFromZero);
}
