using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Services;

public class CorrelationEngine : ICorrelationEngine
{
    private readonly ITableStore _store;

    public CorrelationEngine(ITableStore store) => _store = store;

    public async Task<List<CorrelationDto>> ComputeCorrelationsAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default, string? timezoneId = null)
    {
        var result = await FoodSymptomAssociationService.ComputeAsync(userId, from, to, _store, includeAdditives: true, ct, timezoneId);
        return result.Associations
            .Where(a => a.AssociatedMealWeight >= 3)
            .Select(a => new CorrelationDto
            {
                FoodOrAdditive = a.FoodName,
                SymptomName = a.SymptomName,
                Occurrences = (int)Math.Round(a.AssociatedMealWeight),
                TotalMeals = a.ExposureMeals,
                FrequencyPercent = a.ExposedSymptomRate,
                BaselineFrequencyPercent = a.BaselineSymptomRate,
                AverageSeverity = a.AverageSeverity,
                Confidence = a.Confidence,
                AttributionMethod = a.AttributionMethod,
                Limitations = a.Limitations.ToList()
            })
            .OrderByDescending(c => c.Occurrences)
            .Take(20)
            .ToList();
    }
}
