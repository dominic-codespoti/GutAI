using System.Text.Json;
using FluentAssertions;
using GutAI.Application.Chat;
using Xunit;

namespace GutAI.Infrastructure.Tests;

/// <summary>
/// Contract tests for the SSE { tool_result, summary } payload builder.
/// Guards the typed shapes the frontend renders as rich chat cards
/// (AGENTS.md #3: every new response shape gets explicit assertions).
/// </summary>
public class ChatToolSummariesTests
{
    [Fact]
    public void LogMeal_ProducesMealLoggedShape()
    {
        var result = """{"id":"m1","mealType":"Lunch","totalCalories":540.4,"items":[{"FoodName":"Chicken bowl","Calories":400},{"FoodName":"Rice","Calories":140},{"FoodName":"Apple","Calories":80},{"FoodName":"Hidden 4th"}]}""";

        var json = ChatToolSummaries.Build("log_meal", result);

        json.Should().NotBeNull();
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("meal_logged");
        root.GetProperty("mealType").GetString().Should().Be("Lunch");
        root.GetProperty("calories").GetDecimal().Should().Be(540);
        // Items are capped at 3 for card display.
        root.GetProperty("items").GetArrayLength().Should().Be(3);
        root.GetProperty("items")[0].GetString().Should().Be("Chicken bowl");
    }

    [Fact]
    public void GetTodaysMeals_AggregatesCountAndCalories()
    {
        var result = """[{"mealType":"Breakfast","totalCalories":320.5},{"mealType":"Lunch","totalCalories":610.25}]""";

        var json = ChatToolSummaries.Build("get_todays_meals", result);

        json.Should().NotBeNull();
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("meals_today");
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("calories").GetDecimal().Should().Be(931);
    }

    [Fact]
    public void GetTriggerFoods_ExtractsTopAndCount()
    {
        var result = """[{"food":"Wheat bread","symptoms":["Bloating"],"totalOccurrences":4,"avgSeverity":6.5},{"food":"Milk","symptoms":["Cramps"],"totalOccurrences":2,"avgSeverity":5.0}]""";

        var json = ChatToolSummaries.Build("get_trigger_foods", result);

        json.Should().NotBeNull();
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("triggers");
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("top").GetString().Should().Be("Wheat bread");
    }

    [Theory]
    [InlineData("get_food_safety")]
    [InlineData("search_foods")]
    [InlineData("get_user_profile")]
    public void LowValueTools_ReturnNull(string tool)
    {
        ChatToolSummaries.Build(tool, "{}").Should().BeNull();
    }

    [Fact]
    public void MalformedJson_ReturnsNull()
    {
        ChatToolSummaries.Build("log_meal", "{not-json").Should().BeNull();
    }

    [Fact]
    public void NullOrEmptyResult_ReturnsNull()
    {
        ChatToolSummaries.Build("log_meal", null).Should().BeNull();
        ChatToolSummaries.Build("log_meal", "").Should().BeNull();
    }
}
