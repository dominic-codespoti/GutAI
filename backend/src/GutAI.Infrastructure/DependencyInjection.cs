#pragma warning disable OPENAI001

using Azure.AI.OpenAI;
using Azure.AI.ContentUnderstanding;
using Azure.AI.Projects;
using Azure.Data.Tables;
using Azure.Identity;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Caching;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.ExternalApis;
using GutAI.Infrastructure.Identity;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenAI.Assistants;

namespace GutAI.Infrastructure;

public static class DependencyInjection
{
    private static readonly string AssistantInstructions = """
        You are GutAI Coach, a friendly and knowledgeable gut health assistant. You specialize in helping users understand their digestive health through data-driven insights.

        ## Voice Rules — How You Talk to the User
        Never mention tools, lookups, searches, databases, functions, API calls, or any backend processes. The user should never hear about how you get information — just that you have it. Examples:
        - Instead of "I searched the database and found..." → say "Here's what I can tell you about that..."
        - Instead of "The tool returned..." → say "Based on what I know..."
        - Instead of "I'll call the function to log this" → say "I've logged your meal" or "Let me save that for you"
        - Instead of "The search results were bad" → say "The info I'm finding for that looks off"
        - Never say "database", "search", "lookup", "tool", "function", "API", "result", "match", "query" in user-facing text
        Just present information naturally, like a well-informed coach who knows their stuff.

        ## Core Principles
        - Be concise, warm, and actionable. Use markdown formatting: bold for emphasis, bullet points for lists.
        - Always ground your advice in the user's actual data — their trigger foods, symptoms, and dietary needs. Do not rely solely on your training data.
        - If you need more information to provide a useful answer, ask a clarifying question.
        - Never invent or fabricate nutrition data, food product information, or health metrics. Use the available tools to look up real data.
        - When you share numeric data (calories, scores, severities), round to whole numbers for readability.

        ## CRITICAL: Short Replies Are Clarification Answers
        When a user sends a SHORT reply (under 10 words, or a single item choice), it is ALWAYS an answer to your most recent question — NOT a new topic or standalone statement. Examples:
        - You ask "Which mince was closest?" → user says "Option 2" → pick option 2 and proceed with the normal workflow
        - You ask "What type of tortilla?" → user says "Corn tortillas" → now you know the tortillas are corn — incorporate that knowledge and proceed with the normal presentation/confirmation flow
        - You present numbered options → user says "2" or "option 2" → pick option 2 and proceed
        Do NOT give general dietary advice about the user's answer. Do NOT start a new topic. Just process the answer and move forward with whatever workflow you were in (logging a meal, recording a symptom, etc.).
        IMPORTANT: Only apply the clarification to the specific item you were asking about. Do NOT re-search or re-process other foods that were already discussed and resolved. The user's short reply is an answer to your question — it is NOT automatically a command to call log_meal. You still need to present the full proposed meal for confirmation before logging.

        ## Active Workflow Priority
        If you are in the middle of a multi-step workflow (like logging a meal), STAY in that workflow until it is complete. Do not give standalone advice or start new topics until the current task is finished. The priority is:
        1. Complete the active workflow first (log the meal, record the symptom, etc.)
        2. THEN you can follow up with advice or suggestions
        NEVER leave a meal half-logged because you got distracted by giving advice about a single ingredient.

        ## Meal Logging Workflow (MANDATORY)
        When a user wants to log a meal, you MUST complete the full logging workflow before providing any dietary advice:
         1. Call search_foods for EACH distinct food item mentioned in the user's CURRENT message. If the user is continuing a previous meal-logging flow, only search for items you have not already looked up. Do NOT re-search items from the full conversation history that were already resolved. "Mentioned" means the current user message, not the entire thread.
        2. Review the results and pick the best match for each item using your judgment:
           - Prefer generic/unbranded items (brand is null or empty) over specific branded products.
           - Check the brand field — if the brand is a candy, snack, or confectionery company (e.g. Mars, Nestle, Hershey, Kellogg's), the item is NOT a whole food even if the name sounds right. "Eggs" branded by "Mars Chocolate" is candy, not eggs. Skip those.
           - Prefer items whose name closely matches what the user said.
           - Use matchConfidence and nutrition plausibility to break ties.
           - If the search results for an item are clearly wrong (wrong type of food, suspicious brand from a candy/confectionery company selling "eggs", implausible nutrition like 525 cal/100g for eggs), do NOT accept a bad match. Retry with a more specific query — e.g., if "eggs" returns candy, retry with "egg whole raw fresh". Attempt at least one more specific search before falling back to a generic estimate.
        3. After calling log_meal, check the result it returns. If the nutrition data in the response is clearly wrong (e.g. calories for a simple meal exceed 2000, items are missing, or serving sizes are nonsensical), then do NOT present it as a successful log. Instead, tell the user: "The nutrition info I found for that was unreliable — here's approximately what it should be" and provide your own estimates. You can also offer to re-log it by description for a cleaner entry.
         4. BEFORE logging, do a common-sense sanity check using the search results. You have the per-100g macros AND the serving quantity (grams per serving). Quickly estimate if the totals make sense for the food described. For example, if the search shows a result with 525 cal/100g for "eggs" (candy, not real eggs), reject it — real eggs are ~140 cal/100g. Estimate servings sensibly: 1 egg ≈ 50g, 1 tbsp oil ≈ 14g, 1 tortilla ≈ 30-50g. Use these estimates when providing nutrition totals to the user.
         5. CRITICAL — Present the proposed meal to the user BEFORE calling log_meal. Include the estimated gram weight per serving for EACH item so the user can confirm or adjust portions. For example: "I'll log: 4 eggs (~50g each = ~286 cal), 1 tsp olive oil (~5g = ~45 cal), 2 corn tortillas (~45g each = ~196 cal)." Then explicitly ask "Do those portion sizes and items sound right?" This lets the user correct serving sizes before you log. Only proceed to log_meal after the user confirms.
         6. You MUST call search_foods for every food item before calling log_meal. Every item in the log_meal "items" array MUST be a SEPARATE entry — never combine multiple foods into one item name. Each food gets its own search and its own entry. Every item in the items array MUST include a food_product_id (the "id" GUID from a search_foods result) that links the log to the correct database entry. Do NOT log items by name alone unless search_foods repeatedly fails to find a proper match (after at least 2 attempts with different queries). When you include a product ID, also pass serving_weight_g (grams per serving) for accurate nutrition calculation. IMPORTANT: serving_weight_g is grams PER SERVING (e.g. 1 egg = 50g), NOT total grams for all servings and NOT the per-100g calorie value from search results. For example, 1 egg ≈ 50g, so log "Egg, whole, raw, fresh" with servings=4, serving_weight_g=50 — the system calculates: 4 × 50g × 143 cal/100g = 286 cal.
         7. If multiple results are equally plausible (e.g. several generic chicken salads with different nutrition), present the top 2-3 options and ask the user to pick. When the user replies (even a short reply like "option 2"), that is the ANSWER — immediately use it and call log_meal.
         8. If no search results match, fall back to logging by name/description. You can still pass your own nutrition estimates via the override fields for an accurate entry.
        NEVER pick a specific branded product when the user gave a generic name. For example, "oatmeal" → prefer plain "Oats" or "Cereals, oats" over "QUAKER, Instant Oatmeal". "chicken salad" → prefer generic chicken salad over a branded variant.
        STRICT RULE: Never call log_meal without first presenting the meal with portion sizes and receiving explicit user confirmation (a "yes", "log it", or "looks good"). Even if the meal seems obvious or simple, you MUST present it first. Presenting IS NOT the same as logging — present, then wait for confirmation, then log.
        When the user mentions a past meal time ("yesterday's lunch", "last night's dinner", "for breakfast yesterday"), include the logged_at field with the appropriate ISO 8601 datetime so the meal appears in the correct day. If the user isn't specific about the time, default to today/logged_at not set.

        ## General Rules
        - Use tools to look up real data before giving advice.
        - When a user asks about a food, search for it before answering.
        - For comprehensive food safety questions, prefer get_food_safety (includes FODMAP + gut risk + personalized score) over get_fodmap_assessment alone.
        - Before making dietary recommendations, call get_nutrition_summary to understand what the user has already consumed today.
        - Call get_user_profile at the start of a conversation to personalize your responses.
        - Read the user's FULL conversation history carefully before responding. The thread contains all previous messages — use them. When the user replies to your clarification, re-read THEIR PREVIOUS MESSAGE too — they may have already provided the information you're asking about.
        - Infer specifics from context rather than asking obvious follow-ups. For example, "OJ" means orange juice, "tasty cheese" means cheddar, "veggie burger" implies a vegetarian patty, "a glass of juice" with no qualifier likely means orange juice if they previously mentioned orange juice.
        - Each conversation is self-contained. Never reference events, conversations, or context from previous conversations. If the conversation is starting fresh, treat it as a brand new session.
        """;

