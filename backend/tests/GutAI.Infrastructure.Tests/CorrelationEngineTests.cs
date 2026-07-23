using FluentAssertions;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class CorrelationEngineTests
{
    private static CorrelationEngine MakeSut(GutAI.Application.Common.Interfaces.ITableStore store)
        => new(store);

    // ─── No data ────────────────────────────────────────────────────────

    [Fact]
    public async Task NoData_ReturnsEmpty()
    {
        var store = MockTableStoreFactory.Create().Object;
        var sut = MakeSut(store);

        var result = await sut.ComputeCorrelationsAsync(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), DateOnly.FromDateTime(DateTime.UtcNow));

        result.Should().BeEmpty();
    }

    // ─── Onset window boundaries (1-6h, shared with FoodDiaryAnalysisService) ──

    [Fact]
    public async Task SymptomWithinWindow_CountsTowardCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().Contain(c => c.FoodOrAdditive == "Garlic Bread" && c.SymptomName == "Bloating");
    }

    [Fact]
    public async Task SymptomBeforeMinOnset_ExcludedFromCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            // 30 minutes — below the 1h minimum onset.
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddMinutes(30), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().NotContain(c => c.FoodOrAdditive == "Garlic Bread");
    }

    [Fact]
    public async Task SymptomAfterMaxOnset_ExcludedFromCorrelation()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            // 7 hours — beyond the 6h maximum onset shared with FoodDiaryAnalysisService.
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(7), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().NotContain(c => c.FoodOrAdditive == "Garlic Bread");
    }

    [Fact]
    public async Task SymptomExactlyAtMaxOnsetBoundary_StillCounted()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(6), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().Contain(c => c.FoodOrAdditive == "Garlic Bread");
    }

    // ─── Occurrence threshold (>= 3 meals required to surface at all) ────

    [Fact]
    public async Task FewerThanThreeOccurrences_NotSurfaced()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 2; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Sushi"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Nausea", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().NotContain(c => c.FoodOrAdditive == "Sushi");
    }

    // ─── Confidence tiers ─────────────────────────────────────────────────

    [Theory]
    [InlineData(15, "High")]
    [InlineData(5, "Medium")]
    [InlineData(3, "Low")]
    public async Task ConfidenceTier_MatchesOccurrenceCount(int occurrenceCount, string expectedConfidence)
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < occurrenceCount; i++)
        {
            var mealTime = now.AddDays(-(occurrenceCount * 2) + i * 2);
            meals.AddRange(MakeMeal(userId, mealTime, "Dairy"));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Cramps", 5));
        }

        // Baseline (non-exposure) meals with no symptom nearby — without these there is no
        // comparison group, so confidence would be capped at Medium regardless of support.
        for (int i = 0; i < 5; i++)
        {
            var mealTime = now.AddDays(-(occurrenceCount * 2 + 20) + i * 2);
            meals.AddRange(MakeMeal(userId, mealTime, "Baseline Salad"));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-(occurrenceCount * 2 + 25))), DateOnly.FromDateTime(now));

        var correlation = result.First(c => c.FoodOrAdditive == "Dairy");
        correlation.Confidence.Should().Be(expectedConfidence);
    }

    // ─── Normalized-name grouping (case/plural fragmentation fix) ────────

    [Fact]
    public async Task DifferentCasingAndPlurals_GroupedAsSameFood()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        var variants = new[] { "Chicken Wing", "chicken wings", "CHICKEN WING" };
        for (int i = 0; i < variants.Length; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, variants[i]));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        // All three casing/plural variants must merge into a single correlation bucket
        // with 3 occurrences (crossing the >=3 threshold) rather than three separate
        // buckets of 1 occurrence each that would never surface.
        result.Should().ContainSingle(c => c.SymptomName == "Bloating");
        result.Single(c => c.SymptomName == "Bloating").Occurrences.Should().Be(3);
    }

    // ─── Additive correlation ──────────────────────────────────────────

    [Fact]
    public async Task AdditiveInProduct_CorrelatesWithSymptom()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var productId = Guid.NewGuid();

        var product = new FoodProduct { Id = productId, Name = "Diet Soda" };
        var additive = new FoodAdditive { Id = 1, Name = "Aspartame", Category = "Sweetener" };

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Diet Soda", productId));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(2), "Headache", 5));
        }

        var store = MockTableStoreFactory.Create(
            meals: meals,
            symptoms: symptoms,
            foodProducts: [product],
            additives: [additive],
            additiveIdsByProduct: new Dictionary<Guid, List<int>> { [productId] = [1] }
        ).Object;

        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().Contain(c => c.FoodOrAdditive == "[additive] Aspartame" && c.SymptomName == "Headache");
    }

    // ─── User isolation ────────────────────────────────────────────────

    [Fact]
    public async Task OnlyReturnsCorrelationsForSpecifiedUser()
    {
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 3; i++)
        {
            var mealTime = now.AddDays(-10 + i * 3);
            meals.AddRange(MakeMeal(otherUser, mealTime, "Peanuts"));
            symptoms.AddRange(MakeSymptom(otherUser, mealTime.AddHours(3), "Hives", 8));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-30)), DateOnly.FromDateTime(now));

        result.Should().BeEmpty();
    }

    // ─── Attribution precedence and double-counting fix ─────────────────

    [Fact]
    public async Task RelatedMealLogId_PinsAttributionToLinkedMeal_NotEveryCandidateMeal()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 5; i++)
        {
            var toastTime = now.AddDays(-30 + i * 5);
            var eggsTime = toastTime.AddHours(2);
            var toastMeal = MakeMeal(userId, toastTime, "Toast")[0];
            var eggsMeal = MakeMeal(userId, eggsTime, "Eggs")[0];
            meals.Add(toastMeal);
            meals.Add(eggsMeal);

            // Both meals fall within the 1-6h onset window of the symptom, but the user
            // explicitly linked it to the toast meal — that link must take precedence over
            // splitting inferred evidence across every candidate meal.
            symptoms.Add(new SymptomLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SymptomTypeId = 1,
                Severity = 6,
                OccurredAt = toastTime.AddHours(3),
                RelatedMealLogId = toastMeal.Id,
                SymptomType = new SymptomType { Id = 1, Name = "Bloating", Category = "GI" },
            });
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-40)), DateOnly.FromDateTime(now));

        result.Should().Contain(c => c.FoodOrAdditive == "Toast" && c.SymptomName == "Bloating" && c.Occurrences == 5);
        result.Should().NotContain(c => c.FoodOrAdditive == "Eggs");
    }

    [Fact]
    public async Task InferredAttribution_SplitsOneSymptomAcrossCandidateMeals_DoesNotDoubleCount()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 6; i++)
        {
            var toastTime = now.AddDays(-30 + i * 5);
            var eggsTime = toastTime.AddHours(2);
            meals.AddRange(MakeMeal(userId, toastTime, "Toast"));
            meals.AddRange(MakeMeal(userId, eggsTime, "Eggs"));
            // No RelatedMealLogId — both meals are equally plausible candidates, so this one
            // event must split its evidence between them rather than crediting both fully.
            symptoms.AddRange(MakeSymptom(userId, toastTime.AddHours(3), "Bloating", 6));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-40)), DateOnly.FromDateTime(now));

        var toast = result.Single(c => c.FoodOrAdditive == "Toast");
        var eggs = result.Single(c => c.FoodOrAdditive == "Eggs");
        // 6 symptom events split 50/50 across two equally-plausible meals each time: 3
        // occurrences apiece, not 6 apiece (which would double the true evidence).
        toast.Occurrences.Should().Be(3);
        eggs.Occurrences.Should().Be(3);
    }

    // ─── Ranking / cap ──────────────────────────────────────────────────

    [Fact]
    public async Task ResultsOrderedByOccurrenceDescending_AndCappedAtTwenty()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();

        for (int food = 0; food < 25; food++)
        {
            var occurrences = 3 + food; // ensures each food crosses the surfacing threshold with a distinct count
            for (int i = 0; i < occurrences; i++)
            {
                var mealTime = now.AddDays(-200 + (food * occurrences + i));
                meals.AddRange(MakeMeal(userId, mealTime, $"Food{food}"));
                symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(2), "Bloating", 5));
            }
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-400)), DateOnly.FromDateTime(now));

        result.Should().HaveCountLessOrEqualTo(20);
        result.Should().BeInDescendingOrder(c => c.Occurrences);
    }

    // ─── Behavioral invariants (Phase 7) ───────────────────────────────

    [Fact]
    public async Task EqualExposedAndBaselineRates_DoesNotSurfacesAsStrongEvidence()
    {
        // 10 pizza meals with 5 symptoms (50% exposed rate) against 10 baseline
        // (non-pizza) meals also with 5 symptoms (50% baseline rate) — the rates are
        // identical, so this food must never surface as High or Medium confidence
        // regardless of how many occurrences it accumulates.
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();

        for (int i = 0; i < 10; i++)
        {
            var pizzaTime = now.AddDays(-60 + i * 5);
            meals.AddRange(MakeMeal(userId, pizzaTime, "Pizza"));
            if (i % 2 == 0) // 5 out of 10
                symptoms.AddRange(MakeSymptom(userId, pizzaTime.AddHours(3), "Bloating", 5));

            // Baseline meals on separate days (≥8h apart) so symptoms don't
            // overlap with pizza onset windows.
            var saladTime = now.AddDays(-60 + i * 5 + 20);
            meals.AddRange(MakeMeal(userId, saladTime, "Caesar Salad"));
            if (i % 2 == 0)
                symptoms.AddRange(MakeSymptom(userId, saladTime.AddHours(3), "Bloating", 5));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(
            userId, DateOnly.FromDateTime(now.AddDays(-70)), DateOnly.FromDateTime(now));

        var pizza = result.FirstOrDefault(c => c.FoodOrAdditive == "Pizza");
        // It may surface (5 weighted occurrences crosses the ≥3 display
        // threshold), but confidence must not be higher than Low.
        if (pizza is not null)
            pizza.Confidence.Should().Be("Low");
    }

    [Fact]
    public async Task IncreasingExposureRate_ProducesMonotonicallyHigherConfidence()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Three foods with identical baseline rates but increasing exposure rates.
        // Baseline: 6 "Rice" meals, 1 symptom (16.7% baseline).
        var baseline = new List<MealLog>();
        var baselineSymptoms = new List<SymptomLog>();
        for (int i = 0; i < 6; i++)
        {
            baseline.AddRange(MakeMeal(userId, now.AddDays(-90 + i * 5), "Rice"));
            if (i == 0) baselineSymptoms.AddRange(MakeSymptom(userId, now.AddDays(-90).AddHours(3), "Bloating", 5));
        }

        // Food A: 6 meals, 3 symptoms → 50%. Should be Low.
        var mealsA = new List<MealLog>(baseline);
        var symptomsA = new List<SymptomLog>(baselineSymptoms);
        for (int i = 0; i < 6; i++)
        {
            mealsA.AddRange(MakeMeal(userId, now.AddDays(-60 + i * 5), "FoodA"));
            if (i < 3) symptomsA.AddRange(MakeSymptom(userId, now.AddDays(-60 + i * 5).AddHours(3), "Bloating", 5));
        }
        var resultA = await MakeSut(MockTableStoreFactory.Create(meals: mealsA, symptoms: symptomsA).Object)
            .ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-100)), DateOnly.FromDateTime(now));
        var confA = resultA.First(c => c.FoodOrAdditive == "FoodA").Confidence;

        // Food B: 6 meals, 5 symptoms → 83%. Higher than A.
        var mealsB = new List<MealLog>(baseline);
        var symptomsB = new List<SymptomLog>(baselineSymptoms);
        for (int i = 0; i < 6; i++)
        {
            mealsB.AddRange(MakeMeal(userId, now.AddDays(-60 + i * 5), "FoodB"));
            if (i < 5) symptomsB.AddRange(MakeSymptom(userId, now.AddDays(-60 + i * 5).AddHours(3), "Bloating", 5));
        }
        var resultB = await MakeSut(MockTableStoreFactory.Create(meals: mealsB, symptoms: symptomsB).Object)
            .ComputeCorrelationsAsync(userId, DateOnly.FromDateTime(now.AddDays(-100)), DateOnly.FromDateTime(now));
        var confB = resultB.First(c => c.FoodOrAdditive == "FoodB").Confidence;

        confA.Should().Be("Low");
        // "Medium" > "Low" lexicographically; the confidence tier must not go down
        // when the exposed rate nearly doubles against the same baseline.
        confB.Should().NotBe("Low", "higher exposure rate against the same baseline should produce higher confidence");
    }

    [Fact]
    public async Task LowMatchConfidenceParsedItems_CapAssociationConfidenceAtLow()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Baseline: 10 "Rice" meals, 1 symptom -> 10% baseline rate.
        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 10; i++)
        {
            meals.AddRange(MakeMeal(userId, now.AddDays(-90 + i * 3), "Rice"));
            if (i == 0)
                symptoms.AddRange(MakeSymptom(userId, now.AddDays(-90).AddHours(3), "Bloating", 5));
        }

        // Exposure: 10 "Mystery Snack" meals with a symptom after every single one
        // (100% exposed rate). Statistically this clears the High-confidence bar
        // (>=10 exposures, >=10 associated weight, >=30pt risk difference), but every
        // item was parsed with a poor identity match (0.35 < the 0.6 threshold), so
        // the tier must be capped at Low regardless of how strong the raw stats are.
        for (int i = 0; i < 10; i++)
        {
            var mealTime = now.AddDays(-60 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Mystery Snack", matchConfidence: 0.35m));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 5));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(
            userId, DateOnly.FromDateTime(now.AddDays(-100)), DateOnly.FromDateTime(now));

        result.First(c => c.FoodOrAdditive == "Mystery Snack").Confidence.Should().Be("Low");
    }

    [Fact]
    public async Task HighMatchConfidenceParsedItems_DoNotCapAssociationConfidence()
    {
        // Control for the test above: identical statistics, but a confident identity
        // match (0.95) on every item — the association must be allowed to reach its
        // full statistically-earned tier, proving the cap is confidence-specific and
        // not an accidental blanket downgrade.
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();
        for (int i = 0; i < 10; i++)
        {
            meals.AddRange(MakeMeal(userId, now.AddDays(-90 + i * 3), "Rice"));
            if (i == 0)
                symptoms.AddRange(MakeSymptom(userId, now.AddDays(-90).AddHours(3), "Bloating", 5));
        }

        for (int i = 0; i < 10; i++)
        {
            var mealTime = now.AddDays(-60 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Mystery Snack", matchConfidence: 0.95m));
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 5));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(
            userId, DateOnly.FromDateTime(now.AddDays(-100)), DateOnly.FromDateTime(now));

        result.First(c => c.FoodOrAdditive == "Mystery Snack").Confidence.Should().Be("High");
    }

    [Fact]
    public async Task CorrelationAndFoodDiary_ProduceSameConfidenceTiers()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();

        for (int i = 0; i < 8; i++)
        {
            var mealTime = now.AddDays(-30 + i * 3);
            meals.AddRange(MakeMeal(userId, mealTime, "Garlic Bread"));
            if (i < 6)
                symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 6));
        }
        // Baseline meals with no symptom in range.
        for (int i = 0; i < 4; i++)
            meals.AddRange(MakeMeal(userId, now.AddDays(-45 + i * 5), "Rice"));

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;

        var correlations = await MakeSut(store).ComputeCorrelationsAsync(
            userId, DateOnly.FromDateTime(now.AddDays(-60)), DateOnly.FromDateTime(now));
        var diary = new FoodDiaryAnalysisService();
        var analysis = await diary.AnalyzeAsync(
            userId, DateOnly.FromDateTime(now.AddDays(-60)), DateOnly.FromDateTime(now), store);

        var corrGarlic = correlations.First(c => c.FoodOrAdditive == "Garlic Bread");
        var diaryGarlic = analysis.Patterns.First(p => p.FoodName == "Garlic Bread");

        // Both surfaces must agree on the confidence tier. This fails if either
        // engine has a divergent threshold or an independent computation.
        corrGarlic.Confidence.Should().Be(diaryGarlic.Confidence);
    }

    [Fact]
    public async Task UnresolvedNicheFood_GroupsAcrossNameVariants_DespiteNoFoodProductId()
    {
        // A genuinely niche food with no catalog match (no FoodProductId — the parse-time
        // resolution would have been Unresolved) must still group as ONE tracked food across
        // meals even when logged with different casing/pluralization each time. Resolution
        // failure must never fragment or exclude a food from association tracking.
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var meals = new List<MealLog>();
        var symptoms = new List<SymptomLog>();

        var nameVariants = new[] { "Quinoa Tabbouleh Bowl", "quinoa tabbouleh bowl", "Quinoa Tabbouleh Bowls", "QUINOA TABBOULEH BOWL" };
        for (int i = 0; i < nameVariants.Length; i++)
        {
            var mealTime = now.AddDays(-30 + i * 5);
            meals.AddRange(MakeMeal(userId, mealTime, nameVariants[i])); // foodProductId defaults to null: unresolved
            symptoms.AddRange(MakeSymptom(userId, mealTime.AddHours(3), "Bloating", 5));
        }

        var store = MockTableStoreFactory.Create(meals: meals, symptoms: symptoms).Object;
        var result = await MakeSut(store).ComputeCorrelationsAsync(
            userId, DateOnly.FromDateTime(now.AddDays(-40)), DateOnly.FromDateTime(now));

        result.Should().ContainSingle(c => c.FoodOrAdditive.Contains("Quinoa", StringComparison.OrdinalIgnoreCase),
            "differently-cased/pluralized occurrences of the same unresolved food must collapse into one tracked food, not fragment into several");
        var quinoa = result.First(c => c.FoodOrAdditive.Contains("Quinoa", StringComparison.OrdinalIgnoreCase));
        quinoa.Occurrences.Should().Be(4, "all 4 logged occurrences must be counted despite the food never resolving to a catalog product");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static List<MealLog> MakeMeal(Guid userId, DateTime loggedAt, string foodName, Guid? foodProductId = null, decimal? matchConfidence = null)
    {
        var mealId = Guid.NewGuid();
        return
        [
            new MealLog
            {
                Id = mealId,
                UserId = userId,
                LoggedAt = loggedAt,
                MealType = MealType.Lunch,
                Items =
                [
                    new MealItem
                    {
                        Id = Guid.NewGuid(),
                        MealLogId = mealId,
                        FoodName = foodName,
                        FoodProductId = foodProductId,
                        MatchConfidence = matchConfidence,
                    }
                ],
            }
        ];
    }

    private static List<SymptomLog> MakeSymptom(Guid userId, DateTime occurredAt, string symptomName, int severity)
    {
        return
        [
            new SymptomLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SymptomTypeId = 1,
                Severity = severity,
                OccurredAt = occurredAt,
                SymptomType = new SymptomType { Id = 1, Name = symptomName, Category = "GI" },
            }
        ];
    }
}
