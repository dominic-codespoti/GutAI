using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;

namespace GutAI.Infrastructure.Services;

public class FoodDiaryAnalysisService : IFoodDiaryAnalysisService
{
    private const int MinOnsetHours = 1;
    private const int MaxOnsetHours = 8;

    public async Task<FoodDiaryAnalysisDto> AnalyzeAsync(Guid userId, DateOnly from, DateOnly to, ITableStore store)
    {
        var meals = await store.GetMealLogsByDateRangeAsync(userId, from, to);
        foreach (var meal in meals)
            meal.Items = await store.GetMealItemsAsync(userId, meal.Id);

        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, from, to);
        foreach (var s in symptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);

        var correlations = new List<(string FoodName, string SymptomName, int Severity, double OnsetHours)>();

        foreach (var symptom in symptoms)
        {
            var precedingMeals = meals.Where(m =>
            {
                var hours = (symptom.OccurredAt - m.LoggedAt).TotalHours;
                return hours >= MinOnsetHours && hours <= MaxOnsetHours;
            });

            foreach (var meal in precedingMeals)
            {
                var onsetHours = (symptom.OccurredAt - meal.LoggedAt).TotalHours;
                foreach (var item in meal.Items)
                {
                    correlations.Add((item.FoodName, symptom.SymptomType?.Name ?? "Unknown", symptom.Severity, onsetHours));
                }
            }
        }

