using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Services;

/// <summary>Grounded, deterministic meal-suggestion generation (AGENTS.md N1/N3).</summary>
public sealed class MealSuggestionService : IMealSuggestionService
{
    public const string SuggestionPromptVersion = "2026-09-25.v1-grounded-meal-suggestions";
    private const string Instructions = """
        Suggest meals only from the supplied candidate pool. Return JSON matching the schema.
        Each item uses a pool_index and grams only. Never output calories, macros, nutrition values,
        numeric serving recommendations, or nutrition numbers in titles or rationales. Do not
        invent foods or ingredients. Respect preferences. Rationale is brief and qualitative.
        """;
    private static readonly ChatRole DeveloperRole = new("developer");
    private static readonly Meter Meter = new("GutAI.AI");
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("meal_suggestion.requests");
    private static readonly Regex NutritionNumber = new(@"\b\d+(?:[.,]\d+)?\s*(?:kcal|calories?|cal|g|grams?|mg|mcg|kj|kilojoules?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ServingGrams = new(@"\b(\d+(?:[.,]\d+)?)\s*g\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] CuratedQueries = ["chicken breast", "rice", "potato", "carrot", "spinach", "egg", "salmon", "oats", "banana", "olive oil", "lettuce", "tomato", "tofu", "quinoa", "blueberries", "zucchini"];
    private readonly ITableStore _store;
    private readonly IFoodDiaryAnalysisService _diary;
    private readonly ICorrelationEngine _correlations;
    private readonly IFodmapService _fodmap;
    private readonly SubstitutionService _substitutions;
    private readonly INutritionBudgetService _budgets;
    private readonly IMealDraftService _drafts;
    private readonly IChatClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<MealSuggestionService> _logger;

    public MealSuggestionService(
        ITableStore store, IFoodDiaryAnalysisService diary, ICorrelationEngine correlations,
        IFodmapService fodmap, SubstitutionService substitutions, INutritionBudgetService budgets,
        IMealDraftService drafts, [FromKeyedServices(AiWorkloads.Suggestion)] IChatClient client,
        IConfiguration config, ILogger<MealSuggestionService> logger)
    {
        _store = store; _diary = diary; _correlations = correlations; _fodmap = fodmap;
        _substitutions = substitutions; _budgets = budgets; _drafts = drafts; _client = client;
        _config = config; _logger = logger;
    }

