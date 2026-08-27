using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// The single food/additive ↔ symptom association computation, shared by
/// <see cref="CorrelationEngine"/> (Insights → Correlations/Trigger Foods) and
/// <see cref="FoodDiaryAnalysisService"/> (Food Diary / Elimination Diet). Both screens
/// previously reimplemented this independently and disagreed in two concrete ways this
/// engine fixes:
///
/// 1. <b>Double-counted evidence.</b> A single symptom event whose 1–6h onset window
///    overlapped several candidate meals was credited as a *full* hit against every one
///    of those meals, so one stomach ache could inflate three different foods' occurrence
///    counts. Here, one symptom event contributes exactly one unit of evidence, split
///    evenly across its candidate meals (or pinned entirely to the meal the user
///    explicitly linked via <see cref="SymptomLog.RelatedMealLogId"/>).
/// 2. <b>No baseline comparison.</b> Confidence was derived from raw occurrence count
///    alone, so a food eaten at almost every meal could reach "High" purely from volume
///    even though its post-exposure symptom rate matched the rate on meals without it.
///    Here, confidence requires the exposed rate to exceed the baseline (non-exposure)
///    rate by a minimum margin, and is capped when there aren't enough non-exposure
///    meals to establish a baseline at all.
///
/// This is an internal computation detail, not a wire contract — <see cref="CorrelationEngine"/>
/// and <see cref="FoodDiaryAnalysisService"/> each project <see cref="FoodSymptomAssociationDto"/>
/// into their own existing public DTOs, so no frontend contract changes accompany this.
/// </summary>
internal static class FoodSymptomAssociationService
{
    /// <summary>An association with fewer exposure meals than this is statistical noise
    /// regardless of its rate — not enough opportunities to say anything.</summary>
    public const int MinExposureMeals = 3;

    /// <summary>Minimum non-exposure meals needed before a baseline rate is trusted. Below
    /// this (including zero — the food appears in every meal in range), there is no real
    /// comparison group, so confidence is capped at "Medium" and a limitation is surfaced.</summary>
    private const int MinBaselineMeals = 3;

    /// <summary>A food/symptom pair needs the exposed rate to beat baseline by at least this
    /// much, with enough support, to be reported as "High" confidence.</summary>
    private const decimal HighRiskDifference = 0.30m;
    private const decimal MediumRiskDifference = 0.15m;

    /// <summary>Two foods are flagged as commonly co-consumed when one appears in at least
    /// this fraction of the other's exposure meals — a caveat that either could be responsible.</summary>
    private const decimal CoConsumptionThreshold = 0.8m;

    /// <summary>Below this average identity-match confidence across a food's logged
    /// occurrences, the underlying food identity itself is uncertain (the frontend shows
    /// "Estimated match — verify this item" at this same threshold), so association
    /// evidence built on it cannot be trusted above "Low" regardless of occurrence
    /// counts or rate contrast. Items logged without NLP resolution (manual entry,
    /// barcode scan) carry no <see cref="MealItem.MatchConfidence"/> and are never
    /// penalized by this cap.</summary>
    private const decimal LowQualityMatchConfidenceThreshold = 0.6m;

