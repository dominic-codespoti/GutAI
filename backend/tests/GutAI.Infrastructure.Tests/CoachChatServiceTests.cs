using System.Runtime.CompilerServices;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class CoachChatServiceTests
{
    [Fact]
    public async Task StreamResponse_AttachesCurrentNutritionSnapshotToModelContext()
    {
        var userId = Guid.NewGuid();
        var meal = new MealLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MealType = MealType.Lunch,
            LoggedAt = DateTime.UtcNow,
            TotalCalories = 1422,
            TotalProteinG = 97,
            TotalCarbsG = 134,
            TotalFatG = 60,
        };
        var user = new User
        {
            Id = userId,
            Email = "coach-test@example.com",
            TimezoneId = "UTC",
            DailyCalorieGoal = 2000,
            DailyProteinGoalG = 50,
            DailyCarbGoalG = 250,
            DailyFatGoalG = 65,
            DailyFiberGoalG = 25,
        };

        var store = new Mock<ITableStore>();
        store.Setup(s => s.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        store.Setup(s => s.GetRecentCoachMessagesAsync(userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.GetMealLogsByDateRangeAsync(
                userId,
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([meal]);
        store.Setup(s => s.GetMealItemsAsync(userId, meal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MealItem
                {
                    Id = Guid.NewGuid(),
                    MealLogId = meal.Id,
                    FoodName = "Logged meal item",
                    FiberG = 8,
                },
            ]);
        store.Setup(s => s.UpsertCoachMessageAsync(
                userId,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chatClient = new RecordingChatClient();
        var fodmap = new FodmapService();
        var gutRisk = new GutRiskService();
        var service = new CoachChatService(
            chatClient,
            store.Object,
            new Mock<ICorrelationEngine>().Object,
            new Mock<IFoodDiaryAnalysisService>().Object,
            new Mock<IFoodSearchService>().Object,
            new Mock<INutritionApiService>().Object,
            fodmap,
            gutRisk,
            new PersonalizedScoringService(gutRisk, fodmap),
            NullLogger<CoachChatService>.Instance);

        await foreach (var _ in service.StreamResponseAsync(
                           userId,
                           "How's my nutrition today?",
                           CancellationToken.None,
                           "UTC"))
        {
        }

        var snapshot = Assert.Single(
            chatClient.LastMessages,
            message => message.Text.Contains("<current_nutrition_snapshot>", StringComparison.Ordinal)
                && message.Text.Contains("\"totalCalories\":1422", StringComparison.Ordinal));
        Assert.Contains("\"totalCalories\":1422", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("\"totalProteinG\":97", snapshot.Text, StringComparison.Ordinal);
        Assert.Contains("\"mealCount\":1", snapshot.Text, StringComparison.Ordinal);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "I see your logged meal.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
