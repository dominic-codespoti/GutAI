using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public class CorrelationEngine : ICorrelationEngine
{
    private readonly ITableStore _store;

    public CorrelationEngine(ITableStore store) => _store = store;

    public async Task<List<CorrelationDto>> ComputeCorrelationsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var meals = await _store.GetMealLogsByDateRangeAsync(userId, from, to, ct);
        foreach (var meal in meals)
        {
            meal.Items = await _store.GetMealItemsAsync(userId, meal.Id, ct);
            foreach (var item in meal.Items.Where(i => i.FoodProductId.HasValue))
            {
                item.FoodProduct = await _store.GetFoodProductAsync(item.FoodProductId!.Value, ct);
                if (item.FoodProduct is not null)
                {
                    var additiveIds = await _store.GetAdditiveIdsForProductAsync(item.FoodProduct.Id, ct);
                    item.FoodProduct.FoodProductAdditives = [];
                    foreach (var aid in additiveIds)
                    {
                        var additive = await _store.GetFoodAdditiveAsync(aid, ct);
                        if (additive is not null)
                            item.FoodProduct.FoodProductAdditives.Add(new Domain.Entities.FoodProductAdditive
                            {
                                FoodProductId = item.FoodProduct.Id,
                                FoodAdditiveId = aid,
                                FoodAdditive = additive
                            });
                    }
                }
            }
        }

        var symptoms = await _store.GetSymptomLogsByDateRangeAsync(userId, from, to, ct);
        foreach (var s in symptoms)
            s.SymptomType = await _store.GetSymptomTypeAsync(s.SymptomTypeId, ct);

        var correlations = new Dictionary<string, (HashSet<Guid> mealIds, decimal totalSeverity, int symptomMatches, int totalMeals)>();

        var itemMealCounts = new Dictionary<string, int>();
        foreach (var meal in meals)
        {
            var seen = new HashSet<string>();
            foreach (var item in meal.Items)
            {
                if (seen.Add(item.FoodName))
                {
                    itemMealCounts.TryGetValue(item.FoodName, out var c);
                    itemMealCounts[item.FoodName] = c + 1;
                }

                if (item.FoodProduct?.FoodProductAdditives != null)
                {
                    foreach (var fpa in item.FoodProduct.FoodProductAdditives)
                    {
                        var additiveName = $"[additive] {fpa.FoodAdditive.Name}";
                        if (seen.Add(additiveName))
                        {
                            itemMealCounts.TryGetValue(additiveName, out var ac);
                            itemMealCounts[additiveName] = ac + 1;
                        }
                    }
                }
            }
        }

        foreach (var symptom in symptoms)
        {
            var windowStart = symptom.OccurredAt.AddHours(-6);
            var windowEnd = symptom.OccurredAt.AddHours(-1);
            var priorMeals = meals.Where(m => m.LoggedAt >= windowStart && m.LoggedAt <= windowEnd);

            foreach (var meal in priorMeals)
            {
                var seenInMeal = new HashSet<string>();
                foreach (var item in meal.Items)
                {
                    var foodKey = $"{item.FoodName}|{symptom.SymptomType?.Name ?? "Unknown"}";
                    if (seenInMeal.Add(foodKey))
                    {
                        if (!correlations.ContainsKey(foodKey))
                            correlations[foodKey] = (new HashSet<Guid>(), 0, 0, itemMealCounts.GetValueOrDefault(item.FoodName, 1));

                        var entry = correlations[foodKey];
                        entry.mealIds.Add(meal.Id);
                        entry.totalSeverity += symptom.Severity;
                        entry.symptomMatches++;
                        correlations[foodKey] = (entry.mealIds, entry.totalSeverity, entry.symptomMatches, entry.totalMeals);
                    }

                    if (item.FoodProduct?.FoodProductAdditives != null)
                    {
                        foreach (var fpa in item.FoodProduct.FoodProductAdditives)
                        {
                            var additiveName = $"[additive] {fpa.FoodAdditive.Name}";
                            var additiveKey = $"{additiveName}|{symptom.SymptomType?.Name ?? "Unknown"}";
                            if (seenInMeal.Add(additiveKey))
                            {
                                if (!correlations.ContainsKey(additiveKey))
                                    correlations[additiveKey] = (new HashSet<Guid>(), 0, 0, itemMealCounts.GetValueOrDefault(additiveName, 1));

                                var entry = correlations[additiveKey];
                                entry.mealIds.Add(meal.Id);
                                entry.totalSeverity += symptom.Severity;
                                entry.symptomMatches++;
                                correlations[additiveKey] = (entry.mealIds, entry.totalSeverity, entry.symptomMatches, entry.totalMeals);
                            }
                        }
                    }
                }
            }
        }

        return correlations
            .Where(c => c.Value.mealIds.Count >= 3)
            .Select(c =>
            {
                var parts = c.Key.Split('|');
                var occurrences = c.Value.mealIds.Count;
                var totalMeals = c.Value.totalMeals;
                var frequencyPercent = totalMeals > 0 ? Math.Round((decimal)occurrences / totalMeals * 100, 1) : 0;
                var avgSeverity = Math.Min(Math.Round(c.Value.totalSeverity / c.Value.symptomMatches, 1), 10);
                return new CorrelationDto
                {
                    FoodOrAdditive = parts[0],
                    SymptomName = parts[1],
                    Occurrences = occurrences,
                    TotalMeals = totalMeals,
                    FrequencyPercent = frequencyPercent,
                    AverageSeverity = avgSeverity,
                    Confidence = occurrences >= 15 ? "High" : occurrences >= 5 ? "Medium" : "Low"
                };
            })
            .OrderByDescending(c => c.Occurrences)
            .Take(20)
            .ToList();
    }
}
