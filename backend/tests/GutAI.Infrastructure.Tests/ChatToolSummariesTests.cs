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
    public void ProposeMeal_ProducesDraftCardShape()
    {
        var result = """{"draft_id":"d1","meal_type":"Lunch","totals":{"calories":540.4},"needs_choice_count":1,"items":[{"name":"Chicken bowl"},{"name":"Rice"},{"name":"Apple"},{"name":"Hidden 4th"}]}""";

        var json = ChatToolSummaries.Build("propose_meal", result);

        json.Should().NotBeNull();
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("meal_draft");
        root.GetProperty("draftId").GetString().Should().Be("d1");
        root.GetProperty("mealType").GetString().Should().Be("Lunch");
        root.GetProperty("calories").GetDecimal().Should().Be(540);
        root.GetProperty("needsChoice").GetInt32().Should().Be(1);
        root.GetProperty("items").GetArrayLength().Should().Be(3);
        root.GetProperty("items")[0].GetString().Should().Be("Chicken bowl");
    }

    [Fact]
    public void CommitMeal_ProducesMealLoggedCardShape()
    {
        var result = """{"id":"m1","mealType":"Lunch","totalCalories":540.4,"items":["Chicken bowl","Rice","Apple","Hidden 4th"]}""";

        var json = ChatToolSummaries.Build("commit_meal", result);

        json.Should().NotBeNull();
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("meal_logged");
        root.GetProperty("mealId").GetString().Should().Be("m1");
        root.GetProperty("mealType").GetString().Should().Be("Lunch");
        root.GetProperty("calories").GetDecimal().Should().Be(540);
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

    [Fact]
    public void SuggestMeals_ProducesSuggestionCardSummaries()
    {
        var json = ChatToolSummaries.Build("suggest_meals",
            """{"suggestions":[{"draft_id":"draft-1","title":"Rice bowl","calories":421.6,"items":["Rice","Egg"]}],"budget":{"remaining_calories":900,"meal_target_calories":450},"rejected_count":1}""");
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        Assert.Equal("meal_suggestions", root.GetProperty("type").GetString());
        var suggestion = Assert.Single(root.GetProperty("suggestions").EnumerateArray());
        Assert.Equal("draft-1", suggestion.GetProperty("draftId").GetString());
        Assert.Equal("Rice bowl", suggestion.GetProperty("title").GetString());
        Assert.Equal(422, suggestion.GetProperty("calories").GetDecimal());
        Assert.Equal(new[] { "Rice", "Egg" }, suggestion.GetProperty("items").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Theory]
    [InlineData("""{"suggestions":"invalid"}""")]
    [InlineData("""{"suggestions":[{"title":"Missing id"}]}""")]
    [InlineData("""{"suggestions":[{"draft_id":"id","title":"Meal","calories":"invalid","items":[]}]}""")]
    public void SuggestMeals_MalformedPayloadReturnsNull(string payload)
    {
        Assert.Null(ChatToolSummaries.Build("suggest_meals", payload));
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
        ChatToolSummaries.Build("propose_meal", "{not-json").Should().BeNull();
    }

    [Fact]
    public void NonObjectToolPayload_ReturnsNull()
    {
        ChatToolSummaries.Build("commit_meal", JsonSerializer.Serialize("The user has not confirmed yet."))
            .Should().BeNull();
    }

    [Fact]
    public void NullOrEmptyResult_ReturnsNull()
    {
        ChatToolSummaries.Build("propose_meal", null).Should().BeNull();
        ChatToolSummaries.Build("propose_meal", "").Should().BeNull();
    }
}
