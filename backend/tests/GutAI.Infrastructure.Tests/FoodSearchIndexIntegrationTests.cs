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
    [InlineData("kale", "Kale, raw")]
    [InlineData("asparagus", "Asparagus, raw")]
    [InlineData("cabbage", "Cabbage")]
    [InlineData("celery", "Celery, raw")]
    [InlineData("pumpkin", "Pumpkin, raw")]
    public void SingleWord_ReturnsPlainRawVariant(string query, string expected)
    {
        FirstName(query).Should().StartWith(expected);
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
        top.First().Should().NotContain("roll");
        top.First().Should().NotContain("sliced");
        top.First().Should().NotContain("deli");
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
        var name = FirstName("bread");
        name.Should().NotContain("Navajo");
        name.Should().NotContain("Hopi");
        name.Should().NotContain("pan dulce");
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
        var top = TopNames("tomato", 5);
        top.Should().AllSatisfy(n => n.ToLower().Should().Contain("tomato"));
        top.First().Should().Contain("raw");
        top.First().Should().NotContain("canned");
        top.First().Should().NotContain("products");
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
        name.ToLower().Should().Contain("plain");
        name.Should().NotContain("frozen");
        name.Should().NotContain("chocolate");
        name.Should().NotContain("strawberry");
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

    // ════════════════════════════════════════════════════════════════
    //  EVERYDAY SINGLE-WORD QUERIES: the basics
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("eggs", "egg")]
    [InlineData("tuna", "tuna")]
    [InlineData("shrimp", "shrimp")]
    [InlineData("turkey", "turkey")]
    [InlineData("lettuce", "lettuce")]
    [InlineData("cucumber", "cucumber")]
    [InlineData("mango", "mango")]
    [InlineData("pineapple", "pineapple")]
    [InlineData("grapes", "grape")]
    [InlineData("strawberries", "strawberr")]
    [InlineData("lemon", "lemon")]
    [InlineData("lime", "lime")]
    [InlineData("ginger", "ginger")]
    [InlineData("almonds", "almond")]
    [InlineData("walnuts", "walnut")]
    [InlineData("cashews", "cashew")]
    [InlineData("quinoa", "quinoa")]
    [InlineData("lentils", "lentil")]
    [InlineData("tofu", "tofu")]
    [InlineData("kale", "kale")]
    [InlineData("mushrooms", "mushroom")]
    [InlineData("pumpkin", "pumpkin")]
    [InlineData("zucchini", "zucchini")]
    [InlineData("asparagus", "asparagus")]
    [InlineData("cabbage", "cabbage")]
    [InlineData("celery", "celery")]
    [InlineData("peach", "peach")]
    [InlineData("pear", "pear")]
    [InlineData("coconut", "coconut")]
    [InlineData("corn", "corn")]
    [InlineData("beans", "bean")]
    [InlineData("peas", "pea")]
    [InlineData("lamb", "lamb")]
    [InlineData("crab", "crab")]
    [InlineData("lobster", "lobster")]
    [InlineData("bacon", "bacon")]
    [InlineData("ham", "ham")]
    [InlineData("sausage", "sausage")]
    [InlineData("pancakes", "pancake")]
    [InlineData("waffles", "waffle")]
    [InlineData("muffin", "muffin")]
    [InlineData("bagel", "bagel")]
    [InlineData("crackers", "cracker")]
    public void EverydayQuery_TopResultContainsQueryTerm(string query, string expectedSubstring)
    {
        var top = TopNames(query, 5);
        top.Should().NotBeEmpty();
        top.First().ToLower().Should().Contain(expectedSubstring,
            $"searching '{query}' should return results containing '{expectedSubstring}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  SINGLE-WORD RAW/FRESH PREFERENCE
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("kale")]
    [InlineData("asparagus")]
    [InlineData("cucumber")]
    [InlineData("lettuce")]
    [InlineData("celery")]
    [InlineData("mango")]
    [InlineData("pineapple")]
    [InlineData("peach")]
    [InlineData("pear")]
    [InlineData("strawberries")]
    [InlineData("grapes")]
    [InlineData("cabbage")]
    [InlineData("zucchini")]
    [InlineData("mushrooms")]
    [InlineData("ginger")]
    public void SingleWord_PrefersFreshOrRawOverProcessed(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("canned", because: $"'{query}' should prefer fresh/raw over canned");
        name.Should().NotContain("frozen", because: $"'{query}' should prefer fresh/raw over frozen");
        name.Should().NotContain("dried", because: $"'{query}' should prefer fresh/raw over dried");
        name.Should().NotContain("dehydrated", because: $"'{query}' should prefer fresh/raw over dehydrated");
    }

    // ════════════════════════════════════════════════════════════════
    //  SINGLE-WORD: no Alaska Native / regional specialty first
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("salmon")]
    [InlineData("mushrooms")]
    [InlineData("cranberries")]
    [InlineData("lamb")]
    [InlineData("turkey")]
    [InlineData("duck")]
    [InlineData("trout")]
    public void SingleWord_DoesNotReturnRegionalSpecialtyFirst(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("Alaska Native");
        name.Should().NotContain("Navajo");
        name.Should().NotContain("Hopi");
        name.Should().NotContain("Shoshone");
        name.Should().NotContain("Apache");
    }

    // ════════════════════════════════════════════════════════════════
    //  SPECIFIC FOOD: exact top-result assertions
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Eggs_ReturnsWholeEgg()
    {
        var name = FirstName("eggs");
        name.ToLower().Should().Contain("egg");
        name.ToLower().Should().Contain("whole");
        name.Should().NotContain("substitute");
        name.Should().NotContain("dried");
        name.Should().NotContain("powder");
        name.Should().NotContain("white");
    }

    [Fact]
    public void Tuna_ReturnsFreshOrCannedTuna()
    {
        var top = TopNames("tuna", 5);
        top.Should().AllSatisfy(n => n.ToLower().Should().Contain("tuna"));
    }

    [Fact]
    public void Shrimp_ReturnsShrimp()
    {
        var name = FirstName("shrimp");
        name.ToLower().Should().Contain("shrimp");
        name.Should().NotContain("imitation");
    }

    [Fact]
    public void Turkey_ReturnsActualTurkey()
    {
        var name = FirstName("turkey");
        name.ToLower().Should().Contain("turkey");
        name.Should().NotContain("baby food");
        name.Should().NotContain("mechanically deboned");
    }

    [Fact]
    public void Lettuce_ReturnsLettuce()
    {
        FirstName("lettuce").ToLower().Should().Contain("lettuce");
    }

    [Fact]
    public void Cucumber_ReturnsCucumber()
    {
        FirstName("cucumber").ToLower().Should().Contain("cucumber");
    }

    [Fact]
    public void Mango_ReturnsMango()
    {
        FirstName("mango").ToLower().Should().Contain("mango");
    }

    [Fact]
    public void Pineapple_ReturnsPineapple()
    {
        FirstName("pineapple").ToLower().Should().Contain("pineapple");
    }

    [Fact]
    public void Grapes_ReturnsGrapes()
    {
        FirstName("grapes").ToLower().Should().Contain("grape");
    }

    [Fact]
    public void Strawberries_ReturnsStrawberries()
    {
        FirstName("strawberries").ToLower().Should().Contain("strawberr");
    }

    [Fact]
    public void Lemon_ReturnsLemon()
    {
        var name = FirstName("lemon");
        name.ToLower().Should().Contain("lemon");
        name.Should().NotContain("juice");
        name.Should().NotContain("concentrate");
    }

    [Fact]
    public void Almonds_ReturnsAlmonds()
    {
        var name = FirstName("almonds");
        name.ToLower().Should().Contain("almond");
        name.Should().NotContain("Candy", because: "searching 'almonds' should return actual almonds");
        name.Should().NotContain("butter");
        name.Should().NotContain("oil");
    }

    [Fact]
    public void Walnuts_ReturnsWalnuts()
    {
        var name = FirstName("walnuts");
        name.ToLower().Should().Contain("walnut");
        name.Should().NotContain("oil");
    }

    [Fact]
    public void Quinoa_ReturnsQuinoa()
    {
        FirstName("quinoa").ToLower().Should().Contain("quinoa");
    }

    [Fact]
    public void Lentils_ReturnsLentils()
    {
        FirstName("lentils").ToLower().Should().Contain("lentil");
    }

    [Fact]
    public void Tofu_ReturnsTofu()
    {
        FirstName("tofu").ToLower().Should().Contain("tofu");
    }

    [Fact]
    public void Kale_ReturnsRawKale()
    {
        FirstName("kale").Should().Be("Kale, raw");
    }

    [Fact]
    public void Mushrooms_ReturnsMushrooms()
    {
        FirstName("mushrooms").ToLower().Should().Contain("mushroom");
    }

    [Fact]
    public void Pumpkin_ReturnsPumpkin()
    {
        FirstName("pumpkin").ToLower().Should().Contain("pumpkin");
    }

    [Fact]
    public void Zucchini_ReturnsZucchini()
    {
        FirstName("zucchini").ToLower().Should().Contain("zucchini");
    }

    [Fact]
    public void Asparagus_ReturnsAsparagus()
    {
        var name = FirstName("asparagus");
        name.ToLower().Should().Contain("asparagus");
        name.Should().NotContain("canned");
    }

    [Fact]
    public void Ginger_ReturnsGingerRoot()
    {
        FirstName("ginger").ToLower().Should().Contain("ginger");
    }

    [Fact]
    public void Peach_ReturnsPeach()
    {
        FirstName("peach").ToLower().Should().Contain("peach");
    }

    [Fact]
    public void Pear_ReturnsPear()
    {
        FirstName("pear").ToLower().Should().Contain("pear");
    }

    [Fact]
    public void Coconut_ReturnsCoconut()
    {
        FirstName("coconut").ToLower().Should().Contain("coconut");
    }

    [Fact]
    public void Hummus_ReturnsHummus()
    {
        FirstName("hummus").ToLower().Should().Contain("hummus");
    }

    [Fact]
    public void Lamb_ReturnsLamb()
    {
        var name = FirstName("lamb");
        name.ToLower().Should().Contain("lamb");
        name.ToLower().Should().NotContain("lambsquarter");
    }

    [Fact]
    public void Sausage_ReturnsSausage()
    {
        FirstName("sausage").ToLower().Should().Contain("sausage");
    }

    [Fact]
    public void Bacon_ReturnsBacon()
    {
        var name = FirstName("bacon");
        name.ToLower().Should().Contain("bacon");
        name.Should().NotContain("meatless");
        name.Should().NotContain("bits");
        name.Should().NotContain("turkey");
    }

    [Fact]
    public void Lobster_ReturnsLobster()
    {
        TopNames("lobster", 5).Should()
            .Contain(n => n.ToLower().Contains("lobster"));
    }

    [Fact]
    public void Crab_ReturnsCrab()
    {
        var name = FirstName("crab");
        name.ToLower().Should().Contain("crab");
        name.Should().NotContain("crabapple");
        name.Should().NotContain("Crabapples");
    }

    [Fact]
    public void Egg_ReturnsWholeEgg()
    {
        var name = FirstName("egg");
        name.ToLower().Should().Contain("whole");
        name.Should().NotContain("substitute");
    }

    [Fact]
    public void Lime_ReturnsWholeLime()
    {
        var name = FirstName("lime");
        name.ToLower().Should().Contain("lime");
        name.Should().NotContain("juice");
    }

    [Fact]
    public void Beans_ReturnsActualBeans()
    {
        var name = FirstName("beans");
        name.ToLower().Should().Contain("bean");
        name.Should().NotContain("liquid");
    }

    [Fact]
    public void Milk_ReturnsMilkNotButtermilk()
    {
        var name = FirstName("milk");
        name.ToLower().Should().Contain("milk");
        name.Should().NotContain("buttermilk");
        name.Should().NotContain("dry");
    }

    [Fact]
    public void Cinnamon_ReturnsSpice()
    {
        var name = FirstName("cinnamon");
        name.ToLower().Should().Contain("cinnamon");
        name.Should().NotContain("buns");
        name.Should().NotContain("bread");
        name.Should().NotContain("pastry");
    }

    [Fact]
    public void Chicken_PrefersWholeChicken()
    {
        var name = FirstName("chicken");
        name.Should().NotContain("liver");
        name.Should().NotContain("giblets");
        name.Should().NotContain("heart");
        name.Should().NotContain("mechanically");
    }

    [Fact]
    public void Beef_PrefersBasicBeef()
    {
        var name = FirstName("beef");
        name.Should().NotContain("corned");
        name.Should().NotContain("cured");
        name.Should().NotContain("by-products");
        name.Should().NotContain("mechanically");
    }

    [Fact]
    public void Pork_PrefersBasicPork()
    {
        var name = FirstName("pork");
        name.Should().NotContain("cured");
        name.Should().NotContain("salt pork");
    }

    [Fact]
    public void Duck_ReturnsDuckMeat()
    {
        var name = FirstName("duck");
        name.ToLower().Should().Contain("duck");
        name.Should().NotContain("liver");
    }

    // ════════════════════════════════════════════════════════════════
    //  MULTI-WORD QUERIES: common meal descriptions
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GrilledSalmon_ReturnsGrilledOrRawSalmon()
    {
        var top = TopNames("grilled salmon", 5);
        top.Should().Contain(n =>
            n.Contains("salmon", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Salmon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TunaSalad_ReturnsTunaSalad()
    {
        TopNames("tuna salad", 5).Should()
            .Contain(n => n.ToLower().Contains("tuna"));
    }

    [Fact]
    public void BlackBeans_ReturnsBlackBeans()
    {
        var top = TopNames("black beans", 5);
        top.Should().Contain(n =>
            n.Contains("black", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("bean", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void KidneyBeans_ReturnsKidneyBeans()
    {
        var top = TopNames("kidney beans", 5);
        top.Should().Contain(n =>
            n.Contains("kidney", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("bean", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GreenBeans_ReturnsGreenBeans()
    {
        TopNames("green beans", 5).Should()
            .Contain(n => n.ToLower().Contains("bean") && n.ToLower().Contains("green"));
    }

    [Fact]
    public void RoastedChicken_ContainsChicken()
    {
        var top = TopNames("roasted chicken", 5);
        top.Should().Contain(n => n.ToLower().Contains("chicken"));
    }

    [Fact]
    public void GroundBeef_ReturnsGroundBeef()
    {
        var top = TopNames("ground beef", 5);
        top.Should().Contain(n =>
            n.Contains("ground", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("beef", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroundTurkey_ReturnsGroundTurkey()
    {
        var top = TopNames("ground turkey", 5);
        top.Should().Contain(n =>
            n.ToLower().Contains("turkey") && n.ToLower().Contains("ground"));
    }

    [Fact]
    public void BakedSalmon_ContainsSalmon()
    {
        TopNames("baked salmon", 5).Should()
            .Contain(n => n.ToLower().Contains("salmon"));
    }

    [Fact]
    public void CornTortilla_ReturnsCornTortilla()
    {
        var top = TopNames("corn tortilla", 5);
        top.First().ToLower().Should().Contain("tortilla");
        top.First().Should().NotContain("Apache");
        top.First().Should().NotContain("corned");
    }

    [Fact]
    public void RiceCakes_ReturnsRiceCakes()
    {
        TopNames("rice cakes", 5).Should()
            .Contain(n => n.ToLower().Contains("rice") && n.ToLower().Contains("cake"));
    }

    [Fact]
    public void CreamCheese_ReturnsCreamCheese()
    {
        TopNames("cream cheese", 5).Should()
            .Contain(n =>
                n.Contains("cream", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("cheese", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CottageCheese_ReturnsCottageCheese()
    {
        TopNames("cottage cheese", 5).Should()
            .Contain(n =>
                n.Contains("cottage", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("cheese", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MozzarellaCheese_ReturnsMozzarella()
    {
        FirstName("mozzarella cheese").ToLower().Should().Contain("mozzarella");
    }

    [Fact]
    public void RedCabbage_ReturnsRedCabbage()
    {
        TopNames("red cabbage", 5).Should()
            .Contain(n =>
                n.Contains("red", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("cabbage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WholeWheatBread_ReturnsWholeWheatBread()
    {
        var top = TopNames("whole wheat bread", 5);
        top.Should().Contain(n =>
            n.Contains("bread", StringComparison.OrdinalIgnoreCase) &&
            (n.Contains("whole-wheat", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("whole wheat", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("wheat", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void PitaBread_ReturnsPitaBread()
    {
        TopNames("pita bread", 5).Should()
            .Contain(n =>
                n.Contains("pita", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("bread", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RyeBread_ReturnsRyeBread()
    {
        TopNames("rye bread", 5).Should()
            .Contain(n =>
                n.Contains("rye", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("bread", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BlueberryMuffin_ReturnsBlueberryMuffin()
    {
        TopNames("blueberry muffin", 5).Should()
            .Contain(n =>
                n.Contains("blueberry", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("muffin", StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  COOKING METHOD: varied methods produce relevant results
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("steamed broccoli", "broccoli")]
    [InlineData("roasted turkey", "turkey")]
    [InlineData("poached egg", "poach")]
    [InlineData("sauteed mushrooms", "mushroom")]
    [InlineData("steamed rice", "rice")]
    [InlineData("mashed potato", "potato")]
    [InlineData("boiled egg", "boil")]
    public void CookingMethodVariant_ContainsFoodName(string query, string expectedTerm)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.Contains(expectedTerm, StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  NUTRITION PLAUSIBILITY: expanded assertions
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Lettuce_IsVeryLowCalorie()
    {
        First("lettuce").Calories100g.Should().BeLessThan(25);
    }

    [Fact]
    public void Cucumber_IsVeryLowCalorie()
    {
        First("cucumber").Calories100g.Should().BeLessThan(30);
    }

    [Fact]
    public void Celery_IsVeryLowCalorie()
    {
        First("celery").Calories100g.Should().BeLessThan(25);
    }

    [Fact]
    public void Kale_IsLowCalorie()
    {
        First("kale").Calories100g.Should().BeLessThan(60);
    }

    [Fact]
    public void Almonds_AreHighCalorie()
    {
        First("almonds").Calories100g.Should().BeGreaterThan(400);
    }

    [Fact]
    public void Walnuts_AreHighCalorie()
    {
        First("walnuts").Calories100g.Should().BeGreaterThan(400);
    }

    [Fact]
    public void Salmon_HasHighProtein()
    {
        var food = First("salmon");
        food.Protein100g.Should().BeGreaterThan(15m);
    }

    [Fact]
    public void Tuna_HasHighProtein()
    {
        var food = First("tuna");
        food.Protein100g.Should().BeGreaterThan(15m);
    }

    [Fact]
    public void Shrimp_HasHighProtein()
    {
        var food = First("shrimp");
        food.Protein100g.Should().BeGreaterThan(10m);
    }

    [Fact]
    public void Turkey_HasHighProtein()
    {
        var food = First("turkey");
        food.Protein100g.Should().BeGreaterThan(10m);
    }

    [Fact]
    public void Quinoa_HasReasonableCarbs()
    {
        var food = First("quinoa");
        food.Carbs100g.Should().BeGreaterThan(10m);
        food.Protein100g.Should().BeGreaterThan(2m);
    }

    [Fact]
    public void Lentils_HaveProteinAndCarbs()
    {
        var food = First("lentils");
        food.Protein100g.Should().BeGreaterThan(5m);
        food.Carbs100g.Should().BeGreaterThan(10m);
    }

    [Fact]
    public void Avocado_HasHighFat()
    {
        First("avocado").Fat100g.Should().BeGreaterThan(10m);
    }

    [Fact]
    public void Coconut_HasHighFat()
    {
        First("coconut").Fat100g.Should().BeGreaterThan(10m);
    }

    [Fact]
    public void Mango_HasModerateCarbs()
    {
        var food = First("mango");
        food.Carbs100g.Should().BeGreaterThan(10m);
        food.Calories100g.Should().BeInRange(40, 120);
    }

    [Fact]
    public void Pineapple_HasModerateCarbs()
    {
        var food = First("pineapple");
        food.Carbs100g.Should().BeGreaterThan(8m);
        food.Fat100g.Should().BeLessThan(2m);
    }

    [Fact]
    public void Honey_IsHighSugar()
    {
        First("honey").Sugar100g.Should().BeGreaterThan(50m);
    }

    [Fact]
    public void Butter_IsHighFat()
    {
        First("butter").Fat100g.Should().BeGreaterThan(50m);
    }

    // ════════════════════════════════════════════════════════════════
    //  ALL RESULTS RELEVANCE: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("banana")]
    [InlineData("spinach")]
    [InlineData("avocado")]
    [InlineData("honey")]
    [InlineData("garlic")]
    [InlineData("onion")]
    [InlineData("carrot")]
    [InlineData("kale")]
    public void AllResults_ContainQueryTermExpanded(string query)
    {
        Search(query, 10).Should()
            .AllSatisfy(f => f.Name.ToLower().Should().Contain(query));
    }

    // ════════════════════════════════════════════════════════════════
    //  NAME LENGTH / QUALITY: short legible names preferred
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("eggs")]
    [InlineData("tuna")]
    [InlineData("shrimp")]
    [InlineData("lettuce")]
    [InlineData("cucumber")]
    [InlineData("mushrooms")]
    [InlineData("ginger")]
    [InlineData("lemon")]
    [InlineData("lime")]
    public void TopResult_HasReasonableNameLength(string query)
    {
        FirstName(query).Length.Should().BeLessThan(100,
            $"the top '{query}' result should have a short, legible name");
    }

    // ════════════════════════════════════════════════════════════════
    //  PLURAL / SINGULAR: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("almond", "almonds")]
    [InlineData("walnut", "walnuts")]
    [InlineData("lentil", "lentils")]
    [InlineData("mushroom", "mushrooms")]
    [InlineData("grape", "grapes")]
    [InlineData("peach", "peaches")]
    [InlineData("mango", "mangoes")]
    [InlineData("shrimp", "shrimps")]
    [InlineData("cracker", "crackers")]
    public void SingularAndPlural_TopResultsOverlapExpanded(string singular, string plural)
    {
        var singularResults = TopNames(singular, 10);
        var pluralResults = TopNames(plural, 10);
        singularResults.Intersect(pluralResults).Should().NotBeEmpty(
            $"'{singular}' and '{plural}' should share at least one top-10 result");
    }

    // ════════════════════════════════════════════════════════════════
    //  TYPO TOLERANCE: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tumeric", "turmeric")]
    [InlineData("qunioa", "quinoa")]
    [InlineData("lentills", "lentil")]
    [InlineData("mushroms", "mushroom")]
    [InlineData("zuchini", "zucchini")]
    [InlineData("asparugus", "asparagus")]
    [InlineData("cucummber", "cucumber")]
    [InlineData("mangoe", "mango")]
    [InlineData("letuce", "lettuce")]
    public void FuzzyMatch_ExpandedTypos(string typo, string expected)
    {
        TopNames(typo, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"typo '{typo}' should still find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  DAIRY / DAIRY ALTERNATIVES
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GreekYogurt_ReturnsYogurt()
    {
        TopNames("greek yogurt", 5).Should()
            .Contain(n => n.Contains("yogurt", StringComparison.OrdinalIgnoreCase) ||
                          n.Contains("Yogurt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AlmondMilk_ReturnsAlmondMilk()
    {
        TopNames("almond milk", 5).Should()
            .Contain(n =>
                n.Contains("almond", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("milk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoconutMilk_ReturnsCoconutMilk()
    {
        var top = TopNames("coconut milk", 5);
        top.Should().AllSatisfy(n =>
        {
            n.ToLower().Should().Match(name =>
                name.Contains("coconut") || name.Contains("milk"),
                "all results for 'coconut milk' should contain coconut or milk relevantly");
        });
        top.Take(3).Should().AllSatisfy(n =>
            n.ToLower().Should().Contain("coconut"));
    }

    // ════════════════════════════════════════════════════════════════
    //  GRAINS / STARCHES
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Oats_ReturnsOats()
    {
        TopNames("oats", 5).Should()
            .Contain(n => n.ToLower().Contains("oat"));
    }

    [Fact]
    public void Barley_ReturnsBarley()
    {
        TopNames("barley", 5).Should()
            .Contain(n => n.ToLower().Contains("barley"));
    }

    [Fact]
    public void Cornmeal_ReturnsCornmeal()
    {
        TopNames("cornmeal", 5).Should()
            .Contain(n => n.ToLower().Contains("cornmeal"));
    }

    [Fact]
    public void Flour_ReturnsFlour()
    {
        TopNames("flour", 5).Should()
            .Contain(n => n.ToLower().Contains("flour"));
    }

    [Fact]
    public void WheatFlour_ReturnsWheatFlour()
    {
        TopNames("wheat flour", 5).Should()
            .Contain(n =>
                n.Contains("wheat", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("flour", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RiceFlour_ReturnsRiceFlour()
    {
        TopNames("rice flour", 5).Should()
            .Contain(n =>
                n.Contains("rice", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("flour", StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  CONDIMENTS / SAUCES
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mustard_ReturnsMustard()
    {
        var name = FirstName("mustard");
        name.ToLower().Should().Contain("mustard");
        name.Should().NotContain("greens");
        name.Should().NotContain("spinach");
    }

    [Fact]
    public void Ketchup_ReturnsKetchup()
    {
        TopNames("ketchup", 5).Should()
            .Contain(n => n.ToLower().Contains("ketchup") || n.ToLower().Contains("catsup"));
    }

    [Fact]
    public void Vinegar_ReturnsVinegar()
    {
        TopNames("vinegar", 5).Should()
            .Contain(n => n.ToLower().Contains("vinegar"));
    }

    [Fact]
    public void SoySauce_ReturnsSoySauce()
    {
        TopNames("soy sauce", 5).Should()
            .Contain(n =>
                n.Contains("soy", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("sauce", StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  SPICES / HERBS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("cinnamon", "cinnamon")]
    [InlineData("turmeric", "turmeric")]
    [InlineData("paprika", "paprika")]
    [InlineData("cumin", "cumin")]
    [InlineData("oregano", "oregano")]
    [InlineData("basil", "basil")]
    [InlineData("thyme", "thyme")]
    [InlineData("parsley", "parsley")]
    [InlineData("rosemary", "rosemary")]
    [InlineData("nutmeg", "nutmeg")]
    [InlineData("cloves", "clove")]
    [InlineData("pepper", "pepper")]
    public void Spices_ReturnCorrectSpice(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  DESSERTS / BAKED GOODS
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Pancakes_ReturnsPancakes()
    {
        FirstName("pancakes").ToLower().Should().Contain("pancake");
    }

    [Fact]
    public void Waffles_ReturnsWaffles()
    {
        FirstName("waffles").ToLower().Should().Contain("waffle");
    }

    [Fact]
    public void Bagel_ReturnsBagel()
    {
        FirstName("bagel").ToLower().Should().Contain("bagel");
    }

    [Fact]
    public void Crackers_ReturnsCrackers()
    {
        var name = FirstName("crackers");
        name.ToLower().Should().Contain("cracker");
        name.Should().NotContain("Goya", because: "generic search should prefer generic crackers over branded");
    }

    // ════════════════════════════════════════════════════════════════
    //  BEVERAGES
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Coffee_ReturnsCoffee()
    {
        var name = FirstName("coffee");
        name.ToLower().Should().Contain("coffee");
        name.Should().NotContain("soymilk");
        name.Should().NotContain("SILK");
    }

    [Fact]
    public void Tea_ReturnsTea()
    {
        var name = FirstName("tea");
        name.ToLower().Should().Contain("tea");
        name.Should().NotContain("Hopi");
        name.Should().NotContain("Alaska Native");
        name.Should().NotContain("Hohoysi");
    }

    [Fact]
    public void OrangeJuice_IsLowFat()
    {
        var food = First("orange juice");
        food.Fat100g.Should().BeLessThan(2m);
        food.Carbs100g.Should().BeGreaterThan(5m);
    }

    // ════════════════════════════════════════════════════════════════
    //  REVERSED TOKEN ORDER: more coverage
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("beans black", "black", "bean")]
    [InlineData("cheese cream", "cream", "cheese")]
    [InlineData("bread white", "white", "bread")]
    [InlineData("flour wheat", "wheat", "flour")]
    public void ReversedTokens_StillFindsCorrectFood(string query, string term1, string term2)
    {
        TopNames(query, 5).Should()
            .Contain(n =>
                n.Contains(term1, StringComparison.OrdinalIgnoreCase) &&
                n.Contains(term2, StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════════
    //  REGRESSION: baby food / substitute / industrial should not win
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chicken")]
    [InlineData("beef")]
    [InlineData("turkey")]
    [InlineData("pork")]
    [InlineData("lamb")]
    [InlineData("salmon")]
    [InlineData("tuna")]
    public void MeatQueries_DoNotReturnBabyFoodFirst(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("baby food");
        name.Should().NotContain("infant");
        name.Should().NotContain("formula");
        name.Should().NotContain("liver", because: "generic meat queries should not return organ meats");
        name.Should().NotContain("giblets");
        name.Should().NotContain("mechanically deboned");
        name.Should().NotContain("by-products");
    }

    // ════════════════════════════════════════════════════════════════
    //  RESULT COUNT: common queries return enough results
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chicken")]
    [InlineData("beef")]
    [InlineData("rice")]
    [InlineData("cheese")]
    [InlineData("bread")]
    [InlineData("milk")]
    [InlineData("egg")]
    [InlineData("beans")]
    [InlineData("mushroom")]
    [InlineData("salmon")]
    public void CommonQuery_ReturnsAtLeast5Results(string query)
    {
        Search(query, 15).Count.Should().BeGreaterThanOrEqualTo(5,
            $"'{query}' is a common food and should return many results");
    }

    // ════════════════════════════════════════════════════════════════
    //  DATA SOURCE: USDA for whole foods
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("banana")]
    [InlineData("spinach")]
    [InlineData("broccoli")]
    [InlineData("salmon")]
    [InlineData("chicken")]
    [InlineData("rice")]
    [InlineData("egg")]
    public void WholeFoods_HaveUsdaDataSource(string query)
    {
        First(query).DataSource.Should().Be("USDA");
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("spinach")]
    [InlineData("kale")]
    [InlineData("broccoli")]
    [InlineData("apple")]
    [InlineData("honey")]
    public void WholeFoods_AreMarkedAsWholeFoodKind(string query)
    {
        First(query).FoodKind.Should().Be(GutAI.Domain.Enums.FoodKind.WholeFood);
    }

    // ════════════════════════════════════════════════════════════════
    //  NUTRITION COMPLETENESS: top results have basic macros
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chicken")]
    [InlineData("rice")]
    [InlineData("banana")]
    [InlineData("egg")]
    [InlineData("salmon")]
    [InlineData("broccoli")]
    [InlineData("almond")]
    [InlineData("quinoa")]
    [InlineData("lentils")]
    [InlineData("avocado")]
    public void TopResult_HasBasicNutritionData(string query)
    {
        var food = First(query);
        food.Calories100g.Should().HaveValue();
        food.Protein100g.Should().HaveValue();
        food.Carbs100g.Should().HaveValue();
        food.Fat100g.Should().HaveValue();
    }

    // ════════════════════════════════════════════════════════════════
    //  EDGE CASE: partial word searches
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chick", "chicken")]
    [InlineData("salm", "salmon")]
    [InlineData("broc", "broccoli")]
    [InlineData("straw", "strawberr")]
    [InlineData("pine", "pineapple")]
    [InlineData("mush", "mushroom")]
    public void PartialWord_FindsFullMatch(string partial, string expected)
    {
        TopNames(partial, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"partial search '{partial}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  SEAFOOD VARIETIES
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tilapia", "tilapia")]
    [InlineData("cod", "cod")]
    [InlineData("sardines", "sardin")]
    [InlineData("anchovies", "anchov")]
    [InlineData("oysters", "oyster")]
    [InlineData("clams", "clam")]
    [InlineData("scallops", "scallop")]
    [InlineData("catfish", "catfish")]
    [InlineData("trout", "trout")]
    [InlineData("halibut", "halibut")]
    [InlineData("swordfish", "swordfish")]
    [InlineData("mackerel", "mackerel")]
    [InlineData("herring", "herring")]
    [InlineData("squid", "squid")]
    [InlineData("octopus", "octopus")]
    [InlineData("mussels", "mussel")]
    [InlineData("crawfish", "crayfish")]
    public void Seafood_ReturnsCorrectType(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("tilapia")]
    [InlineData("cod")]
    [InlineData("trout")]
    [InlineData("halibut")]
    [InlineData("catfish")]
    public void Seafood_HasHighProtein(string query)
    {
        var food = First(query);
        food.Protein100g.Should().BeGreaterThan(10m,
            $"'{query}' is a lean fish and should be high in protein");
    }

    [Theory]
    [InlineData("tilapia")]
    [InlineData("cod")]
    [InlineData("trout")]
    [InlineData("scallops")]
    public void Seafood_DoesNotReturnOrganMeat(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("liver");
        name.Should().NotContain("giblets");
    }

    // ════════════════════════════════════════════════════════════════
    //  FRUITS: untested varieties
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("watermelon", "watermelon")]
    [InlineData("cantaloupe", "cantaloupe")]
    [InlineData("cherries", "cherr")]
    [InlineData("plum", "plum")]
    [InlineData("fig", "fig")]
    [InlineData("dates", "date")]
    [InlineData("kiwi", "kiwi")]
    [InlineData("papaya", "papaya")]
    [InlineData("apricot", "apricot")]
    [InlineData("tangerine", "tangerine")]
    [InlineData("nectarine", "nectarine")]
    [InlineData("pomegranate", "pomegranate")]
    [InlineData("persimmon", "persimmon")]
    [InlineData("guava", "guava")]
    [InlineData("passion fruit", "passion")]
    [InlineData("cranberries", "cranberr")]
    [InlineData("blackberries", "blackberr")]
    [InlineData("raspberries", "raspberr")]
    public void Fruit_ReturnsCorrectFruit(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("watermelon")]
    [InlineData("cantaloupe")]
    [InlineData("cherries")]
    [InlineData("plum")]
    [InlineData("kiwi")]
    [InlineData("papaya")]
    [InlineData("apricot")]
    [InlineData("guava")]
    public void Fruit_PrefersRawOverJuice(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("juice",
            $"'{query}' should return the raw fruit, not juice");
        name.Should().NotContain("concentrate");
    }

    [Theory]
    [InlineData("watermelon")]
    [InlineData("cantaloupe")]
    [InlineData("cherries")]
    [InlineData("plum")]
    [InlineData("kiwi")]
    public void Fruit_IsLowFat(string query)
    {
        First(query).Fat100g.Should().BeLessThan(3m,
            $"'{query}' is a fruit and should be low in fat");
    }

    // ════════════════════════════════════════════════════════════════
    //  VEGETABLES: untested varieties
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("artichoke", "artichoke")]
    [InlineData("beets", "beet")]
    [InlineData("turnip", "turnip")]
    [InlineData("radish", "radish")]
    [InlineData("parsnip", "parsnip")]
    [InlineData("okra", "okra")]
    [InlineData("brussels sprouts", "brussels")]
    [InlineData("cauliflower", "cauliflower")]
    [InlineData("bok choy", "pak-choi")]
    [InlineData("pak choi", "pak")]
    [InlineData("endive", "endive")]
    [InlineData("eggplant", "eggplant")]
    [InlineData("leek", "leek")]
    [InlineData("rutabaga", "rutabaga")]
    [InlineData("kohlrabi", "kohlrabi")]
    [InlineData("jicama", "jicama")]
    [InlineData("collards", "collard")]
    [InlineData("swiss chard", "chard")]
    [InlineData("watercress", "watercress")]
    public void Vegetable_ReturnsCorrectVegetable(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("artichoke")]
    [InlineData("beets")]
    [InlineData("turnip")]
    [InlineData("radish")]
    [InlineData("okra")]
    [InlineData("cauliflower")]
    [InlineData("eggplant")]
    public void Vegetable_IsLowCalorie(string query)
    {
        First(query).Calories100g.Should().BeLessThan(80,
            $"'{query}' is a vegetable and should be low calorie");
    }

    // ════════════════════════════════════════════════════════════════
    //  SEEDS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chia seeds", "chia")]
    [InlineData("flax seeds", "flax")]
    [InlineData("sunflower seeds", "sunflower")]
    [InlineData("pumpkin seeds", "pumpkin")]
    [InlineData("sesame seeds", "sesame")]
    [InlineData("hemp seeds", "hemp")]
    public void Seeds_ReturnsCorrectSeed(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("chia seeds")]
    [InlineData("flax seeds")]
    [InlineData("sunflower seeds")]
    [InlineData("pumpkin seeds")]
    public void Seeds_AreHighCalorie(string query)
    {
        First(query).Calories100g.Should().BeGreaterThan(300,
            $"'{query}' are calorie-dense foods");
    }

    // ════════════════════════════════════════════════════════════════
    //  LEGUMES
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chickpeas", "chickpea")]
    [InlineData("edamame", "edamame")]
    [InlineData("split peas", "pea")]
    [InlineData("navy beans", "navy")]
    [InlineData("pinto beans", "pinto")]
    [InlineData("lima beans", "lima")]
    [InlineData("white beans", "white")]
    public void Legumes_ReturnsCorrectLegume(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("chickpeas")]
    [InlineData("lentils")]
    [InlineData("black beans")]
    [InlineData("kidney beans")]
    public void Legumes_HaveProteinAndFiber(string query)
    {
        var food = First(query);
        food.Protein100g.Should().BeGreaterThan(3m);
        food.Fiber100g.Should().BeGreaterThan(2m,
            $"'{query}' should be a good source of fiber");
    }

    // ════════════════════════════════════════════════════════════════
    //  GRAINS: untested varieties
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("bulgur", "bulgur")]
    [InlineData("couscous", "couscous")]
    [InlineData("millet", "millet")]
    [InlineData("amaranth", "amaranth")]
    [InlineData("buckwheat", "buckwheat")]
    [InlineData("wild rice", "wild")]
    public void Grain_ReturnsCorrectGrain(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  CONDIMENTS / SAUCES: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("mayo", "mayonnais")]
    [InlineData("mayonnaise", "mayonnais")]
    [InlineData("hot sauce", "sauce")]
    [InlineData("salsa", "salsa")]
    [InlineData("tahini", "tahini")]
    [InlineData("teriyaki", "teriyaki")]
    [InlineData("worcestershire", "worcestershire")]
    [InlineData("pesto", "pesto")]
    [InlineData("horseradish", "horseradish")]
    [InlineData("relish", "relish")]
    public void Condiment_ReturnsCorrectCondiment(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  SWEETENERS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("maple syrup", "maple")]
    [InlineData("molasses", "molasses")]
    [InlineData("agave", "agave")]
    public void Sweetener_ReturnsCorrectSweetener(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Fact]
    public void MapleSyrup_IsHighSugar()
    {
        First("maple syrup").Sugar100g.Should().BeGreaterThan(40m);
    }

    [Fact]
    public void Molasses_IsHighSugar()
    {
        First("molasses").Sugar100g.Should().BeGreaterThan(40m);
    }

    // ════════════════════════════════════════════════════════════════
    //  BEVERAGES: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("coconut water", "coconut")]
    [InlineData("apple juice", "apple")]
    [InlineData("cranberry juice", "cranberr")]
    [InlineData("grape juice", "grape")]
    [InlineData("tomato juice", "tomato")]
    [InlineData("lemonade", "lemonade")]
    public void Beverage_ReturnsCorrectBeverage(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("apple juice")]
    [InlineData("orange juice")]
    [InlineData("grape juice")]
    [InlineData("cranberry juice")]
    public void Juice_IsLowFat(string query)
    {
        First(query).Fat100g.Should().BeLessThan(2m,
            $"'{query}' should be essentially fat-free");
    }

    // ════════════════════════════════════════════════════════════════
    //  OILS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("canola oil", "canola")]
    [InlineData("sesame oil", "sesame")]
    [InlineData("coconut oil", "coconut")]
    [InlineData("sunflower oil", "sunflower")]
    [InlineData("avocado oil", "avocado")]
    [InlineData("peanut oil", "peanut")]
    [InlineData("corn oil", "corn")]
    [InlineData("flaxseed oil", "flaxseed")]
    public void Oil_ReturnsCorrectOil(string query, string expected)
    {
        var top = TopNames(query, 5);
        top.Should().Contain(n =>
            n.ToLower().Contains(expected) && n.ToLower().Contains("oil"),
            $"searching '{query}' should find an oil result containing '{expected}'");
    }

    [Theory]
    [InlineData("canola oil")]
    [InlineData("sesame oil")]
    [InlineData("coconut oil")]
    [InlineData("olive oil")]
    public void Oil_IsVeryHighFat(string query)
    {
        First(query).Fat100g.Should().BeGreaterThan(80m,
            $"'{query}' should be nearly 100% fat");
    }

    // ════════════════════════════════════════════════════════════════
    //  PREPARED / PROCESSED FOODS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("pasta", "pasta")]
    [InlineData("pizza", "pizza")]
    [InlineData("soup", "soup")]
    [InlineData("spaghetti", "spaghetti")]
    [InlineData("lasagna", "lasagna")]
    [InlineData("burrito", "burrito")]
    [InlineData("tortilla", "tortilla")]
    [InlineData("noodles", "noodle")]
    [InlineData("macaroni", "macaroni")]
    [InlineData("ravioli", "ravioli")]
    [InlineData("croissant", "croissant")]
    [InlineData("pretzel", "pretzel")]
    [InlineData("granola", "granola")]
    [InlineData("coleslaw", "coleslaw")]
    public void PreparedFood_ReturnsCorrectFood(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  NUTS: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("cashews", "cashew")]
    [InlineData("pecans", "pecan")]
    [InlineData("pistachios", "pistachio")]
    [InlineData("hazelnuts", "hazelnut")]
    [InlineData("macadamia", "macadamia")]
    [InlineData("brazil nuts", "brazil")]
    [InlineData("peanuts", "peanut")]
    [InlineData("pine nuts", "pine")]
    public void Nut_ReturnsCorrectNut(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("cashews")]
    [InlineData("pecans")]
    [InlineData("pistachios")]
    [InlineData("hazelnuts")]
    [InlineData("macadamia")]
    [InlineData("peanuts")]
    public void Nut_IsHighCalorie(string query)
    {
        First(query).Calories100g.Should().BeGreaterThan(400,
            $"'{query}' are calorie-dense");
    }

    // ════════════════════════════════════════════════════════════════
    //  THREE-WORD QUERIES
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("whole wheat pasta", "pasta")]
    [InlineData("extra virgin olive", "olive")]
    [InlineData("sharp cheddar cheese", "cheddar")]
    [InlineData("long grain rice", "rice")]
    [InlineData("low fat yogurt", "yogurt")]
    [InlineData("dark chocolate chips", "chocolate")]
    [InlineData("red bell pepper", "pepper")]
    [InlineData("wild caught salmon", "salmon")]
    [InlineData("boneless chicken breast", "chicken")]
    [InlineData("fresh squeezed orange", "orange")]
    public void ThreeWordQuery_ContainsKeyTerm(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"three-word query '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  CROSS-CONTAMINATION: food queries shouldn't return wrong foods
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Corn_DoesNotReturnCornedBeef()
    {
        var top = TopNames("corn", 5);
        top.Should().NotContain(n => n.ToLower().Contains("corned"),
            "searching 'corn' should not return corned beef");
    }

    [Fact]
    public void Apple_DoesNotReturnAppleSauceFirst()
    {
        var name = FirstName("apple");
        name.Should().NotContain("sauce",
            "searching 'apple' should return the raw fruit first");
    }

    [Fact]
    public void Orange_DoesNotReturnJuiceFirst()
    {
        var name = FirstName("orange");
        name.Should().NotContain("juice",
            "searching 'orange' should return the raw fruit first");
    }

    [Fact]
    public void Grape_DoesNotReturnGrapefruit()
    {
        var name = FirstName("grape");
        name.Should().NotContain("grapefruit",
            "searching 'grape' should return grapes, not grapefruit");
    }

    [Fact]
    public void Turkey_DoesNotReturnTurkeyBacon()
    {
        var name = FirstName("turkey");
        name.Should().NotContain("bacon");
    }

    [Fact]
    public void Chicken_DoesNotReturnChickenNuggets()
    {
        var name = FirstName("chicken");
        name.Should().NotContain("nugget",
            "searching 'chicken' should not return nuggets first");
    }

    [Fact]
    public void Rice_DoesNotReturnRiceCakes()
    {
        var name = FirstName("rice");
        name.Should().NotContain("cake",
            "searching 'rice' should return plain rice first");
    }

    [Fact]
    public void Potato_DoesNotReturnPotatoChips()
    {
        var name = FirstName("potato");
        name.Should().NotContain("chip",
            "searching 'potato' should return whole potato first");
    }

    [Fact]
    public void Tomato_DoesNotReturnTomatoSauceFirst()
    {
        var name = FirstName("tomato");
        name.Should().NotContain("sauce",
            "searching 'tomato' should return raw tomato first");
    }

    // ════════════════════════════════════════════════════════════════
    //  DAIRY: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("cheddar", "cheddar")]
    [InlineData("parmesan", "parmesan")]
    [InlineData("swiss cheese", "swiss")]
    [InlineData("gouda", "gouda")]
    [InlineData("brie", "brie")]
    [InlineData("feta", "feta")]
    [InlineData("provolone", "provolone")]
    [InlineData("ricotta", "ricotta")]
    [InlineData("goat cheese", "goat")]
    [InlineData("blue cheese", "blue")]
    public void Cheese_ReturnsCorrectCheese(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find cheese containing '{expected}'");
    }

    [Theory]
    [InlineData("cheddar")]
    [InlineData("parmesan")]
    [InlineData("brie")]
    [InlineData("feta")]
    public void Cheese_HasHighFat(string query)
    {
        First(query).Fat100g.Should().BeGreaterThan(3m,
            $"'{query}' cheese should be high in fat");
    }

    [Fact]
    public void SourCream_ReturnsSourCream()
    {
        TopNames("sour cream", 5).Should()
            .Contain(n =>
                n.Contains("sour", StringComparison.OrdinalIgnoreCase) &&
                n.Contains("cream", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HeavyCream_ReturnsCream()
    {
        TopNames("heavy cream", 5).Should()
            .Contain(n => n.ToLower().Contains("cream"));
    }

    [Fact]
    public void WhippedCream_ReturnsCream()
    {
        TopNames("whipped cream", 5).Should()
            .Contain(n => n.ToLower().Contains("cream"));
    }

    // ════════════════════════════════════════════════════════════════
    //  PROTEIN ALTERNATIVES
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("tofu", "tofu")]
    [InlineData("tempeh", "tempeh")]
    public void ProteinAlternative_ReturnsCorrectFood(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  MEAT CUTS AND PREPARATIONS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chicken breast", "breast")]
    [InlineData("chicken thigh", "thigh")]
    [InlineData("chicken wing", "wing")]
    [InlineData("chicken drumstick", "drumstick")]
    [InlineData("pork chop", "chop")]
    [InlineData("pork tenderloin", "tenderloin")]
    [InlineData("beef sirloin", "sirloin")]
    [InlineData("beef brisket", "brisket")]
    [InlineData("lamb chop", "lamb")]
    public void MeatCut_ReturnsCorrectCut(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    [Theory]
    [InlineData("chicken breast")]
    [InlineData("chicken thigh")]
    [InlineData("beef sirloin")]
    [InlineData("pork chop")]
    public void MeatCut_DoesNotReturnOrganMeat(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("liver");
        name.Should().NotContain("giblets");
        name.Should().NotContain("heart");
    }

    // ════════════════════════════════════════════════════════════════
    //  BAKED GOODS: expanded
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("muffin", "muffin")]
    [InlineData("biscuit", "biscuit")]
    [InlineData("cornbread", "cornbread")]
    [InlineData("english muffin", "english")]
    [InlineData("tortilla", "tortilla")]
    [InlineData("naan", "naan")]
    [InlineData("flatbread", "flatbread")]
    public void BakedGood_ReturnsCorrectItem(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  DESSERTS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chocolate", "chocolate")]
    [InlineData("ice cream", "ice cream")]
    [InlineData("pudding", "pudding")]
    [InlineData("brownie", "brownie")]
    [InlineData("pie", "pie")]
    [InlineData("cake", "cake")]
    [InlineData("cookie", "cookie")]
    [InlineData("cheesecake", "cheesecake")]
    public void Dessert_ReturnsCorrectDessert(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  BREAKFAST ITEMS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("cereal", "cereal")]
    [InlineData("granola", "granola")]
    [InlineData("oatmeal", "oat")]
    [InlineData("french toast", "french")]
    [InlineData("hash browns", "potato")]
    public void Breakfast_ReturnsCorrectItem(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"searching '{query}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL COOKING METHOD VARIATIONS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("fried chicken", "chicken")]
    [InlineData("fried egg", "egg")]
    [InlineData("grilled chicken", "chicken")]
    [InlineData("baked potato", "potato")]
    [InlineData("steamed vegetables", "vegetable")]
    [InlineData("smoked salmon", "salmon")]
    [InlineData("braised beef", "beef")]
    [InlineData("broiled fish", "fish")]
    public void CookingMethod_ContainsMainIngredient(string query, string expected)
    {
        TopNames(query, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"'{query}' should return results containing '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL NUTRITION PLAUSIBILITY
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("watermelon")]
    [InlineData("cantaloupe")]
    [InlineData("grapefruit")]
    public void Melon_IsLowCalorie(string query)
    {
        First(query).Calories100g.Should().BeLessThan(60,
            $"'{query}' is a melon and should be low calorie");
    }

    [Theory]
    [InlineData("olive oil")]
    [InlineData("coconut oil")]
    [InlineData("canola oil")]
    public void Oil_IsVeryHighCalorie(string query)
    {
        First(query).Calories100g.Should().BeGreaterThan(700,
            $"'{query}' should be very calorie-dense");
    }

    [Fact]
    public void Tofu_HasModerateProtein()
    {
        var food = First("tofu");
        food.Protein100g.Should().BeGreaterThan(5m);
        food.Fat100g.Should().BeLessThan(20m);
    }

    [Fact]
    public void Chickpeas_HasModerateCalories()
    {
        First("chickpeas").Calories100g.Should().BeInRange(50, 400);
    }

    [Fact]
    public void IceCream_HasHighSugar()
    {
        var food = First("ice cream");
        food.Calories100g.Should().BeGreaterThan(100);
        food.Carbs100g.Should().BeGreaterThan(10m);
    }

    [Fact]
    public void Pasta_HasHighCarbs()
    {
        First("pasta").Carbs100g.Should().BeGreaterThan(15m);
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL PARTIAL WORD SEARCHES
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("cuc", "cucumber")]
    [InlineData("lem", "lemon")]
    [InlineData("spin", "spinach")]
    [InlineData("avo", "avocado")]
    [InlineData("tom", "tomato")]
    [InlineData("tur", "turkey")]
    [InlineData("ban", "banana")]
    public void PartialWord_FindsFullMatchExpanded(string partial, string expected)
    {
        TopNames(partial, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"partial search '{partial}' should find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL PLURAL/SINGULAR
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("cherry", "cherries")]
    [InlineData("strawberry", "strawberries")]
    [InlineData("blueberry", "blueberries")]
    [InlineData("cranberry", "cranberries")]
    [InlineData("potato", "potatoes")]
    [InlineData("tomato", "tomatoes")]
    [InlineData("olive", "olives")]
    public void SingularAndPlural_BerryAndProduceOverlap(string singular, string plural)
    {
        var singularResults = TopNames(singular, 10);
        var pluralResults = TopNames(plural, 10);
        singularResults.Intersect(pluralResults).Should().NotBeEmpty(
            $"'{singular}' and '{plural}' should share at least one top-10 result");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL ALL-RESULTS RELEVANCE
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("salmon")]
    [InlineData("chicken")]
    [InlineData("beef")]
    [InlineData("turkey")]
    [InlineData("pork")]
    [InlineData("rice")]
    [InlineData("pasta")]
    [InlineData("cheese")]
    public void AllResults_CommonProteinsContainQueryTerm(string query)
    {
        var results = Search(query, 10);
        results.Should().AllSatisfy(f =>
            f.Name.ToLower().Should().Contain(query,
                $"all top results for '{query}' should contain the query term"));
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL REVERSED TOKEN ORDER
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("rice brown", "brown", "rice")]
    [InlineData("sauce soy", "soy", "sauce")]
    [InlineData("oil olive", "oil", "olive")]
    [InlineData("juice apple", "apple", "juice")]
    [InlineData("milk coconut", "coconut", "milk")]
    [InlineData("bread rye", "rye", "bread")]
    public void ReversedTokens_ExpandedStillFindsCorrectFood(string query, string term1, string term2)
    {
        TopNames(query, 5).Should()
            .Contain(n =>
                n.Contains(term1, StringComparison.OrdinalIgnoreCase) &&
                n.Contains(term2, StringComparison.OrdinalIgnoreCase),
                $"reversed query '{query}' should still find both '{term1}' and '{term2}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL BABY FOOD REGRESSION
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("banana")]
    [InlineData("apple")]
    [InlineData("pear")]
    [InlineData("rice")]
    [InlineData("oats")]
    [InlineData("sweet potato")]
    [InlineData("carrot")]
    public void CommonFoods_DoNotReturnBabyFoodFirst(string query)
    {
        var name = FirstName(query);
        name.Should().NotContain("baby food",
            $"'{query}' should return adult food, not baby food");
        name.Should().NotContain("infant");
        name.Should().NotContain("formula");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL RESULT COUNT
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("apple")]
    [InlineData("banana")]
    [InlineData("tomato")]
    [InlineData("potato")]
    [InlineData("corn")]
    [InlineData("turkey")]
    [InlineData("pork")]
    [InlineData("broccoli")]
    [InlineData("spinach")]
    [InlineData("carrot")]
    public void CommonQuery_ExpandedReturnsAtLeast5Results(string query)
    {
        Search(query, 15).Count.Should().BeGreaterThanOrEqualTo(5,
            $"'{query}' is a common food and should return many results");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL NAME QUALITY
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("watermelon")]
    [InlineData("cantaloupe")]
    [InlineData("artichoke")]
    [InlineData("cauliflower")]
    [InlineData("eggplant")]
    [InlineData("chickpeas")]
    [InlineData("salmon")]
    [InlineData("pasta")]
    [InlineData("oats")]
    public void TopResult_ExpandedHasReasonableNameLength(string query)
    {
        FirstName(query).Length.Should().BeLessThan(100,
            $"the top '{query}' result should have a short, legible name");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL TYPO TOLERANCE
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("avacado", "avocado")]
    [InlineData("cinamon", "cinnamon")]
    [InlineData("parsely", "parsley")]
    [InlineData("calliflower", "cauliflower")]
    [InlineData("artichoek", "artichoke")]
    public void FuzzyMatch_MoreExpandedTypos(string typo, string expected)
    {
        TopNames(typo, 5).Should()
            .Contain(n => n.ToLower().Contains(expected),
                $"typo '{typo}' should still find '{expected}'");
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL DATA SOURCE / FOOD KIND
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("watermelon")]
    [InlineData("cantaloupe")]
    [InlineData("artichoke")]
    [InlineData("cauliflower")]
    [InlineData("lentils")]
    [InlineData("quinoa")]
    public void WholeFoods_ExpandedHaveUsdaDataSource(string query)
    {
        First(query).DataSource.Should().Be("USDA");
    }

    [Theory]
    [InlineData("watermelon")]
    [InlineData("cantaloupe")]
    [InlineData("artichoke")]
    [InlineData("cauliflower")]
    [InlineData("lentils")]
    [InlineData("quinoa")]
    public void WholeFoods_ExpandedAreMarkedAsWholeFoodKind(string query)
    {
        First(query).FoodKind.Should().Be(GutAI.Domain.Enums.FoodKind.WholeFood);
    }

    // ════════════════════════════════════════════════════════════════
    //  ADDITIONAL NUTRITION COMPLETENESS
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("watermelon")]
    [InlineData("cantaloupe")]
    [InlineData("artichoke")]
    [InlineData("cauliflower")]
    [InlineData("tilapia")]
    [InlineData("cod")]
    [InlineData("chickpeas")]
    [InlineData("oats")]
    [InlineData("pasta")]
    [InlineData("tofu")]
    public void TopResult_ExpandedHasBasicNutritionData(string query)
    {
        var food = First(query);
        food.Calories100g.Should().HaveValue();
        food.Protein100g.Should().HaveValue();
        food.Carbs100g.Should().HaveValue();
        food.Fat100g.Should().HaveValue();
    }
}