    public static async Task<FoodSymptomAssociationResult> ComputeAsync(
        Guid userId, DateOnly from, DateOnly to, ITableStore store, bool includeAdditives, CancellationToken ct = default, string? timezoneId = null)
    {
        var user = await store.GetUserAsync(userId, ct);
        var (utcStart, utcEnd) = TimeZoneHelper.GetUtcRangeForLocalDateRange(user, from, to, timezoneId);
        var coarseFrom = DateOnly.FromDateTime(utcStart);
        var coarseTo = DateOnly.FromDateTime(utcEnd);

        var meals = await store.GetMealLogsByDateRangeAsync(userId, coarseFrom, coarseTo, ct);
        meals = meals.Where(m => m.LoggedAt >= utcStart && m.LoggedAt <= utcEnd).ToList();
        foreach (var meal in meals)
            meal.Items = await store.GetMealItemsAsync(userId, meal.Id, ct);

        if (includeAdditives)
            await HydrateAdditivesAsync(meals, store, ct);

        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, coarseFrom, coarseTo, ct);
        symptoms = symptoms.Where(s => s.OccurredAt >= utcStart && s.OccurredAt <= utcEnd).ToList();
        foreach (var s in symptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId, ct);
        // Exposure key -> meal ids that contain it. Foods are grouped by a normalized
        // identity key (case/punctuation/plural-insensitive) so "Chicken Breast" and
        // "chicken breasts" share one bucket; additive keys are tagged "[additive] Name".
        var exposureMealIds = new Dictionary<string, HashSet<Guid>>();
        var displayNames = new Dictionary<string, string>();
        var mealKeys = new Dictionary<Guid, HashSet<string>>();
        var matchConfidenceByKey = new Dictionary<string, List<decimal>>();
        foreach (var meal in meals)
        {
            var keys = ExposureKeysFor(meal, includeAdditives, displayNames);
            mealKeys[meal.Id] = keys;
            foreach (var key in keys)
            {
                if (!exposureMealIds.TryGetValue(key, out var set))
                    exposureMealIds[key] = set = [];
                set.Add(meal.Id);
            }

            foreach (var item in meal.Items)
            {
                if (item.MatchConfidence is not { } conf)
                    continue;
                var itemKey = FoodSymptomMatching.NormalizeForGrouping(item.FoodName);
                if (string.IsNullOrWhiteSpace(itemKey))
                    continue;
                if (!matchConfidenceByKey.TryGetValue(itemKey, out var list))
                    matchConfidenceByKey[itemKey] = list = [];
                list.Add(conf);
            }
        }

        // Allocate each symptom event's evidence across its candidate meals. A user-linked
        // symptom pins 100% of its weight to that one meal; an inferred symptom splits its
        // single unit of evidence evenly across every meal in the onset window instead of
        // crediting each one fully.
        var mealsById = meals.ToDictionary(m => m.Id);
        var hitWeight = new Dictionary<(Guid MealId, string Symptom), decimal>();
        var hitSeverityWeighted = new Dictionary<(Guid MealId, string Symptom), decimal>();
        var hitOnsetWeighted = new Dictionary<(Guid MealId, string Symptom), decimal>();
        var hitUserLinkedWeight = new Dictionary<(Guid MealId, string Symptom), decimal>();

        foreach (var symptom in symptoms)
        {
            var symptomName = symptom.SymptomType?.Name ?? "Unknown";
            List<MealLog> candidates;
            bool isUserLinked;
            if (symptom.RelatedMealLogId is { } linkedId && mealsById.TryGetValue(linkedId, out var linkedMeal))
            {
                candidates = [linkedMeal];
                isUserLinked = true;
            }
            else
            {
                candidates = meals.Where(m => FoodSymptomMatching.IsWithinOnsetWindow(m.LoggedAt, symptom.OccurredAt)).ToList();
                isUserLinked = false;
            }

            if (candidates.Count == 0)
                continue;

            var weight = 1m / candidates.Count;
            foreach (var meal in candidates)
            {
                var key = (meal.Id, symptomName);
                hitWeight[key] = hitWeight.GetValueOrDefault(key) + weight;
                hitSeverityWeighted[key] = hitSeverityWeighted.GetValueOrDefault(key) + weight * symptom.Severity;
                hitOnsetWeighted[key] = hitOnsetWeighted.GetValueOrDefault(key) + weight * (decimal)(symptom.OccurredAt - meal.LoggedAt).TotalHours;
                if (isUserLinked)
                    hitUserLinkedWeight[key] = hitUserLinkedWeight.GetValueOrDefault(key) + weight;
            }
        }

        var allMealIds = meals.Select(m => m.Id).ToHashSet();
        var symptomNames = symptoms.Select(s => s.SymptomType?.Name ?? "Unknown").Distinct().ToList();
        var associations = new List<FoodSymptomAssociationDto>();

