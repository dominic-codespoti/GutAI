using System.Text.Json;

namespace GutAI.Application.Chat;

/// <summary>
/// Compact typed summaries for high-value coach tools, embedded in SSE
/// { tool_result, summary } events so clients can render rich cards without
/// re-parsing full tool payloads. Unknown or low-value tools yield null →
/// clients render a neutral "done" chip.
///
/// Supported shapes:
///   propose_meal       → { type:"meal_draft", draftId, mealType, calories, items[], needsChoice }
///   commit_meal        → { type:"meal_logged", mealId, mealType, calories, items[] }
///   get_todays_meals   → { type:"meals_today", count, calories }
///   get_trigger_foods  → { type:"triggers", count, top }
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
                case "propose_meal":
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;
                    var items = root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array
                        ? itemsEl.EnumerateArray().Take(3).Select(item => item.TryGetProperty("name", out var name)
                            ? name.GetString() ?? "" : "").ToList()
                        : [];
                    var calories = root.TryGetProperty("totals", out var totals)
                        && totals.TryGetProperty("calories", out var cal) ? cal.GetDecimal() : 0m;
                    return JsonSerializer.Serialize(new
                    {
                        type = "meal_draft",
                        draftId = root.TryGetProperty("draft_id", out var id) ? id.GetString() : null,
                        mealType = root.TryGetProperty("meal_type", out var mt) ? mt.GetString() : null,
                        calories = Math.Round(calories),
                        items,
                        needsChoice = root.TryGetProperty("needs_choice_count", out var choices) ? choices.GetInt32() : 0
                    }, JsonOpts);
                }
                case "suggest_meals":
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("suggestions", out var suggestions)
                        || suggestions.ValueKind != JsonValueKind.Array)
                        return null;

                    var cards = new List<object>();
                    foreach (var suggestion in suggestions.EnumerateArray())
                    {
                        if (suggestion.ValueKind != JsonValueKind.Object
                            || !suggestion.TryGetProperty("draft_id", out var id)
                            || id.ValueKind != JsonValueKind.String
                            || !suggestion.TryGetProperty("title", out var title)
                            || title.ValueKind != JsonValueKind.String
                            || !suggestion.TryGetProperty("calories", out var calories)
                            || calories.ValueKind != JsonValueKind.Number
                            || !suggestion.TryGetProperty("items", out var itemsElement)
                            || itemsElement.ValueKind != JsonValueKind.Array)
                            return null;

                        var items = new List<string>();
                        foreach (var item in itemsElement.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.String) return null;
                            items.Add(item.GetString()!);
                        }

                        cards.Add(new
                        {
                            draftId = id.GetString(),
                            title = title.GetString(),
                            calories = Math.Round(calories.GetDecimal()),
                            items
                        });
                    }

                    return JsonSerializer.Serialize(new { type = "meal_suggestions", suggestions = cards }, JsonOpts);
                }
                case "commit_meal":
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;
                    var itemNames = root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array
                        ? itemsEl.EnumerateArray().Take(3).Select(item => item.GetString() ?? "").ToList()
                        : [];
                    var calories = root.TryGetProperty("totalCalories", out var cal) ? Math.Round(cal.GetDecimal()) : 0;
                    return JsonSerializer.Serialize(new
                    {
                        type = "meal_logged",
                        mealId = root.TryGetProperty("id", out var id) ? id.GetString() : null,
                        mealType = root.TryGetProperty("mealType", out var mt) ? mt.GetString() : null,
                        calories,
                        items = itemNames
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
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return null;
        }
    }
}
