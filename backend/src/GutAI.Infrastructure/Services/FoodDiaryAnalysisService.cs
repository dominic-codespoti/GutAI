using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;

namespace GutAI.Infrastructure.Services;

public class FoodDiaryAnalysisService : IFoodDiaryAnalysisService
{
    // Onset window, food-name grouping, evidence allocation, and confidence tiers now live
    // in FoodSymptomAssociationService, shared with CorrelationEngine, so the two features
    // can no longer silently disagree about whether a given food correlates with a given
    // symptom, or double-count one symptom event as full-strength evidence across every
    // candidate meal in its onset window.

    public async Task<FoodDiaryAnalysisDto> AnalyzeAsync(Guid userId, DateOnly from, DateOnly to, ITableStore store)
    {
        var result = await FoodSymptomAssociationService.ComputeAsync(userId, from, to, store, includeAdditives: false);
        var meals = result.Meals;
        var symptoms = result.Symptoms;

        var patterns = result.Associations
            .Select(a => new FoodSymptomPatternDto
            {
                FoodName = a.FoodName,
                SymptomName = a.SymptomName,
                Occurrences = (int)Math.Round(a.AssociatedMealWeight),
                ExposureMeals = a.ExposureMeals,
                AssociationRatePercent = a.ExposedSymptomRate,
                AverageSeverity = a.AverageSeverity,
                AverageOnsetHours = a.AverageOnsetHours,
                Confidence = a.Confidence,
                Explanation = BuildPatternExplanation(a)
            })
            .OrderByDescending(p => p.Confidence == "High" ? 3 : p.Confidence == "Medium" ? 2 : 1)
            .ThenByDescending(p => p.Occurrences)
            .ToList();

        var timingInsights = BuildTimingInsights(meals, symptoms);
        var recommendations = BuildRecommendations(patterns);
        var summary = BuildSummary(meals.Count, symptoms.Count, patterns, from, to);

        return new FoodDiaryAnalysisDto
        {
            TotalMealsAnalyzed = meals.Count,
            TotalSymptomsAnalyzed = symptoms.Count,
            PatternsFound = patterns.Count,
            FromDate = from,
            ToDate = to,
            Patterns = patterns,
            TimingInsights = timingInsights,
            Recommendations = recommendations,
            Summary = summary
        };
    }

    private static string BuildPatternExplanation(FoodSymptomAssociationDto a)
    {
        var attribution = a.AttributionMethod == "UserLinked"
            ? "Based on symptoms you linked directly to this meal."
            : "Inferred from a 1-6 hour onset window, not user-confirmed.";
        var baseText = $"{a.FoodName} was linked to {a.SymptomName} after {a.ExposedSymptomRate}% of meals that included it " +
            $"(vs {a.BaselineSymptomRate}% without it), based on {a.AssociatedMealWeight:0.#} symptom events across {a.ExposureMeals} exposures. " +
            $"Average severity was {a.AverageSeverity}/10. This is a temporal association, not proof of causation. {attribution}";
        return a.Limitations.Count > 0 ? $"{baseText} {string.Join(" ", a.Limitations)}" : baseText;
    }