    public async Task<MealSuggestionResultDto> SuggestAsync(Guid userId, MealSuggestionRequest request, string? timezoneId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mealType = NormalizeMealType(request.MealType);
        if (request.Preferences?.Length > 200) throw new ArgumentException("Preferences cannot exceed 200 characters.", nameof(request));
        var budget = await _budgets.GetBudgetAsync(userId, mealType, timezoneId, ct);
        var user = await _store.GetUserAsync(userId, ct);
        if (user is null) throw new ArgumentException("User profile was not found.", nameof(userId));
        var (candidates, correlatedTriggers) = await BuildPoolAsync(user, budget.Date, timezoneId, ct);
        var maxSuggestions = Math.Clamp(_config.GetValue("MealSuggestions:MaxSuggestions", 3), 0, 3);
        var maxItems = Math.Clamp(_config.GetValue("MealSuggestions:MaxItemsPerSuggestion", 6), 0, 6);
        var response = await _client.GetResponseAsync<SuggestionResponse>([
            new ChatMessage(DeveloperRole, Instructions),
            new ChatMessage(ChatRole.User, $"<suggestion_request>\nmeal_type={mealType}\nbudget={JsonSerializer.Serialize(budget.MealTarget)}\npreferences={request.Preferences ?? "(none)"}\npool={JsonSerializer.Serialize(candidates.Select((c, i) => new { pool_index = i, name = c.Product.Name, calories_per_100g = c.Basis.CaloriesKcal, protein_g_per_100g = c.Basis.ProteinG, carbs_g_per_100g = c.Basis.CarbsG, fat_g_per_100g = c.Basis.FatG, fiber_g_per_100g = c.Basis.FiberG, typical_grams = c.TypicalGrams }))}\n</suggestion_request>"),
        ], options: MealScanReasoningOptions.Create(
            AiWorkloads.ResolveReasoningEffort(_config, AiWorkloads.Suggestion) ?? "medium"),
            useJsonSchemaResponseFormat: true, cancellationToken: ct);
        var proposed = response.Result?.Suggestions ?? [];
        var poolMap = candidates.Select((candidate, index) => (candidate, index)).ToDictionary(x => x.index, x => x.candidate);
        var maxPool = candidates.Count;
        var tolerance = Math.Clamp(_config.GetValue("MealSuggestions:KcalTolerance", .10m), 0m, .50m);
        var returned = new List<MealSuggestionDto>();
        var duplicateSets = new HashSet<string>(StringComparer.Ordinal);
        var repairedCount = 0; var dropInvalid = 0; var dropBudget = 0; var returnedItems = 0;
        var proposalsValidated = 0; var validCount = 0; var budgetFitCount = 0;
        foreach (var proposal in proposed.Take(maxSuggestions))
        {
            proposalsValidated++;
            var rows = proposal.Items.Take(maxItems).ToList();
            if (rows.Count == 0 || rows.Count != proposal.Items.Count || rows.Any(r => r.PoolIndex < 0 || r.PoolIndex >= maxPool || r.Grams <= 0)) { dropInvalid++; continue; }
            var chosen = new List<(Candidate C, decimal Grams)>();
            foreach (var row in rows)
            {
                var c = poolMap[row.PoolIndex];
                var (min, max) = Bounds(c.TypicalGrams);
                if (row.Grams < min || row.Grams > max) { chosen.Clear(); break; }
                if (!IsAllowed(c.Product, user) || !IsSafe(c))
                {
                    var replacement = await TryRepairAsync(c, row.Grams, user, correlatedTriggers, ct);
                    if (replacement is null) { chosen.Clear(); break; }
                    c = replacement; candidates.Add(replacement); repairedCount++;
                    var repairedBounds = Bounds(c.TypicalGrams);
                    if (row.Grams < repairedBounds.Min || row.Grams > repairedBounds.Max) { chosen.Clear(); break; }
                }
                chosen.Add((c, row.Grams));
            }
            if (chosen.Count != rows.Count) { dropInvalid++; continue; }
            validCount++;
            var calories = Sum(chosen).Calories;
            var target = budget.MealTarget?.Calories ?? 0m;
            if (target <= 0 || calories <= 0) { dropBudget++; continue; }
            var factor = target / calories;
            if (Math.Abs(factor - 1m) > 0m)
            {
                var scaled = chosen.Select(x => (x.C, decimal.Clamp(decimal.Round(x.Grams * factor, 1), Bounds(x.C.TypicalGrams).Min, Bounds(x.C.TypicalGrams).Max))).ToList();
                chosen = scaled;
            }
            calories = Sum(chosen).Calories;
            if (Math.Abs(calories - target) / target > tolerance) { dropBudget++; continue; }
            if (chosen.Any(x => !IsAllowed(x.C.Product, user) || !IsSafe(x.C)
                || MatchesAnyTrigger(x.C.Product, correlatedTriggers))) { dropInvalid++; continue; }
            budgetFitCount++;
            var productSet = string.Join("|", chosen.Select(x => x.C.Product.Id).Distinct().Order().Select(id => id.ToString("N")));
            if (!duplicateSets.Add(productSet)) { dropInvalid++; continue; }
            var itemDtos = chosen.Select(x => new MealDraftItemDto
            {
                ItemId = Guid.NewGuid(),
                Name = x.C.Product.Name,
                CanonicalName = x.C.Product.Name,
                FoodProductId = x.C.Product.Id,
                Source = x.C.Product.DataSource.ToLowerInvariant(),
                Grams = x.Grams,
                PortionLowGrams = x.Grams,
                PortionHighGrams = x.Grams,
                PortionMethod = "suggestion",
                IncludedByDefault = true,
                Per100g = x.C.Basis,
                NutritionProvenance = nameof(NutritionProvenance.Sourced),
                MatchConfidence = x.C.Product.MatchConfidence,
                Grounding = new GroundingAttemptDto
                {
                    Query = x.C.Product.Name,
                    ResolutionStatus = "exact",
                    AutoSelected = true,
                    SelectedFoodProductId = x.C.Product.Id,
                    CanonicalName = x.C.Product.Name,
                    MatchConfidence = x.C.Product.MatchConfidence,
                    Method = "suggestion",
                },
            }).ToList();
            var draft = await _drafts.CreateAsync(userId, new MealDraftCreateRequest
            {
                Origin = MealDraftOrigins.Suggestion,
                MealType = mealType,
                Items = itemDtos,
                PromptVersion = SuggestionPromptVersion,
                ModelDeployment = AiWorkloads.ResolveDeployment(_config, AiWorkloads.Suggestion),
                RawModelJson = JsonSerializer.Serialize(response.Result),
                OverallConfidence = 1m,
            }, ct);
            returned.Add(new MealSuggestionDto { Draft = draft, Title = Sanitize(proposal.Title, 80), Rationale = Sanitize(proposal.Rationale, 300) });
            returnedItems += draft.Items.Count;
        }
        var triggerViolations = returned.SelectMany(s => s.Draft.Items).Count(item =>
            !candidates.Any(c => c.Product.Id == item.FoodProductId && IsSafe(c) && IsAllowed(c.Product, user)
                && !MatchesAnyTrigger(c.Product, correlatedTriggers)));
        if (triggerViolations != 0) throw new InvalidOperationException("Meal suggestion safety invariant violated: returned item failed safety screen.");
        var metrics = new
        {
            proposed = proposed.Count,
            proposals_validated = proposalsValidated,
            valid = validCount,
            budget_fit = budgetFitCount,
            validity_rate = proposalsValidated == 0 ? 0m : (decimal)validCount / proposalsValidated,
            budget_fit_rate = proposalsValidated == 0 ? 0m : (decimal)budgetFitCount / proposalsValidated,
            trigger_violations = triggerViolations,
            trigger_violation_rate = proposalsValidated == 0 ? 0m : (decimal)triggerViolations / proposalsValidated,
            repaired = repairedCount,
            dropped_by_reason = new { invalid = dropInvalid, budget = dropBudget },
            returned_item_trigger_violations = triggerViolations,
            diversity = returnedItems == 0 ? 0m : (decimal)returned.SelectMany(s => s.Draft.Items).Select(i => i.FoodProductId).Distinct().Count() / returnedItems,
        };
        RequestCounter.Add(1,
            new KeyValuePair<string, object?>("valid", validCount),
            new KeyValuePair<string, object?>("returned", returned.Count),
            new KeyValuePair<string, object?>("proposals_validated", proposalsValidated),
            new KeyValuePair<string, object?>("budget_fit", budgetFitCount),
            new KeyValuePair<string, object?>("trigger_violations", triggerViolations),
            new KeyValuePair<string, object?>("validity_rate", metrics.validity_rate),
            new KeyValuePair<string, object?>("budget_fit_rate", metrics.budget_fit_rate),
            new KeyValuePair<string, object?>("trigger_violation_rate", metrics.trigger_violation_rate),
            new KeyValuePair<string, object?>("diversity", metrics.diversity));
        _logger.LogInformation("Meal suggestion request metrics: {@Metrics}", metrics);
        return new MealSuggestionResultDto { Budget = budget, Suggestions = returned, PromptVersion = SuggestionPromptVersion, RejectedCount = Math.Max(0, proposed.Count - returned.Count) };
    }