        foreach (var (foodKey, exposedIds) in exposureMealIds)
        {
            var exposureCount = exposedIds.Count;

            var nonExposedIds = allMealIds.Except(exposedIds).ToHashSet();
            var hasBaseline = nonExposedIds.Count >= MinBaselineMeals;

            foreach (var symptomName in symptomNames)
            {
                var exposedWeight = exposedIds.Sum(id => hitWeight.GetValueOrDefault((id, symptomName)));
                if (exposedWeight <= 0)
                    continue; // no evidence at all linking this food to this symptom

                var baselineWeight = nonExposedIds.Sum(id => hitWeight.GetValueOrDefault((id, symptomName)));
                var exposedRate = exposedWeight / exposureCount;
                var baselineRate = nonExposedIds.Count > 0 ? baselineWeight / nonExposedIds.Count : 0m;
                var riskDifference = exposedRate - baselineRate;
                decimal? lift = baselineRate > 0 ? Math.Round(exposedRate / baselineRate, 2) : null;

                var userLinkedWeight = exposedIds.Sum(id => hitUserLinkedWeight.GetValueOrDefault((id, symptomName)));
                var attributionMethod = userLinkedWeight / exposedWeight > 0.5m ? "UserLinked" : "InferredOnsetWindow";

                var avgSeverity = exposedIds.Sum(id => hitSeverityWeighted.GetValueOrDefault((id, symptomName))) / exposedWeight;
                var avgOnset = exposedIds.Sum(id => hitOnsetWeighted.GetValueOrDefault((id, symptomName))) / exposedWeight;

                var avgMatchConfidence = matchConfidenceByKey.TryGetValue(foodKey, out var confList) && confList.Count > 0
                    ? confList.Average()
                    : (decimal?)null;
                var confidence = ComputeConfidence(exposureCount, exposedWeight, riskDifference, hasBaseline, avgMatchConfidence);

                var limitations = new List<string>();
                if (!hasBaseline)
                    limitations.Add("Not enough meals without this item in range to establish a baseline symptom rate.");
                var coConsumed = FindHeavyCoConsumption(foodKey, exposedIds, exposureMealIds, displayNames);
                if (coConsumed is not null)
                    limitations.Add($"Rarely eaten without {coConsumed} in this window - either item could be responsible.");

                associations.Add(new FoodSymptomAssociationDto
                {
                    FoodKey = foodKey,
                    FoodName = displayNames.GetValueOrDefault(foodKey, foodKey),
                    SymptomName = symptomName,
                    ExposureMeals = exposureCount,
                    AssociatedMealWeight = Math.Round(exposedWeight, 2),
                    ExposedSymptomRate = Math.Round(exposedRate * 100, 1),
                    BaselineSymptomRate = Math.Round(baselineRate * 100, 1),
                    Lift = lift,
                    AverageSeverity = Math.Round(Math.Min(avgSeverity, 10), 1),
                    AverageOnsetHours = Math.Round(avgOnset, 1),
                    Confidence = confidence,
                    AttributionMethod = attributionMethod,
                    Limitations = limitations
                });
            }
        }

