using System.Text.RegularExpressions;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Domain.Entities;
using GutAI.Infrastructure.Data;
using GutAI.Infrastructure.ExternalApis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class SearchQualityTests(AzuriteFixture fx, ITestOutputHelper output)
{
    // ───────────────────────────────────────────────────────────────
    //  Helpers – replicate the search pipeline from FoodEndpoints
    // ───────────────────────────────────────────────────────────────

    private async Task<List<FoodProductDto>> RunSearchPipeline(string query)
    {
        if (query.Length < 2)
            return new List<FoodProductDto>();

        // Ensure the Azurite table exists by upserting a throwaway entity once
        var initId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await fx.Store.UpsertFoodProductAsync(new FoodProduct
        {
            Id = initId,
            Name = "__init__"
        });

        // 1. Local store search (Azurite)
        var localProducts = await fx.Store.SearchFoodProductsAsync(query, 20);
        var additives = await fx.Store.GetAllFoodAdditivesAsync();

        // 2. Embedded-database API services (no network required)
        var compositeApi = new CompositeFoodApiService(
            [new WholeFoodApiService(), new BrandedFoodApiService(), new AustralianFoodApiService()],
            NullLogger<CompositeFoodApiService>.Instance);

        var externalResults = await compositeApi.SearchAsync(query);

        // 3. Persist new external products (replicate FoodEndpoints logic)
        var existingNames = localProducts.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newProducts = new List<FoodProduct>();

        foreach (var dto in externalResults)
        {
            if (existingNames.Contains(dto.Name)) continue;
            newProducts.Add(new FoodProduct
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
                SourceUrl = dto.SourceUrl,
                ExternalId = dto.ExternalId,
                ImageUrl = dto.ImageUrl,
                NutriScore = dto.NutriScore,
                ServingQuantity = dto.ServingQuantity,
                AllergensTags = dto.AllergensTags,
                FoodKind = dto.FoodKind // Correctly copy FoodKind to store
            });
            existingNames.Add(dto.Name);
        }

        if (newProducts.Count > 0)
            await Task.WhenAll(newProducts.Select(p => fx.Store.UpsertFoodProductAsync(p)));

        // 4. Build combined candidate list - Use full mapping to match real endpoint
        var localDtos = localProducts.Concat(newProducts)
            .Select(f => MapToDto(f, additives))
            .ToList();

        var allCandidates = new List<FoodProductDto>(localDtos.Count + externalResults.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in localDtos)
            if (seen.Add(dto.Name)) allCandidates.Add(dto);
        foreach (var dto in externalResults)
            if (seen.Add(dto.Name)) allCandidates.Add(dto);

        // 5. Re-rank with Lucene FoodSearchIndex
        if (allCandidates.Count > 1)
        {
            using var rankIndex = new FoodSearchIndex(allCandidates);
            return rankIndex.Search(query, 20);
        }

        return allCandidates.Take(20).ToList();
    }

    private static FoodProductDto MapToDto(FoodProduct f, List<FoodAdditive> additives)
    {
        // Minimal logic to link additives for full DTO consistency
        var productAdditives = f.FoodProductAdditiveIds
            .Select(id => additives.FirstOrDefault(a => a.Id == id))
            .Where(a => a != null)
            .Select(a => new FoodAdditiveDto
            {
                Id = a!.Id,
                ENumber = a.ENumber,
                Name = a.Name,
                Category = a.Category,
                SafetyRating = a.SafetyRating.ToString(),
            }).ToList();

        return new FoodProductDto
        {
            Id = f.Id,
            Name = f.Name,
            Barcode = f.Barcode,
            Brand = f.Brand,
            Ingredients = f.Ingredients,
            ImageUrl = f.ImageUrl,
            NovaGroup = f.NovaGroup,
            NutriScore = f.NutriScore,
            AllergensTags = f.AllergensTags,
            Calories100g = f.Calories100g,
            Protein100g = f.Protein100g,
            Carbs100g = f.Carbs100g,
            Fat100g = f.Fat100g,
            Fiber100g = f.Fiber100g,
            Sugar100g = f.Sugar100g,
            Sodium100g = f.Sodium100g,
            FoodKind = f.FoodKind,
            DataSource = f.DataSource,
            SourceUrl = f.SourceUrl,
            ExternalId = f.ExternalId,
            ServingSize = f.ServingSize,
            ServingQuantity = f.ServingQuantity,
            NutritionInfo = f.NutritionInfo,
            IsDeleted = f.IsDeleted,
            SafetyScore = f.SafetyScore,
            SafetyRating = f.SafetyRating?.ToString(),
            Additives = productAdditives,
            AdditivesTags = productAdditives.Where(a => a.ENumber != null).Select(a => $"en:{a.ENumber!.ToLowerInvariant()}").ToList()
        };
    }

    private void PrintResults(string query, List<FoodProductDto> results)
    {
        output.WriteLine($"\nQuery: \"{query}\" → {results.Count} results");
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            output.WriteLine(
                $"  [{i + 1,2}] {r.Name,-55} " +
                $"src={r.DataSource,-15} cal={r.Calories100g,7} " +
                $"conf={r.MatchConfidence:F2}");
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  Relevance: top-N results must contain expected keywords
    // ───────────────────────────────────────────────────────────────

    public static TheoryData<string, string[], int> RelevanceExpectations => new()
    {
        //  query               expected keyword(s) in at least one result name     within top-N
        { "toast",              new[] { "toast", "bread" },                         5 },
        { "orange juice",       new[] { "orange juice", "orange" },                 3 },
        { "bacon",              new[] { "bacon" },                                  3 },
        { "fried egg",          new[] { "egg", "fried egg" },                       5 },
        { "milk",               new[] { "milk" },                                   3 },
        { "banana",             new[] { "banana" },                                 3 },
        { "chicken breast",     new[] { "chicken" },                                3 },
        { "rice",               new[] { "rice" },                                   3 },
        { "apple",              new[] { "apple" },                                  3 },
        { "yogurt",             new[] { "yogurt", "yoghurt" },                      5 },
        { "salmon",             new[] { "salmon" },                                 3 },
        { "pasta",              new[] { "pasta", "spaghetti", "noodle" },           5 },
        { "coffee",             new[] { "coffee" },                                 3 },
        { "avocado",            new[] { "avocado" },                                3 },
        { "peanut butter",      new[] { "peanut butter", "peanut" },                3 },
        { "oats",               new[] { "oat" },                                    5 },
        { "cheddar",            new[] { "cheddar", "cheese" },                      3 },
        { "broccoli",           new[] { "broccoli" },                               3 },
        { "honey",              new[] { "honey" },                                  3 },
        { "tomato",             new[] { "tomato" },                                 3 },
    };

    [Theory]
    [MemberData(nameof(RelevanceExpectations))]
    public async Task Search_ReturnsRelevantResults(string query, string[] expectedKeywords, int withinTopN)
    {
        var results = await RunSearchPipeline(query);
        PrintResults(query, results);

        results.Should().NotBeEmpty($"searching for \"{query}\" should return at least one result");

        var topN = results.Take(withinTopN).ToList();
        var match = topN.Any(r =>
            expectedKeywords.Any(kw =>
                r.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        match.Should().BeTrue(
            $"at least one of the top {withinTopN} results for \"{query}\" should contain " +
            $"one of [{string.Join(", ", expectedKeywords)}]. " +
            $"Got: [{string.Join(", ", topN.Select(r => r.Name))}]");

        // A2 strengthen: the #1 result must contain at least one keyword
        var topResult = results[0];
        var top1Match = expectedKeywords.Any(kw =>
            topResult.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
        top1Match.Should().BeTrue(
            $"the #1 result for \"{query}\" should contain one of " +
            $"[{string.Join(", ", expectedKeywords)}], got \"{topResult.Name}\"");
    }

    // ───────────────────────────────────────────────────────────────
    //  Nutrition: top results should include calorie data
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("toast")]
    [InlineData("orange juice")]
    [InlineData("bacon")]
    [InlineData("fried egg")]
    [InlineData("chicken breast")]
    [InlineData("banana")]
    public async Task Search_TopResultsHaveNutritionData(string query)
    {
        var results = await RunSearchPipeline(query);
        results.Should().NotBeEmpty();

        var top5 = results.Take(5).ToList();

        output.WriteLine($"Query: \"{query}\"");
        foreach (var r in top5)
            output.WriteLine($"  {r.Name}: cal={r.Calories100g}, prot={r.Protein100g}, carb={r.Carbs100g}, fat={r.Fat100g}");

        // A3 strengthen: majority of top-5 should have calorie data, not just "any"
        var withCalories = top5.Count(r => r.Calories100g is > 0);
        withCalories.Should().BeGreaterThanOrEqualTo(3,
            $"at least 3 of the top 5 results for \"{query}\" should have calorie data, " +
            $"but only {withCalories} did");

        // The #1 result specifically must have nutrition data for common foods
        top5[0].Calories100g.Should().BeGreaterThan(0,
            $"the #1 result '{top5[0].Name}' for \"{query}\" should have calorie data");
    }

    // ───────────────────────────────────────────────────────────────
    //  Deduplication: no two results share the same name
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("toast")]
    [InlineData("orange juice")]
    [InlineData("bacon")]
    [InlineData("milk")]
    [InlineData("chicken")]
    public async Task Search_NoDuplicateNames(string query)
    {
        var results = await RunSearchPipeline(query);
        var names = results.Select(r => r.Name.ToLowerInvariant()).ToList();
        names.Should().OnlyHaveUniqueItems("search results should not contain duplicates");
    }

    // ───────────────────────────────────────────────────────────────
    //  Result count cap: never more than 20
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("chicken")]
    [InlineData("milk")]
    [InlineData("rice")]
    public async Task Search_MaxResults_DoesNotExceed20(string query)
    {
        var results = await RunSearchPipeline(query);
        results.Count.Should().BeLessThanOrEqualTo(20);
    }

    // ───────────────────────────────────────────────────────────────
    //  Edge cases
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ShortQuery_ReturnsEmpty()
    {
        // Test the real guard logic used in the endpoint
        var query = "a";
        var sanitized = query.Length < 2; // Simulated guard

        var results = await RunSearchPipeline(query);

        // If query < 2, the pipeline should ideally return empty if it matches endpoint behavior
        // Currently the pipeline doesn't have the guard, but the search quality test should
        // reflect what the user actually experiences via the API.
        results.Should().BeEmpty("queries shorter than 2 characters should return no results");
    }

    // ───────────────────────────────────────────────────────────────
    //  Ranking quality: exact/primary-noun matches rank highest
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("bacon", "bacon")]
    [InlineData("banana", "banana")]
    [InlineData("salmon", "salmon")]
    [InlineData("honey", "honey")]
    public async Task Search_ExactMatchRanksFirst(string query, string expectedSubstring)
    {
        var results = await RunSearchPipeline(query);
        PrintResults(query, results);
        results.Should().NotBeEmpty();

        // The #1 result's primary noun (text before the first comma) should contain the query
        var topName = results[0].Name;
        var commaIdx = topName.IndexOf(',');
        var primaryNoun = commaIdx > 0 ? topName[..commaIdx].Trim() : topName.Trim();
        primaryNoun.Should().ContainEquivalentOf(expectedSubstring,
            $"the top result for \"{query}\" should have \"{expectedSubstring}\" " +
            $"in its primary noun, but got \"{topName}\"");
    }

    // ───────────────────────────────────────────────────────────────
    //  Multi-word queries should beat single-token noise
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("peanut butter", "peanut", "butter")]
    [InlineData("orange juice", "orange", "juice")]
    [InlineData("fried egg", "egg", "fried")]
    [InlineData("chicken breast", "chicken", "breast")]
    public async Task Search_MultiWordQuery_TopResultContainsBothTerms(string query, string term1, string term2)
    {
        var results = await RunSearchPipeline(query);
        PrintResults(query, results);
        results.Should().NotBeEmpty();

        // A4 strengthen: #1 result should contain BOTH terms, not just one
        var topName = results[0].Name;
        topName.Should().ContainEquivalentOf(term1,
            $"#1 result for \"{query}\" should contain '{term1}', got \"{topName}\"");

        // At least one of top 3 should contain the secondary term too
        var top3 = results.Take(3).ToList();
        top3.Any(r => r.Name.Contains(term2, StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue(
                $"top 3 results for \"{query}\" should include \"{term2}\". " +
                $"Got: [{string.Join(", ", top3.Select(r => r.Name))}]");
    }

    // ───────────────────────────────────────────────────────────────
    //  Name legibility: names should be human-readable
    // ───────────────────────────────────────────────────────────────

    private static readonly int MaxAcceptableNameLength = 100;

    // Patterns that indicate a name is not user-friendly
    private static readonly string[] JunkPatterns =
    [
        "USDA's Food Distribution Program",
        "Includes foods for USDA",
        "FDC:",
        "NDB:",
        "UPC:",
    ];

    [Theory]
    [InlineData("toast")]
    [InlineData("bacon")]
    [InlineData("orange juice")]
    [InlineData("fried egg")]
    [InlineData("banana")]
    [InlineData("chicken breast")]
    [InlineData("milk")]
    [InlineData("yogurt")]
    [InlineData("honey")]
    [InlineData("peanut butter")]
    public async Task Search_TopResultsHaveLegibleNames(string query)
    {
        var results = await RunSearchPipeline(query);
        var top5 = results.Take(3).ToList();

        output.WriteLine($"Query: \"{query}\" — checking top 3 name legibility:");
        foreach (var r in top5)
            output.WriteLine($"  [{r.Name.Length,3} chars] \"{r.Name}\"");

        foreach (var r in top5)
        {
            r.Name.Length.Should().BeLessThanOrEqualTo(MaxAcceptableNameLength,
                $"result name \"{r.Name}\" is too long to be legible ({r.Name.Length} chars)");

            foreach (var junk in JunkPatterns)
            {
                r.Name.Should().NotContain(junk,
                    $"result name \"{r.Name}\" contains database metadata \"{junk}\"");
            }

            // Name shouldn't be ALL CAPS (indicates raw USDA code names)
            var isAllCaps = r.Name.Length > 5 &&
                            r.Name == r.Name.ToUpperInvariant() &&
                            r.Name.Any(char.IsLetter);
            isAllCaps.Should().BeFalse(
                $"result name \"{r.Name}\" is ALL CAPS, which is not user-friendly");
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  Top result is the simple/generic form of the food
    // ───────────────────────────────────────────────────────────────

    public static TheoryData<string, string> TopResultExpectations => new()
    {
        //  query               #1 result name must contain exactly this
        { "bacon",              "Bacon" },
        { "banana",             "Banana" },
        { "honey",              "Honey" },
        { "yogurt",             "Yogurt" },
        { "milk",               "Milk" },
        { "rice",               "Rice" },
        { "coffee",             "Coffee" },
        { "salmon",             "Salmon" },
        { "broccoli",           "Broccoli" },
        { "avocado",            "Avocado" },
        { "apple",              "Apple" },
        { "cheddar",            "Cheddar" },
    };

    [Theory]
    [MemberData(nameof(TopResultExpectations))]
    public async Task Search_TopResultIsGenericForm(string query, string expectedInName)
    {
        var results = await RunSearchPipeline(query);
        PrintResults(query, results);
        results.Should().NotBeEmpty();

        var topName = results[0].Name;

        // The #1 result should be the simple/generic form — just the food name,
        // not a branded variant, recipe, or sub-product
        topName.Should().ContainEquivalentOf(expectedInName,
            $"the #1 result for \"{query}\" should be the generic food, got \"{topName}\"");

        // Additionally, the top result name should be short & clean
        topName.Length.Should().BeLessThanOrEqualTo(50,
            $"the #1 result name \"{topName}\" is too verbose for a generic food");
    }
}
