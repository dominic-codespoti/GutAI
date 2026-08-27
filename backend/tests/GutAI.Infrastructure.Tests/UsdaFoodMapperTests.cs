using FluentAssertions;
using GutAI.Domain.Constants;
using GutAI.Infrastructure.ExternalApis;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class UsdaFoodMapperTests
{
    [Fact]
    public void ToDto_BrandedFood_WithIngredients_MapsThroughTrimmed()
    {
        // Arrange
        var food = new UsdaFood
        {
            FdcId = 123456,
            Description = "Organic Almond Milk",
            BrandOwner = "Silk",
            DataType = "Branded",
            Ingredients = " Almondmilk (Filtered Water, Almonds), Cane Sugar, Sea Salt.  ",
            FoodNutrients =
            [
                new UsdaNutrient { NutrientId = 1008, NutrientName = "Energy", Value = 60m },
                new UsdaNutrient { NutrientId = 2000, NutrientName = "Sugars, total including NLEA", Value = 7m }
            ]
        };

        // Act
        var dto = UsdaFoodMapper.ToDto(food);

        // Assert
        dto.Should().NotBeNull();
        dto.Name.Should().Be("Organic Almond Milk");
        dto.Brand.Should().Be("Silk");
        dto.Ingredients.Should().Be("Almondmilk (Filtered Water, Almonds), Cane Sugar, Sea Salt.");
        dto.Calories100g.Should().Be(60m);
        dto.Sugar100g.Should().Be(7m);
        dto.DataSource.Should().Be(DataSources.Usda);
        dto.SourceVersion.Should().Be("live-api");
        dto.LicenseType.Should().Be("USDA FoodData Central terms");
        dto.Attribution.Should().Be("USDA FoodData Central");
        dto.ExternalId.Should().Be("123456");
        dto.SourceUrl.Should().Be("https://fdc.nal.usda.gov/fdc-app.html#/food-details/123456/nutrients");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ToDto_NullOrWhitespaceIngredients_BecomesNull(string? rawIngredients)
    {
        // Arrange
        var food = new UsdaFood
        {
            FdcId = 999,
            Description = "Sample Food",
            BrandOwner = "Sample Brand",
            DataType = "Branded",
            Ingredients = rawIngredients
        };

        // Act
        var dto = UsdaFoodMapper.ToDto(food);

        // Assert
        dto.Ingredients.Should().BeNull();
    }

    [Fact]
    public void ToDto_WholeFoodSrLegacy_WithNullIngredients_TitleCasesNameAndSetsBrandNull()
    {
        // Arrange
        var food = new UsdaFood
        {
            FdcId = 78910,
            Description = "APPLES, RAW, WITH SKIN",
            BrandOwner = "Grower Association",
            DataType = "SR Legacy",
            Ingredients = null,
            FoodNutrients =
            [
                new UsdaNutrient { NutrientId = 1008, Value = 52m },
                new UsdaNutrient { NutrientId = 1003, Value = 0.26m },
                new UsdaNutrient { NutrientId = 1005, Value = 13.81m },
                new UsdaNutrient { NutrientId = 1004, Value = 0.17m },
                new UsdaNutrient { NutrientId = 1079, Value = 2.4m },
                new UsdaNutrient { NutrientId = 2000, Value = 10.39m },
                new UsdaNutrient { NutrientId = 1093, Value = 1m }
            ]
        };

        // Act
        var dto = UsdaFoodMapper.ToDto(food);

        // Assert
        dto.Name.Should().Be("Apples, Raw, With Skin");
        dto.Brand.Should().BeNull();
        dto.Ingredients.Should().BeNull();
        dto.Calories100g.Should().Be(52m);
        dto.Protein100g.Should().Be(0.26m);
        dto.Carbs100g.Should().Be(13.81m);
        dto.Fat100g.Should().Be(0.17m);
        dto.Fiber100g.Should().Be(2.4m);
        dto.Sugar100g.Should().Be(10.39m);
        dto.SodiumMg100g.Should().Be(1m);
    }

    [Fact]
    public void ToDto_WholeFoodFoundation_SetsBrandNull()
    {
        // Arrange
        var food = new UsdaFood
        {
            FdcId = 111222,
            Description = "Bananas, raw",
            BrandOwner = "Farm Co",
            DataType = "Foundation",
            Ingredients = null
        };

        // Act
        var dto = UsdaFoodMapper.ToDto(food);

        // Assert
        dto.Name.Should().Be("Bananas, raw");
        dto.Brand.Should().BeNull();
        dto.Ingredients.Should().BeNull();
    }
}
