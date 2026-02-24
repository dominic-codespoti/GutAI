using System.Text.Json;
using FluentAssertions;

namespace GutAI.Api.Tests;

public static class JsonAssertions
{
    public static void AssertHasStringProperty(this JsonElement element, string propertyName)
    {
        element.TryGetProperty(propertyName, out var prop).Should().BeTrue(
            $"expected property '{propertyName}' to exist on {element}");
        (prop.ValueKind == JsonValueKind.String || prop.ValueKind == JsonValueKind.Null).Should().BeTrue(
            $"expected '{propertyName}' to be String or Null but was {prop.ValueKind}");
    }

    public static void AssertHasNumberProperty(this JsonElement element, string propertyName)
    {
        element.TryGetProperty(propertyName, out var prop).Should().BeTrue(
            $"expected property '{propertyName}' to exist");
        (prop.ValueKind == JsonValueKind.Number || prop.ValueKind == JsonValueKind.Null).Should().BeTrue(
            $"expected '{propertyName}' to be Number or Null but was {prop.ValueKind}");
    }

    public static void AssertHasBoolProperty(this JsonElement element, string propertyName)
    {
        element.TryGetProperty(propertyName, out var prop).Should().BeTrue(
            $"expected property '{propertyName}' to exist");
        (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False).Should().BeTrue(
            $"expected '{propertyName}' to be Boolean but was {prop.ValueKind}");
    }

    public static void AssertHasProperty(this JsonElement element, string propertyName, JsonValueKind expectedKind)
    {
        element.TryGetProperty(propertyName, out var prop).Should().BeTrue(
            $"expected property '{propertyName}' to exist");
        (prop.ValueKind == expectedKind || prop.ValueKind == JsonValueKind.Null).Should().BeTrue(
            $"expected '{propertyName}' to be {expectedKind} or Null but was {prop.ValueKind}");
    }
}