        var patterns = correlations
            .GroupBy(c => (c.FoodName, c.SymptomName))
            .Select(g =>
            {
                var occurrences = g.Count();
                var avgSeverity = (decimal)g.Average(x => x.Severity);
                var avgOnset = (decimal)g.Average(x => x.OnsetHours);
                var confidence = occurrences >= 5 && avgSeverity >= 5m ? "High"
                    : occurrences >= 3 || avgSeverity >= 6m ? "Medium"
                    : "Low";

                return new FoodSymptomPatternDto
                {
                    FoodName = g.Key.FoodName,
                    SymptomName = g.Key.SymptomName,
                    Occurrences = occurrences,
                    AverageSeverity = Math.Round(avgSeverity, 1),
                    AverageOnsetHours = Math.Round(avgOnset, 1),
                    Confidence = confidence,
                    Explanation = $"{g.Key.FoodName} was followed by {g.Key.SymptomName} {occurrences} time(s) " +
                        $"with avg severity {Math.Round(avgSeverity, 1)}/10, typically {Math.Round(avgOnset, 1)}h after eating."
                };
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
                    .Where(s =>
                    {
                        var hours = (s.OccurredAt - meal.LoggedAt).TotalHours;
                        return hours >= MinOnsetHours && hours <= MaxOnsetHours;
                    })
                    .Select(s => s.Severity);
                followingSymptoms.AddRange(triggered);
            }

            var result = followingSymptoms.Count > 0 ? "Reacted" : "Tolerated";
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

        if (stillEatingTriggers && reintroResults.Count == 0)
            return "Assessment";

        if (!stillEatingTriggers && reintroResults.Count == 0)
            return "Elimination";

        if (reintroResults.Count > 0 && reintroResults.Count < highConfidence.Count)
            return "Reintroduction";

        if (reintroResults.Count >= highConfidence.Count)
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
                        g.Any(m =>
                        {
                            var hours = (s.OccurredAt - m.LoggedAt).TotalHours;
                            return hours >= MinOnsetHours && hours <= MaxOnsetHours;
                        }));
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
            recs.Add($"Consider eliminating these high-confidence triggers: {foods}.");
            recs.Add("Track symptoms for 2–4 weeks after removal to confirm improvement.");
        }

        var medPatterns = patterns.Where(p => p.Confidence == "Medium").ToList();
        if (medPatterns.Count > 0)
        {
            var foods = string.Join(", ", medPatterns.Select(p => p.FoodName).Distinct());
            recs.Add($"Monitor these moderate-confidence triggers closely: {foods}.");
        }

        if (patterns.Count == 0)
            recs.Add("No clear food-symptom patterns detected yet. Keep logging meals and symptoms for more data.");

        if (patterns.Any(p => p.AverageOnsetHours <= 2))
            recs.Add("Some symptoms appear quickly (within 2 hours) — consider food intolerances or allergies.");

        if (patterns.Any(p => p.AverageOnsetHours >= 6))
            recs.Add("Some symptoms appear 6+ hours after eating — this may indicate fermentation-related issues (e.g., FODMAPs).");

        return recs;
    }

    private static string BuildSummary(int mealCount, int symptomCount, List<FoodSymptomPatternDto> patterns, DateOnly from, DateOnly to)
    {
        if (mealCount == 0 && symptomCount == 0)
            return $"No meals or symptoms logged between {from} and {to}.";

        if (symptomCount == 0)
            return $"Analyzed {mealCount} meals between {from} and {to}. No symptoms were reported during this period — great!";

        if (patterns.Count == 0)
            return $"Analyzed {mealCount} meals and {symptomCount} symptoms between {from} and {to}. " +
                "No clear food-symptom correlations found. Continue logging for more data.";

        var highCount = patterns.Count(p => p.Confidence == "High");
        var topTrigger = patterns.First();
        var sb = $"Analyzed {mealCount} meals and {symptomCount} symptoms between {from} and {to}. " +
            $"Found {patterns.Count} pattern(s)";

        if (highCount > 0)
            sb += $", {highCount} with high confidence";

        sb += $". Top trigger: {topTrigger.FoodName} → {topTrigger.SymptomName} " +
            $"({topTrigger.Occurrences}x, avg severity {topTrigger.AverageSeverity}/10).";

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
                    recs.Add($"Your data suggests these foods may be triggers: {string.Join(", ", highConfidence)}.");
                    recs.Add("Consider removing them from your diet for 2–4 weeks to see if symptoms improve.");
                }
                else
                    recs.Add("Continue logging — not enough data yet to identify strong triggers.");
                break;
            case "Elimination":
                recs.Add("You've removed your identified triggers. Monitor symptoms for improvement over the next 2–4 weeks.");
                if (safeFoods.Count > 0)
                    recs.Add($"Safe foods to rely on: {string.Join(", ", safeFoods.Take(5))}.");
                recs.Add("Once symptoms stabilize, consider reintroducing one trigger food at a time.");
                break;
            case "Reintroduction":
                recs.Add("You're reintroducing foods — add only one new food every 3 days.");
                var reacted = reintroResults.Where(r => r.Result == "Reacted").Select(r => r.FoodName).ToList();
                var tolerated = reintroResults.Where(r => r.Result == "Tolerated").Select(r => r.FoodName).ToList();
                if (reacted.Count > 0)
                    recs.Add($"Foods that caused reactions: {string.Join(", ", reacted)} — continue avoiding.");
                if (tolerated.Count > 0)
                    recs.Add($"Foods tolerated so far: {string.Join(", ", tolerated)} — safe to keep.");
                if (mediumConfidence.Count > 0)
                    recs.Add($"Still to test: {string.Join(", ", mediumConfidence)}.");
                break;
            case "Maintenance":
                recs.Add("You've completed reintroduction testing for your identified triggers.");
                var avoid = reintroResults.Where(r => r.Result == "Reacted").Select(r => r.FoodName).ToList();
                if (avoid.Count > 0)
                    recs.Add($"Continue avoiding: {string.Join(", ", avoid)}.");
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
                  "Consider starting an elimination trial."
                : "Assessment phase: still gathering data to identify trigger foods.",
            "Elimination" => $"Elimination phase: avoiding {highConfidence.Count} trigger food(s). " +
                $"{safeFoods.Count} safe food(s) identified. Monitor symptoms for improvement.",
            "Reintroduction" => $"Reintroduction phase: {reintroResults.Count}/{highConfidence.Count} trigger food(s) tested. " +
                $"{reintroResults.Count(r => r.Result == "Tolerated")} tolerated, " +
                $"{reintroResults.Count(r => r.Result == "Reacted")} caused reactions.",
            "Maintenance" => $"Maintenance phase: testing complete. " +
                $"{reintroResults.Count(r => r.Result == "Tolerated")} food(s) can be safely reintroduced, " +
                $"{reintroResults.Count(r => r.Result == "Reacted")} should continue to be avoided.",
            _ => "Unable to determine elimination diet status."
        };
    }
}
