using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Logging;

public static class FoodEndpoints
{
    public static RouteGroupBuilder MapFoodEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/search", SearchFoodProducts);
        group.MapGet("/barcode/{barcode}", GetFoodProductByBarcode);
        group.MapGet("/additives", GetFoodAdditives);
        group.MapGet("/additives/{id:int}", GetFoodAdditive);
        group.MapGet("/{id:guid}", GetFoodProduct);
        group.MapGet("/{id:guid}/safety-report", GetSafetyReport);
        group.MapGet("/{id:guid}/gut-risk", GetGutRisk);
        group.MapGet("/{id:guid}/fodmap", GetFodmap);
        group.MapGet("/{id:guid}/substitutions", GetSubstitutions);
        group.MapGet("/{id:guid}/glycemic", GetGlycemic);
        group.MapGet("/{id:guid}/personalized-score", GetPersonalizedScore);
        group.MapPost("/", CreateFoodProduct);
        group.MapPut("/{id:guid}", UpdateFoodProduct);
        group.MapDelete("/{id:guid}", DeleteFoodProduct);
        return group;
    }

    static async Task<IResult> SearchFoodProducts(string? q, ITableStore store, IFoodApiService foodApi, ICacheService cache, ILogger<Program> logger)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length < 2)
            return Results.Ok(Array.Empty<FoodProductDto>());

        var cacheKey = $"food-search:{query.ToLowerInvariant()}";
        var cached = await cache.GetAsync<List<FoodProductDto>>(cacheKey);
        if (cached is not null)
            return Results.Ok(cached);

        var localTask = store.SearchFoodProductsAsync(query, 20, default);
        var additivesTask = store.GetAllFoodAdditivesAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var externalTask = foodApi.SearchAsync(query, cts.Token);

        try
        {
            await Task.WhenAll(localTask, additivesTask, externalTask);
        }
        catch
        {
        }

        var localResults = await localTask;
        var additives = await additivesTask;

        List<FoodProductDto> externalResults = [];
        if (externalTask.IsCompletedSuccessfully)
        {
            externalResults = externalTask.Result;
        }
        cts.Dispose();

        var existingNames = localResults.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newProducts = new List<FoodProduct>();

        foreach (var dto in externalResults)
        {
            if (existingNames.Contains(dto.Name))
                continue;

            var product = new FoodProduct
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                Barcode = dto.Barcode,
                Brand = dto.Brand,
                Ingredients = dto.Ingredients,
                NovaGroup = dto.NovaGroup,
                ServingSize = dto.ServingSize,
                NutritionInfo = dto.NutritionInfo,
                Calories100g = dto.Calories100g,
                Protein100g = dto.Protein100g,
                Carbs100g = dto.Carbs100g,
                Fat100g = dto.Fat100g,
                Fiber100g = dto.Fiber100g,
                Sugar100g = dto.Sugar100g,
                Sodium100g = dto.Sodium100g,
                DataSource = dto.DataSource,
                ExternalId = dto.ExternalId,
                ImageUrl = dto.ImageUrl,
                NutriScore = dto.NutriScore,
                ServingQuantity = dto.ServingQuantity,
                AllergensTags = dto.AllergensTags,
            };
            newProducts.Add(product);
            existingNames.Add(dto.Name);
        }

        // Batch upserts in parallel
        if (newProducts.Count > 0)
            await Task.WhenAll(newProducts.Select(p => store.UpsertFoodProductAsync(p)));

        // Convert local DB results to DTOs and merge with already-ranked external results
        var localDtos = localResults.Concat(newProducts)
            .Select(f => MapToDto(f, additives))
            .ToList();

        // Combine local + external into Lucene for unified ranking
        var allCandidates = new List<FoodProductDto>(localDtos.Count + externalResults.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in localDtos)
            if (seen.Add(dto.Name))
                allCandidates.Add(dto);
        foreach (var dto in externalResults)
            if (seen.Add(dto.Name))
                allCandidates.Add(dto);

        List<FoodProductDto> finalResults;
        if (allCandidates.Count > 1)
        {
            using var rankIndex = new FoodSearchIndex(allCandidates);
            finalResults = rankIndex.Search(query, 20);
        }
        else
        {
            finalResults = allCandidates.Take(20).ToList();
        }

        await cache.SetAsync(cacheKey, finalResults, TimeSpan.FromMinutes(10));

        return Results.Ok(finalResults);
    }

    static async Task<IResult> GetFoodProductByBarcode(string barcode, ITableStore store)
    {
        var product = await store.GetFoodProductByBarcodeAsync(barcode);
        if (product is null) return Results.NotFound();
        var additives = await store.GetAllFoodAdditivesAsync();
        return Results.Ok(MapToDto(product, additives));
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

    static async Task<IResult> GetFoodProduct(Guid id, ITableStore store)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var additives = await store.GetAllFoodAdditivesAsync();
        return Results.Ok(MapToDto(product, additives));
    }

    static async Task<IResult> GetSafetyReport(Guid id, ITableStore store, GutRiskService gutRiskService, FodmapService fodmapService, SubstitutionService substitutionService, GlycemicIndexService glycemicService)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var additives = await store.GetAllFoodAdditivesAsync();
        var dto = MapToDto(product, additives);
        return Results.Ok(new
        {
            product = dto,
            additives = dto.Additives,
            safetyScore = dto.SafetyScore,
            safetyRating = dto.SafetyRating,
            novaGroup = dto.NovaGroup,
            nutriScore = dto.NutriScore,
            gutRisk = gutRiskService.Assess(dto),
            fodmap = fodmapService.Assess(dto),
            substitutions = substitutionService.GetSubstitutions(dto),
            glycemic = glycemicService.Assess(dto)
        });
    }

    static async Task<IResult> GetGutRisk(Guid id, ITableStore store, GutRiskService gutRiskService)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var additives = await store.GetAllFoodAdditivesAsync();
        var dto = MapToDto(product, additives);
        var result = gutRiskService.Assess(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetFodmap(Guid id, ITableStore store, FodmapService fodmapService)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var additives = await store.GetAllFoodAdditivesAsync();
        var dto = MapToDto(product, additives);
        var result = fodmapService.Assess(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetSubstitutions(Guid id, ITableStore store, SubstitutionService substitutionService)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var additives = await store.GetAllFoodAdditivesAsync();
        var dto = MapToDto(product, additives);
        var result = substitutionService.GetSubstitutions(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetGlycemic(Guid id, ITableStore store, GlycemicIndexService glycemicService)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var additives = await store.GetAllFoodAdditivesAsync();
        var dto = MapToDto(product, additives);
        var result = glycemicService.Assess(dto);
        return Results.Ok(result);
    }

    static async Task<IResult> GetPersonalizedScore(Guid id, ClaimsPrincipal principal, ITableStore store, PersonalizedScoringService scoringService)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        var userId = Guid.Parse(principal.FindFirstValue("sub")!);
        var additives = await store.GetAllFoodAdditivesAsync();
        var dto = MapToDto(product, additives);
        var result = await scoringService.ScoreAsync(dto, userId, store);
        return Results.Ok(result);
    }

    static async Task<IResult> CreateFoodProduct(CreateFoodProductRequest request, ITableStore store)
    {
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
            IsDeleted = false
        };
        await store.UpsertFoodProductAsync(product);
        return Results.Created($"/api/food/{product.Id}", MapToDto(product, await store.GetAllFoodAdditivesAsync()));
    }

    static async Task<IResult> UpdateFoodProduct(Guid id, UpdateFoodProductRequest request, ITableStore store)
    {
        var product = await store.GetFoodProductAsync(id);
        if (product is null) return Results.NotFound();
        product.Name = request.Name;
        product.Barcode = request.Barcode;
        product.NovaGroup = int.TryParse(request.NovaGroup, out var ng) ? ng : product.NovaGroup;
        product.Brand = request.Brand;
        product.Ingredients = request.Ingredients;
        product.ServingSize = request.ServingSize;
        product.NutritionInfo = request.NutritionInfo;
        product.FoodProductAdditiveIds = request.AdditiveIds;
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
            Sodium100g = f.Sodium100g,
            DataSource = f.DataSource,
            ExternalId = f.ExternalId,
            ImageUrl = f.ImageUrl,
            NutriScore = f.NutriScore,
            ServingQuantity = f.ServingQuantity,
            AdditivesTags = additiveDtos.Where(a => a.ENumber != null).Select(a => $"en:{a.ENumber!.ToLowerInvariant()}").ToList(),
        };
    }
}
