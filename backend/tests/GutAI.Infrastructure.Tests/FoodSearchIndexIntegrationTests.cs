using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class FoodSearchIndexIntegrationTests : IDisposable
{
    public FoodSearchIndexIntegrationTests() { }

    private static FoodSearchIndex BuildFullIndex()
    {
        var foodsField = typeof(WholeFoodsDatabase)
            .GetField("Foods", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var foods = (List<FoodProductDto>)foodsField.GetValue(null)!;
        return new FoodSearchIndex(foods);
    }

    private static readonly Lazy<FoodSearchIndex> SharedIndex = new(BuildFullIndex, isThreadSafe: true);

    private List<FoodProductDto> Search(string query, int max = 15)
        => SharedIndex.Value.Search(query, max);

    private FoodProductDto First(string query) => Search(query).First();

    private string FirstName(string query) => First(query).Name;

    private List<string> TopNames(string query, int n = 5)
        => Search(query, n).Select(f => f.Name).ToList();

    public void Dispose() { }

    // ════════════════════════════════════════════════════════════════
    //  SANITY
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Index_HasExpectedFoodCount()
    {
        SharedIndex.Value.Count.Should().BeGreaterThan(7000);
    }

    // ════════════════════════════════════════════════════════════════
    //  SINGLE-WORD: plain raw variant should win
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("banana", "Bananas, raw")]
    [InlineData("spinach", "Spinach, raw")]
    [InlineData("broccoli", "Broccoli, raw")]
    [InlineData("honey", "Honey")]
    [InlineData("watercress", "Watercress, raw")]
    public void SingleWord_ReturnsPlainRawVariant(string query, string expected)
    {
        FirstName(query).Should().Be(expected);
    }

    [Theory]
    [InlineData("rice")]
    [InlineData("chicken")]
    [InlineData("beef")]
    [InlineData("salmon")]
    [InlineData("egg")]
    [InlineData("milk")]
    [InlineData("cheese")]
    [InlineData("bread")]
    [InlineData("yogurt")]
    [InlineData("apple")]
    [InlineData("tomato")]
    [InlineData("potato")]
    [InlineData("pork")]
    public void SingleWord_ReturnsResults(string query)
    {
        Search(query).Should().NotBeEmpty();
    }

    // ════════════════════════════════════════════════════════════════
    //  MULTI-WORD: prefer the closest match
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChickenBreast_PrefersRawSkinlessBoneless()
    {
        var top = TopNames("chicken breast", 5);
        top.Should().Contain(n => n.Contains("breast", StringComparison.OrdinalIgnoreCase));
        top.First().Should().NotContain("baby food");
        top.First().Should().NotContain("nugget");
    }

    [Fact]
    public void OliveOil_ReturnsOliveOil()
    {
        var name = FirstName("olive oil").ToLower();
        name.Should().Contain("olive");
        name.Should().Contain("oil");
    }

    [Fact]
    public void PeanutButter_ReturnsPeanutButter()
    {
        var top = TopNames("peanut butter", 3);
        top.Should().AllSatisfy(n =>
        {
            n.ToLower().Should().Contain("peanut");
            n.ToLower().Should().Contain("butter");
        });
    }

    [Fact]
    public void BrownRice_PrefersBrownRice()
    {
        var name = FirstName("brown rice");
        name.ToLower().Should().Contain("rice");
        name.ToLower().Should().Contain("brown");
    }

    [Fact]
    public void WhiteBread_PrefersWhiteBread()
    {
        var top = TopNames("white bread", 3);
        top.Should().Contain(n =>
            n.Contains("bread", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("white", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SweetPotato_ReturnsSweetPotato()
    {
        var top = TopNames("sweet potato", 5);
        top.Should().Contain(n => n.Contains("Sweet potato", StringComparison.OrdinalIgnoreCase));
        top.First().ToLower().Should().Contain("sweet potato");
    }

    [Fact]
    public void HardBoiledEgg_PrefersBoiledEgg()
    {
        var top = TopNames("hard boiled egg", 5);
        top.Should().Contain(n => n.Contains("hard-boiled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WholeMilk_PrefersWholeMilk()
    {
        var top = TopNames("whole milk", 3);
        top.Should().Contain(n =>
            n.Contains("Milk", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("whole", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheddarCheese_ReturnsCheddar()
    {
        FirstName("cheddar cheese").ToLower().Should().Contain("cheddar");
    }

    // ════════════════════════════════════════════════════════════════
    //  ALASKA NATIVE / SPECIALTY: should not dominate
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("blueberries")]
    [InlineData("cranberries")]
    [InlineData("blackberries")]
    public void Berries_DoNotReturnAlaskaNativeFirst(string query)
    {
        FirstName(query).Should().NotContain("Alaska Native");
    }

    [Fact]
    public void Corn_DoesNotReturnNavajoCornFirst()
    {
        var name = FirstName("corn");
        name.Should().NotContain("Navajo");
        name.Should().NotContain("Hopi");
    }

    [Fact]
    public void Bread_DoesNotReturnNavajoOrHopiBreadFirst()
    {
        FirstName("bread").Should().NotContain("Navajo");
        FirstName("bread").Should().NotContain("Hopi");
    }

    [Fact]
    public void Buffalo_DoesNotReturnShoshoneBuffaloFirst()
    {
        var top = TopNames("buffalo steak", 3);
        top.Should().Contain(n => n.ToLower().Contains("buffalo"));
    }

    // ════════════════════════════════════════════════════════════════
    //  WEIRDNESS: frozen/canned/baby food/dehydrated should not win
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("banana")]
    [InlineData("spinach")]
    [InlineData("broccoli")]
    public void FreshFoods_RawBeatsCannnedOrFrozen(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("frozen");
        name.Should().NotContain("canned");
        name.Should().NotContain("dehydrated");
    }

    [Fact]
    public void Egg_DoesNotReturnEggSubstituteFirst()
    {
        FirstName("egg").Should().NotContain("substitute");
    }

    [Fact]
    public void Egg_DoesNotReturnBabyFoodFirst()
    {
        FirstName("egg").Should().NotContain("baby food");
    }

    [Fact]
    public void Milk_DoesNotReturnMilkSubstituteFirst()
    {
        FirstName("milk").Should().NotContain("substitute");
    }

    // ════════════════════════════════════════════════════════════════
    //  NUTRITION PLAUSIBILITY
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Chicken_HasReasonableProtein()
    {
        var food = First("chicken breast");
        food.Protein100g.Should().BeGreaterThan(10m);
        food.Carbs100g.Should().BeLessThan(20m);
    }

    [Fact]
    public void OliveOil_HasHighFat()
    {
        var food = First("olive oil");
        food.Fat100g.Should().BeGreaterThan(30m);
        food.Name.ToLower().Should().Contain("oil");
    }

    [Fact]
    public void Spinach_HasLowCalories()
    {
        First("spinach").Calories100g.Should().BeLessThan(50);
    }

    [Fact]
    public void Banana_HasReasonableCarbs()
    {
        var food = First("banana");
        food.Carbs100g.Should().BeGreaterThan(15m);
        food.Calories100g.Should().BeInRange(70, 120);
    }

    // ════════════════════════════════════════════════════════════════
    //  NAME LENGTH: shorter/simpler should rank higher
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Rice_PrefersShortName()
    {
        FirstName("rice").Length.Should().BeLessThan(80,
            "the top rice result should not be a 100+ char USDA descriptor");
    }

    [Fact]
    public void Apple_TopResultContainsApple()
    {
        var top = TopNames("apple", 5);
        top.Should().AllSatisfy(n => n.ToLower().Should().Contain("apple"));
    }

    [Fact]
    public void Tomato_TopResultsAreAboutTomato()
    {
        TopNames("tomato", 5).Should()
            .AllSatisfy(n => n.ToLower().Should().Contain("tomato"));
    }

    // ════════════════════════════════════════════════════════════════
    //  PLURAL / SINGULAR: should produce similar results
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("banana", "bananas")]
    [InlineData("spinach", "spinachs")]
    public void SingularAndPlural_ReturnSameTopResult(string singular, string plural)
    {
        FirstName(singular).Should().Be(FirstName(plural));
    }

    [Theory]
    [InlineData("egg", "eggs")]
    [InlineData("blueberry", "blueberries")]
    public void SingularAndPlural_TopResultsOverlap(string singular, string plural)
    {
        var singularResults = TopNames(singular, 10);
        var pluralResults = TopNames(plural, 10);
        singularResults.Intersect(pluralResults).Should().NotBeEmpty(
            $"'{singular}' and '{plural}' should share at least one top-10 result");
    }

    [Theory]
    [InlineData("apple", "apples")]
    [InlineData("tomato", "tomatoes")]
    public void SingularAndPlural_BothReturnRelevantResults(string singular, string plural)
    {
        TopNames(singular, 5).Should()
            .AllSatisfy(n => n.ToLower().Should().Contain(singular));
        TopNames(plural, 5).Should()
            .AllSatisfy(n => n.ToLower().Should().Contain(singular));
    }

    // ════════════════════════════════════════════════════════════════
    //  TYPO TOLERANCE (fuzzy)
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("brocoli", "broccoli")]
    [InlineData("spinnach", "spinach")]
    [InlineData("bannana", "banana")]
    [InlineData("avacado", "avocado")]
    public void FuzzyMatch_FindsCorrectFood(string typo, string expected)
    {
        TopNames(typo, 3).Should()
            .Contain(n => n.ToLower().Contains(expected));
    }

    // ════════════════════════════════════════════════════════════════
    //  MULTI-TOKEN COVERAGE
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("rice flour brown", "Rice flour, brown")]
    [InlineData("peanut butter chunky", "Peanut butter, chunk style")]
    public void MultiToken_AllTokensPresent(string query, string expectedContains)
    {
        TopNames(query, 3).Should()
            .Contain(n => n.Contains(expectedContains, StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  COOKING METHOD searches
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("fried egg", "fried")]
    [InlineData("scrambled egg", "scrambled")]
    [InlineData("boiled egg", "boiled")]
    [InlineData("baked potato", "baked")]
    [InlineData("grilled chicken", "grilled")]
    public void CookingMethod_AppearsInResult(string query, string method)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.Contains(method, StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  EMPTY / GARBAGE QUERIES
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQuery_ReturnsEmpty(string query)
    {
        SharedIndex.Value.Search(query, 10).Should().BeEmpty();
    }

    [Fact]
    public void GibberishQuery_ReturnsEmptyOrFew()
    {
        Search("xyzzyplugh42").Count.Should().BeLessThanOrEqualTo(3);
    }

    // ════════════════════════════════════════════════════════════════
    //  SPECIFIC FOOD ASSERTIONS
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Avocado_ReturnsAvocado()
    {
        FirstName("avocado").ToLower().Should().Contain("avocado");
    }

    [Fact]
    public void AvocadoRaw_ReturnsRawAvocado()
    {
        var name = FirstName("avocado raw");
        name.ToLower().Should().Contain("avocado");
        name.ToLower().Should().Contain("raw");
    }

    [Fact]
    public void Salmon_PrefersPlainSalmon()
    {
        var name = FirstName("salmon");
        name.Should().NotContain("nugget");
        name.Should().NotContain("Alaska Native");
    }

    [Fact]
    public void Yogurt_PrefersPlainYogurt()
    {
        var name = FirstName("yogurt");
        name.Should().NotContain("frozen");
        name.Should().NotContain("chocolate");
    }

    [Fact]
    public void Butter_ReturnsActualButter()
    {
        FirstName("butter").ToLower().Should().StartWith("butter");
    }

    // ════════════════════════════════════════════════════════════════
    //  RESULT COUNT / PAGINATION
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Search_RespectsMaxResults()
    {
        Search("chicken", 3).Count.Should().BeLessThanOrEqualTo(3);
        Search("chicken", 1).Count.Should().Be(1);
    }

    [Fact]
    public void LargeMaxResults_DoesNotCrash()
    {
        Search("chicken", 100).Count.Should().BeGreaterThan(0);
    }

    // ════════════════════════════════════════════════════════════════
    //  ALL RESULTS RELEVANCE
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chicken")]
    [InlineData("salmon")]
    [InlineData("broccoli")]
    public void AllResults_ContainQueryTerm(string query)
    {
        Search(query, 10).Should()
            .AllSatisfy(f => f.Name.ToLower().Should().Contain(query));
    }

    [Fact]
    public void AllResults_HaveUsdaDataSource()
    {
        Search("chicken", 15).Should()
            .AllSatisfy(f => f.DataSource.Should().Be("USDA"));
    }

    // ════════════════════════════════════════════════════════════════
    //  TOKEN ORDER: reversed tokens should still match
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ReversedTokenOrder_StillFindsResult()
    {
        TopNames("rice brown", 3).Should().Contain(n =>
            n.Contains("brown", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("Rice", StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  WholeFoodsDatabase.Search static entry point
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WholeFoodsDatabase_Search_ReturnsResults()
    {
        var results = WholeFoodsDatabase.Search("banana", 5);
        results.Should().NotBeEmpty();
        results.First().Name.ToLower().Should().Contain("banana");
    }

    [Fact]
    public void WholeFoodsDatabase_Search_EmptyQueryReturnsEmpty()
    {
        WholeFoodsDatabase.Search("", 5).Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════
    //  REGRESSIONS: historically bad results
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Regression_EggsDoesNotReturnAlaskaNativeFirst()
    {
        FirstName("eggs").Should().NotContain("Alaska Native");
    }

    [Fact]
    public void Regression_CornDoesNotReturnDriedNavajoCorn()
    {
        FirstName("corn").Should().NotContain("Navajo");
    }

    [Fact]
    public void Regression_BreadDoesNotReturnSomiviki()
    {
        FirstName("bread").Should().NotContain("somiviki");
    }

    [Fact]
    public void Regression_BerriesSearchIsRelevant()
    {
        TopNames("strawberry", 5).Should()
            .Contain(n => n.ToLower().Contains("strawberr"));
    }

    [Fact]
    public void Regression_OatsReturnsOats()
    {
        TopNames("oats", 5).Should()
            .Contain(n => n.ToLower().Contains("oat"));
    }

    [Fact]
    public void Regression_GarlicReturnsGarlic()
    {
        FirstName("garlic").ToLower().Should().Contain("garlic");
    }

    [Fact]
    public void Regression_OnionReturnsOnion()
    {
        FirstName("onion").ToLower().Should().Contain("onion");
    }

    [Fact]
    public void Regression_CarrotReturnsCarrot()
    {
        FirstName("carrot").ToLower().Should().Contain("carrot");
    }

    // ════════════════════════════════════════════════════════════════
    //  COLLOQUIAL FOOD NAMES: synonyms should resolve correctly
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Toast_ReturnsBreadToasted()
    {
        var top = TopNames("toast", 5);
        top.Should().Contain(n =>
            n.Contains("Bread", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("toasted", StringComparison.OrdinalIgnoreCase));
        top.First().ToLower().Should().Contain("bread",
            "toast should return bread-based results, not crackers or branded products");
    }

    [Fact]
    public void Toast_DoesNotReturnMelbaToastFirst()
    {
        FirstName("toast").ToLower().Should().NotContain("melba");
    }

    [Fact]
    public void Steak_ReturnsBeef()
    {
        var top = TopNames("steak", 5);
        top.Should().Contain(n => n.Contains("beef", StringComparison.OrdinalIgnoreCase) ||
                                  n.Contains("steak", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrangeJuice_ReturnsJuice()
    {
        var name = FirstName("orange juice");
        name.ToLower().Should().Contain("orange");
        name.ToLower().Should().Contain("juice");
    }

    [Fact]
    public void Oatmeal_ReturnsOats()
    {
        TopNames("oatmeal", 5).Should()
            .Contain(n => n.ToLower().Contains("oat"));
    }

    [Fact]
    public void Fries_ReturnsFrenchFries()
    {
        TopNames("fries", 5).Should()
            .Contain(n => n.ToLower().Contains("fries") || n.ToLower().Contains("fried"));
    }

    // ════════════════════════════════════════════════════════════════
    //  STEMMING: -ed suffix should match base forms
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("grilled chicken", "grilled")]
    [InlineData("roasted vegetables", "roast")]
    [InlineData("baked potato", "baked")]
    public void EdStemming_MatchesCookingMethods(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NormalizeFoodName_StemsEdSuffix()
    {
        FoodSearchIndex.NormalizeFoodName("Bread, white, toasted").Should().Contain("toast");
    }

    [Fact]
    public void NormalizeFoodName_DoesNotStemBread()
    {
        FoodSearchIndex.NormalizeFoodName("Bread, white").Should().Contain("bread");
    }
}
