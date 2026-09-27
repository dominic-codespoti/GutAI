using FluentAssertions;
using ScanMealRepair;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class ScanMealRepairPlannerTests
{
    [Fact]
    public void Repairs_stale_grams_from_matching_candidate_per_100g()
    {
        var draft = Draft(grams: 100m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m,
            foodProductId: Guid.NewGuid(), candidates: [new DraftCandidateInput(
                Guid.Parse("11111111-1111-1111-1111-111111111111"), "usda", 250m, 12m, 30m, 8m, 4m, 6m, 100m)]);
        var committed = Item("MEALITEM|meal|item", "Food", Guid.Parse("11111111-1111-1111-1111-111111111111"),
            grams: 200m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m, provenance: "Sourced");

        var result = ScanMealRepairPlanner.Plan([draft], [committed]);

        result.Repairs.Should().ContainSingle();
        result.Repairs[0].Reason.Should().Be("stale-grams");
        result.Repairs[0].After.Should().Be(new ItemNutrition(500m, 24m, 60m, 16m, 8m, 12m, 200m, "Sourced"));
        result.Ambiguities.Should().BeEmpty();
    }

    [Fact]
    public void Repairs_stale_grams_from_draft_derived_basis_when_candidate_is_missing()
    {
        var draft = Draft(grams: 100m, calories: 123m, protein: 7.5m, carbs: 11.2m, fat: 3.4m,
            fiber: 2.1m, sugar: 4.2m, sodium: 55m);
        var committed = Item("item", "Food", null, grams: 250m, calories: 123m, protein: 7.5m, carbs: 11.2m, fat: 3.4m);

        var result = ScanMealRepairPlanner.Plan([draft], [committed]);

        result.Repairs.Should().ContainSingle();
        result.Repairs[0].After.Should().Be(new ItemNutrition(308m, 18.8m, 28m, 8.5m, 5.3m, 10.5m, 138m, null));
    }

    [Fact]
    public void Restores_web_item_and_sets_web_provenance()
    {
        var draft = Draft(grams: 100m, calories: 210m, protein: 8m, carbs: 25m, fat: 7m, source: "web");
        var committed = Item("item", "Food", null, grams: 150m, calories: 0m, protein: 0m, carbs: 0m, fat: 0m, provenance: "Sourced");

        var result = ScanMealRepairPlanner.Plan([draft], [committed]);

        result.Repairs.Should().ContainSingle();
        result.Repairs[0].Reason.Should().Be("web-zero");
        result.Repairs[0].After.Should().Be(new ItemNutrition(315m, 12m, 37.5m, 10.5m, null, null, null, "Web"));
    }

    [Fact]
    public void Leaves_unchanged_grams_untouched()
    {
        var draft = Draft(grams: 100m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m);
        var committed = Item("item", "Food", null, grams: 100.5m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m);

        var result = ScanMealRepairPlanner.Plan([draft], [committed]);

        result.Repairs.Should().BeEmpty();
        result.Ambiguities.Should().BeEmpty();
        result.MealTotals.Calories.Should().Be(200m);
    }

    [Fact]
    public void Leaves_already_recomputed_item_untouched()
    {
        var draft = Draft(grams: 100m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m);
        var committed = Item("item", "Food", null, grams: 200m, calories: 400m, protein: 20m, carbs: 40m, fat: 10m);

        var result = ScanMealRepairPlanner.Plan([draft], [committed]);

        result.Repairs.Should().BeEmpty();
        result.MealTotals.Calories.Should().Be(400m);
    }

    [Fact]
    public void Reports_duplicate_name_match_as_ambiguity_without_changing_item()
    {
        var drafts = new[]
        {
            Draft(name: "Food", grams: 100m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m),
            Draft(name: "food", grams: 100m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m)
        };
        var committed = Item("item", "FOOD", null, grams: 250m, calories: 200m, protein: 10m, carbs: 20m, fat: 5m);

        var result = ScanMealRepairPlanner.Plan(drafts, [committed]);

        result.Repairs.Should().BeEmpty();
        result.Ambiguities.Should().ContainSingle().Which.Should().Be(new ScanMealAmbiguity("item", "multiple name matches"));
        result.MealTotals.Calories.Should().Be(200m);
    }

    [Fact]
    public void Recomputes_meal_totals_from_repaired_and_unchanged_items()
    {
        var staleDraft = Draft(itemId: Guid.NewGuid(), name: "Stale", grams: 100m, calories: 100m,
            protein: 5m, carbs: 10m, fat: 2m, fiber: 1m);
        var unchangedDraft = Draft(itemId: Guid.NewGuid(), name: "Other", grams: 100m, calories: 80m,
            protein: 2m, carbs: 8m, fat: 1m, sugar: 3m);
        var stale = Item("stale-row", "Stale", null, grams: 200m, calories: 100m, protein: 5m,
            carbs: 10m, fat: 2m, fiber: 2m);
        var unchanged = Item("unchanged-row", "Other", null, grams: 100m, calories: 80m,
            protein: 2m, carbs: 8m, fat: 1m, sugar: 3m);

        var result = ScanMealRepairPlanner.Plan([staleDraft, unchangedDraft], [stale, unchanged]);

        result.Repairs.Should().ContainSingle();
        result.MealTotals.Calories.Should().Be(280m);
        result.MealTotals.ProteinG.Should().Be(12m);
        result.MealTotals.CarbsG.Should().Be(28m);
        result.MealTotals.FatG.Should().Be(5m);
        // The repaired item has 1 g fiber per 100 g × 200 g = 2 g; the unchanged item has no fiber value.
        result.MealTotals.FiberG.Should().Be(2m);
        result.MealTotals.SugarG.Should().Be(3m);
    }

    private static LegacyDraftItemInput Draft(
        decimal grams,
        decimal calories,
        decimal protein,
        decimal carbs,
        decimal fat,
        string name = "Food",
        Guid? itemId = null,
        Guid? foodProductId = null,
        string source = "usda",
        decimal? fiber = null,
        decimal? sugar = null,
        decimal? sodium = null,
        IReadOnlyList<DraftCandidateInput>? candidates = null) => new(
            itemId ?? Guid.NewGuid(), name, null, foodProductId, source, grams, calories,
            protein, carbs, fat, fiber, sugar, sodium, candidates);

    private static CommittedMealItemInput Item(
        string id,
        string name,
        Guid? foodProductId,
        decimal grams,
        decimal calories,
        decimal protein,
        decimal carbs,
        decimal fat,
        decimal? fiber = null,
        decimal? sugar = null,
        decimal? sodium = null,
        string? provenance = null) => new(
            id, name, foodProductId, grams, calories, protein, carbs, fat, fiber, sugar, sodium, provenance);
}
