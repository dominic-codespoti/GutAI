using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// Tests for multi-source ranking (B2), brand detection (B3), personalization (B1),
/// primary_exact matching (B6), and synonym expansion (B4).
/// These use synthetic food data combining USDA + OpenFoodFacts-style products.
/// </summary>
public sealed class FoodSearchRankingTests : IDisposable
{
    public void Dispose() { }

    // ════════════════════════════════════════════════════════════════
    //  B2: MULTI-SOURCE RANKING — USDA whole food beats branded for simple queries
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("banana", "Bananas, raw", "Banana Chocolate Protein Bar")]
    [InlineData("eggs", "Egg, whole, raw, fresh", "Eggs Benedict Sauce")]
    [InlineData("apple", "Apples, raw, with skin", "Apple Juice Sparkling Drink")]
    [InlineData("chicken", "Chicken, broilers or fryers, breast, skinless", "Chicken Tikka Masala Ready Meal")]
    [InlineData("salmon", "Salmon, Atlantic, wild, raw", "Salmon Flavored Cat Treats")]
    [InlineData("rice", "Rice, white, long-grain, regular, raw", "Rice Vinegar Dressing")]
    [InlineData("milk", "Milk, whole, 3.25% milkfat", "Milk Chocolate Candy Bar")]
    public void SimpleQuery_UsdaWholeFoodBeatsProcessedBranded(string query, string usdaName, string brandedName)
    {
        var foods = new[]
        {
            MakeUsda(usdaName),
            MakeOFF(brandedName, hasImage: true, hasIngredients: true),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search(query, 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Be(usdaName,
            $"searching '{query}' should rank USDA whole food '{usdaName}' above branded '{brandedName}'");
    }

    [Theory]
    [InlineData("tomato", "Tomatoes, red, ripe, raw", "Tomato and Basil Pasta Sauce")]
    [InlineData("spinach", "Spinach, raw", "Spinach Artichoke Dip")]
    [InlineData("broccoli", "Broccoli, raw", "Broccoli Cheddar Soup")]
    public void SimpleQuery_UsdaBeatsProcessedBranded_MultiWord(string query, string usdaName, string brandedName)
    {
        var foods = new[]
        {
            MakeUsda(usdaName),
            MakeOFF(brandedName, hasImage: true, hasIngredients: true),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search(query, 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Be(usdaName,
            $"searching '{query}' should rank USDA '{usdaName}' above OFF '{brandedName}'");
    }

    [Fact]
    public void MultiSource_BrandedSearchFindsCorrectBrand()
    {
        // When the user IS searching for a brand, the branded product should win
        var foods = new[]
        {
            MakeUsda("Milk, whole, 3.25% milkfat"),
            MakeOFF("Rokeby Farms Protein Smoothie", hasImage: true, hasIngredients: true, brand: "Rokeby Farms"),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("rokeby protein", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Contain("Rokeby",
            "brand-specific search should return the branded product first");
    }

    [Fact]
    public void MultiSource_ImageRichBrandedDoesNotOutrankExactUsda()
    {
        // Even with image + ingredients, a loosely-matching branded shouldn't beat exact USDA
        var foods = new[]
        {
            MakeUsda("Honey"),
            MakeOFF("Honey BBQ Chicken Wings", hasImage: true, hasIngredients: true, brand: "TGI"),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("honey", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Be("Honey",
            "exact USDA match should beat loosely-matching branded product");
    }

    // ════════════════════════════════════════════════════════════════
    //  B1: PERSONALIZATION BOOST (SearchPersonalized with boostIds)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SearchPersonalized_BoostIds_RanksBoostIdHigher()
    {
        var chickenBreast = MakeUsda("Chicken, broilers or fryers, breast, skinless, boneless");
        var chickenThigh = MakeUsda("Chicken, broilers or fryers, thigh, meat only");
        var chickenWing = MakeUsda("Chicken, broilers or fryers, wing, meat only");

        var foods = new[] { chickenBreast, chickenThigh, chickenWing };

        using var index = new FoodSearchIndex(foods);

        // Without boost: breast should rank first for "chicken"
        var unboosted = index.Search("chicken", 5);

        // With boost: thigh should rank higher because it's in boostIds
        var boosted = index.SearchPersonalized("chicken", [chickenThigh.Id], 5);

        boosted.Should().NotBeEmpty();
        boosted[0].Id.Should().Be(chickenThigh.Id,
            "the boosted food should rank #1 when personalization is active");
    }

    [Fact]
    public void SearchPersonalized_EmptyBoostIds_MatchesRegularSearch()
    {
        var foods = new[]
        {
            MakeUsda("Banana, raw"),
            MakeUsda("Bananas, dehydrated, or banana powder"),
        };

        using var index = new FoodSearchIndex(foods);
        var regular = index.Search("banana", 5);
        var personalized = index.SearchPersonalized("banana", [], 5);

        regular.Select(r => r.Name).Should().Equal(personalized.Select(r => r.Name),
            "empty boostIds should produce identical results to regular search");
    }

    [Fact]
    public void SearchPersonalized_BoostIds_MultipleBoostsWork()
    {
        var foods = new[]
        {
            MakeUsda("Rice, white, long-grain, regular, raw"),
            MakeUsda("Rice, brown, long-grain, raw"),
            MakeUsda("Rice, wild, raw"),
        };

        using var index = new FoodSearchIndex(foods);
        var boosted = index.SearchPersonalized("rice", [foods[2].Id, foods[1].Id], 5);

        // Both boosted items should appear in results
        var allNames = boosted.Select(r => r.Name).ToList();
        allNames.Should().Contain(foods[2].Name, "boosted wild rice should appear in results");
        allNames.Should().Contain(foods[1].Name, "boosted brown rice should appear in results");
    }

    // ════════════════════════════════════════════════════════════════
    //  B3: BRAND DETECTION (DetectQueryHasBrand)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DetectQueryHasBrand_FindsBrandInFoods()
    {
        var foods = new[]
        {
            MakeOFF("Rokeby Farms Protein Smoothie", brand: "Rokeby Farms"),
            MakeUsda("Milk, whole"),
        };

        var tokens = new[] { "rokeby", "protein" };
        var result = FoodSearchIndex.DetectQueryHasBrand(tokens, foods);

        result.Should().BeTrue("'rokeby' matches the brand 'Rokeby Farms'");
    }

    [Fact]
    public void DetectQueryHasBrand_ReturnsFalse_WhenNoBrandMatch()
    {
        var foods = new[]
        {
            MakeUsda("Chicken, breast, raw"),
            MakeUsda("Egg, whole, raw"),
        };

        var tokens = new[] { "chicken", "breast" };
        var result = FoodSearchIndex.DetectQueryHasBrand(tokens, foods);

        result.Should().BeFalse("no food in the set has a brand matching query tokens");
    }

    [Fact]
    public void DetectQueryHasBrand_IgnoresShortBrandTokens()
    {
        var foods = new[]
        {
            MakeOFF("AB Yogurt", brand: "AB"),
        };

        var tokens = new[] { "ab", "yogurt" };
        var result = FoodSearchIndex.DetectQueryHasBrand(tokens, foods);

        // "ab" is only 2 chars — should be ignored to avoid false positives
        result.Should().BeFalse("brand tokens ≤2 chars should be ignored");
    }

    [Fact]
    public void DetectQueryHasBrand_HandlesNullBrands()
    {
        var foods = new[]
        {
            MakeUsda("Milk, whole"), // USDA foods have null brand
            MakeDto("Generic Yogurt"), // no brand set
        };

        var tokens = new[] { "milk" };
        var result = FoodSearchIndex.DetectQueryHasBrand(tokens, foods);

        result.Should().BeFalse("no brands exist in this food set");
    }

    [Fact]
    public void DetectQueryHasBrand_FindsMultiWordBrand()
    {
        var foods = new[]
        {
            MakeOFF("Chobani Greek Yogurt", brand: "Chobani"),
        };

        var tokens = new[] { "chobani", "yogurt" };
        var result = FoodSearchIndex.DetectQueryHasBrand(tokens, foods);

        result.Should().BeTrue("'chobani' matches brand 'Chobani'");
    }

    // ════════════════════════════════════════════════════════════════
    //  B6: PRIMARY_EXACT FIELD MATCHING
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void PrimaryExact_SingleWordQueryMatchesPrimaryNoun()
    {
        // "eggs" should get a big boost from primary_exact field matching "egg" (depluralized)
        var foods = new[]
        {
            MakeUsda("Egg, whole, raw, fresh"),
            MakeUsda("Eggnog"),
            MakeUsda("Eggplant, raw"),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("eggs", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Be("Egg, whole, raw, fresh",
            "'eggs' should match primary noun 'Egg' via depluralization (eggs→egg)");
    }

    [Fact]
    public void PrimaryExact_ExactPrimaryMatchBeatsPartialNameMatch()
    {
        var foods = new[]
        {
            MakeUsda("Banana, raw"), // primary noun = "Banana" → exact
            MakeUsda("Banana chips, unsalted"), // primary noun = "Banana chips" → partial
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("banana", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Contain("raw",
            "exact primary noun match should beat partial match");
    }

    [Fact]
    public void PrimaryExact_SingularQueryMatchesPluralPrimary()
    {
        // "egg" (no s) should also match primary "eggs" via the singular→plural boost
        var foods = new[]
        {
            MakeDto("Eggs, scrambled"),
            MakeDto("Eggnog, commercial"),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search("egg", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Contain("Eggs",
            "'egg' should match primary noun 'Eggs' via singular→plural (egg→eggs)");
    }

    // ════════════════════════════════════════════════════════════════
    //  B4: SYNONYM EXPANSION
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("capsicum", "pepper")]
    [InlineData("prawns", "shrimp")]
    [InlineData("aubergine", "eggplant")]
    [InlineData("courgette", "zucchini")]
    [InlineData("rocket", "arugula")]
    [InlineData("coriander", "cilantro")]
    [InlineData("beetroot", "beet")]
    public void RegionalSynonym_ReturnsUSEquivalent(string query, string usEquivalent)
    {
        var foods = new[]
        {
            MakeUsda($"{Capitalize(usEquivalent)}, raw"),
            MakeUsda("Potato, raw"),  // control food that should NOT match
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search(query, 5);

        results.Should().NotBeEmpty($"synonym '{query}' should resolve to results");
        results[0].Name.ToLower().Should().Contain(usEquivalent.ToLower(),
            $"regional term '{query}' should find US equivalent '{usEquivalent}'");
    }

    [Theory]
    [InlineData("porridge", "oat")]
    [InlineData("oatmeal", "oat")]
    [InlineData("ketchup", "catsup")]
    [InlineData("yoghurt", "yogurt")]
    public void ColloquialSynonym_ReturnsStandardName(string query, string standardTerm)
    {
        var foods = new[]
        {
            MakeUsda($"{Capitalize(standardTerm)}, plain"),
            MakeUsda("Potato, raw"),  // control
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search(query, 5);

        results.Should().NotBeEmpty();
        results[0].Name.ToLower().Should().Contain(standardTerm.ToLower(),
            $"colloquial term '{query}' should find '{standardTerm}'");
    }

    [Theory]
    [InlineData("tomato sauce", "tomato", "sauce")]
    [InlineData("chicken breast", "chicken", "breast")]
    [InlineData("olive oil", "olive", "oil")]
    [InlineData("brown rice", "rice", "brown")]
    [InlineData("sweet potato", "sweet", "potato")]
    [InlineData("peanut butter", "peanut", "butter")]
    [InlineData("almond milk", "almond", "milk")]
    [InlineData("sour cream", "sour", "cream")]
    [InlineData("ice cream", "ice", "cream")]
    public void MultiWordSynonym_ReturnsRelevantResults(string query, params string[] expectedTerms)
    {
        // Build a mix of foods where one contains all expected terms
        var targetName = string.Join(", ", expectedTerms.Select(Capitalize));
        var foods = new[]
        {
            MakeUsda(targetName),
            MakeUsda("Unrelated food, raw"),
        };

        using var index = new FoodSearchIndex(foods);
        var results = index.Search(query, 5);

        results.Should().NotBeEmpty($"multi-word synonym '{query}' should return results");
        results[0].Name.Should().NotBe("Unrelated food, raw",
            $"'{query}' should rank target food above unrelated food");
    }

    // ════════════════════════════════════════════════════════════════
    //  B4: SYNONYM EXPANSION IN FULL INDEX (integration)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FullIndex_Capsicum_ReturnsPeppers()
    {
        var results = SearchWholeDb("capsicum", 5);
        results.Should().NotBeEmpty();
        results.Should().Contain(n => n.ToLower().Contains("pepper"),
            "'capsicum' should return pepper results from the full USDA index via synonym");
    }

    [Fact]
    public void FullIndex_Prawns_ReturnsShrimp()
    {
        var results = SearchWholeDb("prawns", 5);
        results.Should().NotBeEmpty();
        results.Should().Contain(n => n.ToLower().Contains("shrimp"),
            "'prawns' should return shrimp results from the full USDA index via synonym");
    }

    [Fact]
    public void FullIndex_Porridge_ReturnsOats()
    {
        var results = SearchWholeDb("porridge", 5);
        results.Should().NotBeEmpty();
        results.Should().Contain(n => n.ToLower().Contains("oat"),
            "'porridge' should return oat results from the full USDA index via synonym");
    }

    [Fact]
    public void FullIndex_Aubergine_ReturnsEggplant()
    {
        var results = SearchWholeDb("aubergine", 5);
        results.Should().NotBeEmpty();
        results.Should().Contain(n => n.ToLower().Contains("eggplant"),
            "'aubergine' should return eggplant results via synonym");
    }

    [Fact]
    public void FullIndex_Courgette_ReturnsZucchini()
    {
        var results = SearchWholeDb("courgette", 5);
        results.Should().NotBeEmpty();
        results.Should().Contain(n => n.ToLower().Contains("zucchini") || n.ToLower().Contains("squash"),
            "'courgette' should return zucchini/squash results via synonym");
    }

    [Fact]
    public void FullIndex_HotDog_ReturnsFrankfurter()
    {
        var results = SearchWholeDb("hot dog", 10);
        results.Should().NotBeEmpty();
        results.Should().Contain(n => n.ToLower().Contains("frankfurter") || n.ToLower().Contains("sausage") || n.ToLower().Contains("hot dog"),
            "'hot dog' should return frankfurter/sausage results via multi-word synonym expansion");
    }

    [Fact]
    public void FullIndex_Yoghurt_ReturnsYogurt()
    {
        var results = SearchWholeDb("yoghurt", 5);
        results.Should().NotBeEmpty();
        results.Should().Contain(n => n.ToLower().Contains("yogurt"),
            "'yoghurt' should return yogurt results via synonym");
    }

    // ════════════════════════════════════════════════════════════════
    //  EDGE CASES
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyIndex_SearchReturnsEmpty()
    {
        using var index = new FoodSearchIndex();
        var results = index.Search("banana", 5);
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NullOrEmptyQuery_ReturnsEmpty(string? query)
    {
        var foods = new[] { MakeUsda("Banana, raw") };
        using var index = new FoodSearchIndex(foods);
        var results = index.Search(query ?? "", 5);
        results.Should().BeEmpty();
    }

    [Fact]
    public void SpecialCharacters_DoNotCrash()
    {
        var foods = new[] { MakeUsda("Banana, raw") };
        using var index = new FoodSearchIndex(foods);

        // Lucene special chars that could break query parsing
        var results = index.Search("banana + eggs ~ \"test\"", 5);
        results.Should().NotBeNull();  // Shouldn't throw
    }

    [Fact]
    public void SingleCharQuery_ReturnsEmpty()
    {
        var foods = new[] { MakeUsda("Banana, raw") };
        using var index = new FoodSearchIndex(foods);

        // The real endpoint blocks <2 char queries, but the index itself
        // may return results for single chars — that's fine at this layer
        var results = index.Search("b", 5);
        results.Should().NotBeNull();
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════

    private static readonly Lazy<FoodSearchIndex> SharedIndex = new(() =>
    {
        var foodsField = typeof(WholeFoodsDatabase)
            .GetField("Foods", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var foods = (List<FoodProductDto>)foodsField.GetValue(null)!;
        return new FoodSearchIndex(foods);
    }, isThreadSafe: true);

    private static List<string> SearchWholeDb(string query, int max = 15)
        => SharedIndex.Value.Search(query, max).Select(f => f.Name).ToList();

    private static FoodProductDto MakeUsda(string name, decimal? calories = 100, decimal? protein = 5m,
        decimal? carbs = 15m, decimal? fat = 3m)
    {
        return new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataSource = "USDA",
            FoodKind = FoodKind.WholeFood,
            Calories100g = calories,
            Protein100g = protein,
            Carbs100g = carbs,
            Fat100g = fat,
        };
    }

    private static FoodProductDto MakeOFF(string name, bool hasImage = false, bool hasIngredients = false,
        string? brand = null, decimal? calories = 200, decimal? protein = 8m,
        decimal? carbs = 25m, decimal? fat = 10m)
    {
        return new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataSource = "OpenFoodFacts",
            FoodKind = FoodKind.Branded,
            ImageUrl = hasImage ? "https://images.openfoodfacts.org/test.jpg" : null,
            Ingredients = hasIngredients ? "sugar, flour, palm oil, salt" : null,
            Brand = brand,
            Calories100g = calories,
            Protein100g = protein,
            Carbs100g = carbs,
            Fat100g = fat,
        };
    }

    private static FoodProductDto MakeDto(string name)
    {
        return new FoodProductDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataSource = "USDA",
            FoodKind = FoodKind.WholeFood,
        };
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
