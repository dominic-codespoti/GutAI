using System.Text.Json;

namespace GutAI.Application.Chat;

/// <summary>
/// Compact typed summaries for high-value coach tools, embedded in SSE
/// { tool_result, summary } events so clients can render rich cards without
/// re-parsing full tool payloads. Unknown or low-value tools yield null →
/// clients render a neutral "done" chip.
///
/// Supported shapes:
///   log_meal          → { type:"meal_logged", mealType, calories, items[] }
///   get_todays_meals  → { type:"meals_today", count, calories }
///   get_trigger_foods → { type:"triggers", count, top }
/// </summary>
public static class ChatToolSummaries
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static string? Build(string toolName, string? resultJson)
    {
        if (string.IsNullOrEmpty(resultJson)) return null;
        try
        {
            switch (toolName)
            {
                case "log_meal":
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    var root = doc.RootElement;
                    var calories = root.TryGetProperty("totalCalories", out var cal)
                        ? Math.Round(cal.GetDecimal()) : 0;
                    var mealType = root.TryGetProperty("mealType", out var mt)
                        ? mt.GetString() : null;
                    var itemNames = new List<string>();
                    if (root.TryGetProperty("items", out var itemsEl)
                        && itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in itemsEl.EnumerateArray())
                        {
                            if (it.TryGetProperty("FoodName", out var fn))
                                itemNames.Add(fn.GetString() ?? "");
                            else if (it.TryGetProperty("foodName", out var fn2))
                                itemNames.Add(fn2.GetString() ?? "");
                            if (itemNames.Count >= 3) break;
                        }
                    }
                    return JsonSerializer.Serialize(new
                    {
                        type = "meal_logged",
                        mealType,
                        calories,
                        items = itemNames,
                    }, JsonOpts);
                }
                case "get_todays_meals" when resultJson.StartsWith("["):
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    int count = 0;
                    decimal totalCalories = 0;
                    foreach (var m in doc.RootElement.EnumerateArray())
                    {
                        count++;
                        if (m.TryGetProperty("totalCalories", out var cal))
                            totalCalories += cal.GetDecimal();
                    }
                    return JsonSerializer.Serialize(new
                    {
                        type = "meals_today",
                        count,
                        calories = Math.Round(totalCalories),
                    }, JsonOpts);
                }
                case "get_trigger_foods" when resultJson.StartsWith("["):
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    string? top = null;
                    int count = 0;
                    foreach (var t in doc.RootElement.EnumerateArray())
                    {
                        count++;
                        if (top is null && t.TryGetProperty("food", out var f))
                            top = f.GetString();
                    }
                    return JsonSerializer.Serialize(new
                    {
                        type = "triggers",
                        count,
                        top,
                    }, JsonOpts);
                }
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
