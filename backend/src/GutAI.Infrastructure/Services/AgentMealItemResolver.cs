using Microsoft.Extensions.Logging;

using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;

namespace GutAI.Infrastructure.Services;

/// <summary>Builds grounded draft items for Coach and MCP without committing meals.</summary>
public sealed class AgentMealItemResolver(
    ITableStore store,
    IFoodSearchService foodSearch,
    INutritionApiService nutritionApi,
    ILogger<AgentMealItemResolver> logger) : IAgentMealItemResolver
{
    public async Task<IReadOnlyList<MealDraftItemDto>> ResolveAsync(
        IReadOnlyList<AgentMealItemInput> items,
        string? fallbackDescription = null,
        FoodRegion region = FoodRegion.Default,
        CancellationToken ct = default)
    {
        var resolved = new List<MealDraftItemDto>(items.Count);
        foreach (var input in items)
        {
            ct.ThrowIfCancellationRequested();
            var servings = MealValidation.ClampServings(input.Servings);
            var product = input.FoodProductId is { } id
                ? await store.GetFoodProductAsync(id, ct)
                : null;

            if (product is not null)
            {
                resolved.Add(FromProduct(input, product, servings));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(input.Name))
            {
                resolved.AddRange(await ResolveNameAsync(input, servings, region, ct));
            }
        }

        if (resolved.Count == 0 && !string.IsNullOrWhiteSpace(fallbackDescription))
            resolved.AddRange(await ParseNaturalLanguageAsync(fallbackDescription, 1m, region, ct));

        return resolved;
    }

    private MealDraftItemDto FromProduct(AgentMealItemInput input, FoodProduct product, decimal servings)
    {
        var (perServing, confidence) = GetServing(input.ServingWeightG, product.ServingQuantity, input.Name ?? product.Name);
        var grams = Math.Min(servings * perServing, 5000m);
        var basis = NutritionCalculator.BasisFrom(product);
        var source = GroundingAttempts.MapSource(product.DataSource);
        // Without search-time confidence, an unverified model-supplied id defaults to 0.5,
        // below the association engine's 0.6 trust threshold.
        return CreateItem(
            input.Name ?? product.Name,
            product.Name,
            product.Id,
            source,
            product.SourceUrl,
            grams,
            confidence,
            basis,
            basis is null ? nameof(NutritionProvenance.Unknown) : nameof(NutritionProvenance.Sourced),
            input.MatchConfidence ?? 0.5m);
    }

    private async Task<IReadOnlyList<MealDraftItemDto>> ResolveNameAsync(AgentMealItemInput input, decimal servings, FoodRegion region, CancellationToken ct)
    {
        var name = input.Name!;
        var query = QuerySanitizer.Sanitize(name);
        var resolution = await foodSearch.ResolveAsync(query, [], ct);
        var decision = GroundingPolicy.Decide(resolution);
        var candidates = GroundingAttempts.ToCandidates(decision.Candidates);

        if (decision.AutoSelected && decision.Selected is { } selected)
        {
            Guid? productId = null;
            try
            {
                productId = await FoodProductPersistence.ResolveOrPersistAsync(selected, store, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not persist resolved agent food candidate '{FoodName}'", selected.Name);
            }

            var (perServing, portionConfidence) = GetServing(input.ServingWeightG, selected.ServingQuantity, name);
            var basis = NutritionCalculator.BasisFrom(selected);
            var grams = Math.Min(servings * perServing, 5000m);
            return [CreateItem(
                name,
                selected.Name,
                productId,
                GroundingAttempts.MapSource(selected.DataSource),
                selected.SourceUrl,
                grams,
                portionConfidence,
                basis,
                basis is null ? nameof(NutritionProvenance.Unknown) : nameof(NutritionProvenance.Sourced),
                resolution.MatchConfidence,
                grounding: GroundingAttempts.MakeGrounding(query, resolution, true, productId, selected.Name, candidates))];
        }

        if (candidates.Count > 0)
        {
            var (perServing, portionConfidence) = GetServing(input.ServingWeightG, null, name);
            return [CreateItem(
                name, null, null, "ai", null, Math.Min(servings * perServing, 5000m), portionConfidence,
                null, nameof(NutritionProvenance.Unknown), resolution.MatchConfidence,
                needsChoice: true,
                candidateNames: candidates.Select(candidate => candidate.Name).ToArray(),
                grounding: GroundingAttempts.MakeGrounding(query, resolution, false, null, null, candidates))];
        }

        var parsed = await nutritionApi.ParseNaturalLanguageAsync(name, region, ct);
        return MapParsed(parsed, servings);
    }

    private async Task<IReadOnlyList<MealDraftItemDto>> ParseNaturalLanguageAsync(string text, decimal servings, FoodRegion region, CancellationToken ct)
    {
        var parsed = await nutritionApi.ParseNaturalLanguageAsync(text, region, ct);
        return MapParsed(parsed, servings);
    }

    private static IReadOnlyList<MealDraftItemDto> MapParsed(IEnumerable<ParsedFoodItemDto> parsed, decimal servings) =>
        parsed.Select(item =>
        {
            var basis = item.Per100g;
            var grams = Math.Min(Math.Max(0m, item.ServingWeightG) * servings, 5000m);
            var provenance = item.NutritionProvenance;
            var source = NutritionProvenanceRules.TryParse(provenance, out var parsedProvenance)
                ? parsedProvenance switch
                {
                    NutritionProvenance.Sourced => "db",
                    NutritionProvenance.Web => "web",
                    NutritionProvenance.Estimated or NutritionProvenance.ModelEstimated => "estimate",
                    _ => "estimate",
                }
                : "estimate";
            return CreateItem(
                item.Name,
                item.Name,
                item.FoodProductId,
                source,
                null,
                grams,
                item.PortionConfidence,
                basis,
                basis is null ? nameof(NutritionProvenance.Unknown) : provenance,
                item.MatchConfidence,
                needsChoice: item.NeedsChoice,
                grounding: item.Grounding);
        }).ToArray();

    private static MealDraftItemDto CreateItem(
        string name,
        string? canonicalName,
        Guid? productId,
        string source,
        string? sourceUrl,
        decimal grams,
        decimal portionConfidence,
        NutritionPer100gDto? basis,
        string provenance,
        decimal matchConfidence,
        bool needsChoice = false,
        IReadOnlyList<string>? candidateNames = null,
        GroundingAttemptDto? grounding = null)
    {
        var amounts = basis is null ? null : NutritionCalculator.Compute(basis, grams);
        return new MealDraftItemDto
        {
            ItemId = Guid.NewGuid(),
            Name = name,
            CanonicalName = canonicalName,
            FoodProductId = productId,
            Source = source,
            SourceUrl = sourceUrl,
            Grams = grams,
            PortionMethod = "agent_estimate",
            PortionConfidence = portionConfidence,
            IncludedByDefault = true,
            NeedsChoice = needsChoice,
            Per100g = basis,
            NutritionProvenance = provenance,
            Calories = amounts?.Calories,
            ProteinG = amounts?.ProteinG,
            CarbsG = amounts?.CarbsG,
            FatG = amounts?.FatG,
            FiberG = amounts?.FiberG,
            SugarG = amounts?.SugarG,
            SodiumMg = amounts?.SodiumMg,
            MatchConfidence = matchConfidence,
            VisionConfidence = null,
            CandidateNames = candidateNames,
            Grounding = grounding,
        };
    }

    private static (decimal Grams, decimal Confidence) GetServing(decimal? explicitWeight, decimal? productServing, string name)
    {
        if (explicitWeight is > 0) return (Math.Min(explicitWeight.Value, 2000m), 0.6m);
        if (productServing is > 0) return (productServing.Value, 0.5m);
        return (ServingEstimator.EstimateDefaultServingG(name), 0.3m);
    }


}
