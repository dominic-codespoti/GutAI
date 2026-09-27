using Microsoft.Extensions.Logging;

using System.Text.Json;
using System.Text.Json.Serialization;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace GutAI.Infrastructure.Services;

public sealed class MealDraftService(
    ITableStore store,
    IConfiguration config,
    TimeProvider time,
    ILogger<MealDraftService> logger) : IMealDraftService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<MealDraftDto> CreateAsync(Guid userId, MealDraftCreateRequest request, CancellationToken ct = default)
    {
        var mealType = ResolveMealType(request.MealType);
        var now = time.GetUtcNow();
        var items = request.Items.Select(i => Normalize(i with
        {
            ItemId = i.ItemId == Guid.Empty ? Guid.NewGuid() : i.ItemId,
            Grounding = i.Grounding is null ? null : i.Grounding with { Candidates = i.Grounding.Candidates.Take(3).ToList() },
            CandidateNames = i.CandidateNames?.Take(3).ToList()
        })).ToList();
        var json = JsonSerializer.Serialize(items, JsonOptions);
        if (json.Length > 32_000) throw Validation("Draft items exceed the storage limit");
        var record = new MealDraftRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Origin = request.Origin,
            Status = MealDraftStatuses.PendingReview,
            MealType = request.MealType is null ? null : mealType.ToString(),
            LoggedAt = request.LoggedAt,
            ItemsJson = json,
            Warnings = request.Warnings,
            ReferenceObjectVisible = request.ReferenceObjectVisible,
            OverallConfidence = request.OverallConfidence,
            RawModelJson = request.RawModelJson is { Length: > 30_000 } raw ? raw[..30_000] : request.RawModelJson,
            PromptVersion = request.PromptVersion,
            ModelDeployment = request.ModelDeployment,
            CalibrationVersion = request.CalibrationVersion,
            CreatedAt = now,
            ExpiresAt = now.AddHours(config.GetValue("MealDrafts:TtlHours", 24))
        };
        await store.UpsertMealDraftAsync(record, ct);
        return ToDto(record, items);
    }

    public async Task<MealDraftDto?> GetAsync(Guid userId, Guid draftId, CancellationToken ct = default)
    {
        var record = await store.GetMealDraftAsync(userId, draftId, ct);
        if (record is null || record.Status != MealDraftStatuses.PendingReview) return null;
        if (record.ExpiresAt <= time.GetUtcNow())
        {
            try { await store.TryReplaceMealDraftAsync(record with { Status = MealDraftStatuses.Expired }, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not mark expired meal draft {DraftId}", draftId); }
            return null;
        }
        return ToDto(record, Deserialize(record.ItemsJson));
    }

    public async Task<IReadOnlyList<MealDraftDto>> ListPendingAsync(Guid userId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var records = await store.GetMealDraftsByStatusAsync(userId, MealDraftStatuses.PendingReview, ct);
        var active = records.Where(r => r.ExpiresAt > now).OrderByDescending(r => r.CreatedAt).ToList();
        foreach (var expired in records.Where(r => r.ExpiresAt <= now))
        {
            try { await store.TryReplaceMealDraftAsync(expired with { Status = MealDraftStatuses.Expired }, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not mark expired meal draft {DraftId}", expired.Id); }
        }
        return active.Select(r => ToDto(r, Deserialize(r.ItemsJson))).ToList();
    }

    public async Task<MealDraftDto> UpdateAsync(Guid userId, Guid draftId, MealDraftUpdateRequest request, CancellationToken ct = default)
    {
        ValidateMealType(request.MealType);
        var record = await RequirePending(userId, draftId, ct);
        var items = Deserialize(record.ItemsJson);
        var updates = await ResolveItems(items, request.Items, false, true, ct);
        var updated = items.Select(item => updates.TryGetValue(item.ItemId, out var value) ? value.Item : item).ToList();
        var saved = record with { ItemsJson = SerializeChecked(updated), MealType = request.MealType is null ? record.MealType : ResolveMealType(request.MealType).ToString(), LoggedAt = request.LoggedAt ?? record.LoggedAt };
        var etag = await store.TryReplaceMealDraftAsync(saved, ct);
        if (etag is null) throw new MealDraftException(MealDraftErrorCode.NotPending, "Draft changed while it was being updated");
        saved = saved with { ETag = etag };
        return ToDto(saved, updated);
    }

    public async Task<MealDraftCommitResult> CommitAsync(Guid userId, Guid draftId, MealDraftCommitRequest? request, MealDraftCommitGuard? guard = null, CancellationToken ct = default)
    {
        ValidateMealType(request?.MealType);
        var record = await RequirePending(userId, draftId, ct);
        if (guard?.RequiredOrigin is { } required && record.Origin != required) throw new MealDraftException(MealDraftErrorCode.OriginMismatch, "Draft origin does not match the required origin");
        if (guard?.CreatedBefore is { } before && record.CreatedAt >= before) throw new MealDraftException(MealDraftErrorCode.SameTurnCommit, "Draft was created during this turn");
        if (request?.Notes is { Length: > MealValidation.MaxNotesLength }) throw Validation($"Notes must not exceed {MealValidation.MaxNotesLength} characters");
        var items = Deserialize(record.ItemsJson);
        var requestItems = request?.Items ?? items.Where(i => i.IncludedByDefault).Select(i => new MealDraftCommitItem { ItemId = i.ItemId, Grams = i.Grams }).ToList();
        if (requestItems.Count is < 1 or > 50) throw Validation("A meal must contain 1 to 50 items");
        var resolved = await ResolveItems(items, requestItems, true, false, ct);
        var selected = requestItems.Select(x => resolved[x.ItemId]).ToList();
        var mealId = Guid.NewGuid();
        var now = time.GetUtcNow();
        var deltas = items.Select(item =>
        {
            var entry = resolved.GetValueOrDefault(item.ItemId);
            var oldKey = item.Grounding?.Candidates.FirstOrDefault(c => c.FoodProductId == item.FoodProductId)?.CandidateKey;
            return new
            {
                itemId = item.ItemId,
                name = item.Name,
                foodClass = FoodClassClassifier.Classify(item.Name),
                portionConfidenceTier = FoodClassClassifier.ConfidenceTier(item.PortionConfidence),
                method = item.PortionMethod,
                predictedGrams = item.Grams,
                committedGrams = entry?.Grams,
                gramRatio = entry is null || item.Grams == 0 ? (decimal?)null : entry.Grams / item.Grams,
                removed = entry is null,
                swappedFromKey = oldKey,
                swappedToKey = entry?.SelectedCandidateKey,
                replacementFoodProductId = entry?.ReplacementFoodProductId
            };
        }).ToList();
        var claimed = record with
        {
            Status = MealDraftStatuses.Committed,
            CommittedMealId = mealId,
            CommittedAt = now,
            CorrectionDeltaJson = JsonSerializer.Serialize(deltas, JsonOptions)
        };
        var claimEtag = await store.TryReplaceMealDraftAsync(claimed, ct);
        if (claimEtag is null) throw new MealDraftException(MealDraftErrorCode.NotPending, "Draft was committed or changed by another request");
        var claimedWithEtag = claimed with { ETag = claimEtag };

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                if (selected[i].CandidateProduct is not { } candidateProduct) continue;
                var productId = await FoodProductPersistence.ResolveOrPersistAsync(candidateProduct, store, ct);
                selected[i] = selected[i] with { Item = selected[i].Item with { FoodProductId = productId }, CandidateProduct = null };
            }

            var mealItems = selected.Select(x => new MealItem
            {
                Id = Guid.NewGuid(),
                MealLogId = mealId,
                FoodName = FoodDisplayNameFormatter.ToTitleCase(x.Item.Name),
                FoodProductId = x.Item.FoodProductId,
                Servings = 1,
                ServingUnit = "g",
                ServingWeightG = x.Grams,
                ServingHintUnit = x.Item.ServingHintUnit,
                ServingHintUnitPlural = x.Item.ServingHintUnitPlural,
                ServingHintUnitGrams = x.Item.ServingHintUnitGrams,
                Calories = x.Amounts.Calories,
                ProteinG = x.Amounts.ProteinG,
                CarbsG = x.Amounts.CarbsG,
                FatG = x.Amounts.FatG,
                FiberG = x.Amounts.FiberG ?? 0,
                SugarG = x.Amounts.SugarG ?? 0,
                SodiumMg = x.Amounts.SodiumMg ?? 0,
                MatchConfidence = x.UserSelected ? null : x.Item.MatchConfidence,
                NutritionProvenance = x.Provenance
            }).ToList();
            var meal = new MealLog
            {
                Id = mealId,
                UserId = userId,
                MealType = ResolveMealType(request?.MealType ?? record.MealType),
                LoggedAt = (request?.LoggedAt ?? record.LoggedAt ?? now).UtcDateTime,
                Notes = request?.Notes,
                OriginalText = $"{record.Origin} draft {record.Id}",
                Items = mealItems,
                TotalCalories = mealItems.Sum(i => i.Calories),
                TotalProteinG = mealItems.Sum(i => i.ProteinG),
                TotalCarbsG = mealItems.Sum(i => i.CarbsG),
                TotalFatG = mealItems.Sum(i => i.FatG)
            };
            await store.UpsertMealLogAsync(meal, ct);
            await store.UpsertMealItemsAsync(userId, mealId, mealItems, ct);
            return new MealDraftCommitResult
            {
                MealId = mealId,
                TotalCalories = meal.TotalCalories,
                TotalProteinG = meal.TotalProteinG,
                TotalCarbsG = meal.TotalCarbsG,
                TotalFatG = meal.TotalFatG,
                ItemCount = mealItems.Count,
                ItemsWithoutNutrition = selected.Count(x => x.Basis is null)
            };
        }
        catch (Exception)
        {
            try
            {
                var pending = claimedWithEtag with
                {
                    Status = MealDraftStatuses.PendingReview,
                    CommittedMealId = null,
                    CommittedAt = null,
                    CorrectionDeltaJson = null
                };
                if (await store.TryReplaceMealDraftAsync(pending, CancellationToken.None) is null)
                    logger.LogWarning("Could not revert meal draft {DraftId} after a failed meal write", draftId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not revert meal draft {DraftId} after a failed meal write", draftId);
            }
            throw;
        }
    }

    public async Task DiscardAsync(Guid userId, Guid draftId, CancellationToken ct = default)
    {
        var record = await store.GetMealDraftAsync(userId, draftId, ct);
        if (record is null || record.Status == MealDraftStatuses.Discarded) return;
        if (record.Status != MealDraftStatuses.PendingReview)
            throw new MealDraftException(MealDraftErrorCode.NotPending, "Draft is no longer pending");
        var etag = await store.TryReplaceMealDraftAsync(record with { Status = MealDraftStatuses.Discarded }, ct);
        if (etag is null) throw new MealDraftException(MealDraftErrorCode.NotPending, "Draft changed while it was being discarded");
    }

    private async Task<MealDraftRecord> RequirePending(Guid userId, Guid id, CancellationToken ct)
    {
        var record = await store.GetMealDraftAsync(userId, id, ct);
        if (record is null) throw new MealDraftException(MealDraftErrorCode.NotFound, "Draft not found");
        if (record.Status == MealDraftStatuses.Expired) throw new MealDraftException(MealDraftErrorCode.NotFound, "Draft not found");
        if (record.Status != MealDraftStatuses.PendingReview) throw new MealDraftException(MealDraftErrorCode.NotPending, "Draft is no longer pending");
        if (record.ExpiresAt <= time.GetUtcNow())
        {
            try
            {
                await store.TryReplaceMealDraftAsync(record with { Status = MealDraftStatuses.Expired }, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Could not mark expired meal draft {DraftId}", id); }
            throw new MealDraftException(MealDraftErrorCode.NotFound, "Draft not found");
        }
        return record;
    }

    private async Task<Dictionary<Guid, Resolved>> ResolveItems(List<MealDraftItemDto> draftItems, IReadOnlyList<MealDraftCommitItem> requests, bool committing, bool persistCandidateProducts, CancellationToken ct)
    {
        if (requests.Select(r => r.ItemId).Distinct().Count() != requests.Count) throw Validation("Duplicate item id");
        var source = draftItems.ToDictionary(i => i.ItemId);
        var result = new Dictionary<Guid, Resolved>();
        var unresolved = new List<Guid>();
        foreach (var req in requests)
        {
            if (!source.TryGetValue(req.ItemId, out var item)) throw Validation("Unknown draft item id");
            if (req.Grams <= 0 || req.Grams > 5000) throw Validation("Grams must be greater than 0 and no more than 5000");
            if (committing && item.NeedsChoice
                && req.SelectedCandidateKey is null
                && req.ReplacementFoodProductId is null
                && !req.LogWithoutCalories)
            {
                unresolved.Add(req.ItemId);
                continue;
            }
            NutritionPer100gDto? basis = item.Per100g;
            Guid? productId = item.FoodProductId;
            string provenance = item.NutritionProvenance;
            bool selectedByUser = false;
            string? candidateKey = null;
            string? selectedName = null;
            FoodProductDto? candidateProduct = null;
            if (req.SelectedCandidateKey is not null)
            {
                var candidate = item.Grounding?.Candidates.FirstOrDefault(c => c.CandidateKey == req.SelectedCandidateKey) ?? throw Validation("Unknown candidate key");
                selectedName = candidate.Name;
                basis = NutritionCalculator.BasisFrom(candidate) ?? throw Validation("Selected candidate has no nutrition");
                productId = candidate.FoodProductId;
                if (productId is null)
                {
                    var dataSource = candidate.Source.ToLowerInvariant() switch { "usda" => "USDA", "off" => "OpenFoodFacts", "au" => "AUSNUT", _ => candidate.Source };
                    candidateProduct = new FoodProductDto
                    {
                        Name = candidate.Name,
                        Brand = candidate.Brand,
                        ExternalId = candidate.ExternalId,
                        SourceUrl = candidate.SourceUrl,
                        Calories100g = candidate.Calories100g,
                        Protein100g = candidate.Protein100g,
                        Carbs100g = candidate.Carbs100g,
                        Fat100g = candidate.Fat100g,
                        Fiber100g = candidate.Fiber100g,
                        Sugar100g = candidate.Sugar100g,
                        SodiumMg100g = candidate.SodiumMg100g,
                        DataSource = dataSource,
                        MatchConfidence = candidate.MatchConfidence
                    };
                    if (persistCandidateProducts)
                    {
                        productId = await FoodProductPersistence.ResolveOrPersistAsync(candidateProduct, store, ct);
                        candidateProduct = null;
                    }
                }
                provenance = candidate.Source.Equals("web", StringComparison.OrdinalIgnoreCase) ? nameof(NutritionProvenance.Web) : nameof(NutritionProvenance.Sourced);
                selectedByUser = true; candidateKey = req.SelectedCandidateKey;
            }
            else if (req.ReplacementFoodProductId is { } replacementId)
            {
                var product = await store.GetFoodProductAsync(replacementId, ct) ?? throw Validation("Replacement product not found");
                basis = NutritionCalculator.BasisFrom(product) ?? throw Validation("Replacement product has no nutrition");
                productId = replacementId; provenance = nameof(NutritionProvenance.Sourced); selectedByUser = true;
                selectedName = product.Name;
            }
            else if (committing && item.NeedsChoice && req.LogWithoutCalories)
            {
                basis = null;
                productId = null;
                provenance = nameof(NutritionProvenance.Unknown);
            }
            if (basis is null)
            {
                if (committing && !req.LogWithoutCalories) { unresolved.Add(req.ItemId); continue; }
                provenance = nameof(NutritionProvenance.Unknown);
            }
            var amounts = basis is null ? new NutritionAmountsDto() : NutritionCalculator.Compute(basis, req.Grams);
            var updatedItem = item with
            {
                Name = selectedName ?? item.Name,
                Grams = req.Grams,
                FoodProductId = productId,
                Per100g = basis,
                NutritionProvenance = provenance,
                Calories = basis is null ? null : amounts.Calories,
                ProteinG = basis is null ? null : amounts.ProteinG,
                CarbsG = basis is null ? null : amounts.CarbsG,
                FatG = basis is null ? null : amounts.FatG,
                FiberG = basis is null ? null : amounts.FiberG,
                SugarG = basis is null ? null : amounts.SugarG,
                SodiumMg = basis is null ? null : amounts.SodiumMg
            };
            result.Add(req.ItemId, new Resolved(updatedItem, req.Grams, basis, amounts, provenance, selectedByUser, candidateKey, req.ReplacementFoodProductId, candidateProduct));
        }
        if (unresolved.Count > 0) throw new MealDraftUnresolvedItemsException(unresolved);
        return result;
    }

    private static MealDraftItemDto Normalize(MealDraftItemDto item)
    {
        if (item.Per100g is { } basis)
        {
            var n = NutritionCalculator.Compute(basis, item.Grams);
            var provenance = string.IsNullOrEmpty(item.NutritionProvenance) || item.NutritionProvenance == "Unknown"
                ? NutritionProvenanceRules.ForDraftSource(item.Source, true).ToString()
                : item.NutritionProvenance;
            return item with { NutritionProvenance = provenance, Calories = n.Calories, ProteinG = n.ProteinG, CarbsG = n.CarbsG, FatG = n.FatG, FiberG = n.FiberG, SugarG = n.SugarG, SodiumMg = n.SodiumMg };
        }
        var estimate = item.NutritionProvenance is nameof(NutritionProvenance.Estimated) or nameof(NutritionProvenance.ModelEstimated) or nameof(NutritionProvenance.Web);
        return item with { NutritionProvenance = estimate ? item.NutritionProvenance : nameof(NutritionProvenance.Unknown), Calories = null, ProteinG = null, CarbsG = null, FatG = null, FiberG = null, SugarG = null, SodiumMg = null };
    }

    private static string SerializeChecked(List<MealDraftItemDto> items)
    {
        var json = JsonSerializer.Serialize(items, JsonOptions);
        if (json.Length > 32_000) throw Validation("Draft items exceed the storage limit");
        return json;
    }
    private static List<MealDraftItemDto> Deserialize(string json) => JsonSerializer.Deserialize<List<MealDraftItemDto>>(json, JsonOptions) ?? [];
    private static MealDraftDto ToDto(MealDraftRecord record, List<MealDraftItemDto> items)
    {
        var included = items.Where(i => i.IncludedByDefault).ToList();
        var nutrition = included.Where(i => i.Per100g is not null).Select(i => NutritionCalculator.Compute(i.Per100g!, i.Grams));
        var sum = NutritionCalculator.Sum(nutrition);
        return new MealDraftDto
        {
            DraftId = record.Id,
            Origin = record.Origin,
            Status = record.Status,
            MealType = record.MealType,
            LoggedAt = record.LoggedAt,
            Items = items,
            Warnings = record.Warnings,
            ReferenceObjectVisible = record.ReferenceObjectVisible,
            OverallConfidence = record.OverallConfidence,
            Totals = new MealDraftTotalsDto { Calories = sum.Calories, ProteinG = sum.ProteinG, CarbsG = sum.CarbsG, FatG = sum.FatG, ItemsWithoutNutrition = included.Count(i => i.Per100g is null) },
            CreatedAt = record.CreatedAt,
            ExpiresAt = record.ExpiresAt
        };
    }
    private static void ValidateMealType(string? mealType)
    {
        if (mealType is not null && !MealValidation.TryParseMealType(mealType, out _))
            throw Validation($"Meal type must be {MealValidation.MealTypeNames}");
    }
    private static MealType ResolveMealType(string? mealType)
    {
        if (mealType is null) return MealType.Snack;
        if (!MealValidation.TryParseMealType(mealType, out var parsed))
            throw Validation($"Meal type must be {MealValidation.MealTypeNames}");
        return parsed;
    }


    private static MealDraftException Validation(string message) => new(MealDraftErrorCode.Validation, message);
    private sealed record Resolved(MealDraftItemDto Item, decimal Grams, NutritionPer100gDto? Basis, NutritionAmountsDto Amounts, string Provenance, bool UserSelected, string? SelectedCandidateKey, Guid? ReplacementFoodProductId, FoodProductDto? CandidateProduct);
}

public sealed class MealDraftUnresolvedItemsException(IReadOnlyList<Guid> itemIds)
    : MealDraftException(MealDraftErrorCode.UnresolvedItems, "Items require a nutrition choice or explicit log-without-calories confirmation")
{
    public IReadOnlyList<Guid> ItemIds { get; } = itemIds;
}
