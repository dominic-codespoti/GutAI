using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.ExternalApis;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class NaturalLanguageFallbackServiceTests
{
    private readonly Mock<IFoodApiService> _foodApiMock = new();
    private readonly Mock<ITableStore> _storeMock = new();
    private readonly Mock<ILogger<NaturalLanguageFallbackService>> _loggerMock = new();
    private NaturalLanguageFallbackService CreateService() => new(_foodApiMock.Object, _storeMock.Object, _loggerMock.Object);

    private static FoodProductDto MakeFood(string name, decimal cal = 100, decimal protein = 10,
        decimal carbs = 20, decimal fat = 5, decimal? servingQty = null) => new()
        {
            Name = name,
            Calories100g = cal,
            Protein100g = protein,
            Carbs100g = carbs,
            Fat100g = fat,
            Fiber100g = 3,
            Sugar100g = 8,
            Sodium100g = 200,
            ServingQuantity = servingQty
        };

    private void SetupFood(string query, string name, decimal cal = 100, decimal protein = 10,
        decimal carbs = 20, decimal fat = 5, decimal? servingQty = null)
    {
        _foodApiMock.Setup(x => x.SearchAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeFood(name, cal, protein, carbs, fat, servingQty)]);
    }

    // ════════════════════════════════════════════════════════
    //  SplitIntoFoodSegments
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2 eggs and a banana", new[] { "2 eggs", "a banana" })]
    [InlineData("chicken, rice, broccoli", new[] { "chicken", "rice", "broccoli" })]
    [InlineData("toast with butter", new[] { "toast", "butter" })]
    [InlineData("oatmeal plus honey", new[] { "oatmeal", "honey" })]
    [InlineData("eggs & bacon", new[] { "eggs", "bacon" })]
    [InlineData("apple + peanut butter", new[] { "apple", "peanut butter" })]
    [InlineData("just a salad", new[] { "just a salad" })]
    [InlineData("rice; beans; avocado", new[] { "rice", "beans", "avocado" })]
    [InlineData("eggs then toast", new[] { "eggs", "toast" })]
    [InlineData("coffee. toast. eggs", new[] { "coffee", "toast", "eggs" })]
    public void SplitIntoFoodSegments_SplitsCorrectly(string input, string[] expected)
    {
        NaturalLanguageFallbackService.SplitIntoFoodSegments(input)
            .Should().BeEquivalentTo(expected);
    }

    // ════════════════════════════════════════════════════════
    //  ExtractQuantityAndFood — basics
    // ════════════════════════════════════════════════════════

    [Fact]
    public void Extract_NumericNoUnit()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("2 eggs");
        qty.Should().Be(2); unit.Should().BeEmpty(); food.Should().Be("eggs");
    }

    [Fact]
    public void Extract_NumericWithUnit()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("100g chicken");
        qty.Should().Be(100); unit.Should().Be("g"); food.Should().Be("chicken");
    }

    [Fact]
    public void Extract_WordQuantity_A()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("a banana");
        qty.Should().Be(1); unit.Should().BeEmpty(); food.Should().Be("banana");
    }

    [Fact]
    public void Extract_WordWithUnit()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("two cups rice");
        qty.Should().Be(2); unit.Should().Be("cups"); food.Should().Be("rice");
    }

    [Fact]
    public void Extract_Fraction()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("1/2 cup milk");
        qty.Should().Be(0.5m); unit.Should().Be("cup"); food.Should().Be("milk");
    }

    [Fact]
    public void Extract_MixedFraction()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("1 1/2 cups flour");
        qty.Should().Be(1.5m); unit.Should().Be("cups"); food.Should().Be("flour");
    }

    [Fact]
    public void Extract_NoQuantity_Defaults1()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("banana");
        qty.Should().Be(1); unit.Should().BeEmpty(); food.Should().Be("banana");
    }

    [Fact]
    public void Extract_Decimal()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("1.5 oz steak");
        qty.Should().Be(1.5m); unit.Should().Be("oz"); food.Should().Be("steak");
    }

    [Fact]
    public void Extract_Dozen()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("dozen eggs");
        qty.Should().Be(12); food.Should().Be("eggs");
    }

    // ════════════════════════════════════════════════════════
    //  ExtractQuantityAndFood — "of" patterns
    // ════════════════════════════════════════════════════════

    [Fact]
    public void Extract_CupOfRice()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("a cup of rice");
        qty.Should().Be(1); unit.Should().Be("cup"); food.Should().Be("rice");
    }

    [Fact]
    public void Extract_TwoSlicesOfPizza()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("two slices of pizza");
        qty.Should().Be(2); unit.Should().Be("slices"); food.Should().Be("pizza");
    }

    [Fact]
    public void Extract_GlassOfMilk()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("a glass of milk");
        qty.Should().Be(1); unit.Should().Be("glass"); food.Should().Be("milk");
    }

    [Fact]
    public void Extract_BowlOfSoup()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("a bowl of soup");
        qty.Should().Be(1); unit.Should().Be("bowl"); food.Should().Be("soup");
    }

    [Fact]
    public void Extract_HandfulOfAlmonds()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("a handful of almonds");
        qty.Should().Be(1); unit.Should().Be("handful"); food.Should().Be("almonds");
    }

    [Fact]
    public void Extract_ScoopOfProtein()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("two scoops of protein powder");
        qty.Should().Be(2); unit.Should().Be("scoops"); food.Should().Be("protein powder");
    }

    [Fact]
    public void Extract_CanOfTuna()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("a can of tuna");
        qty.Should().Be(1); unit.Should().Be("can"); food.Should().Be("tuna");
    }

    [Fact]
    public void Extract_BottleOfWater()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("a bottle of water");
        qty.Should().Be(1); unit.Should().Be("bottle"); food.Should().Be("water");
    }

    [Fact]
    public void Extract_3PiecesOfChicken()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("3 pieces of chicken");
        qty.Should().Be(3); unit.Should().Be("pieces"); food.Should().Be("chicken");
    }

    // ════════════════════════════════════════════════════════
    //  ExtractQuantityAndFood — filler words
    // ════════════════════════════════════════════════════════

    [Fact]
    public void Extract_AboutPrefix()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("about 200g chicken");
        qty.Should().Be(200); unit.Should().Be("g"); food.Should().Be("chicken");
    }

    [Fact]
    public void Extract_RoughlyPrefix()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("roughly 3 eggs");
        qty.Should().Be(3); unit.Should().BeEmpty(); food.Should().Be("eggs");
    }

    [Fact]
    public void Extract_SomeOatmeal()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("some oatmeal");
        // "some" as filler stripped, then "oatmeal" has no quantity → defaults to 1
        qty.Should().Be(1); food.Should().Be("oatmeal");
    }

    [Fact]
    public void Extract_MaybePrefix()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("maybe 2 cups rice");
        qty.Should().Be(2); unit.Should().Be("cups"); food.Should().Be("rice");
    }

    [Fact]
    public void Extract_JustPrefix()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("just a banana");
        qty.Should().Be(1); food.Should().Be("banana");
    }

    // ════════════════════════════════════════════════════════
    //  ExtractQuantityAndFood — more word numbers
    // ════════════════════════════════════════════════════════

    [Fact]
    public void Extract_Eleven()
    {
        var (qty, _, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("eleven grapes");
        qty.Should().Be(11); food.Should().Be("grapes");
    }

    [Fact]
    public void Extract_Fifteen()
    {
        var (qty, _, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("fifteen almonds");
        qty.Should().Be(15); food.Should().Be("almonds");
    }

    [Fact]
    public void Extract_Twenty()
    {
        var (qty, _, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("twenty blueberries");
        qty.Should().Be(20); food.Should().Be("blueberries");
    }

    [Fact]
    public void Extract_Few()
    {
        var (qty, _, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("few strawberries");
        qty.Should().Be(3); food.Should().Be("strawberries");
    }

    [Fact]
    public void Extract_Several()
    {
        var (qty, _, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("several crackers");
        qty.Should().Be(4); food.Should().Be("crackers");
    }

    [Fact]
    public void Extract_Couple()
    {
        var (qty, _, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("couple cookies");
        qty.Should().Be(2); food.Should().Be("cookies");
    }

    [Fact]
    public void Extract_An()
    {
        var (qty, unit, food) = NaturalLanguageFallbackService.ExtractQuantityAndFood("an apple");
        qty.Should().Be(1); unit.Should().BeEmpty(); food.Should().Be("apple");
    }

    // ════════════════════════════════════════════════════════
    //  PreprocessText
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("I had 2 eggs and toast", "2 eggs and toast")]
    [InlineData("I ate a banana", "a banana")]
    [InlineData("for lunch I had chicken and rice", "chicken and rice")]
    [InlineData("I just ate some pizza", "some pizza")]
    [InlineData("for breakfast I ate oatmeal", "oatmeal")]
    [InlineData("I grabbed a coffee", "a coffee")]
    [InlineData("just normal text", "just normal text")]
    [InlineData("I snacked on some almonds", "some almonds")]
    [InlineData("2 eggs and toast.", "2 eggs and toast")]
    public void PreprocessText_RemovesFillers(string input, string expected)
    {
        NaturalLanguageFallbackService.PreprocessText(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("½ cup milk", "1/2 cup milk")]
    [InlineData("¼ lb beef", "1/4 lb beef")]
    [InlineData("¾ cup flour", "3/4 cup flour")]
    [InlineData("⅓ cup sugar", "1/3 cup sugar")]
    public void PreprocessText_ConvertsUnicodeFractions(string input, string expected)
    {
        NaturalLanguageFallbackService.PreprocessText(input).Should().Contain(expected.Split(' ')[0]);
    }

    // ════════════════════════════════════════════════════════
    //  CleanFoodName
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chicken (grilled)", "chicken")]
    [InlineData("eggs (scrambled)", "eggs")]
    [InlineData("salmon (raw)", "salmon")]
    [InlineData("steak (medium rare)", "steak")]
    [InlineData("broccoli (steamed)", "broccoli")]
    [InlineData("chicken breast", "chicken breast")]
    public void CleanFoodName_RemovesParentheticals(string input, string expected)
    {
        NaturalLanguageFallbackService.CleanFoodName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("chicken on the side", "chicken")]
    [InlineData("cake for dessert", "cake")]
    [InlineData("oatmeal this morning", "oatmeal")]
    [InlineData("pasta tonight", "pasta")]
    [InlineData("pizza yesterday", "pizza")]
    [InlineData("stir fry last night", "stir fry")]
    public void CleanFoodName_RemovesTrailingPhrases(string input, string expected)
    {
        NaturalLanguageFallbackService.CleanFoodName(input).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════
    //  ExtractSizeMultiplier
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("small banana", 0.7, "banana")]
    [InlineData("large apple", 1.3, "apple")]
    [InlineData("medium potato", 1.0, "potato")]
    [InlineData("extra large egg", 1.5, "egg")]
    [InlineData("big steak", 1.3, "steak")]
    [InlineData("tiny muffin", 0.7, "muffin")]
    [InlineData("jumbo shrimp", 1.5, "shrimp")]
    [InlineData("regular banana", 1.0, "regular banana")]
    public void SizeMultiplier_ExtractsCorrectly(string input, double expectedMultiplier, string expectedFood)
    {
        var food = input;
        var multiplier = NaturalLanguageFallbackService.ExtractSizeMultiplier(ref food);
        multiplier.Should().Be((decimal)expectedMultiplier);
        food.Should().Be(expectedFood);
    }

    // ════════════════════════════════════════════════════════
    //  EstimateServingWeightG
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("g", "chicken", 1)]
    [InlineData("oz", "steak", 28.35)]
    [InlineData("kg", "rice", 1000)]
    [InlineData("lb", "beef", 453.6)]
    [InlineData("mg", "supplement", 0.001)]
    public void Serving_WeightUnits(string unit, string food, double expected)
    {
        NaturalLanguageFallbackService.EstimateServingWeightG(MakeFood(food), unit, food)
            .Should().Be((decimal)expected);
    }

    [Fact]
    public void Serving_CupOfRice() =>
        NaturalLanguageFallbackService.EstimateServingWeightG(MakeFood("rice"), "cup", "rice")
            .Should().Be(185m);

    [Fact]
    public void Serving_GlassOfMilk() =>
        NaturalLanguageFallbackService.EstimateServingWeightG(MakeFood("milk"), "glass", "milk")
            .Should().Be(240m);

    [Fact]
    public void Serving_BowlOfSoup() =>
        NaturalLanguageFallbackService.EstimateServingWeightG(MakeFood("soup"), "bowl", "soup")
            .Should().Be(300m);

    [Fact]
    public void Serving_PintOfBeer() =>
        NaturalLanguageFallbackService.EstimateServingWeightG(MakeFood("beer"), "pint", "beer")
            .Should().Be(473m);

    [Fact]
    public void Serving_UsesProductServing_WhenNoUnit() =>
        NaturalLanguageFallbackService.EstimateServingWeightG(MakeFood("banana", servingQty: 120m), "", "banana")
            .Should().Be(120m);

    [Fact]
    public void Serving_DefaultsForEgg() =>
        NaturalLanguageFallbackService.EstimateServingWeightG(MakeFood("egg"), "", "egg")
            .Should().Be(50m);

    // ════════════════════════════════════════════════════════
    //  Full ParseAsync — basic patterns
    // ════════════════════════════════════════════════════════

    [Fact]
    public async Task ParseAsync_TwoEggsAndBanana()
    {
        SetupFood("eggs", "Egg", cal: 155, protein: 13, carbs: 1.1m, fat: 11);
        SetupFood("banana", "Banana", cal: 89, protein: 1.1m, carbs: 23, fat: 0.3m);

        var result = await CreateService().ParseAsync("2 eggs and a banana");

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Egg");
        result[0].ServingQuantity.Should().Be(2);
        result[0].Calories.Should().BeGreaterThan(0);
        result[1].Name.Should().Be("Banana");
        result[1].ServingQuantity.Should().Be(1);
    }

    [Fact]
    public async Task ParseAsync_NoResults_ReturnsGenericEstimate()
    {
        _foodApiMock.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().ParseAsync("xyznonexistentfood");
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("xyznonexistentfood");
        result[0].Calories.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ParseAsync_WithGrams_ScalesCorrectly()
    {
        SetupFood("chicken", "Chicken Breast", cal: 165, protein: 31, carbs: 0, fat: 3.6m);

        var result = await CreateService().ParseAsync("200g chicken");

        result.Should().HaveCount(1);
        result[0].Calories.Should().Be(330m);
        result[0].ProteinG.Should().Be(62m);
        result[0].ServingWeightG.Should().Be(200m);
    }

    [Fact]
    public async Task ParseAsync_CommaSeparated()
    {
        SetupFood("chicken", "Chicken");
        SetupFood("rice", "Rice");
        SetupFood("broccoli", "Broccoli");

        (await CreateService().ParseAsync("chicken, rice, broccoli")).Should().HaveCount(3);
    }

    [Fact]
    public async Task ParseAsync_ApiException_FallsBackToGenericEstimate()
    {
        _foodApiMock.Setup(x => x.SearchAsync("eggs", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));
        SetupFood("banana", "Banana");

        var result = await CreateService().ParseAsync("eggs and banana");
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("eggs");
        result[0].Calories.Should().BeGreaterThan(0);
        result[1].Name.Should().Be("Banana");
    }

    [Fact]
    public async Task ParseAsync_WithOz_ScalesCorrectly()
    {
        SetupFood("steak", "Steak", cal: 250, protein: 26, carbs: 0, fat: 15);

        var result = await CreateService().ParseAsync("8 oz steak");
        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(226.8m);
        result[0].Calories.Should().Be(Math.Round(250m * 2.268m, 1));
    }

    // ════════════════════════════════════════════════════════
    //  Full ParseAsync — rich natural language sentences
    // ════════════════════════════════════════════════════════

    [Fact]
    public async Task ParseAsync_IHadSentence()
    {
        SetupFood("eggs", "Egg", cal: 155, protein: 13, carbs: 1.1m, fat: 11);
        SetupFood("toast", "Toast", cal: 265, protein: 9, carbs: 49, fat: 3);

        var result = await CreateService().ParseAsync("I had 2 eggs and toast");

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Egg");
        result[0].ServingQuantity.Should().Be(2);
        result[1].Name.Should().Be("Toast");
    }

    [Fact]
    public async Task ParseAsync_ForBreakfastIAte()
    {
        SetupFood("oatmeal", "Oatmeal", cal: 68, protein: 2.4m, carbs: 12, fat: 1.4m);
        SetupFood("blueberries", "Blueberries", cal: 57, protein: 0.7m, carbs: 14, fat: 0.3m);

        var result = await CreateService().ParseAsync("for breakfast I ate oatmeal and blueberries");

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Oatmeal");
        result[1].Name.Should().Be("Blueberries");
    }

    [Fact]
    public async Task ParseAsync_CupOfRice()
    {
        SetupFood("rice", "White Rice", cal: 130, protein: 2.7m, carbs: 28, fat: 0.3m);

        var result = await CreateService().ParseAsync("a cup of rice");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("White Rice");
        result[0].ServingWeightG.Should().Be(185m);
    }

    [Fact]
    public async Task ParseAsync_TwoSlicesOfPizza()
    {
        SetupFood("pizza", "Pizza", cal: 266, protein: 11, carbs: 33, fat: 10);

        var result = await CreateService().ParseAsync("2 slices of pizza");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Pizza");
        result[0].ServingQuantity.Should().Be(2);
    }

    [Fact]
    public async Task ParseAsync_GlassOfMilk()
    {
        SetupFood("milk", "Whole Milk", cal: 61, protein: 3.2m, carbs: 4.8m, fat: 3.3m);

        var result = await CreateService().ParseAsync("a glass of milk");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Whole Milk");
        result[0].ServingWeightG.Should().Be(240m);
    }

    [Fact]
    public async Task ParseAsync_BowlOfSoup()
    {
        SetupFood("soup", "Chicken Soup", cal: 36, protein: 2.5m, carbs: 4, fat: 1);

        var result = await CreateService().ParseAsync("a bowl of soup");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Chicken Soup");
        result[0].ServingWeightG.Should().Be(300m);
    }

    [Fact]
    public async Task ParseAsync_SizeModifier_LargeApple()
    {
        SetupFood("apple", "Apple", cal: 52, protein: 0.3m, carbs: 14, fat: 0.2m);

        var result = await CreateService().ParseAsync("a large apple");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Apple");
        // large = 1.3x, default apple = 180g → 234g
        result[0].ServingWeightG.Should().Be(234m);
    }

    [Fact]
    public async Task ParseAsync_SizeModifier_SmallBanana()
    {
        SetupFood("banana", "Banana", cal: 89, protein: 1.1m, carbs: 23, fat: 0.3m);

        var result = await CreateService().ParseAsync("a small banana");

        result.Should().HaveCount(1);
        // small = 0.7x, default banana = 120g → 84g
        result[0].ServingWeightG.Should().Be(84m);
    }

    [Fact]
    public async Task ParseAsync_ParentheticalDescription()
    {
        SetupFood("chicken", "Chicken", cal: 165, protein: 31, carbs: 0, fat: 3.6m);

        var result = await CreateService().ParseAsync("200g chicken (grilled)");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Chicken");
        result[0].ServingWeightG.Should().Be(200m);
    }

    [Fact]
    public async Task ParseAsync_AboutPrefix()
    {
        SetupFood("chicken", "Chicken Breast", cal: 165, protein: 31, carbs: 0, fat: 3.6m);

        var result = await CreateService().ParseAsync("about 200g chicken");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(200m);
        result[0].Calories.Should().Be(330m);
    }

    [Fact]
    public async Task ParseAsync_SemicolonSeparated()
    {
        SetupFood("eggs", "Eggs");
        SetupFood("bacon", "Bacon");
        SetupFood("toast", "Toast");

        var result = await CreateService().ParseAsync("eggs; bacon; toast");

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task ParseAsync_PeriodSeparated()
    {
        SetupFood("coffee", "Coffee");
        SetupFood("toast", "Toast");

        var result = await CreateService().ParseAsync("coffee. toast");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseAsync_HandfulOfNuts()
    {
        SetupFood("almonds", "Almonds", cal: 579, protein: 21, carbs: 22, fat: 50, servingQty: 28);

        var result = await CreateService().ParseAsync("a handful of almonds");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Almonds");
        result[0].ServingWeightG.Should().Be(28m);
    }

    [Fact]
    public async Task ParseAsync_ThenSeparator()
    {
        SetupFood("cereal", "Cereal");
        SetupFood("orange juice", "Orange Juice");

        var result = await CreateService().ParseAsync("cereal then orange juice");

        result.Should().HaveCount(2);
    }

    // ════════════════════════════════════════════════════════
    //  Full ParseAsync — weird/edge-case sentences
    // ════════════════════════════════════════════════════════

    [Fact]
    public async Task ParseAsync_IJustGrabbed()
    {
        SetupFood("coffee", "Coffee", cal: 2, protein: 0.3m, carbs: 0, fat: 0);

        var result = await CreateService().ParseAsync("I just grabbed a coffee");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Coffee");
    }

    [Fact]
    public async Task ParseAsync_CasualMixedSentence()
    {
        SetupFood("chicken", "Chicken", cal: 165, protein: 31, carbs: 0, fat: 3.6m);
        SetupFood("rice", "Rice", cal: 130, protein: 2.7m, carbs: 28, fat: 0.3m);
        SetupFood("salad", "Salad", cal: 20, protein: 1.5m, carbs: 3.5m, fat: 0.2m);

        var result = await CreateService().ParseAsync("I had 200g chicken with a cup of rice and some salad");

        result.Should().HaveCount(3);
        result[0].Name.Should().Be("Chicken");
        result[0].ServingWeightG.Should().Be(200m);
        result[1].Name.Should().Be("Rice");
        result[1].ServingWeightG.Should().Be(185m);
    }

    [Fact]
    public async Task ParseAsync_FewCookies()
    {
        SetupFood("cookies", "Cookie", cal: 502, protein: 5, carbs: 65, fat: 25);

        var result = await CreateService().ParseAsync("few cookies");

        result.Should().HaveCount(1);
        result[0].ServingQuantity.Should().Be(3);
    }

    [Fact]
    public async Task ParseAsync_SeveralSlicesOfBread()
    {
        SetupFood("bread", "Bread", cal: 265, protein: 9, carbs: 49, fat: 3);

        var result = await CreateService().ParseAsync("several slices of bread");

        result.Should().HaveCount(1);
        result[0].ServingQuantity.Should().Be(4);
    }

    [Fact]
    public async Task ParseAsync_CanOfTuna()
    {
        SetupFood("tuna", "Tuna", cal: 116, protein: 26, carbs: 0, fat: 0.8m, servingQty: 85);

        var result = await CreateService().ParseAsync("a can of tuna");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Tuna");
        result[0].ServingWeightG.Should().Be(85m);
    }

    [Fact]
    public async Task ParseAsync_EmptyText_ReturnsEmpty()
    {
        var result = await CreateService().ParseAsync("");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_WhitespaceOnly_ReturnsEmpty()
    {
        var result = await CreateService().ParseAsync("   \n  ");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_ComplexMeal()
    {
        SetupFood("steak", "Steak", cal: 250, protein: 26, carbs: 0, fat: 15);
        SetupFood("potato", "Baked Potato", cal: 93, protein: 2.5m, carbs: 21, fat: 0.1m);
        SetupFood("asparagus", "Asparagus", cal: 20, protein: 2.2m, carbs: 3.9m, fat: 0.1m);

        var result = await CreateService().ParseAsync("8 oz steak, a large potato, and asparagus");

        result.Should().HaveCount(3);
        result[0].Name.Should().Be("Steak");
        result[0].ServingWeightG.Should().Be(226.8m);
        result[1].Name.Should().Be("Baked Potato");
        result[1].ServingWeightG.Should().Be(195m); // large = 1.3 * 150
    }

    [Fact]
    public async Task ParseAsync_UnicodeFractionHalfCupRice()
    {
        SetupFood("rice", "Rice", cal: 130, protein: 2.7m, carbs: 28, fat: 0.3m);

        var result = await CreateService().ParseAsync("½ cup rice");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(92.5m); // 0.5 * 185
    }

    [Fact]
    public async Task ParseAsync_UnicodeFractionThreeQuarterLbBeef()
    {
        SetupFood("beef", "Ground Beef", cal: 250, protein: 26, carbs: 0, fat: 15);

        var result = await CreateService().ParseAsync("¾ lb beef");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(340.2m); // 0.75 * 453.6
    }

    [Fact]
    public async Task ParseAsync_PoundAndAHalf()
    {
        SetupFood("ground turkey", "Ground Turkey", cal: 170, protein: 21, carbs: 0, fat: 9);

        var result = await CreateService().ParseAsync("1 1/2 lbs ground turkey");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(680.4m); // 1.5 * 453.6
    }

    [Fact]
    public async Task ParseAsync_ForDinnerPrefix()
    {
        SetupFood("salmon", "Salmon", cal: 208, protein: 20, carbs: 0, fat: 13);
        SetupFood("salad", "Salad", cal: 20, protein: 1.5m, carbs: 3.5m, fat: 0.2m);

        var result = await CreateService().ParseAsync("for dinner I had salmon and salad");

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Salmon");
        result[1].Name.Should().Be("Salad");
    }

    [Fact]
    public async Task ParseAsync_MultiLineInput()
    {
        SetupFood("eggs", "Eggs", cal: 155, protein: 13, carbs: 1.1m, fat: 11);
        SetupFood("toast", "Toast", cal: 265, protein: 9, carbs: 49, fat: 3);
        SetupFood("orange juice", "Orange Juice", cal: 45, protein: 0.7m, carbs: 10, fat: 0.2m);

        var result = await CreateService().ParseAsync("2 eggs\ntoast\norange juice");

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task ParseAsync_DecimalOunces()
    {
        SetupFood("cheese", "Cheddar Cheese", cal: 402, protein: 25, carbs: 1.3m, fat: 33);

        var result = await CreateService().ParseAsync("1.5 oz cheese");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(42.525m); // 1.5 * 28.35
    }

    [Fact]
    public async Task ParseAsync_JustPrefix()
    {
        SetupFood("banana", "Banana", cal: 89, protein: 1.1m, carbs: 23, fat: 0.3m);

        var result = await CreateService().ParseAsync("just a banana");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Banana");
    }

    [Fact]
    public async Task ParseAsync_ApproximatelyPrefix()
    {
        SetupFood("rice", "Rice", cal: 130, protein: 2.7m, carbs: 28, fat: 0.3m);

        var result = await CreateService().ParseAsync("approximately 2 cups rice");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(370m); // 2 * 185
    }

    [Fact]
    public async Task ParseAsync_TrailingPeriod()
    {
        SetupFood("banana", "Banana", cal: 89, protein: 1.1m, carbs: 23, fat: 0.3m);

        var result = await CreateService().ParseAsync("a banana.");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Banana");
    }

    [Fact]
    public async Task ParseAsync_ExtraLargeModifier()
    {
        SetupFood("egg", "Egg", cal: 155, protein: 13, carbs: 1.1m, fat: 11);

        var result = await CreateService().ParseAsync("an extra large egg");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(75m); // 1.5 * 50
    }

    [Fact]
    public async Task ParseAsync_MixedFractionCup()
    {
        SetupFood("flour", "Flour", cal: 364, protein: 10, carbs: 76, fat: 1);

        var result = await CreateService().ParseAsync("1 1/2 cups flour");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(187.5m); // 1.5 * 125
    }

    [Fact]
    public async Task ParseAsync_GrilledChickenParenthetical()
    {
        SetupFood("chicken breast", "Chicken Breast", cal: 165, protein: 31, carbs: 0, fat: 3.6m);

        var result = await CreateService().ParseAsync("6 oz chicken breast (grilled)");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Chicken Breast");
        result[0].ServingWeightG.Should().Be(170.1m); // 6 * 28.35
    }

    [Fact]
    public async Task ParseAsync_TwoBottlesOfWater()
    {
        SetupFood("water", "Water", cal: 0, protein: 0, carbs: 0, fat: 0, servingQty: 500);

        var result = await CreateService().ParseAsync("two bottles of water");

        result.Should().HaveCount(1);
        result[0].ServingQuantity.Should().Be(2);
        result[0].ServingWeightG.Should().Be(1000m); // 2 * 500
    }

    [Fact]
    public async Task ParseAsync_QuarterCupOliveOil()
    {
        SetupFood("olive oil", "Olive Oil", cal: 884, protein: 0, carbs: 0, fat: 100);

        var result = await CreateService().ParseAsync("quarter cup of olive oil");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(55m); // 0.25 * 220
    }

    [Fact]
    public async Task ParseAsync_OnePlainWord()
    {
        SetupFood("banana", "Banana", cal: 89, protein: 1.1m, carbs: 23, fat: 0.3m);

        var result = await CreateService().ParseAsync("banana");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Banana");
        result[0].ServingQuantity.Should().Be(1);
    }

    [Fact]
    public async Task ParseAsync_PlusSeparator()
    {
        SetupFood("protein shake", "Protein Shake");
        SetupFood("banana", "Banana");

        var result = await CreateService().ParseAsync("protein shake + banana");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseAsync_AmpersandSeparator()
    {
        SetupFood("peanut butter", "Peanut Butter");
        SetupFood("jelly", "Jelly");

        var result = await CreateService().ParseAsync("peanut butter & jelly");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseAsync_MultipleSizeModifiers()
    {
        SetupFood("apple", "Apple", cal: 52, protein: 0.3m, carbs: 14, fat: 0.2m);
        SetupFood("banana", "Banana", cal: 89, protein: 1.1m, carbs: 23, fat: 0.3m);

        var result = await CreateService().ParseAsync("a large apple and a small banana");

        result.Should().HaveCount(2);
        result[0].ServingWeightG.Should().Be(234m);   // 1.3 * 180
        result[1].ServingWeightG.Should().Be(84m);     // 0.7 * 120
    }

    [Fact]
    public async Task ParseAsync_KilogramUnit()
    {
        SetupFood("chicken", "Chicken", cal: 165, protein: 31, carbs: 0, fat: 3.6m);

        var result = await CreateService().ParseAsync("1 kg chicken");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(1000m);
        result[0].Calories.Should().Be(1650m);
    }

    [Fact]
    public async Task ParseAsync_TablespoonHoney()
    {
        SetupFood("honey", "Honey", cal: 304, protein: 0.3m, carbs: 82, fat: 0);

        var result = await CreateService().ParseAsync("2 tbsp honey");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(30m); // 2 * 15
    }

    [Fact]
    public async Task ParseAsync_TeaspoonSugar()
    {
        SetupFood("sugar", "Sugar", cal: 387, protein: 0, carbs: 100, fat: 0);

        var result = await CreateService().ParseAsync("3 tsp sugar");

        result.Should().HaveCount(1);
        result[0].ServingWeightG.Should().Be(15m); // 3 * 5
    }

    [Fact]
    public async Task ParseAsync_ForSnackISnackedOn()
    {
        SetupFood("almonds", "Almonds", cal: 579, protein: 21, carbs: 22, fat: 50);

        var result = await CreateService().ParseAsync("for snack I snacked on some almonds");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Almonds");
    }

    [Fact]
    public async Task ParseAsync_DrankAGlassOfJuice()
    {
        SetupFood("orange juice", "Orange Juice", cal: 45, protein: 0.7m, carbs: 10, fat: 0.2m);

        var result = await CreateService().ParseAsync("I drank a glass of orange juice");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Orange Juice");
        result[0].ServingWeightG.Should().Be(240m);
    }

    // ════════════════════════════════════════════════════════
    //  Cup/Default estimates (expanded)
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("rice", 185)]
    [InlineData("milk", 240)]
    [InlineData("flour", 125)]
    [InlineData("spinach", 60)]
    [InlineData("almonds", 140)]
    [InlineData("unknown food", 150)]
    [InlineData("quinoa", 185)]
    [InlineData("yogurt", 245)]
    [InlineData("granola", 120)]
    [InlineData("honey", 340)]
    [InlineData("black beans", 180)]
    [InlineData("chicken broth", 240)]
    [InlineData("ice cream", 140)]
    public void CupWeight_CorrectValues(string food, int expected) =>
        NaturalLanguageFallbackService.EstimateCupWeightG(food).Should().Be(expected);

    [Theory]
    [InlineData("egg", 50)]
    [InlineData("banana", 120)]
    [InlineData("chicken breast", 140)]
    [InlineData("bread", 30)]
    [InlineData("cheese", 30)]
    [InlineData("random thing", 100)]
    [InlineData("avocado", 150)]
    [InlineData("pizza", 110)]
    [InlineData("cookie", 30)]
    [InlineData("bacon", 8)]
    [InlineData("muffin", 115)]
    [InlineData("taco", 80)]
    [InlineData("pancake", 75)]
    [InlineData("sausage", 50)]
    [InlineData("salmon", 140)]
    [InlineData("garlic", 4)]
    [InlineData("watermelon", 280)]
    [InlineData("tofu", 125)]
    [InlineData("olive", 15)]
    [InlineData("donut", 60)]
    public void DefaultServing_CorrectValues(string food, int expected) =>
        NaturalLanguageFallbackService.EstimateDefaultServingG(food).Should().Be(expected);

    // ════════════════════════════════════════════════════════
    //  FormatServingSize
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData(2, "cups", "2 cups")]
    [InlineData(1, "", "1")]
    [InlineData(0.5, "cup", "0.5 cup")]
    [InlineData(3, "slices", "3 slices")]
    [InlineData(1.5, "oz", "1.5 oz")]
    [InlineData(12, "", "12")]
    public void FormatServingSize_FormatsCorrectly(double qty, string unit, string expected) =>
        NaturalLanguageFallbackService.FormatServingSize((decimal)qty, unit).Should().Be(expected);

    // ════════════════════════════════════════════════════════
    //  StripOfPrefix
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("of rice", "rice")]
    [InlineData("of peanut butter", "peanut butter")]
    [InlineData("chicken", "chicken")]
    [InlineData("of", "of")]
    public void StripOfPrefix_Works(string input, string expected)
    {
        NaturalLanguageFallbackService.StripOfPrefix(input).Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════
    //  GenericEstimate — when food APIs return nothing
    // ════════════════════════════════════════════════════════

    [Theory]
    [InlineData("protein shake", 80)]
    [InlineData("protein bar", 350)]
    [InlineData("smoothie", 70)]
    [InlineData("juice", 45)]
    [InlineData("coffee", 40)]
    [InlineData("beer", 43)]
    [InlineData("pizza", 270)]
    [InlineData("burger", 250)]
    [InlineData("salad", 20)]
    [InlineData("xyzunknownfood", 150)]
    public void EstimateGenericCaloriesPer100g_ReturnsReasonable(string food, decimal expectedCal)
    {
        var result = NaturalLanguageFallbackService.EstimateGenericCaloriesPer100g(food);
        result.calories.Should().Be(expectedCal);
        (result.protein + result.carbs + result.fat).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateGenericEstimate_ProteinShake_HasReasonableValues()
    {
        var result = NaturalLanguageFallbackService.CreateGenericEstimate("rokeby protein shake", 1m, "", 1m);
        result.Name.Should().Be("rokeby protein shake");
        result.Calories.Should().BeGreaterThan(100);
        result.ProteinG.Should().BeGreaterThan(10);
        result.ServingWeightG.Should().Be(350m);
    }

    [Fact]
    public async Task ParseAsync_UnknownBrand_ReturnsGenericEstimate()
    {
        _foodApiMock.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().ParseAsync("a rokeby protein shake");
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("rokeby protein shake");
        result[0].Calories.Should().BeGreaterThan(0);
        result[0].ProteinG.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ParseAsync_UnknownBrandMultiple_ReturnsEstimatesForAll()
    {
        _foodApiMock.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().ParseAsync("a protein shake and a banana");
        result.Should().HaveCount(2);
    }
}