    public async Task<EliminationDietStatusDto> GetEliminationStatusAsync(Guid userId, ITableStore store)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-90);
        var analysis = await AnalyzeAsync(userId, from, to, store);

        var highConfidence = analysis.Patterns
            .Where(p => p.Confidence == "High")
            .Select(p => p.FoodName)
            .Distinct()
            .ToList();

        var mediumConfidence = analysis.Patterns
            .Where(p => p.Confidence == "Medium")
            .Select(p => p.FoodName)
            .Distinct()
            .Except(highConfidence)
            .ToList();

        var allCorrelatedFoods = analysis.Patterns
            .Select(p => p.FoodName)
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recentMeals = await store.GetMealLogsByDateRangeAsync(userId, from, to);
        foreach (var meal in recentMeals)
            meal.Items = await store.GetMealItemsAsync(userId, meal.Id);

        var foodFrequency = recentMeals
            .SelectMany(m => m.Items)
            .GroupBy(i => i.FoodName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var safeFoods = foodFrequency
            .Where(kv => kv.Value >= 5 && !allCorrelatedFoods.Contains(kv.Key))
            .Select(kv => kv.Key)
            .OrderByDescending(f => foodFrequency[f])
            .ToList();

        var fourteenDaysAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        var recentFoods = recentMeals
            .Where(m => DateOnly.FromDateTime(m.LoggedAt) >= fourteenDaysAgo)
            .SelectMany(m => m.Items.Select(i => i.FoodName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var olderMeals = recentMeals
            .Where(m => DateOnly.FromDateTime(m.LoggedAt) < fourteenDaysAgo);
        var olderFoods = olderMeals
            .SelectMany(m => m.Items.Select(i => i.FoodName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recentSymptoms = await store.GetSymptomLogsByDateRangeAsync(userId, fourteenDaysAgo, DateOnly.FromDateTime(DateTime.UtcNow));
        foreach (var s in recentSymptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);

        var reintroductionResults = new List<ReintroductionResultDto>();
        foreach (var food in highConfidence)
        {
            var wasEliminated = olderFoods.Contains(food) && !recentMeals
                .Where(m => DateOnly.FromDateTime(m.LoggedAt) >= fourteenDaysAgo
                    && DateOnly.FromDateTime(m.LoggedAt) < DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)))
                .SelectMany(m => m.Items)
                .Any(i => i.FoodName.Equals(food, StringComparison.OrdinalIgnoreCase));

            if (!wasEliminated)
                continue;

            var reintroMeals = recentMeals
                .Where(m => DateOnly.FromDateTime(m.LoggedAt) >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7))
                    && m.Items.Any(i => i.FoodName.Equals(food, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (reintroMeals.Count == 0)
                continue;

            var followingSymptoms = new List<int>();
            foreach (var meal in reintroMeals)
            {
                var triggered = recentSymptoms
                    .Where(s => FoodSymptomMatching.IsWithinOnsetWindow(meal.LoggedAt, s.OccurredAt))
                    .Select(s => s.Severity);
                followingSymptoms.AddRange(triggered);
            }

            var result = reintroMeals.Count < 3
                ? "Insufficient data"
                : followingSymptoms.Count >= 2 ? "Possible association" : "No repeated association";
            var avgSev = followingSymptoms.Count > 0 ? (decimal)followingSymptoms.Average() : 0m;

            reintroductionResults.Add(new ReintroductionResultDto
            {
                FoodName = food,
                Result = result,
                AverageSeverity = Math.Round(avgSev, 1),
                TestCount = reintroMeals.Count
            });
        }

        var phase = DeterminePhase(analysis, highConfidence, recentFoods, reintroductionResults);
        var recommendations = BuildEliminationRecommendations(phase, highConfidence, mediumConfidence, safeFoods, reintroductionResults);
        var summary = BuildEliminationSummary(phase, highConfidence, safeFoods, reintroductionResults);

        return new EliminationDietStatusDto
        {
            Phase = phase,
            FoodsToEliminate = highConfidence,
            FoodsToReintroduce = mediumConfidence,
            SafeFoods = safeFoods,
            ReintroductionResults = reintroductionResults,
            Recommendations = recommendations,
            Summary = summary
        };
    }

    private static string DeterminePhase(
        FoodDiaryAnalysisDto analysis,
        List<string> highConfidence,
        HashSet<string> recentFoods,
        List<ReintroductionResultDto> reintroResults)
    {
        if (analysis.TotalSymptomsAnalyzed == 0)
            return "Not Started";

        if (highConfidence.Count == 0)
            return "Assessment";

        var stillEatingTriggers = highConfidence.Any(f => recentFoods.Contains(f));

        var conclusiveReintroductions = reintroResults.Count(result => result.Result != "Insufficient data");
        if (stillEatingTriggers && conclusiveReintroductions == 0)
            return "Assessment";

        if (!stillEatingTriggers && conclusiveReintroductions == 0)
            return "Elimination";

        if (conclusiveReintroductions > 0 && conclusiveReintroductions < highConfidence.Count)
            return "Reintroduction";

        if (conclusiveReintroductions >= highConfidence.Count)
            return "Maintenance";

        return "Assessment";
    }

    private static List<TimingInsightDto> BuildTimingInsights(
        List<Domain.Entities.MealLog> meals,
        List<Domain.Entities.SymptomLog> symptoms)
    {
        var insights = new List<TimingInsightDto>();

        if (symptoms.Count > 0)
        {
            var hourGroups = symptoms
                .GroupBy(s => s.OccurredAt.Hour / 4)
                .OrderByDescending(g => g.Count())
                .First();
            var startHour = hourGroups.Key * 4;
            var timeLabel = startHour switch
            {
                0 => "midnight–4 AM",
                4 => "4–8 AM",
                8 => "8 AM–noon",
                12 => "noon–4 PM",
                16 => "4–8 PM",
                _ => "8 PM–midnight"
            };
            insights.Add(new TimingInsightDto
            {
                Insight = $"Symptoms peak between {timeLabel} ({hourGroups.Count()} occurrences).",
                Category = "Peak symptom onset",
                SupportingDataPoints = hourGroups.Count()
            });

            var dayGroups = symptoms
                .GroupBy(s => s.OccurredAt.DayOfWeek)
                .OrderByDescending(g => g.Count())
                .First();
            insights.Add(new TimingInsightDto
            {
                Insight = $"{dayGroups.Key} has the most symptoms ({dayGroups.Count()}).",
                Category = "Most reactive day",
                SupportingDataPoints = dayGroups.Count()
            });
        }

        if (meals.Count > 0 && symptoms.Count > 0)
        {
            var mealTypeSymptomCounts = meals
                .SelectMany(m => m.Items, (m, _) => m)
                .GroupBy(m => m.MealType)
                .Select(g =>
                {
                    var count = symptoms.Count(s =>
                        g.Any(m => FoodSymptomMatching.IsWithinOnsetWindow(m.LoggedAt, s.OccurredAt)));
                    return (MealType: g.Key, Count: count);
                })
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();

            if (mealTypeSymptomCounts.Count > 0)
            {
                insights.Add(new TimingInsightDto
                {
                    Insight = $"{mealTypeSymptomCounts.MealType} is most often followed by symptoms ({mealTypeSymptomCounts.Count} linked).",
                    Category = "Worst meal type",
                    SupportingDataPoints = mealTypeSymptomCounts.Count
                });
            }
        }

        if (symptoms.Count >= 2)
        {
            var ordered = symptoms.OrderBy(s => s.OccurredAt).ToList();
            var maxGap = TimeSpan.Zero;
            for (var i = 1; i < ordered.Count; i++)
            {
                var gap = ordered[i].OccurredAt - ordered[i - 1].OccurredAt;
                if (gap > maxGap)
                    maxGap = gap;
            }
            var streakDays = (int)maxGap.TotalDays;
            if (streakDays >= 1)
            {
                insights.Add(new TimingInsightDto
                {
                    Insight = $"Longest symptom-free streak: {streakDays} day(s).",
                    Category = "Symptom-free streak",
                    SupportingDataPoints = 2
                });
            }
        }

        return insights;
    }

    private static List<string> BuildRecommendations(List<FoodSymptomPatternDto> patterns)
    {
        var recs = new List<string>();

        var highPatterns = patterns.Where(p => p.Confidence == "High").ToList();
        if (highPatterns.Count > 0)
        {
            var foods = string.Join(", ", highPatterns.Select(p => p.FoodName).Distinct());
            recs.Add($"Your logs show a recurring pattern between these foods and your symptoms: {foods}.");
            recs.Add("If you choose to adjust your diet, tracking symptoms for 2–4 weeks may help you observe changes.");
        }

        var medPatterns = patterns.Where(p => p.Confidence == "Medium").ToList();
        if (medPatterns.Count > 0)
        {
            var foods = string.Join(", ", medPatterns.Select(p => p.FoodName).Distinct());
            recs.Add($"These foods showed a moderate pattern with your symptoms — worth keeping an eye on: {foods}.");
        }

        if (patterns.Count == 0)
            recs.Add("No clear food-symptom patterns detected yet. Keep logging meals and symptoms for more data.");

        if (patterns.Any(p => p.AverageOnsetHours <= 2))
            recs.Add("Some symptoms appeared quickly (within 2 hours) — quick onset can have many causes. A healthcare provider can help investigate further.");

        if (patterns.Any(p => p.AverageOnsetHours >= 6))
            recs.Add("Some symptoms appeared 6+ hours after eating — delayed onset is sometimes associated with fermentation of certain carbohydrates.");

        return recs;
    }

    private static string BuildSummary(int mealCount, int symptomCount, List<FoodSymptomPatternDto> patterns, DateOnly from, DateOnly to)
    {
        if (mealCount == 0 && symptomCount == 0)
            return $"No meals or symptoms logged between {from} and {to}.";

        if (symptomCount == 0)
            return $"Analyzed {mealCount} meals between {from} and {to}. No symptoms were logged during this period; incomplete logging cannot establish tolerance.";

        if (patterns.Count == 0)
            return $"Analyzed {mealCount} meals and {symptomCount} symptoms between {from} and {to}. " +
                "No clear food-symptom correlations found. Continue logging for more data.";

        var highCount = patterns.Count(p => p.Confidence == "High");
        var topTrigger = patterns.First();
        var sb = $"Analyzed {mealCount} meals and {symptomCount} symptoms between {from} and {to}. " +
            $"Found {patterns.Count} pattern(s)";

        if (highCount > 0)
            sb += $", {highCount} with high confidence";

        sb += $". Strongest association: {topTrigger.FoodName} → {topTrigger.SymptomName} " +
            $"({topTrigger.Occurrences}/{topTrigger.ExposureMeals} exposure meals, avg severity {topTrigger.AverageSeverity}/10). Associations do not establish causation.";

        return sb;
    }

    private static List<string> BuildEliminationRecommendations(
        string phase,
        List<string> highConfidence,
        List<string> mediumConfidence,
        List<string> safeFoods,
        List<ReintroductionResultDto> reintroResults)
    {
        var recs = new List<string>();

        switch (phase)
        {
            case "Not Started":
                recs.Add("Start logging your meals and symptoms consistently to identify patterns.");
                recs.Add("Aim to log every meal and any symptoms for at least 2 weeks.");
                break;
            case "Assessment":
                if (highConfidence.Count > 0)
                {
                    recs.Add($"Your logs show a pattern between these foods and your symptoms: {string.Join(", ", highConfidence)}.");
                    recs.Add("Some people find it helpful to discuss potential elimination trials with a dietitian or doctor.");
                }
                else
                    recs.Add("Continue logging — not enough data yet to identify strong triggers.");
                break;
            case "Elimination":
                recs.Add("It looks like you've stopped eating these foods. Continuing to log symptoms may help you spot any changes.");
                if (safeFoods.Count > 0)
                    recs.Add($"Foods with repeated logged exposure and no detected pattern: {string.Join(", ", safeFoods.Take(5))}. This does not prove medical safety or tolerance.");
                recs.Add("When you feel ready, reintroducing foods one at a time can help you understand your personal tolerances — a dietitian can guide this process.");
                break;
            case "Reintroduction":
                recs.Add("During reintroduction, change one variable at a time and consider clinician or dietitian guidance.");
                var reacted = reintroResults.Where(r => r.Result == "Possible association").Select(r => r.FoodName).ToList();
                var noRepeatedAssociation = reintroResults.Where(r => r.Result == "No repeated association").Select(r => r.FoodName).ToList();
                if (reacted.Count > 0)
                    recs.Add($"Foods with repeated symptom associations after reintroduction: {string.Join(", ", reacted)}.");
                if (noRepeatedAssociation.Count > 0)
                    recs.Add($"Foods without a repeated association across the logged tests: {string.Join(", ", noRepeatedAssociation)}. Continue observing; this is not proof of tolerance.");
                if (mediumConfidence.Count > 0)
                    recs.Add($"Still to test: {string.Join(", ", mediumConfidence)}.");
                break;
            case "Maintenance":
                recs.Add("Logged reintroduction observations are complete for the current candidates.");
                var avoid = reintroResults.Where(r => r.Result == "Possible association").Select(r => r.FoodName).ToList();
                if (avoid.Count > 0)
                    recs.Add($"These foods retained repeated temporal associations: {string.Join(", ", avoid)}.");
                recs.Add("Review restrictive diet changes with a qualified clinician or dietitian.");
                recs.Add("Keep logging periodically to catch any new patterns.");
                break;
        }

        return recs;
    }

    private static string BuildEliminationSummary(
        string phase,
        List<string> highConfidence,
        List<string> safeFoods,
        List<ReintroductionResultDto> reintroResults)
    {
        return phase switch
        {
            "Not Started" => "No symptoms have been logged yet. Start tracking meals and symptoms to begin analysis.",
            "Assessment" => highConfidence.Count > 0
                ? $"Assessment phase: {highConfidence.Count} potential trigger food(s) identified ({string.Join(", ", highConfidence)}). " +
                  "You may find it helpful to discuss these patterns with a healthcare provider or dietitian."
                : "Assessment phase: still gathering data to identify trigger foods.",
            "Elimination" => $"Observation phase: {highConfidence.Count} stronger food-symptom association(s) are no longer present in recent meals. " +
                $"{safeFoods.Count} repeatedly logged food(s) had no detected association; neither result establishes causation or safety.",
            "Reintroduction" => $"Reintroduction observation phase: {reintroResults.Count(r => r.Result != "Insufficient data")}/{highConfidence.Count} candidate food(s) have enough repeated observations. " +
                $"{reintroResults.Count(r => r.Result == "No repeated association")} had no repeated association and " +
                $"{reintroResults.Count(r => r.Result == "Possible association")} retained a possible association.",
            "Maintenance" => $"Observation cycle complete. " +
                $"{reintroResults.Count(r => r.Result == "No repeated association")} food(s) had no repeated logged association and " +
                $"{reintroResults.Count(r => r.Result == "Possible association")} retained possible associations. These results are not medical clearance.",
            _ => "Unable to determine elimination diet status."
        };
    }
}
