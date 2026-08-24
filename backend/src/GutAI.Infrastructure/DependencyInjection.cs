#pragma warning disable OPENAI001

using Azure.AI.OpenAI;
using Azure.AI.ContentUnderstanding;
using Azure.AI.Projects;
using Azure.Data.Tables;
using Azure.Identity;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Caching;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.ExternalApis;
using GutAI.Infrastructure.Identity;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure;

public static class DependencyInjection
{
    // Coach system instructions moved verbatim to Services/CoachPrompts.cs during the
    // P0b Assistants-API sunset migration.

    private static Azure.Core.TokenCredential CreateCredential(IConfiguration configuration)
    {
        var env = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
        {
            // Direct Azure CLI credential in dev — instant, deterministic, 0 timeout probes.
            return new AzureCliCredential();
        }

        return new DefaultAzureCredential();
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Azure Table Storage
        var storageConn = configuration.GetConnectionString("AzureStorage")
            ?? "UseDevelopmentStorage=true";
        services.AddSingleton(new TableServiceClient(storageConn));
        services.AddSingleton<ITableStore, TableStorageStore>();

        // Offline food database — self-constructs its own TableServiceClient using
        // DefaultAzureCredential (az login, managed identity) so it doesn't conflict
        // with the connection-string-based client used by TableStorageStore.
        var storageAccountName = configuration["AzureStorage:AccountName"];
        services.AddSingleton<IOfflineFoodDatabase>(sp =>
        {
            var cache = sp.GetRequiredService<IMemoryCache>();
            var logger = sp.GetRequiredService<ILogger<AzureTableOfflineDatabase>>();

            if (!string.IsNullOrEmpty(storageAccountName))
            {
                var cred = CreateCredential(configuration);
                var endpoint = new Uri($"https://{storageAccountName}.table.core.windows.net");
                return new AzureTableOfflineDatabase(new TableServiceClient(endpoint, cred), cache, logger);
            }

            // Fall back to connection string (also used by Azurite in dev)
            return new AzureTableOfflineDatabase(new TableServiceClient(storageConn), cache, logger);
        });

        // JWT
        services.AddSingleton<IJwtService, JwtService>();

        // In-memory caches
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddSingleton<ICacheService, InMemoryCacheService>();

        // Correlation engine
        services.AddScoped<ICorrelationEngine, CorrelationEngine>();

        // HTTP Clients for external APIs
        // Search-a-licious (Elasticsearch) responds in 2-3s; barcode lookups on v2 ~2-3s with fields.
        // Keep sensible timeouts for when the service is slow/degraded.
        services.AddHttpClient<OpenFoodFactsClient>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "GutAI/1.0 (contact@gutai.app)");
            client.Timeout = TimeSpan.FromSeconds(12);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 1;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
        });

        // Register leaf data providers as concrete types for explicit composition
        services.AddScoped<OpenFoodFactsClient>();
        services.AddScoped<UsdaFoodDataClient>();
        services.AddScoped<WholeFoodApiService>();
        services.AddScoped<AustralianFoodApiService>();
        services.AddScoped<BrandedFoodApiService>();

        // Single ranking owner — stateless, safe as a singleton (no shared/cached
        // state across requests; builds a throwaway in-memory candidate index per call).
        services.AddSingleton<IFoodRanker, FoodRanker>();

        // Fan-out to every registered provider; isolates failures, propagates
        // cancellation, reports structured per-provider outcomes. No ranking/caching.
        services.AddScoped<IExternalFoodAggregator>(sp =>
        {
            var providers = new List<IFoodProvider>
            {
                sp.GetRequiredService<OpenFoodFactsClient>(),
                sp.GetRequiredService<UsdaFoodDataClient>(),
                sp.GetRequiredService<WholeFoodApiService>(),
                sp.GetRequiredService<AustralianFoodApiService>(),
                sp.GetRequiredService<BrandedFoodApiService>()
            };
            var logger = sp.GetRequiredService<ILogger<ExternalFoodProviderAggregator>>();
            return new ExternalFoodProviderAggregator(providers, logger);
        });

        // General-purpose search service for consumers with no local-store concerns
        // (chat tools, MCP tools, NLP meal parsing): aggregate -> canonicalize -> rank once.
        services.AddScoped<IFoodSearchService, FoodSearchService>();

        // Nutrition specific
        services.AddScoped<INutritionApiService, CompositeNutritionService>();
        services.AddScoped<CompositeNutritionService>();

        services.AddScoped<NaturalLanguageFallbackService>();
        services.AddSingleton<GutRiskService>();
        services.AddSingleton<IGutRiskService>(sp => sp.GetRequiredService<GutRiskService>());
        services.AddSingleton<FodmapService>();
        services.AddSingleton<IFodmapService>(sp => sp.GetRequiredService<FodmapService>());
        services.AddSingleton<SubstitutionService>();
        services.AddSingleton<GlycemicIndexService>();
        services.AddSingleton<IGlycemicIndexService>(sp => sp.GetRequiredService<GlycemicIndexService>());
        services.AddScoped<PersonalizedScoringService>();
        services.AddScoped<IFoodDiaryAnalysisService, FoodDiaryAnalysisService>();
        // Foundry Agent Service — optional, used by ContentUnderstandingService for nutrition estimation
        var foundryEndpoint = configuration["Foundry:ProjectEndpoint"];
        if (!string.IsNullOrEmpty(foundryEndpoint))
        {
            services.AddSingleton(new AIProjectClient(new Uri(foundryEndpoint), CreateCredential(configuration)));
        }

        services.AddScoped<IContentUnderstandingService>(sp =>
        {
            var client = sp.GetRequiredService<ContentUnderstandingClient>();
            var openAiClient = sp.GetService<AzureOpenAIClient>();
            var config = sp.GetService<IConfiguration>();
            var logger = sp.GetService<ILogger<ContentUnderstandingService>>();
            var projectClient = sp.GetService<AIProjectClient>();
            var chatClient = sp.GetService<IChatClient>();
            return new ContentUnderstandingService(client, openAiClient, config, logger, projectClient, chatClient);
        });

        // Coach chat (Microsoft.Extensions.AI over Azure OpenAI)
        var aiEndpoint = configuration["AzureOpenAI:Endpoint"];
        var cuEndpoint = configuration["AzureOpenAI:ContentUnderstandingEndpoint"] ?? configuration["AzureOpenAI:Endpoint"];
        var aiDeployment = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-5-nano";

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config["AzureOpenAI:ContentUnderstandingEndpoint"] ?? config["AzureOpenAI:Endpoint"];
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException("AzureOpenAI:ContentUnderstandingEndpoint or AzureOpenAI:Endpoint must be configured.");
            }
            return new ContentUnderstandingClient(new Uri(endpoint), CreateCredential(config));
        });

        if (!string.IsNullOrEmpty(aiEndpoint))
        {
            var azureClient = new AzureOpenAIClient(new Uri(aiEndpoint), CreateCredential(configuration));
            services.AddSingleton(azureClient);

            // Coach model transport: Microsoft.Extensions.AI IChatClient over Azure OpenAI
            // Responses API. UseFunctionInvocation middleware executes the coach tools and
            // feeds results back to the model automatically (replaces the former hand-rolled
            // Assistants run/tool-output loop). The AsIChatClient adapter carries experimental
            // metadata upstream — it is deliberately referenced ONLY here.
            services.AddSingleton<IChatClient>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
#pragma warning disable OPENAI001 // experimental Responses surface
                var transport = configuration["AzureOpenAI:Transport"] ?? "responses";
                var inner = transport.Equals("responses", StringComparison.OrdinalIgnoreCase)
                    ? azureClient.GetResponsesClient().AsIChatClient(aiDeployment)
                    : azureClient.GetChatClient(aiDeployment).AsIChatClient();
#pragma warning restore OPENAI001
                return new ChatClientBuilder(inner)
                    .UseFunctionInvocation(loggerFactory)
                    .Build();
            });

        services.AddScoped<IChatService>(sp =>
        {
            return new CoachChatService(
                    sp.GetRequiredService<IChatClient>(),
                    sp.GetRequiredService<ITableStore>(),
                    sp.GetRequiredService<ICorrelationEngine>(),
                    sp.GetRequiredService<IFoodDiaryAnalysisService>(),
                    sp.GetRequiredService<IFoodSearchService>(),
                    sp.GetRequiredService<CompositeNutritionService>(),
                    sp.GetRequiredService<FodmapService>(),
                    sp.GetRequiredService<GutRiskService>(),
                    sp.GetRequiredService<PersonalizedScoringService>(),
                    sp.GetRequiredService<ILogger<CoachChatService>>(),
                    sp.GetService<IWebNutritionLookup>()
                );
            });

            // AI meal photo scanning (P1 skeleton — see docs/meal-scan-detailed-design.md).
            // Shares the coach's IChatClient pipeline (Responses transport + function middleware);
            // the scan path itself is non-agentic structured-output inference.
            services.AddScoped<IMealScanService>(sp => new MealScanService(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<ITableStore>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IFoodSearchService>(),
                sp.GetRequiredService<IWebNutritionLookup>(),
                sp.GetRequiredService<FodmapService>(),
                sp.GetRequiredService<GutRiskService>(),
                sp.GetRequiredService<ILogger<MealScanService>>()
            ));

            // Stage B3 — free web-results cascade for items the resolver couldn't ground.
            // Flag-gated (Features:WebGrounding); keyless DDG search + Jina Reader + cheap extraction.
            services.AddHttpClient<WebNutritionCascade>();
            services.AddScoped<IWebNutritionLookup>(sp => sp.GetRequiredService<WebNutritionCascade>());

            // Stage A alone — used by the golden-image regression harness.
            services.AddSingleton<IMealVisionStage>(sp => new MealScanService(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<ITableStore>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IFoodSearchService>(),
                sp.GetRequiredService<IWebNutritionLookup>(),
                sp.GetRequiredService<FodmapService>(),
                sp.GetRequiredService<GutRiskService>(),
                sp.GetRequiredService<ILogger<MealScanService>>()
            ));
        }

        return services;
    }
}