        return new FoodSymptomAssociationResult(associations, meals, symptoms);
    }

    private static string ComputeConfidence(int exposureMeals, decimal associatedWeight, decimal riskDifference, bool hasBaseline, decimal? avgMatchConfidence)
    {
        if (avgMatchConfidence is { } conf && conf < LowQualityMatchConfidenceThreshold)
            return "Low";
        if (hasBaseline && exposureMeals >= 10 && associatedWeight >= 10 && riskDifference >= HighRiskDifference)
            return "High";
        if (exposureMeals >= 5 && associatedWeight >= 5 && (!hasBaseline || riskDifference >= MediumRiskDifference))
            return "Medium";
        return "Low";
    }

    /// <summary>Names the item most heavily co-consumed with <paramref name="foodKey"/>, if any
    /// other tracked item appears in at least <see cref="CoConsumptionThreshold"/> of its exposure
    /// meals — evidence too confounded to single out one item as responsible.</summary>
    private static string? FindHeavyCoConsumption(
        string foodKey, HashSet<Guid> exposedIds, Dictionary<string, HashSet<Guid>> exposureMealIds, Dictionary<string, string> displayNames)
    {
        if (exposedIds.Count < MinExposureMeals)
            return null;

        string? bestKey = null;
        var bestOverlap = 0m;
        foreach (var (otherKey, otherIds) in exposureMealIds)
        {
            if (otherKey == foodKey || otherIds.Count < MinExposureMeals)
                continue;

            var intersection = exposedIds.Count(otherIds.Contains);
            var fraction = (decimal)intersection / exposedIds.Count;
            if (fraction >= CoConsumptionThreshold && fraction > bestOverlap)
            {
                bestOverlap = fraction;
                bestKey = otherKey;
            }
        }

        return bestKey is null ? null : displayNames.GetValueOrDefault(bestKey, bestKey);
    }

    private static HashSet<string> ExposureKeysFor(MealLog meal, bool includeAdditives, Dictionary<string, string> displayNames)
    {
        var keys = new HashSet<string>();
        foreach (var item in meal.Items)
        {
            var foodKey = FoodSymptomMatching.NormalizeForGrouping(item.FoodName);
            if (string.IsNullOrWhiteSpace(foodKey))
                continue;

            keys.Add(foodKey);
            if (!displayNames.ContainsKey(foodKey))
                displayNames[foodKey] = item.FoodName;

            if (!includeAdditives || item.FoodProduct?.FoodProductAdditives is null)
                continue;

            foreach (var fpa in item.FoodProduct.FoodProductAdditives)
            {
                var additiveKey = $"[additive] {fpa.FoodAdditive.Name}";
                keys.Add(additiveKey);
                displayNames[additiveKey] = additiveKey;
            }
        }
        return keys;
    }

    private static async Task HydrateAdditivesAsync(List<MealLog> meals, ITableStore store, CancellationToken ct)
    {
        foreach (var meal in meals)
        {
            foreach (var item in meal.Items.Where(i => i.FoodProductId.HasValue))
            {
                item.FoodProduct = await store.GetFoodProductAsync(item.FoodProductId!.Value, ct);
                if (item.FoodProduct is null)
                    continue;

                var additiveIds = await store.GetAdditiveIdsForProductAsync(item.FoodProduct.Id, ct);
                item.FoodProduct.FoodProductAdditives = [];
                foreach (var aid in additiveIds)
                {
                    var additive = await store.GetFoodAdditiveAsync(aid, ct);
                    if (additive is not null)
                        item.FoodProduct.FoodProductAdditives.Add(new FoodProductAdditive
                        {
                            FoodProductId = item.FoodProduct.Id,
                            FoodAdditiveId = aid,
                            FoodAdditive = additive
                        });
                }
            }
        }
    }
}

/// <summary>One food/additive ↔ symptom pair's association evidence, computed once and
/// projected by both <see cref="CorrelationEngine"/> and <see cref="FoodDiaryAnalysisService"/>
/// into their respective public DTOs.</summary>
internal sealed record FoodSymptomAssociationDto
{
    public required string FoodKey { get; init; }
    public required string FoodName { get; init; }
    public required string SymptomName { get; init; }
    public int ExposureMeals { get; init; }
    public decimal AssociatedMealWeight { get; init; }
    public decimal ExposedSymptomRate { get; init; }
    public decimal BaselineSymptomRate { get; init; }
    public decimal? Lift { get; init; }
    public decimal AverageSeverity { get; init; }
    public decimal AverageOnsetHours { get; init; }
    public required string Confidence { get; init; }
    public required string AttributionMethod { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

/// <summary>Bundles the computed associations with the already-hydrated meals/symptoms so
/// callers that need overall counts or timing detail don't re-fetch the same range twice.</summary>
internal sealed record FoodSymptomAssociationResult(
    List<FoodSymptomAssociationDto> Associations,
    List<MealLog> Meals,
    List<SymptomLog> Symptoms);