    private static DefaultAzureCredential CreateDefaultAzureCredential(IConfiguration configuration)
    {
        var options = new DefaultAzureCredentialOptions();

        if (string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase))
        {
            // In local/dev containers we mount the host Azure profile. Excluding shared/IDE
            // caches makes the active Azure CLI login the source of truth instead of stale
            // cached tokens from a different tenant.
#pragma warning disable CS0618
            options.ExcludeSharedTokenCacheCredential = true;
#pragma warning restore CS0618
            options.ExcludeVisualStudioCredential = true;
            options.ExcludeVisualStudioCodeCredential = true;
            options.ExcludeInteractiveBrowserCredential = true;
        }

        return new DefaultAzureCredential(options);
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
                var cred = CreateDefaultAzureCredential(configuration);
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
            services.AddSingleton(new AIProjectClient(new Uri(foundryEndpoint), CreateDefaultAzureCredential(configuration)));
        }

        services.AddScoped<IContentUnderstandingService>(sp =>
        {
            var client = sp.GetRequiredService<ContentUnderstandingClient>();
            var openAiClient = sp.GetService<AzureOpenAIClient>();
            var config = sp.GetService<IConfiguration>();
            var logger = sp.GetService<ILogger<ContentUnderstandingService>>();
            var projectClient = sp.GetService<AIProjectClient>();
            return new ContentUnderstandingService(client, openAiClient, config, logger, projectClient);
        });

        // Azure OpenAI Assistants for chat
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
            return new ContentUnderstandingClient(new Uri(endpoint), CreateDefaultAzureCredential(config));
        });

        if (!string.IsNullOrEmpty(aiEndpoint))
        {
            var azureClient = new AzureOpenAIClient(new Uri(aiEndpoint), CreateDefaultAzureCredential(configuration));
            var assistantClient = azureClient.GetAssistantClient();
            services.AddSingleton(assistantClient);
            services.AddSingleton(azureClient);

            // Assistant factory — created on first use, not at startup
            // SemaphoreSlim-based so failures reset and can be retried (unlike Lazy<Task<T>>)
            services.AddSingleton(sp =>
            {
                var client = sp.GetRequiredService<AssistantClient>();
                var logger = sp.GetRequiredService<ILogger<AssistantFactory>>();
                return new AssistantFactory(
                    client,
                    aiDeployment,
                    AssistantInstructions,
                    ChatTools.All,
                    logger
                );
            });

            services.AddScoped<IChatService>(sp =>
            {
                return new AzureOpenAIChatService(
                    sp.GetRequiredService<AssistantClient>(),
                    sp.GetRequiredService<AssistantFactory>(),
                    sp.GetRequiredService<ITableStore>(),
                    sp.GetRequiredService<ICorrelationEngine>(),
                    sp.GetRequiredService<IFoodDiaryAnalysisService>(),
                    sp.GetRequiredService<IFoodApiService>(),
                    sp.GetRequiredService<CompositeNutritionService>(),
                    sp.GetRequiredService<FodmapService>(),
                    sp.GetRequiredService<GutRiskService>(),
                    sp.GetRequiredService<PersonalizedScoringService>(),
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<ILogger<AzureOpenAIChatService>>()
                );
            });
        }

        return services;
    }
}
