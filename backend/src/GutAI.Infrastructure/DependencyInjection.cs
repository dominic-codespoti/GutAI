#pragma warning disable OPENAI001

using Azure.AI.OpenAI;
using Azure.AI.ContentUnderstanding;
using Azure.AI.Projects;
using Azure.Data.Tables;
using Azure.Identity;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Application.Common.Services;
using GutAI.Infrastructure.Caching;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.ExternalApis;
using GutAI.Infrastructure.Identity;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure;

public static class DependencyInjection
{
    // Coach developer instructions moved verbatim to Services/CoachPrompts.cs during the
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
        services.TryAddSingleton(TimeProvider.System);

        // Meal drafts: purge expired pending drafts and aged closed drafts (plan §4.6, D8).
        services.AddHostedService<MealDraftCleanupService>();

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

        // AI-consumer linking (pairing codes → personal access tokens for MCP)
        services.AddScoped<IPairingService, PairingService>();

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

        // USDA FoodData Central — 8s timeout, base address configured for relative endpoint calls
        services.AddHttpClient<UsdaFoodDataClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.nal.usda.gov/");
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.Add("User-Agent", "GutAI/1.0 (contact@gutai.app)");
        });

        // Register leaf data providers as concrete types for explicit composition
        services.AddScoped<OpenFoodFactsClient>();
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

        // Meal drafts: every AI-originated meal is a draft committed by the user (AGENTS.md N3).
        services.AddScoped<IMealDraftService, MealDraftService>();
        // Shared Coach/MCP item builder (plan §2.4): one grounding policy for every agent.
        services.AddScoped<IAgentMealItemResolver, AgentMealItemResolver>();
        // Meal-scan helpers: Stage-A dedupe cache (§4.5) and opt-in portion calibration (§6.2).
        services.AddSingleton<VisionResultCache>();
        services.AddSingleton<PortionCalibrator>();
        // Remaining-budget math shared by the Coach and meal suggestions (plan §7.1).
        services.AddScoped<INutritionBudgetService, NutritionBudgetService>();

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
            var config = sp.GetService<IConfiguration>();
            var logger = sp.GetService<ILogger<ContentUnderstandingService>>();
            var projectClient = sp.GetService<AIProjectClient>();
            var extractionChatClient = sp.GetKeyedService<IChatClient>(AiWorkloads.Extraction);
            var describeChatClient = sp.GetKeyedService<IChatClient>(AiWorkloads.Describe);
            // Describe-food decomposes and grounds each component through the shared resolver (§6.5).
            var foodSearch = sp.GetService<IFoodSearchService>();
            return new ContentUnderstandingService(client, config, logger, projectClient, extractionChatClient, describeChatClient, foodSearch);
        });

        // Coach chat (Microsoft.Extensions.AI over Azure OpenAI)
        var aiEndpoint = configuration["AzureOpenAI:Endpoint"];

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
            var networkTimeoutSeconds = configuration.GetValue("AzureOpenAI:NetworkTimeoutSeconds", 300);
            var clientOptions = new AzureOpenAIClientOptions
            {
                NetworkTimeout = TimeSpan.FromSeconds(Math.Clamp(networkTimeoutSeconds, 30, 600)),
            };
            var azureClient = new AzureOpenAIClient(
                new Uri(aiEndpoint),
                CreateCredential(configuration),
                clientOptions);

            // AzureOpenAI:Pricing schema: { "<deployment>": { "InputPer1M": USD, "OutputPer1M": USD } }.
            foreach (var workload in AiWorkloads.All)
            {
                services.AddKeyedChatClient(workload, _ =>
                {
#pragma warning disable OPENAI001 // experimental Responses surface
                    var inner = azureClient.GetResponsesClient().AsIChatClient(AiWorkloads.ResolveDeployment(configuration, workload));
#pragma warning restore OPENAI001
                    var loggerFactory = _.GetRequiredService<ILoggerFactory>();
                    var builder = new ChatClientBuilder(inner);

                    builder.UseFunctionInvocation(loggerFactory, client =>
                    {
                        if (workload == AiWorkloads.Coach)
                        {
                            client.MaximumIterationsPerRequest = configuration.GetValue(
                                "AzureOpenAI:Workloads:coach:MaxToolIterations", 8);
                            client.MaximumConsecutiveErrorsPerRequest = configuration.GetValue(
                                "AzureOpenAI:Workloads:coach:MaxConsecutiveToolErrors", 2);
                        }
                    });

                    return builder
                        .UseOpenTelemetry(
                            loggerFactory,
                            sourceName: "GutAI.AI",
                            client => client.EnableSensitiveData = configuration.GetValue(
                                "AzureOpenAI:Telemetry:EnableSensitiveData", false))
                        .UseLogging(loggerFactory)
                        .Build(_);
                });
            }

            services.AddScoped<IChatService>(sp =>
            {
                return new CoachChatService(
                        sp.GetRequiredKeyedService<IChatClient>(AiWorkloads.Coach),
                        sp.GetRequiredService<ITableStore>(),
                        sp.GetRequiredService<ICorrelationEngine>(),
                        sp.GetRequiredService<IFoodDiaryAnalysisService>(),
                        sp.GetRequiredService<IFoodSearchService>(),
                        sp.GetRequiredService<CompositeNutritionService>(),
                        sp.GetRequiredService<FodmapService>(),
                        sp.GetRequiredService<GutRiskService>(),
                        sp.GetRequiredService<PersonalizedScoringService>(),
                        sp.GetRequiredService<IMealDraftService>(),
                        sp.GetRequiredService<IAgentMealItemResolver>(),
                        sp.GetRequiredService<INutritionBudgetService>(),
                        sp.GetRequiredService<IConfiguration>(),
                        sp.GetRequiredService<TimeProvider>(),
                        sp.GetRequiredService<ILogger<CoachChatService>>(),
                        sp.GetService<IWebNutritionLookup>(),
                        sp.GetService<IOfflineFoodDatabase>(),
                        sp.GetService<IExternalFoodAggregator>(),
                        sp.GetService<IMealSuggestionService>()
                    );
            });

            // Grounded meal suggestions (plan Phase 7) on the suggestion workload; each valid
            // suggestion becomes a pending suggestion-origin draft (AGENTS.md N3).
            services.AddScoped<IMealSuggestionService, MealSuggestionService>();

            // AI meal photo scanning (docs/meal-scan-detailed-design.md): Stage A on the vision
            // workload, batched B2 + agent review on the selection workload; drafts persist via
            // IMealDraftService and reach the diary only through a user commit (AGENTS.md N3).
            services.AddScoped<IMealScanService>(sp => new MealScanService(
                sp.GetRequiredKeyedService<IChatClient>(AiWorkloads.Vision),
                sp.GetRequiredKeyedService<IChatClient>(AiWorkloads.Selection),
                sp.GetRequiredService<ITableStore>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IFoodSearchService>(),
                sp.GetRequiredService<IWebNutritionLookup>(),
                sp.GetRequiredService<FodmapService>(),
                sp.GetRequiredService<GutRiskService>(),
                sp.GetRequiredService<IMealDraftService>(),
                sp.GetRequiredService<VisionResultCache>(),
                sp.GetRequiredService<PortionCalibrator>(),
                sp.GetRequiredService<ILogger<MealScanService>>()
            ));

            // Stage B3 — free web-results cascade for items the resolver couldn't ground.
            // Flag-gated (Features:WebGrounding); keyless DDG search + Jina Reader + cheap extraction.
            services.AddHttpClient<WebNutritionCascade>();
            services.AddScoped<IWebNutritionLookup>(sp => sp.GetRequiredService<WebNutritionCascade>());
        }


        return services;
    }
}
