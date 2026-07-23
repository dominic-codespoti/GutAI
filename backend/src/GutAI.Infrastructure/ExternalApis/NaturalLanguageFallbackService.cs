using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.ExternalApis;

public partial class NaturalLanguageFallbackService
{
    private readonly IFoodSearchService _foodApi;
    private readonly ITableStore _store;
    private readonly ILogger<NaturalLanguageFallbackService> _logger;

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(4);

    public NaturalLanguageFallbackService(IFoodSearchService foodApi, ITableStore store, ILogger<NaturalLanguageFallbackService> logger)
    {
        _foodApi = foodApi;
        _store = store;
        _logger = logger;
    }

    public virtual async Task<List<ParsedFoodItemDto>> ParseAsync(string text, CancellationToken ct = default)
    {
        var cleaned = PreprocessText(text);
        var rawSegments = SplitIntoSegmentsWithJoins(cleaned);
        var parsedSegments = ParseSegments(rawSegments);
        var mergedSegments = await MergeConjunctionSegmentsAsync(parsedSegments, ct);
        var results = new List<ParsedFoodItemDto>();

        foreach (var seg in mergedSegments)
        {
            try
            {
                var resolution = seg.Precomputed ?? await TryResolveAsync(seg.FoodName, ct)
                    ?? new FoodResolutionDto { OriginalQuery = seg.FoodName };

                if (resolution.Selected is not null)
                {
                    var match = resolution.Selected;
                    var unitWeightG = EstimateUnitWeightG(match, seg.Unit, seg.FoodName) * seg.SizeMultiplier;
                    var totalWeightG = unitWeightG * seg.Quantity;
                    var scale = totalWeightG / 100m;
                    var portionConfidence = ServingEstimator.EstimatePortionConfidence(match.ServingQuantity, seg.Unit, seg.FoodName);

                    Guid? foodProductId = null;
                    try
                    {
                        foodProductId = await FoodProductPersistence.ResolveOrPersistAsync(match, _store, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist FoodProduct for '{Name}'", match.Name);
                    }

                    results.Add(new ParsedFoodItemDto
                    {
                        Name = match.Name,
                        FoodProductId = foodProductId,
                        Calories = Round(match.Calories100g, scale),
                        ProteinG = Round(match.Protein100g, scale),
                        CarbsG = Round(match.Carbs100g, scale),
                        FatG = Round(match.Fat100g, scale),
                        FiberG = Round(match.Fiber100g, scale),
                        SugarG = Round(match.Sugar100g, scale),
                        SodiumMg = Round(match.SodiumMg100g, scale),
                        ServingWeightG = totalWeightG,
                        ServingSize = FormatServingSize(seg.Quantity, seg.Unit),
                        ServingQuantity = seg.Quantity,
                        MatchConfidence = resolution.MatchConfidence,
                        PortionConfidence = portionConfidence,
                        NutritionProvenance = nameof(NutritionProvenance.Sourced),
                        ResolutionStatus = resolution.Status.ToString(),
                    });
                }
                else
                {
                    // Unresolved: nothing in the catalog had meaningful overlap with this food
                    // name. Falling back to a generic estimate here (rather than omitting the
                    // item) matches existing meal-logging UX, but NutritionProvenance.Estimated
                    // and zero MatchConfidence distinguish it from a real sourced match.
                    _logger.LogDebug("No food match found for '{FoodName}', using generic estimate", seg.FoodName);
                    results.Add(CreateGenericEstimate(seg.FoodName, seg.Quantity, seg.Unit, seg.SizeMultiplier));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process segment '{FoodName}'", seg.FoodName);
                results.Add(CreateGenericEstimate(seg.FoodName, seg.Quantity, seg.Unit, seg.SizeMultiplier));
            }
        }

        return results;
    }

    private async Task<FoodResolutionDto?> TryResolveAsync(string foodName, CancellationToken ct)
    {
        try
        {
            using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            searchCts.CancelAfter(SearchTimeout);
            return await _foodApi.ResolveAsync(foodName, [], searchCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Food search timed out for '{FoodName}', using generic estimate", foodName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to look up food for '{FoodName}'", foodName);
            return null;
        }
    }

    private readonly record struct ParsedSegment(
        string FoodName, decimal Quantity, string Unit, decimal SizeMultiplier, bool JoinedByConjunction, bool HasExplicitQuantity);

    private readonly record struct MergedSegment(
        string FoodName, decimal Quantity, string Unit, decimal SizeMultiplier, FoodResolutionDto? Precomputed);

    private static List<ParsedSegment> ParseSegments(List<FoodSegment> rawSegments)
    {
        var result = new List<ParsedSegment>();
        foreach (var raw in rawSegments)
        {
            var trimmed = raw.Text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var hasExplicitQuantity = HasExplicitQuantity(trimmed);
            var (quantity, unit, foodName) = ExtractQuantityAndFood(trimmed);

            if (string.IsNullOrWhiteSpace(foodName))
                continue;

            var sizeMultiplier = ExtractSizeMultiplier(ref foodName);
            foodName = CleanFoodName(foodName);

            if (string.IsNullOrWhiteSpace(foodName))
                continue;

            result.Add(new ParsedSegment(foodName, quantity, unit, sizeMultiplier, raw.JoinedByConjunction, hasExplicitQuantity));
        }
        return result;
    }

    /// <summary>
    /// Real compound dish names ("macaroni and cheese", "fish and chips") get destroyed by
    /// unconditionally splitting on "and"/"&" — each half is logged as an unrelated food with
    /// wrong nutrition instead of the one dish it actually names. This merges an "and"/"&"-joined
    /// pair back into one segment only when: neither side carries its own explicit quantity
    /// (so they read as one dish name, not two counted items like "2 eggs and a banana"), the
    /// first half resolves weakly alone, and the combined phrase resolves decisively. Any other
    /// case (both halves resolve well independently, or the combined phrase doesn't either)
    /// falls back to treating them as separate items — never silently drops or invents a merge.
    /// </summary>
    private async Task<List<MergedSegment>> MergeConjunctionSegmentsAsync(List<ParsedSegment> parsed, CancellationToken ct)
    {
        var merged = new List<MergedSegment>();
        var i = 0;
        while (i < parsed.Count)
        {
            var seg = parsed[i];
            var canConsiderMerge = i + 1 < parsed.Count
                && parsed[i + 1].JoinedByConjunction
                && !seg.HasExplicitQuantity
                && !parsed[i + 1].HasExplicitQuantity;

            if (!canConsiderMerge)
            {
                merged.Add(new MergedSegment(seg.FoodName, seg.Quantity, seg.Unit, seg.SizeMultiplier, null));
                i++;
                continue;
            }

            var next = parsed[i + 1];
            var individual = await TryResolveAsync(seg.FoodName, ct);
            var isWeak = individual is null || individual.Status is FoodResolutionStatus.Unresolved or FoodResolutionStatus.Ambiguous;

            if (isWeak)
            {
                var combinedName = $"{seg.FoodName} and {next.FoodName}";
                var combined = await TryResolveAsync(combinedName, ct);
                if (combined is not null && combined.Status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable)
                {
                    merged.Add(new MergedSegment(combinedName, seg.Quantity, seg.Unit, seg.SizeMultiplier, combined));
                    i += 2;
                    continue;
                }
            }

            merged.Add(new MergedSegment(seg.FoodName, seg.Quantity, seg.Unit, seg.SizeMultiplier, individual));
            i++;
        }
        return merged;
    }

    internal static string PreprocessText(string text)
    {
        var result = text.Trim();

        // Replace unicode fractions
        result = result.Replace("½", " 1/2").Replace("⅓", " 1/3").Replace("⅔", " 2/3")
            .Replace("¼", " 1/4").Replace("¾", " 3/4").Replace("⅕", " 1/5")
            .Replace("⅛", " 1/8").Replace("⅜", " 3/8").Replace("⅝", " 5/8").Replace("⅞", " 7/8");

        // Strip leading filler phrases like "I had", "I ate", "I just ate", "for lunch I had", etc.
        result = LeadingFillerPattern().Replace(result, "").Trim();

        // Remove trailing periods
        result = result.TrimEnd('.');

        return result;
    }

    internal static string FormatServingSize(decimal quantity, string unit)
    {
        var qtyStr = quantity == Math.Floor(quantity) ? ((int)quantity).ToString() : quantity.ToString("0.##");
        return string.IsNullOrEmpty(unit) ? qtyStr : $"{qtyStr} {unit}";
    }

    internal readonly record struct FoodSegment(string Text, bool JoinedByConjunction);

    internal static List<string> SplitIntoFoodSegments(string text) =>
        SplitIntoSegmentsWithJoins(text).Select(s => s.Text).ToList();

    /// <summary>Splits on every recognized separator (same set as before) but additionally
    /// reports which pairs were joined specifically by "and"/"&" — the only delimiters common
    /// in real compound dish names — so <see cref="MergeConjunctionSegmentsAsync"/> knows which
    /// adjacent pairs are merge candidates. Comma/semicolon/period/"then"/"plus"/"with" are
    /// treated as unambiguous item separators, same as before.</summary>
    internal static List<FoodSegment> SplitIntoSegmentsWithJoins(string text)
    {
        var normalized = text.Trim();
        var parts = SplitPatternCapturing().Split(normalized);
        var result = new List<FoodSegment>();

        for (var i = 0; i < parts.Length; i += 2)
        {
            var raw = parts[i].Trim();
            var cleaned = LeadingAndOrPattern().Replace(raw, "").Trim();
            if (cleaned.Length == 0)
                continue;

            var joinedByConjunction = i > 0 && IsConjunctionDelimiter(parts[i - 1]);
            result.Add(new FoodSegment(cleaned, joinedByConjunction));
        }

        return result;
    }

    private static bool IsConjunctionDelimiter(string delimiter) =>
        delimiter.Contains("and", StringComparison.OrdinalIgnoreCase) || delimiter.Contains('&');

    /// <summary>True if <paramref name="segment"/> starts with a number, fraction, or word
    /// quantity ("2", "1/2", "a", "some", ...) — used to tell a genuinely counted item ("2 eggs")
    /// apart from a bare compound-dish noun phrase ("macaroni") when deciding whether an
    /// "and"/"&amp;"-joined pair may name one dish instead of two separate items.</summary>
    internal static bool HasExplicitQuantity(string segment)
    {
        var cleaned = LeadingFillerWordPattern().Replace(segment, "").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = segment;
        return WordQuantityPattern().IsMatch(cleaned) || FractionPattern().IsMatch(cleaned) || NumericQuantityPattern().IsMatch(cleaned);
    }

    internal static (decimal quantity, string unit, string foodName) ExtractQuantityAndFood(string segment)
    {
        // Strip leading filler words: "some", "about", "around", "approximately", "roughly", "maybe", "like"
        var cleaned = LeadingFillerWordPattern().Replace(segment, "").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = segment;

        var wordMatch = WordQuantityPattern().Match(cleaned);
        if (wordMatch.Success)
        {
            var wordQty = ParseWordNumber(wordMatch.Groups["word"].Value);
            var wordUnit = wordMatch.Groups["unit"].Value.Trim();
            var wordFood = wordMatch.Groups["food"].Value.Trim();
            wordFood = StripOfPrefix(wordFood);
            if (string.IsNullOrEmpty(wordFood) && !string.IsNullOrEmpty(wordUnit))
            {
                wordFood = wordUnit;
                wordUnit = "";
            }
            return (wordQty, wordUnit, wordFood);
        }

        var fracMatch = FractionPattern().Match(cleaned);
        if (fracMatch.Success)
        {
            var whole = string.IsNullOrEmpty(fracMatch.Groups["whole"].Value) ? 0m : decimal.Parse(fracMatch.Groups["whole"].Value);
            var numerator = decimal.Parse(fracMatch.Groups["num"].Value);
            var denominator = decimal.Parse(fracMatch.Groups["den"].Value);
            var qty = whole + (denominator != 0 ? numerator / denominator : 0);
            var unit = fracMatch.Groups["unit"].Value.Trim();
            var food = fracMatch.Groups["food"].Value.Trim();
            food = StripOfPrefix(food);
            if (string.IsNullOrEmpty(food) && !string.IsNullOrEmpty(unit))
            {
                food = unit;
                unit = "";
            }
            return (qty, unit, food);
        }

        var numMatch = NumericQuantityPattern().Match(cleaned);
        if (numMatch.Success)
        {
            var qty = decimal.Parse(numMatch.Groups["qty"].Value);
            var unit = numMatch.Groups["unit"].Value.Trim();
            var food = numMatch.Groups["food"].Value.Trim();
            food = StripOfPrefix(food);
            if (string.IsNullOrEmpty(food) && !string.IsNullOrEmpty(unit))
            {
                food = unit;
                unit = "";
            }
            return (qty, unit, food);
        }

        return (1m, "", cleaned);
    }

    internal static string StripOfPrefix(string food)
    {
        if (food.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
            return food[3..].Trim();
        return food;
    }

    internal static string CleanFoodName(string foodName)
    {
        // Unwrap parenthetical descriptions into plain trailing tokens instead of discarding
        // them outright — "(grilled)" carries real preparation-method evidence the resolver
        // needs to distinguish "Grilled chicken breast" from a raw/generic match. Only the
        // truly non-food trailing phrases below ("on the side") are dropped.
        var result = ParentheticalPattern().Replace(foodName, m => $" {m.Groups[1].Value.Trim()} ");
        result = CollapseWhitespacePattern().Replace(result, " ").Trim();

        // Strip trailing preposition phrases if the core food was already captured
        // e.g., "chicken on the side" → "chicken"
        result = TrailingPrepPattern().Replace(result, "").Trim();

        return result;
    }

    internal static decimal ExtractSizeMultiplier(ref string foodName)
        => ServingEstimator.ExtractSizeMultiplier(ref foodName);

    internal static decimal EstimateUnitWeightG(FoodProductDto product, string unit, string foodName)
        => ServingEstimator.EstimateUnitWeightG(product.ServingQuantity, unit, foodName);

    // Keep the old name as a forwarding method for binary compat
    internal static decimal EstimateServingWeightG(FoodProductDto product, string unit, string foodName)
        => EstimateUnitWeightG(product, unit, foodName);

    private static bool IsWeightUnit(string unit) => ServingEstimator.IsWeightUnit(unit);

    private static decimal WeightUnitToGrams(string unit) => ServingEstimator.WeightUnitToGrams(unit);

    private static bool IsVolumeUnit(string unit) => ServingEstimator.IsVolumeUnit(unit);

    private static decimal VolumeUnitToGrams(string unit, string foodName) => ServingEstimator.VolumeUnitToGrams(unit, foodName);

    private static bool IsCountUnit(string unit) => ServingEstimator.IsCountUnit(unit);

    internal static decimal EstimateCupWeightG(string foodName)
        => ServingEstimator.EstimateCupWeightG(foodName);

    internal static decimal EstimateDefaultServingG(string foodName)
        => ServingEstimator.EstimateDefaultServingG(foodName);

    internal static ParsedFoodItemDto CreateGenericEstimate(string foodName, decimal quantity, string unit, decimal sizeMultiplier)
    {
        var servingG = EstimateDefaultServingG(foodName) * sizeMultiplier;
        var totalG = servingG * quantity;
        var cals = EstimateGenericCaloriesPer100g(foodName);
        var scale = totalG / 100m;

        return new ParsedFoodItemDto
        {
            Name = foodName,
            Calories = Math.Round(cals.calories * scale, 1),
            ProteinG = Math.Round(cals.protein * scale, 1),
            CarbsG = Math.Round(cals.carbs * scale, 1),
            FatG = Math.Round(cals.fat * scale, 1),
            FiberG = 0m,
            SugarG = 0m,
            SodiumMg = 0m,
            ServingWeightG = totalG,
            ServingSize = FormatServingSize(quantity, unit),
            ServingQuantity = quantity,
            MatchConfidence = 0m,
            PortionConfidence = ServingEstimator.EstimatePortionConfidence(null, unit, foodName),
            NutritionProvenance = nameof(NutritionProvenance.Estimated),
            ResolutionStatus = FoodResolutionStatus.Unresolved.ToString(),
        };
    }

    internal static (decimal calories, decimal protein, decimal carbs, decimal fat) EstimateGenericCaloriesPer100g(string foodName)
    {
        var lower = foodName.ToLowerInvariant();

        if (lower.Contains("protein") && (lower.Contains("shake") || lower.Contains("drink") || lower.Contains("smoothie")))
            return (80m, 10m, 5m, 1.5m);
        if (lower.Contains("protein") && lower.Contains("bar"))
            return (350m, 25m, 30m, 12m);
        if (lower.Contains("shake") || lower.Contains("smoothie"))
            return (70m, 3m, 12m, 1.5m);
        if (lower.Contains("juice"))
            return (45m, 0.5m, 10m, 0.1m);
        if (lower.Contains("soda") || lower.Contains("cola") || lower.Contains("pop") || lower.Contains("lemonade"))
            return (40m, 0m, 10m, 0m);
        if (lower.Contains("coffee") || lower.Contains("latte") || lower.Contains("cappuccino") || lower.Contains("espresso"))
            return (40m, 2m, 4m, 2m);
        if (lower.Contains("tea"))
            return (1m, 0m, 0.3m, 0m);
        if (lower.Contains("beer") || lower.Contains("ale") || lower.Contains("lager"))
            return (43m, 0.5m, 3.5m, 0m);
        if (lower.Contains("wine"))
            return (85m, 0.1m, 2.5m, 0m);
        if (lower.Contains("energy drink") || lower.Contains("energy"))
            return (45m, 0m, 11m, 0m);
        if (lower.Contains("milk"))
            return (60m, 3.3m, 5m, 3.2m);
        if (lower.Contains("salad"))
            return (20m, 1.5m, 3m, 0.3m);
        if (lower.Contains("soup") || lower.Contains("broth"))
            return (35m, 2m, 5m, 1m);
        if (lower.Contains("sandwich") || lower.Contains("wrap") || lower.Contains("sub"))
            return (220m, 10m, 25m, 8m);
        if (lower.Contains("pizza"))
            return (270m, 11m, 33m, 10m);
        if (lower.Contains("burger"))
            return (250m, 14m, 20m, 13m);
        if (lower.Contains("fries") || lower.Contains("chips"))
            return (310m, 3.5m, 40m, 15m);
        if (lower.Contains("cake") || lower.Contains("brownie") || lower.Contains("pastry"))
            return (370m, 5m, 50m, 16m);
        if (lower.Contains("cookie") || lower.Contains("biscuit"))
            return (450m, 5m, 60m, 22m);
        if (lower.Contains("candy") || lower.Contains("sweet") || lower.Contains("gummy"))
            return (380m, 2m, 80m, 5m);
        if (lower.Contains("chip") || lower.Contains("crisp") || lower.Contains("snack"))
            return (530m, 6m, 53m, 33m);
        if (lower.Contains("cereal") || lower.Contains("granola") || lower.Contains("muesli"))
            return (370m, 8m, 70m, 7m);

        // Generic food fallback: roughly balanced macros
        return (150m, 5m, 20m, 5m);
    }

    private static decimal ParseWordNumber(string word) => word.ToLowerInvariant() switch
    {
        "a" or "an" or "one" => 1m,
        "two" or "couple" => 2m,
        "three" => 3m,
        "four" => 4m,
        "five" => 5m,
        "six" => 6m,
        "seven" => 7m,
        "eight" => 8m,
        "nine" => 9m,
        "ten" => 10m,
        "eleven" => 11m,
        "twelve" or "dozen" => 12m,
        "fifteen" => 15m,
        "twenty" => 20m,
        "half" => 0.5m,
        "quarter" => 0.25m,
        "few" => 3m,
        "several" => 4m,
        "some" => 2m,
        _ => 1m
    };

    private static decimal Round(decimal? value, decimal scale) =>
        Math.Round((value ?? 0m) * scale, 1);

    // Split on comma, "and", "plus", "with", "&", "+", newline, semicolon, period-followed-by-space, "then" —
    // captured so SplitIntoSegmentsWithJoins can tell which delimiter joined each pair.
    [GeneratedRegex(@"\s*(,\s*|\s+and\s+|\s+plus\s+|\s+with\s+|\s+then\s+|\s*&\s*|\s*\+\s*|\s*;\s*|\.\s+|\n+)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SplitPatternCapturing();

    // Put longer alternations first to avoid partial matches (e.g. "lbs" before "lb", "ounces" before "oz")
    private const string UnitGroup = @"cups?|tablespoons?|tbsp|teaspoons?|tsp|ounces?|oz|grams?|g|kilograms?|kg|lbs|lb|pounds?|slices?|pieces?|milliliters?|ml|liters?|litres?|l|glass(?:es)?|bowls?|handfuls?|servings?|cans?|bottles?|scoops?|bars?|packets?|strips?|fillets?|patt(?:y|ies)|wings?|thighs?|drumsticks?|breasts?|cloves?|stalks?|sprigs?|lea(?:f|ves)|wedges?|chunks?|rings?|sticks?|pints?|quarts?|gallons?|fl\s?oz";

    [GeneratedRegex(@$"^(?<word>a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|half|quarter|dozen|couple|few|several|some)\s+(?:(?<unit>{UnitGroup})\s+)?(?<food>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WordQuantityPattern();

    [GeneratedRegex(@$"^(?<qty>\d+\.?\d*)\s*(?<unit>{UnitGroup})?\s*(?<food>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericQuantityPattern();

    [GeneratedRegex(@$"^(?:(?<whole>\d+)\s+)?(?<num>\d+)/(?<den>\d+)\s*(?<unit>{UnitGroup})?\s*(?<food>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"^(?:some|about|around|approximately|roughly|maybe|like|just|probably)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFillerWordPattern();

    [GeneratedRegex(@"^(?:(?:for\s+)?(?:breakfast|lunch|dinner|supper|brunch|snack|my\s+snack|my\s+meal)\s+)?(?:i\s+)?(?:just\s+)?(?:had|ate|eaten|consumed|grabbed|munched|snacked\s+on|drank|drunk|downed)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFillerPattern();

    [GeneratedRegex(@"\s*\(([^)]*)\)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ParentheticalPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespacePattern();

    [GeneratedRegex(@"\s+(?:on the side|on top|for dessert|for dinner|for lunch|for breakfast|for supper|for snack|this morning|tonight|yesterday|today|last night|earlier)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingPrepPattern();

    [GeneratedRegex(@"^(?<size>small|mini|tiny|medium|med|large|big|lg|extra[\s-]?large|xl|huge|jumbo)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SizeModifierPattern();

    [GeneratedRegex(@"^(?:and|or)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingAndOrPattern();

}
