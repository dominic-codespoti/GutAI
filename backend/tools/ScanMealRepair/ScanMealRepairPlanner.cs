using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;

namespace ScanMealRepair;

public sealed record DraftCandidateInput(
    Guid? FoodProductId,
    string Source,
    decimal? Calories100g,
    decimal? Protein100g,
    decimal? Carbs100g,
    decimal? Fat100g,
    decimal? Fiber100g = null,
    decimal? Sugar100g = null,
    decimal? SodiumMg100g = null);

public sealed record LegacyDraftItemInput(
    Guid ItemId,
    string Name,
    string? CanonicalName,
    Guid? FoodProductId,
    string Source,
    decimal Grams,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG,
    decimal? FiberG = null,
    decimal? SugarG = null,
    decimal? SodiumMg = null,
    IReadOnlyList<DraftCandidateInput>? Candidates = null);

public sealed record CommittedMealItemInput(
    string Id,
    string FoodName,
    Guid? FoodProductId,
    decimal ServingWeightG,
    decimal Calories,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    string? NutritionProvenance);

public sealed record ItemNutrition(
    decimal Calories,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    string? NutritionProvenance);

public sealed record ScanMealRepair(
    string CommittedItemId,
    Guid DraftItemId,
    string Reason,
    ItemNutrition Before,
    ItemNutrition After);

public sealed record ScanMealAmbiguity(string CommittedItemId, string Reason);

public sealed record ScanMealRepairPlan(
    IReadOnlyList<ScanMealRepair> Repairs,
    IReadOnlyList<ScanMealAmbiguity> Ambiguities,
    NutritionAmountsDto MealTotals);

/// <summary>Plans deterministic corrections for historical scan-confirmed meal rows.</summary>
public static class ScanMealRepairPlanner
{
    public static ScanMealRepairPlan Plan(
        IReadOnlyList<LegacyDraftItemInput> drafts,
        IReadOnlyList<CommittedMealItemInput> committedItems)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(committedItems);

        var draftForCommitted = new int?[committedItems.Count];
        var ambiguous = new Dictionary<int, string>();
        var usedDrafts = new HashSet<int>();

        for (var i = 0; i < committedItems.Count; i++)
        {
            var committed = committedItems[i];
            if (committed.FoodProductId is not { } foodProductId) continue;
            var matches = Enumerable.Range(0, drafts.Count)
                .Where(d => drafts[d].FoodProductId == foodProductId)
                .ToArray();
            if (matches.Length > 1)
            {
                ambiguous[i] = "multiple food-product matches";
                foreach (var draft in matches) usedDrafts.Add(draft);
            }
            else if (matches.Length == 1 && usedDrafts.Add(matches[0]))
            {
                draftForCommitted[i] = matches[0];
            }
            else if (matches.Length == 1)
            {
                ambiguous[i] = "multiple committed items match draft";
            }
        }

        for (var i = 0; i < committedItems.Count; i++)
        {
            if (draftForCommitted[i] is not null || ambiguous.ContainsKey(i)) continue;
            var committed = committedItems[i];
            var matches = Enumerable.Range(0, drafts.Count)
                .Where(d => !usedDrafts.Contains(d) && NameMatches(committed.FoodName, drafts[d]))
                .ToArray();
            AssignOrMarkAmbiguous(i, matches, "multiple name matches", draftForCommitted, ambiguous, usedDrafts);
        }

        var unmatchedCommitted = Enumerable.Range(0, committedItems.Count)
            .Where(i => draftForCommitted[i] is null && !ambiguous.ContainsKey(i)).ToArray();
        var unmatchedDrafts = Enumerable.Range(0, drafts.Count).Where(d => !usedDrafts.Contains(d)).ToArray();
        if (drafts.Count == committedItems.Count && unmatchedCommitted.Length == 1 && unmatchedDrafts.Length == 1)
        {
            var committedIndex = unmatchedCommitted[0];
            var draftIndex = unmatchedDrafts[0];
            if (committedIndex == draftIndex)
            {
                draftForCommitted[committedIndex] = draftIndex;
                usedDrafts.Add(draftIndex);
            }
        }
        else if (drafts.Count == committedItems.Count && unmatchedCommitted.Length == unmatchedDrafts.Length && unmatchedCommitted.Length > 1)
        {
            foreach (var i in unmatchedCommitted)
                ambiguous[i] = "unmatched positional items";
        }

        var repairs = new List<ScanMealRepair>();
        var ambiguities = new List<ScanMealAmbiguity>();
        var finalNutrition = committedItems.Select(ToNutrition).ToArray();

