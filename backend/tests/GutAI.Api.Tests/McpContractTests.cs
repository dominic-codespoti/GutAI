using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace GutAI.Api.Tests;

public class McpContractTests
{
    public static IEnumerable<object[]> AllToolTypes()
    {
        yield return new object[] { typeof(GutAI.Api.Mcp.FoodTools) };
        yield return new object[] { typeof(GutAI.Api.Mcp.MealSymptomTools) };
        yield return new object[] { typeof(GutAI.Api.Mcp.ProfileTools) };
    }

    [Fact]
    public void AllToolTypes_HaveToolTypeAttribute()
    {
        var types = AllToolTypes().Select(t => (Type)t[0]).ToList();
        foreach (var type in types)
        {
            type.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false)
                .Should().NotBeEmpty($"{type.Name} should have [McpServerToolType]");
        }
    }

    [Fact]
    public void AllTools_TotalCount_IsEleven()
    {
        var allTools = new List<MethodInfo>();
        foreach (var type in AllToolTypes().Select(t => (Type)t[0]))
        {
            allTools.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0));
        }

        allTools.Should().HaveCount(11, "all 11 MCP tools should have [McpServerTool] attribute");

        var toolNames = allTools.Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name).ToList();
        toolNames.Should().Contain("gutai_search_foods");
        toolNames.Should().Contain("gutai_get_fodmap_assessment");
        toolNames.Should().Contain("gutai_get_food_safety");
        toolNames.Should().Contain("gutai_log_meal");
        toolNames.Should().Contain("gutai_log_symptom");
        toolNames.Should().Contain("gutai_get_todays_meals");
        toolNames.Should().Contain("gutai_get_nutrition_summary");
        toolNames.Should().Contain("gutai_get_trigger_foods");
        toolNames.Should().Contain("gutai_get_symptom_history");
        toolNames.Should().Contain("gutai_get_elimination_diet_status");
        toolNames.Should().Contain("gutai_get_user_profile");
    }

    [Fact]
    public void AllTools_HaveDescription()
    {
        foreach (var type in AllToolTypes().Select(t => (Type)t[0]))
        {
            var tools = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0);

            foreach (var tool in tools)
            {
                var attr = tool.GetCustomAttribute<McpServerToolAttribute>()!;
                var desc = tool.GetCustomAttribute<DescriptionAttribute>()?.Description;
                desc.Should().NotBeNullOrEmpty($"Tool '{attr.Name}' should have a [Description]");
            }
        }
    }

    [Fact]
    public void AllReadOnlyTools_HaveReadOnlyFlag()
    {
        var expectedReadOnly = new HashSet<string>
        {
            "gutai_search_foods",
            "gutai_get_fodmap_assessment",
            "gutai_get_food_safety",
            "gutai_get_todays_meals",
            "gutai_get_nutrition_summary",
            "gutai_get_trigger_foods",
            "gutai_get_symptom_history",
            "gutai_get_elimination_diet_status",
            "gutai_get_user_profile",
        };

        foreach (var type in AllToolTypes().Select(t => (Type)t[0]))
        {
            var tools = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0);

            foreach (var tool in tools)
            {
                var attr = tool.GetCustomAttribute<McpServerToolAttribute>()!;
                if (expectedReadOnly.Contains(attr.Name!))
                {
                    attr.ReadOnly.Should().BeTrue($"Tool '{attr.Name}' should be marked ReadOnly");
                }
            }
        }
    }
}
