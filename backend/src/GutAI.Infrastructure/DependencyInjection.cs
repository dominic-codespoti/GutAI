using Azure.Data.Tables;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Caching;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.ExternalApis;
using GutAI.Infrastructure.Identity;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace GutAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Azure Table Storage
        var storageConn = configuration.GetConnectionString("AzureStorage")
            ?? "UseDevelopmentStorage=true";
        services.AddSingleton(new TableServiceClient(storageConn));
        services.AddSingleton<ITableStore, TableStorageStore>();

        // JWT
        services.AddSingleton<IJwtService, JwtService>();

        // In-memory distributed cache
        services.AddDistributedMemoryCache();
        services.AddSingleton<ICacheService, InMemoryCacheService>();

        // Correlation engine
        services.AddScoped<ICorrelationEngine, CorrelationEngine>();

        // HTTP Clients for external APIs
        services.AddHttpClient<OpenFoodFactsClient>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "GutAI/1.0 (contact@gutai.app)");
            client.Timeout = TimeSpan.FromSeconds(10);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<CalorieNinjasClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<EdamamFoodClient>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "GutAI/1.0");
            client.Timeout = TimeSpan.FromSeconds(10);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        });

        // Register as interfaces
        services.AddScoped<IFoodApiService, CompositeFoodApiService>();
        services.AddScoped<NaturalLanguageFallbackService>();
        services.AddScoped<INutritionApiService, CompositeNutritionService>();
        services.AddSingleton<GutRiskService>();
        services.AddSingleton<FodmapService>();
        services.AddSingleton<SubstitutionService>();
        services.AddSingleton<GlycemicIndexService>();
        services.AddScoped<PersonalizedScoringService>();
        services.AddScoped<IFoodDiaryAnalysisService, FoodDiaryAnalysisService>();

        return services;
    }
}