        for (var i = 0; i < committedItems.Count; i++)
        {
            if (ambiguous.TryGetValue(i, out var ambiguity))
            {
                ambiguities.Add(new ScanMealAmbiguity(committedItems[i].Id, ambiguity));
                continue;
            }
            if (draftForCommitted[i] is not { } draftIndex) continue;

            var draft = drafts[draftIndex];
            var committed = committedItems[i];
            var staleGrams = draft.Calories is { } draftCalories &&
                             Math.Abs(committed.ServingWeightG - draft.Grams) > 0.5m &&
                             committed.Calories == NutritionCalculator.Round0(draftCalories);
            var webZero = string.Equals(draft.Source, "web", StringComparison.OrdinalIgnoreCase) &&
                          committed.Calories == 0m && draft.Calories is > 0m;
            if (!staleGrams && !webZero) continue;

            var basis = FindBasis(draft, committed.FoodProductId);
            if (basis is null)
            {
                ambiguities.Add(new ScanMealAmbiguity(committed.Id, "no basis"));
                continue;
            }

            var computed = NutritionCalculator.Compute(basis, committed.ServingWeightG);
            var reason = webZero ? "web-zero" : "stale-grams";
            var after = new ItemNutrition(
                computed.Calories,
                computed.ProteinG,
                computed.CarbsG,
                computed.FatG,
                computed.FiberG,
                computed.SugarG,
                computed.SodiumMg,
                webZero ? "Web" : committed.NutritionProvenance);
            var before = ToNutrition(committed);
            repairs.Add(new ScanMealRepair(committed.Id, draft.ItemId, reason, before, after));
            finalNutrition[i] = after;
        }

        var totals = NutritionCalculator.Sum(finalNutrition.Select(n => new NutritionAmountsDto
        {
            Calories = n.Calories,
            ProteinG = n.ProteinG,
            CarbsG = n.CarbsG,
            FatG = n.FatG,
            FiberG = n.FiberG,
            SugarG = n.SugarG,
            SodiumMg = n.SodiumMg
        }));

        return new ScanMealRepairPlan(repairs, ambiguities, totals);
    }

    private static void AssignOrMarkAmbiguous(
        int committedIndex,
        int[] matches,
        string reason,
        int?[] draftForCommitted,
        Dictionary<int, string> ambiguous,
        HashSet<int> usedDrafts)
    {
        if (matches.Length == 1)
        {
            draftForCommitted[committedIndex] = matches[0];
            usedDrafts.Add(matches[0]);
        }
        else if (matches.Length > 1)
        {
            ambiguous[committedIndex] = reason;
            foreach (var draft in matches) usedDrafts.Add(draft);
        }
    }

    private static bool NameMatches(string committedName, LegacyDraftItemInput draft) =>
        string.Equals(committedName, draft.Name, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(draft.CanonicalName) &&
         string.Equals(committedName, draft.CanonicalName, StringComparison.OrdinalIgnoreCase));

    private static NutritionPer100gDto? FindBasis(LegacyDraftItemInput draft, Guid? committedFoodProductId)
    {
        var candidate = draft.Candidates?.FirstOrDefault(c =>
            committedFoodProductId is { } id && c.FoodProductId == id && c.Calories100g is not null);
        if (candidate is not null)
        {
            return new NutritionPer100gDto
            {
                CaloriesKcal = candidate.Calories100g!.Value,
                ProteinG = candidate.Protein100g ?? 0m,
                CarbsG = candidate.Carbs100g ?? 0m,
                FatG = candidate.Fat100g ?? 0m,
                FiberG = candidate.Fiber100g,
                SugarG = candidate.Sugar100g,
                SodiumMg = candidate.SodiumMg100g
            };
        }

        if (draft.Grams <= 0m || draft.Calories is null) return null;
        return new NutritionPer100gDto
        {
            CaloriesKcal = Per100(draft.Calories.Value, draft.Grams),
            ProteinG = Per100(draft.ProteinG ?? 0m, draft.Grams),
            CarbsG = Per100(draft.CarbsG ?? 0m, draft.Grams),
            FatG = Per100(draft.FatG ?? 0m, draft.Grams),
            FiberG = draft.FiberG is { } fiber ? Per100(fiber, draft.Grams) : null,
            SugarG = draft.SugarG is { } sugar ? Per100(sugar, draft.Grams) : null,
            SodiumMg = draft.SodiumMg is { } sodium ? Per100(sodium, draft.Grams) : null
        };
    }

    private static decimal Per100(decimal value, decimal grams) => value / grams * 100m;

    private static ItemNutrition ToNutrition(CommittedMealItemInput item) => new(
        item.Calories, item.ProteinG, item.CarbsG, item.FatG, item.FiberG, item.SugarG, item.SodiumMg, item.NutritionProvenance);
}