    private async Task<(List<Candidate> Candidates, HashSet<string> CorrelatedTriggers)> BuildPoolAsync(User user, DateOnly today, string? timezoneId, CancellationToken ct)
    {
        var (start, end) = TimeZoneHelper.GetUtcRangeForLocalDateRange(user, today.AddDays(-30), today, timezoneId);
        var meals = await _store.GetMealLogsByDateRangeAsync(user.Id, DateOnly.FromDateTime(start), DateOnly.FromDateTime(end), ct);
        var frequencies = new Dictionary<Guid, int>();
        foreach (var meal in meals.Where(m => m.LoggedAt >= start && m.LoggedAt <= end))
            foreach (var item in await _store.GetMealItemsAsync(user.Id, meal.Id, ct))
                if (item.FoodProductId is { } id) frequencies[id] = frequencies.GetValueOrDefault(id) + 1;
        var safe = await _diary.GetEliminationStatusAsync(user.Id, _store, timezoneId);
        var correlations = await _correlations.ComputeCorrelationsAsync(user.Id, today.AddDays(-30), today, ct, timezoneId);
        var triggers = correlations.Where(c => c.Occurrences >= 2 && c.AverageSeverity >= 4).Select(c => c.FoodOrAdditive).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var safeNames = safe.SafeFoods.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var raw = new Dictionary<Guid, FoodProduct>();
        foreach (var id in frequencies.OrderByDescending(x => x.Value).Select(x => x.Key).Take(50).Concat(Array.Empty<Guid>()).Distinct())
            if (await _store.GetFoodProductAsync(id, ct) is { IsDeleted: false } product) raw[product.Id] = product;
        foreach (var name in safeNames.Concat(CuratedQueries).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var product in await _store.SearchFoodProductsAsync(name, 4, ct))
                if (!product.IsDeleted) raw.TryAdd(product.Id, product);
        var max = Math.Clamp(_config.GetValue("MealSuggestions:MaxPoolSize", 60), 0, 60);
        var candidates = raw.Values.Select(p => ToDto(p))
            .Where(p => p.Id != Guid.Empty && p.Calories100g is not null)
            .Select(p => new Candidate(p, NutritionCalculator.BasisFrom(p)!, TypicalGrams(p),
                safeNames.Contains(p.Name), frequencies.ContainsKey(p.Id)))
            .Where(c => IsAllowed(c.Product, user) && !MatchesAnyTrigger(c.Product, triggers) && IsSafe(c))
            .OrderByDescending(c => c.IsEliminationSafe)
            .ThenByDescending(c => c.IsFrequent)
            .Take(max).ToList();
        return (candidates, triggers);
    }

