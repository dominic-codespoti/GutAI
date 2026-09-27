using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public sealed class MealDraftConcurrencyTests(AzuriteFixture fx)
{
    [Fact]
    public async Task ConcurrentCommits_OnlyOneClaimsDraftAndPersistsMeal()
    {
        var userId = Guid.NewGuid();
        var draftService = new MealDraftService(fx.Store, new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<MealDraftService>.Instance);
        var draft = await draftService.CreateAsync(userId, new MealDraftCreateRequest
        {
            Origin = MealDraftOrigins.Photo,
            Items = [new MealDraftItemDto
            {
                ItemId = Guid.NewGuid(), Name = "rice", Source = "usda", Grams = 100,
                Per100g = new NutritionPer100gDto { CaloriesKcal = 130 }, MatchConfidence = .95m
            }]
        });

        async Task<object> AttemptCommit()
        {
            try { return await draftService.CommitAsync(userId, draft.DraftId, null); }
            catch (MealDraftException ex) { return ex; }
        }

        var outcomes = await Task.WhenAll(AttemptCommit(), AttemptCommit());
        var successes = outcomes.OfType<MealDraftCommitResult>().ToList();
        var failures = outcomes.OfType<MealDraftException>().ToList();
        Assert.Single(successes);
        Assert.Single(failures);
        Assert.Equal(MealDraftErrorCode.NotPending, failures[0].Code);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var meals = await fx.Store.GetMealLogsByDateRangeAsync(userId, today, today);
        Assert.Single(meals);
        Assert.Equal(successes[0].MealId, meals[0].Id);
    }
}
