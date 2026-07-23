using System.Security.Claims;
using GutAI.Api.Middleware;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Constants;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using GutAI.Api.Imaging;

public static class FoodEndpoints
{
    public static RouteGroupBuilder MapFoodEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/search", SearchFoodProducts);
        group.MapGet("/barcode/{barcode}", GetFoodProductByBarcode);
        group.MapGet("/additives", GetFoodAdditives);
        group.MapGet("/additives/{id:int}", GetFoodAdditive);
        group.MapGet("/favorites", GetFavoriteFoods);
        group.MapPost("/{id:guid}/favorite", AddFavoriteFood);
        group.MapDelete("/{id:guid}/favorite", RemoveFavoriteFood);
        group.MapGet("/{id:guid}", GetFoodProduct);
        group.MapGet("/{id:guid}/safety-report", GetSafetyReport);
        group.MapGet("/{id:guid}/gut-risk", GetGutRisk);
        group.MapGet("/{id:guid}/fodmap", GetFodmap);
        group.MapGet("/{id:guid}/substitutions", GetSubstitutions);
        group.MapGet("/{id:guid}/glycemic", GetGlycemic);
        group.MapGet("/{id:guid}/personalized-score", GetPersonalizedScore);
        group.MapGet("/custom", GetCustomFoods);
        group.MapPost("/custom", CreateCustomFood);
        group.MapPut("/custom/{id:guid}", UpdateCustomFood);
        group.MapDelete("/custom/{id:guid}", DeleteCustomFood);
        group.MapPost("/describe", DescribeFoodFromText).RequireRateLimiting("aiExtraction");
        group.MapPost("/parse-label", ParseNutritionLabel)
            .RequireRateLimiting("aiExtraction")
            .DisableAntiforgery();

