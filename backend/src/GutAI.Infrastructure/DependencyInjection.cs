#pragma warning disable OPENAI001

using Azure.AI.OpenAI;
using Azure.Data.Tables;
using Azure.Identity;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Caching;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.ExternalApis;
using GutAI.Infrastructure.Identity;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenAI.Assistants;

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

        // Register leaf data providers as concrete types for explicit composition
        services.AddScoped<OpenFoodFactsClient>();
        services.AddScoped<UsdaFoodDataClient>();
        services.AddScoped<WholeFoodApiService>();
        services.AddScoped<AustralianFoodApiService>();
        services.AddScoped<BrandedFoodApiService>();

        // Register the composite orchestrator as the primary IFoodApiService
        services.AddScoped<IFoodApiService>(sp =>
        {
            var providers = new List<IFoodApiService>
            {
                sp.GetRequiredService<OpenFoodFactsClient>(),
                sp.GetRequiredService<UsdaFoodDataClient>(),
                sp.GetRequiredService<WholeFoodApiService>(),
                sp.GetRequiredService<AustralianFoodApiService>(),
                sp.GetRequiredService<BrandedFoodApiService>()
            };
            var logger = sp.GetRequiredService<ILogger<CompositeFoodApiService>>();
            return new CompositeFoodApiService(providers, logger);
        });

        // Nutrition specific
        services.AddScoped<INutritionApiService, CompositeNutritionService>();
        services.AddScoped<CompositeNutritionService>();

        services.AddScoped<NaturalLanguageFallbackService>();
        services.AddSingleton<GutRiskService>();
        services.AddSingleton<FodmapService>();
        services.AddSingleton<SubstitutionService>();
        services.AddSingleton<GlycemicIndexService>();
        services.AddScoped<PersonalizedScoringService>();
        services.AddScoped<IFoodDiaryAnalysisService, FoodDiaryAnalysisService>();

        // Azure OpenAI Assistants for chat
        var aiEndpoint = configuration["AzureOpenAI:Endpoint"];
        var aiDeployment = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-5-nano";
        if (!string.IsNullOrEmpty(aiEndpoint))
        {
            var azureClient = new AzureOpenAIClient(new Uri(aiEndpoint), new DefaultAzureCredential());
            var assistantClient = azureClient.GetAssistantClient();
            services.AddSingleton(assistantClient);

            // Lazy assistant creation — created on first use, not at startup
            var assistantIdTask = new Lazy<Task<string>>(async () =>
            {
                var assistant = await assistantClient.CreateAssistantAsync(
                    model: aiDeployment,
                    new AssistantCreationOptions
                    {
                        Name = "GutAI Coach",
                        Instructions = """
                            You are GutAI Coach, a friendly and knowledgeable gut health assistant.
                            You help users track meals, understand food sensitivities, manage symptoms, and make better food choices.
                            Be concise, warm, and actionable.

                            ## Meal Logging Workflow (MANDATORY)
                            When a user wants to log a meal:
                            1. Call search_foods for EACH food item mentioned (e.g. "chicken salad" and "water" are separate searches).
                            2. Review the results and pick the best match for each item using your judgment:
                               - Prefer generic/unbranded items over specific branded products (e.g. "Chicken Salad" over "M&S Chicken Katsu Salad").
                               - Prefer items whose name closely matches what the user said.
                               - Use matchConfidence and nutrition plausibility to break ties.
                            3. If one result clearly matches, select it automatically and proceed to log_meal.
                            4. If multiple results are equally plausible (e.g. several generic chicken salads with different nutrition), present the top 2-3 options and ask the user to pick.
                            5. When calling log_meal, you MUST include the "food_product_id" field (the "id" GUID from search_foods results) for EVERY item. This is critical for accurate nutrition data. NEVER omit food_product_id.
                            6. If no search results match, fall back to logging by name/description.
                            NEVER pick a specific branded product when the user gave a generic name. "chicken salad" → pick the generic chicken salad, not a branded variant.

                            ## General Rules
                            - Use tools to look up real data before giving advice.
                            - Always ground your advice in the user's actual data — their trigger foods, symptoms, and dietary needs.
                            - When a user asks about a food, search for it first.
                            """,
                        Tools = { ChatTools.SearchFoods, ChatTools.GetFoodSafety, ChatTools.GetFodmapAssessment,
                            ChatTools.LogMeal, ChatTools.LogSymptom, ChatTools.GetTodaysMeals,
                            ChatTools.GetTriggerFoods, ChatTools.GetSymptomHistory,
                            ChatTools.GetNutritionSummary, ChatTools.GetEliminationDietStatus }
                    }
                );
                return assistant.Value.Id;
            });

            services.AddScoped<IChatService>(sp =>
            {
                return new AzureOpenAIChatService(
                    sp.GetRequiredService<AssistantClient>(),
                    assistantIdTask,
                    sp.GetRequiredService<ITableStore>(),
                    sp.GetRequiredService<ICorrelationEngine>(),
                    sp.GetRequiredService<IFoodDiaryAnalysisService>(),
                    sp.GetRequiredService<IFoodApiService>(),
                    sp.GetRequiredService<CompositeNutritionService>(),
                    sp.GetRequiredService<FodmapService>(),
                    sp.GetRequiredService<GutRiskService>(),
                    sp.GetRequiredService<PersonalizedScoringService>(),
                    sp.GetRequiredService<ILogger<AzureOpenAIChatService>>()
                );
            });
        }

        return services;
    }
}
