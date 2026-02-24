using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Moq;

namespace GutAI.Infrastructure.Tests;

public static class MockTableStoreFactory
{
    public static Mock<ITableStore> Create(
        List<User>? users = null,
        List<MealLog>? meals = null,
        List<MealItem>? items = null,
        List<SymptomLog>? symptoms = null)
    {
        var mock = new Mock<ITableStore>();

        // Merge items from meals' Items collections with standalone items list
        var allItems = new List<MealItem>(items ?? []);
        foreach (var meal in meals ?? [])
        {
            if (meal.Items is { Count: > 0 })
                allItems.AddRange(meal.Items);
        }

        mock.Setup(x => x.GetUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => (users ?? []).FirstOrDefault(u => u.Id == id));

        mock.Setup(x => x.GetMealLogsByDateRangeAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, DateOnly from, DateOnly to, CancellationToken _) =>
                (meals ?? []).Where(m => m.UserId == userId && !m.IsDeleted && DateOnly.FromDateTime(m.LoggedAt) >= from && DateOnly.FromDateTime(m.LoggedAt) <= to).ToList());

        mock.Setup(x => x.GetMealItemsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, Guid mealId, CancellationToken _) =>
                allItems.Where(i => i.MealLogId == mealId).ToList());

        mock.Setup(x => x.GetSymptomLogsByDateRangeAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, DateOnly from, DateOnly to, CancellationToken _) =>
                (symptoms ?? []).Where(s => s.UserId == userId && !s.IsDeleted && DateOnly.FromDateTime(s.OccurredAt) >= from && DateOnly.FromDateTime(s.OccurredAt) <= to).ToList());

        // Collect all symptom types from symptom logs
        var symptomTypes = (symptoms ?? [])
            .Where(s => s.SymptomType != null)
            .Select(s => s.SymptomType!)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .ToList();

        mock.Setup(x => x.GetSymptomTypeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => symptomTypes.FirstOrDefault(t => t.Id == id));

        return mock;
    }
}