        group.MapPost("/", CreateFoodProduct).AddEndpointFilter<AdminKeyFilter>();
        group.MapPut("/{id:guid}", UpdateFoodProduct).AddEndpointFilter<AdminKeyFilter>();
        group.MapDelete("/{id:guid}", DeleteFoodProduct).AddEndpointFilter<AdminKeyFilter>();
        return group;
    }

    internal static async Task<IResult> SearchFoodProducts(string? q, string? region, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator foodAggregator, IFoodRanker ranker, ICacheService cache, ILogger<Program> logger)
    {
        var userId = user.FindFirstValue("sub");
        var query = QuerySanitizer.Sanitize(q ?? string.Empty);
        if (query.Length < 2)
            return Results.Ok(Array.Empty<FoodProductDto>());

        if (query.Length > 200)
            return Results.BadRequest(new { error = "Search query must not exceed 200 characters" });

        // Personalization: Fetch history to boost items
        var boostIds = new List<Guid>();
        if (Guid.TryParse(userId, out var uid))
        {
            var history = await store.GetAllUserMealItemsAsync(uid, 50);
            boostIds = history
                .Where(x => x.FoodProductId.HasValue)
                .Select(x => x.FoodProductId!.Value)
                .Distinct()
                .ToList();
        }

        // Incorporate boostIds into cache key to avoid mixed results for different users
        var normalizedRegion = region?.Trim().ToUpperInvariant() switch
        {
            "AU" or "US" => region!.Trim().ToUpperInvariant(),
            _ => "DEFAULT"
        };
        var regionPolicy = FoodSourcePolicy.ParseRegion(normalizedRegion);
        var cacheKey = $"food-search:{query.ToLowerInvariant()}:{normalizedRegion}:{userId ?? "anonymous"}";
        var cached = await cache.GetAsync<List<FoodProductDto>>(cacheKey);
        if (cached is not null)
            return Results.Ok(cached);

        var localTask = store.SearchFoodProductsAsync(query, 20, default);
        var additivesTask = store.GetAllFoodAdditivesAsync();

        // Search-a-licious responds in 2-3s; USDA in 1-5s.
        // 10s gives plenty of headroom for degraded conditions.
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var externalTask = foodAggregator.SearchAsync(query, cts.Token);

        try
        {
            await Task.WhenAll(localTask, additivesTask, externalTask);
        }
        catch
        {
        }

        var localResults = await localTask;
        var additives = await additivesTask;

        IReadOnlyList<FoodProductDto> externalResults = [];
        if (externalTask.IsCompletedSuccessfully)
        {
            var outcome = externalTask.Result;
            externalResults = outcome.Candidates;
            var failedProviders = outcome.ProviderOutcomes.Where(o => o.Status == ProviderSearchStatus.Failed).ToList();
            if (failedProviders.Count > 0)
                logger.LogWarning("Food search providers failed for query '{Query}': {Providers}", query,
                    string.Join(", ", failedProviders.Select(p => p.Source)));
        }
        cts.Dispose();

        var localDtos = localResults.Select(f => MapToDto(f, additives));

        // Single canonicalization pass: same-identity duplicates collapse to the
        // highest region-aware source priority; distinct same-name products survive.
        var allCandidates = FoodCandidateCanonicalizer.Canonicalize(localDtos.Concat(externalResults), regionPolicy);

        // ranker.Rank already applies eligibility filtering (an ineligible candidate set
        // returns empty rather than a confident wrong guess) and source-kind preference for
        // whole foods — a single ranking pass, no endpoint-local re-ranking on top of it.
        var ranked = ranker.Rank(allCandidates, query, boostIds, 20);

        // Persist canonical identity only for the candidates actually being returned to the
        // user — not every raw external candidate the providers returned. FoodProductPersistence
        // resolves existing identity (barcode -> source+externalId -> name+brand) first, so a
        // product already seen under a different query reuses its row instead of duplicating.
        var finalResults = new List<FoodProductDto>(ranked.Count);
        foreach (var dto in ranked)
        {
            if (dto.Id != Guid.Empty)
            {
                finalResults.Add(dto);
                continue;
            }

            var persistedId = await FoodProductPersistence.ResolveOrPersistAsync(dto, store);
            finalResults.Add(dto with { Id = persistedId });
        }

        // Only cache results that appear meaningful (at least 2 results or non-empty)
        // to avoid locking in degraded results from API timeouts
        if (finalResults.Count >= 2)
            await cache.SetAsync(cacheKey, finalResults, TimeSpan.FromMinutes(3));
        else if (finalResults.Count > 0)
            await cache.SetAsync(cacheKey, finalResults, TimeSpan.FromSeconds(30));

        return Results.Ok(finalResults);
    }


    static async Task<FoodProductDto?> GetResolvedFoodProductDtoAsync(Guid id, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator? foodApi = null, IOfflineFoodDatabase? offlineDb = null)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product != null)
        {
            // Enrich OFF products that are missing ingredients via barcode lookup.
            // Search-a-licious doesn't return ingredients_text, and many products lack
            // ingredients_tags — so we lazy-enrich on detail page view.
            if (product.DataSource == DataSources.OpenFoodFacts &&
                string.IsNullOrEmpty(product.Ingredients) &&
                !string.IsNullOrEmpty(product.Barcode))
            {
                await EnrichFromOffBarcodeAsync(product, offlineDb, foodApi, store);
            }

            var additives = await store.GetAllFoodAdditivesAsync();
            return MapToDto(product, additives);
        }

        var uidStr = user.FindFirstValue("sub");
        if (uidStr != null && Guid.TryParse(uidStr, out var uid))
        {
            var customFood = await store.GetCustomFoodAsync(uid, id);
            if (customFood != null)
                return MapCustomToFoodProductDto(customFood, await store.GetAllFoodAdditivesAsync());
        }

        return null;
    }

    static async Task EnrichFromOffBarcodeAsync(FoodProduct product, IOfflineFoodDatabase? offlineDb, IExternalFoodAggregator? foodApi, ITableStore store)
    {
        try
        {
            var enriched = await LookupOffProductAsync(product.Barcode!, offlineDb, foodApi);
            if (enriched is null) return;

            product.Ingredients = enriched.Ingredients ?? product.Ingredients;
            product.NovaGroup = enriched.NovaGroup ?? product.NovaGroup;
            product.NutriScore = enriched.NutriScore ?? product.NutriScore;
            product.ServingSize = enriched.ServingSize ?? product.ServingSize;
            product.ServingQuantity = enriched.ServingQuantity ?? product.ServingQuantity;
            product.ImageUrl = enriched.ImageUrl ?? product.ImageUrl;
            product.AllergensTags = enriched.AllergensTags.Length > 0 ? enriched.AllergensTags : product.AllergensTags;
            product.Calories100g = enriched.Calories100g ?? product.Calories100g;
            product.Protein100g = enriched.Protein100g ?? product.Protein100g;
            product.Carbs100g = enriched.Carbs100g ?? product.Carbs100g;
            product.Fat100g = enriched.Fat100g ?? product.Fat100g;
            product.Fiber100g = enriched.Fiber100g ?? product.Fiber100g;
            product.Sugar100g = enriched.Sugar100g ?? product.Sugar100g;
            product.SodiumMg100g = enriched.SodiumMg100g ?? product.SodiumMg100g;

            // Re-persist enriched data so subsequent views don't need another lookup
            await store.UpsertFoodProductAsync(product);
        }
        catch
        {
            // Silently degrade — the product will show "Ingredients unavailable"
            // and we'll try again on next view
        }
    }

    static async Task<FoodProductDto?> LookupOffProductAsync(string barcode, IOfflineFoodDatabase? offlineDb, IExternalFoodAggregator? foodApi)
    {
        // 1. Try offline database (Azure Table "offproducts", unlimited lookups)
        if (offlineDb is not null)
        {
            var result = await offlineDb.LookupByBarcodeAsync(barcode);
            if (result is not null)
                return result;
        }

        // 2. Fall back to barcode API (rate-limited to 12 req/min/IP)
        if (foodApi is not null)
            return await foodApi.LookupBarcodeAsync(barcode);

        return null;
    }

    static async Task<IResult> GetFoodProductByBarcode(string barcode, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb)
    {
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Length > 50)
            return Results.BadRequest(new { error = "Barcode must be between 1 and 50 characters" });

        var product = await store.GetFoodProductByBarcodeAsync(barcode);
        if (product is not null)
        {
            // Enrich OFF products that were cached from search without ingredients
            if (product.DataSource == DataSources.OpenFoodFacts &&
                string.IsNullOrEmpty(product.Ingredients))
            {
                await EnrichFromOffBarcodeAsync(product, offlineDb, foodApi, store);
            }

            var additives = await store.GetAllFoodAdditivesAsync();
            return Results.Ok(MapToDto(product, additives));
        }

        // Fallback to external APIs on local cache miss
        var externalDto = await foodApi.LookupBarcodeAsync(barcode);
        if (externalDto is null) return Results.NotFound();

        // Persist to local store so the product has a real GUID and can be favorited
        var newProduct = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = externalDto.Name,
            Barcode = externalDto.Barcode,
            Brand = externalDto.Brand,
            Ingredients = externalDto.Ingredients,
            NovaGroup = externalDto.NovaGroup,
            ServingSize = externalDto.ServingSize,
            NutritionInfo = externalDto.NutritionInfo,
            Calories100g = externalDto.Calories100g,
            Protein100g = externalDto.Protein100g,
            Carbs100g = externalDto.Carbs100g,
            Fat100g = externalDto.Fat100g,
            Fiber100g = externalDto.Fiber100g,
            Sugar100g = externalDto.Sugar100g,
            SodiumMg100g = externalDto.SodiumMg100g,
            DataSource = externalDto.DataSource,
            SourceUrl = externalDto.SourceUrl,
            ExternalId = externalDto.ExternalId,
            SourceVersion = externalDto.SourceVersion ?? externalDto.DataSource,
            LicenseType = externalDto.LicenseType ?? externalDto.DataSource switch
            {
                "USDA" => "USDA FoodData Central terms",
                "OpenFoodFacts" => "Open Food Facts ODbL",
                _ => null
            },
            Attribution = externalDto.Attribution ?? externalDto.DataSource,
            RetrievedAt = externalDto.RetrievedAt ?? DateTime.UtcNow,
            ImageUrl = externalDto.ImageUrl,
            NutriScore = externalDto.NutriScore,
            ServingQuantity = externalDto.ServingQuantity,
            AllergensTags = externalDto.AllergensTags,
            FoodKind = externalDto.FoodKind
        };
        await store.UpsertFoodProductAsync(newProduct);

        var allAdditives = await store.GetAllFoodAdditivesAsync();
        return Results.Ok(MapToDto(newProduct, allAdditives));
    }

    static async Task<IResult> GetFoodAdditives(ITableStore store)
    {
        var additives = await store.GetAllFoodAdditivesAsync();
        return Results.Ok(additives.OrderBy(a => a.Name).Select(a => new
        {
            id = a.Id,
            eNumber = a.ENumber,
            name = a.Name,
            category = a.Category,
            cspiRating = a.CspiRating.ToString(),
            safetyRating = a.SafetyRating.ToString(),
            usStatus = a.UsRegulatoryStatus.ToString(),
            euStatus = a.EuRegulatoryStatus.ToString(),
            healthConcerns = a.HealthConcerns,
            bannedInCountries = a.BannedInCountries,
            description = a.Description,
            alternateNames = a.AlternateNames,
            efsaAdiMgPerKgBw = a.EfsaAdiMgPerKgBw
        }));
    }

    static async Task<IResult> GetFoodAdditive(int id, ITableStore store)
    {
        var additive = await store.GetFoodAdditiveAsync(id);
        if (additive is null) return Results.NotFound();
        return Results.Ok(new
        {
            id = additive.Id,
            eNumber = additive.ENumber,
            name = additive.Name,
            category = additive.Category,
            cspiRating = additive.CspiRating.ToString(),
            safetyRating = additive.SafetyRating.ToString(),
            usStatus = additive.UsRegulatoryStatus.ToString(),
            euStatus = additive.EuRegulatoryStatus.ToString(),
            healthConcerns = additive.HealthConcerns,
            bannedInCountries = additive.BannedInCountries,
            description = additive.Description,
            alternateNames = additive.AlternateNames,
            efsaAdiMgPerKgBw = additive.EfsaAdiMgPerKgBw
        });
    }



    static async Task<IResult> GetFoodProduct(Guid id, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb)
    {
        var dto = await GetResolvedFoodProductDtoAsync(id, user, store, foodApi, offlineDb);
        return dto != null ? Results.Ok(dto) : Results.NotFound();
    }

    static async Task<IResult> GetSafetyReport(Guid id, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb, GutRiskService gutRiskService, FodmapService fodmapService, SubstitutionService substitutionService, GlycemicIndexService glycemicService)
    {
        var dto = await GetResolvedFoodProductDtoAsync(id, user, store, foodApi, offlineDb);
        if (dto is null) return Results.NotFound();

        var gutRisk = gutRiskService.Assess(dto);
        var fodmap = fodmapService.Assess(dto);


        return Results.Ok(new
        {
            product = dto,
            additives = dto.Additives,
            safetyScore = dto.SafetyScore,
            safetyRating = dto.SafetyRating,
            novaGroup = dto.NovaGroup,
            nutriScore = dto.NutriScore,
            gutRisk,
            fodmap,
            substitutions = substitutionService.GetSubstitutions(dto),
            glycemic = glycemicService.Assess(dto)
        });
    }

    static async Task<IResult> GetGutRisk(Guid id, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb, GutRiskService gutRiskService)
    {
        var dto = await GetResolvedFoodProductDtoAsync(id, user, store, foodApi, offlineDb);
        if (dto is null) return Results.NotFound();
        var result = gutRiskService.Assess(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetFodmap(Guid id, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb, FodmapService fodmapService)
    {
        var dto = await GetResolvedFoodProductDtoAsync(id, user, store, foodApi, offlineDb);
        if (dto is null) return Results.NotFound();
        var result = fodmapService.Assess(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetSubstitutions(Guid id, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb, SubstitutionService substitutionService)
    {
        var dto = await GetResolvedFoodProductDtoAsync(id, user, store, foodApi, offlineDb);
        if (dto is null) return Results.NotFound();
        var result = substitutionService.GetSubstitutions(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetGlycemic(Guid id, ClaimsPrincipal user, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb, GlycemicIndexService glycemicService)
    {
        var dto = await GetResolvedFoodProductDtoAsync(id, user, store, foodApi, offlineDb);
        if (dto is null) return Results.NotFound();
        var result = glycemicService.Assess(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetPersonalizedScore(Guid id, ClaimsPrincipal principal, ITableStore store, IExternalFoodAggregator foodApi, IOfflineFoodDatabase offlineDb, PersonalizedScoringService scoringService)
    {
        var dto = await GetResolvedFoodProductDtoAsync(id, principal, store, foodApi, offlineDb);
        if (dto is null) return Results.NotFound();
        var userId = Guid.Parse(principal.FindFirstValue("sub")!);
        var result = await scoringService.ScoreAsync(dto, userId, store);
        return Results.Ok(result);
    }

    static async Task<IResult> CreateFoodProduct(CreateFoodProductRequest request, ITableStore store)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 300)
            return Results.BadRequest(new { error = "Product name is required (max 300 characters)" });

        if (request.Brand is not null && request.Brand.Length > 200)
            return Results.BadRequest(new { error = "Brand must not exceed 200 characters" });

        if (request.Ingredients is not null && request.Ingredients.Length > 5000)
            return Results.BadRequest(new { error = "Ingredients must not exceed 5000 characters" });

        if (request.Barcode is not null && request.Barcode.Length > 50)
            return Results.BadRequest(new { error = "Barcode must not exceed 50 characters" });

        if (request.AdditiveIds.Count > 100)
            return Results.BadRequest(new { error = "Cannot have more than 100 additives" });

        var product = new FoodProduct
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Barcode = request.Barcode,
            NovaGroup = int.TryParse(request.NovaGroup, out var ng) ? ng : (int?)null,
            Brand = request.Brand,
            Ingredients = request.Ingredients,
            ServingSize = request.ServingSize,
            NutritionInfo = request.NutritionInfo,
            FoodProductAdditiveIds = request.AdditiveIds,
            FoodKind = Enum.TryParse<FoodKind>(request.FoodKind, true, out var fk) ? fk : FoodKind.Unknown,
            IsDeleted = false
        };
        await store.UpsertFoodProductAsync(product);
        return Results.Created($"/api/food/{product.Id}", MapToDto(product, await store.GetAllFoodAdditivesAsync()));
    }

    static async Task<IResult> UpdateFoodProduct(Guid id, UpdateFoodProductRequest request, ITableStore store)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 300)
            return Results.BadRequest(new { error = "Product name is required (max 300 characters)" });

        if (request.Brand is not null && request.Brand.Length > 200)
            return Results.BadRequest(new { error = "Brand must not exceed 200 characters" });

        if (request.Ingredients is not null && request.Ingredients.Length > 5000)
            return Results.BadRequest(new { error = "Ingredients must not exceed 5000 characters" });

        if (request.Barcode is not null && request.Barcode.Length > 50)
            return Results.BadRequest(new { error = "Barcode must not exceed 50 characters" });

        if (request.AdditiveIds.Count > 100)
            return Results.BadRequest(new { error = "Cannot have more than 100 additives" });

        product.Name = request.Name;
        product.Barcode = request.Barcode;
        product.NovaGroup = int.TryParse(request.NovaGroup, out var ng) ? ng : product.NovaGroup;
        product.Brand = request.Brand;
        product.Ingredients = request.Ingredients;
        product.ServingSize = request.ServingSize;
        product.NutritionInfo = request.NutritionInfo;
        product.FoodProductAdditiveIds = request.AdditiveIds;
        if (Enum.TryParse<FoodKind>(request.FoodKind, true, out var fk))
            product.FoodKind = fk;
        await store.UpsertFoodProductAsync(product);
        return Results.Ok(MapToDto(product, await store.GetAllFoodAdditivesAsync()));
    }

    static async Task<IResult> DeleteFoodProduct(Guid id, ITableStore store)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        product.IsDeleted = true;
        await store.UpsertFoodProductAsync(product);
        return Results.NoContent();
    }

    static FoodProductDto MapToDto(FoodProduct f, IEnumerable<FoodAdditive> additives)
    {
        var additiveDtos = (f.FoodProductAdditiveIds ?? []).Select(additiveId =>
        {
            var a = additives.FirstOrDefault(x => x.Id == additiveId);
            return new FoodAdditiveDto
            {
                Id = a?.Id ?? additiveId,
                Name = a?.Name ?? "Unknown",
                CspiRating = a?.CspiRating.ToString() ?? "Unknown",
                UsRegulatoryStatus = a?.UsRegulatoryStatus.ToString() ?? "Unknown",
                EuRegulatoryStatus = a?.EuRegulatoryStatus.ToString() ?? "Unknown",
                SafetyRating = a?.SafetyRating.ToString() ?? "Unknown",
                Category = a?.Category ?? "Unknown",
                ENumber = a?.ENumber,
                HealthConcerns = a?.HealthConcerns ?? "",
                BannedInCountries = a?.BannedInCountries ?? [],
                Description = a?.Description,
                AlternateNames = a?.AlternateNames ?? [],
                EfsaAdiMgPerKgBw = a?.EfsaAdiMgPerKgBw,
                EfsaLastReviewDate = a?.EfsaLastReviewDate,
                EpaCancerClass = a?.EpaCancerClass,
                FdaAdverseEventCount = a?.FdaAdverseEventCount,
                FdaRecallCount = a?.FdaRecallCount,
                EvidenceSources = a?.EvidenceSources ?? [],
                LastUpdated = a?.LastUpdated
            };
        }).ToList();

        return new FoodProductDto
        {
            Id = f.Id,
            Name = f.Name,
            Barcode = f.Barcode,
            NovaGroup = f.NovaGroup,
            Brand = f.Brand,
            Ingredients = f.Ingredients,
            ServingSize = f.ServingSize,
            NutritionInfo = f.NutritionInfo,
            Additives = additiveDtos,
            IsDeleted = f.IsDeleted,
            SafetyRating = f.SafetyRating?.ToString(),
            SafetyScore = f.SafetyScore,
            AllergensTags = f.AllergensTags,
            Calories100g = f.Calories100g,
            Protein100g = f.Protein100g,
            Carbs100g = f.Carbs100g,
            Fat100g = f.Fat100g,
            Fiber100g = f.Fiber100g,
            Sugar100g = f.Sugar100g,
            SodiumMg100g = f.SodiumMg100g,
            DataSource = f.DataSource,
            FoodKind = f.FoodKind,
            SourceUrl = f.SourceUrl,
            ExternalId = f.ExternalId,
            SourceVersion = f.SourceVersion,
            LicenseType = f.LicenseType,
            Attribution = f.Attribution,
            RetrievedAt = f.RetrievedAt,
            ImageUrl = f.ImageUrl,
            NutriScore = f.NutriScore,
            ServingQuantity = f.ServingQuantity,
            AdditivesTags = additiveDtos.Where(a => a.ENumber != null).Select(a => $"en:{a.ENumber!.ToLowerInvariant()}").ToList(),
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Favorite Foods
    // ═══════════════════════════════════════════════════════════

    static async Task<IResult> GetFavoriteFoods(ClaimsPrincipal principal, ITableStore store)
    {
        var userId = Guid.Parse(principal.FindFirstValue("sub")!);
        var favorites = await store.GetUserFavoriteFoodsAsync(userId);
        var results = new List<object>();
        foreach (var fav in favorites.OrderByDescending(f => f.CreatedAt))
        {
            var product = await store.GetFoodProductAsync(fav.FoodProductId);
            if (product is null || product.IsDeleted) continue;
            results.Add(new
            {
                foodProductId = product.Id,
                foodName = product.Name,
                brand = product.Brand,
                calories100g = product.Calories100g,
                protein100g = product.Protein100g,
                carbs100g = product.Carbs100g,
                fat100g = product.Fat100g,
                servingSize = product.ServingSize,
                servingQuantity = product.ServingQuantity,
                servingWeightG = product.ServingQuantity,
                imageUrl = product.ImageUrl,
                createdAt = fav.CreatedAt
            });
        }
        return Results.Ok(results);
    }

    static async Task<IResult> AddFavoriteFood(Guid id, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = Guid.Parse(principal.FindFirstValue("sub")!);
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var existing = await store.GetUserFavoriteFoodAsync(userId, id);
        if (existing is not null) return Results.Ok(new { message = "Already favorited" });
        var favorite = new FavoriteFoodProduct
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FoodProductId = id,
            CreatedAt = DateTime.UtcNow
        };
        await store.UpsertFavoriteFoodAsync(favorite);
        return Results.Created($"/api/food/{id}/favorite", new { message = "Food favorited" });
    }

    static async Task<IResult> RemoveFavoriteFood(Guid id, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = Guid.Parse(principal.FindFirstValue("sub")!);
        await store.DeleteFavoriteFoodAsync(userId, id);
        return Results.NoContent();
    }
    static FoodProductDto MapCustomToFoodProductDto(CustomFood customFood, IEnumerable<FoodAdditive>? additives = null)
    {
        var ratio = customFood.ServingSize > 0 ? (100m / customFood.ServingSize) : 1m;
        var f = new FoodProduct
        {
            Id = customFood.Id,
            Name = customFood.Name,
            Brand = customFood.BrandName,
            Ingredients = customFood.Ingredients,
            ServingSize = customFood.ServingSizeUnit != null ? $"{customFood.ServingSize}{customFood.ServingSizeUnit}" : $"{customFood.ServingSize}g",
            ServingQuantity = customFood.ServingSize,
            Calories100g = Math.Round(customFood.Calories * ratio, 2),
            Protein100g = Math.Round(customFood.ProteinG * ratio, 2),
            Carbs100g = Math.Round(customFood.CarbG * ratio, 2),
            Fat100g = Math.Round(customFood.FatG * ratio, 2),
            Fiber100g = customFood.FiberG.HasValue ? Math.Round(customFood.FiberG.Value * ratio, 2) : null,
            Sugar100g = customFood.SugarG.HasValue ? Math.Round(customFood.SugarG.Value * ratio, 2) : null,
            SodiumMg100g = customFood.SodiumMg.HasValue ? Math.Round(customFood.SodiumMg.Value * ratio, 2) : null,
            FoodKind = GutAI.Domain.Enums.FoodKind.Unknown,
            DataSource = "Custom"
        };
        return MapToDto(f, additives ?? Array.Empty<FoodAdditive>());
    }

    static async Task<IResult> GetCustomFoods(ClaimsPrincipal user, ITableStore store)
    {
        if (Guid.TryParse(user.FindFirstValue("sub"), out var uid))
        {
            var items = await store.GetCustomFoodsAsync(uid);
            var results = items.Select(c => MapCustomToFoodProductDto(c));
            return Results.Ok(results);
        }
        return Results.Unauthorized();
    }

    static string? ValidateCustomFoodDto(CustomFoodDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 300)
            return "Name is required and must not exceed 300 characters.";
        if (dto.ServingSize <= 0 || dto.ServingSize > 10000)
            return "Serving size must be greater than 0 and not exceed 10000.";
        if (dto.Calories < 0 || dto.ProteinG < 0 || dto.CarbG < 0 || dto.FatG < 0
            || dto.FiberG < 0 || dto.SugarG < 0 || dto.SodiumMg < 0)
            return "Nutrition values cannot be negative.";
        if (dto.Calories > MealValidation.MaxCalories || dto.ProteinG > MealValidation.MaxMacroG
            || dto.CarbG > MealValidation.MaxMacroG || dto.FatG > MealValidation.MaxMacroG)
            return "Nutrition values are unrealistically high.";
        if (dto.Ingredients is { Length: > 2000 })
            return "Ingredients must not exceed 2000 characters.";
        return null;
    }

    static async Task<IResult> CreateCustomFood(CustomFoodDto dto, ClaimsPrincipal user, ITableStore store)
    {
        var validationError = ValidateCustomFoodDto(dto);
        if (validationError is not null)
            return Results.BadRequest(new { error = validationError });

        var uid = Guid.Parse(user.FindFirstValue("sub")!);
        var customFood = new CustomFood
        {
            Id = Guid.NewGuid(),
            UserId = uid,
            Name = dto.Name,
            BrandName = dto.BrandName,
            ServingSize = dto.ServingSize,
            ServingSizeUnit = dto.ServingSizeUnit,
            Calories = dto.Calories,
            ProteinG = dto.ProteinG,
            CarbG = dto.CarbG,
            FatG = dto.FatG,
            FiberG = dto.FiberG,
            SugarG = dto.SugarG,
            SodiumMg = dto.SodiumMg,
            Ingredients = dto.Ingredients,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await store.UpsertCustomFoodAsync(customFood);
        return Results.Created($"/api/food/custom/{customFood.Id}", customFood);
    }

    static async Task<IResult> UpdateCustomFood(Guid id, CustomFoodDto dto, ClaimsPrincipal user, ITableStore store)
    {
        var validationError = ValidateCustomFoodDto(dto);
        if (validationError is not null)
            return Results.BadRequest(new { error = validationError });

        var uid = Guid.Parse(user.FindFirstValue("sub")!);
        var existing = await store.GetCustomFoodAsync(uid, id);
        if (existing == null)
            return Results.NotFound();

        existing.Name = dto.Name;
        existing.BrandName = dto.BrandName;
        existing.ServingSize = dto.ServingSize;
        existing.ServingSizeUnit = dto.ServingSizeUnit;
        existing.Calories = dto.Calories;
        existing.ProteinG = dto.ProteinG;
        existing.CarbG = dto.CarbG;
        existing.FatG = dto.FatG;
        existing.FiberG = dto.FiberG;
        existing.SugarG = dto.SugarG;
        existing.SodiumMg = dto.SodiumMg;
        existing.Ingredients = dto.Ingredients;
        existing.UpdatedAt = DateTime.UtcNow;

        await store.UpsertCustomFoodAsync(existing);
        return Results.Ok(existing);
    }

    static async Task<IResult> DeleteCustomFood(Guid id, ClaimsPrincipal user, ITableStore store)
    {
        var uid = Guid.Parse(user.FindFirstValue("sub")!);
        var existing = await store.GetCustomFoodAsync(uid, id);
        if (existing == null)
            return Results.NotFound();

        await store.DeleteCustomFoodAsync(uid, id);
        return Results.NoContent();
    }

    static async Task<IResult> DescribeFoodFromText(DescribeCustomFoodRequest request, IContentUnderstandingService aiService, ILogger<Program> logger, CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Results.BadRequest(new { error = "Description is required." });
        }

        if (text.Length < 8)
        {
            return Results.BadRequest(new { error = "Description must be at least 8 characters." });
        }

        if (text.Length > 2000)
        {
            return Results.BadRequest(new { error = "Description must not exceed 2000 characters." });
        }

        var result = await aiService.DescribeFoodFromTextAsync(text, ct);
        if (result is null)
        {
            logger.LogWarning("AI food description returned no usable data for prompt '{Prompt}'.", text[..Math.Min(text.Length, 120)]);
            return Results.BadRequest(new { error = "Could not generate food details from that description." });
        }

        return Results.Ok(result);
    }

    [Microsoft.AspNetCore.Mvc.RequestSizeLimit(20_000_000)] // 20 MB — generous for a phone photo, bounded against abuse
    static async Task<IResult> ParseNutritionLabel(Microsoft.AspNetCore.Http.HttpRequest request, ClaimsPrincipal principal, ITableStore store, IContentUnderstandingService aiService, ILogger<Program> logger)
    {
        try
        {
            var uid = Guid.Parse(principal.FindFirstValue("sub")!);
            var user = await store.GetUserAsync(uid);

            if (!request.HasFormContentType || !request.Form.Files.Any())
            {
                logger.LogWarning("ParseNutritionLabel called without multipart form content or files.");
                return Results.BadRequest("No image provided.");
            }

            var file = request.Form.Files.GetFile("file") ?? request.Form.Files.FirstOrDefault();

            if (file == null || file.Length == 0)
            {
                logger.LogWarning("ParseNutritionLabel received an empty file from user {UserId}.", uid);
                return Results.BadRequest("No image provided.");
            }

            const long MaxUploadBytes = 20_000_000;
            if (file.Length > MaxUploadBytes)
            {
                logger.LogWarning("ParseNutritionLabel rejected oversized file {FileName} ({Size} bytes) from user {UserId}.", file.FileName, file.Length, uid);
                return Results.BadRequest("Image is too large. Please use a photo under 20MB.");
            }

            logger.LogInformation("Processing nutrition label image {FileName} of original size {Size} bytes for user {UserId}.", file.FileName, file.Length, uid);

            using var originalStream = file.OpenReadStream();
            using var preprocessed = await NutritionLabelImagePreprocessor.PreprocessAsync(originalStream);

            var reduction = file.Length > 0
                ? 100.0 * (1.0 - (double)preprocessed.OutputBytes / file.Length)
                : 0.0;

            logger.LogInformation(
                "Successfully preprocessed image from {OriginalSize} bytes to {ProcessedSize} bytes ({Reduction:F1}% reduction) in {ElapsedMs} ms.",
                file.Length,
                preprocessed.OutputBytes,
                reduction,
                preprocessed.ElapsedMilliseconds);

            var result = await aiService.ParseNutritionLabelAsync(preprocessed.Stream, preprocessed.ContentType);

            if (result == null)
            {
                logger.LogWarning("AI content understanding could not extract data for file {FileName}, user {UserId}.", file.FileName, uid);
                return Results.BadRequest("Could not parse nutrition label from image.");
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while parsing the nutrition label.");
            return Results.Problem("An error occurred while processing your image. Please try again or use a lower resolution image.", statusCode: 500);
        }
    }
}