    private async Task<Candidate?> TryRepairAsync(Candidate original, decimal grams, User user, HashSet<string> correlatedTriggers, CancellationToken ct)
    {
        foreach (var suggestion in _substitutions.GetSubstitutions(original.Product).Suggestions)
        {
            var words = suggestion.Substitute.Split(new[] { " or ", ",", " (" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var query in words.Take(2))
                foreach (var product in await _store.SearchFoodProductsAsync(query, 4, ct))
                {
                    var dto = ToDto(product);
                    var basis = NutritionCalculator.BasisFrom(dto);
                    if (product.Id == Guid.Empty || product.IsDeleted || basis is null) continue;
                    if (MatchesAnyTrigger(dto, correlatedTriggers)) continue;
                    var candidate = new Candidate(dto, basis, TypicalGrams(dto), false, false);
                    if (IsAllowed(candidate.Product, user) && IsSafe(candidate)) return candidate;
                }
        }
        return null;
    }

    private bool IsSafe(Candidate c) => _fodmap.Assess(c.Product).Status == nameof(FodmapAssessmentStatus.NoKnownTriggersDetected);

    private static bool MatchesAnyTrigger(FoodProductDto product, HashSet<string> triggers) =>
        triggers.Any(t => Contains(product.Name, t) || Contains(product.Ingredients, t));
    /// <summary>
    /// Exclusions are normalized by case-insensitive substring matching against product name,
    /// full ingredient text and allergen tags. Profiles store free-form exclusion phrases.
    /// </summary>
    private static bool IsAllowed(FoodProductDto p, User u)
    {
        var text = $"{p.Name} {p.Ingredients} {string.Join(' ', p.AllergensTags)}";
        return !(u.Allergies ?? []).Concat(u.DietaryPreferences ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Any(ex => Contains(text, ex));
    }
    private static bool Contains(string? text, string term) =>
        !string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(term)
        && text.Contains(term, StringComparison.OrdinalIgnoreCase);
    private static string NormalizeMealType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "breakfast" => "Breakfast",
        "lunch" => "Lunch",
        "dinner" => "Dinner",
        "snack" => "Snack",
        _ => throw new ArgumentException("Meal type must be Breakfast, Lunch, Dinner, or Snack.", nameof(value)),
    };
    private static string Sanitize(string? text, int max) => NutritionNumber.Replace(text ?? "", "").Trim() is var clean && clean.Length > max ? clean[..max] : clean;
    private static (decimal Min, decimal Max) Bounds(decimal typical) => (Math.Max(5m, typical * .25m), Math.Min(800m, Math.Max(5m, typical * 3m)));
    /// <summary>Use an explicit gram serving when available; otherwise use the standard 100 g basis.</summary>
    private static decimal TypicalGrams(FoodProductDto p)
    {
        var match = ServingGrams.Match(p.ServingSize ?? "");
        return match.Success && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var grams)
            ? decimal.Clamp(grams, 5m, 800m)
            : 100m;
    }
    private static NutritionAmountsDto Sum(List<(Candidate C, decimal Grams)> rows) => NutritionCalculator.Sum(rows.Select(x => NutritionCalculator.Compute(x.C.Basis, x.Grams)));
    private static FoodProductDto ToDto(FoodProduct p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Brand = p.Brand,
        Ingredients = p.Ingredients,
        AllergensTags = p.AllergensTags,
        Calories100g = p.Calories100g,
        Protein100g = p.Protein100g,
        Carbs100g = p.Carbs100g,
        Fat100g = p.Fat100g,
        Fiber100g = p.Fiber100g,
        Sugar100g = p.Sugar100g,
        SodiumMg100g = p.SodiumMg100g,
        FoodKind = p.FoodKind,
        DataSource = p.DataSource,
        ServingSize = p.ServingSize,
        ServingQuantity = p.ServingQuantity,
        MatchConfidence = 1m,
    };
    private sealed record Candidate(FoodProductDto Product, NutritionPer100gDto Basis, decimal TypicalGrams, bool IsEliminationSafe, bool IsFrequent);
    private sealed class SuggestionResponse { public SuggestionResponse() { } [JsonPropertyName("suggestions")] public List<Suggestion> Suggestions { get; set; } = []; }
    private sealed class Suggestion { public Suggestion() { } [JsonPropertyName("title")] public string Title { get; set; } = "Meal idea"; [JsonPropertyName("items")] public List<SuggestionItem> Items { get; set; } = []; [JsonPropertyName("rationale")] public string Rationale { get; set; } = ""; }
    private sealed class SuggestionItem { public SuggestionItem() { } [JsonPropertyName("pool_index")] public int PoolIndex { get; set; } [JsonPropertyName("grams")] public decimal Grams { get; set; } }
}
