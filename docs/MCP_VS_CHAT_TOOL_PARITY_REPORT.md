# MCP vs Chat Tool Parity Analysis

> **Date:** 2026-05-14
> **SDK:** `ModelContextProtocol.AspNetCore` **v1.3.0** (upgraded from v1.0.0)
> **Transport:** Streamable HTTP (stateless) — replaced legacy SSE (obsolete per spec 2025-11-25)
> **Files compared:**
>
> - **MCP Tools:** `GutAI.Api/Mcp/FoodTools.cs`, `MealSymptomTools.cs`, `ProfileTools.cs` (3 domain-grouped classes)
> - **Chat:** `GutAI.Infrastructure/Services/AzureOpenAIChatService.cs` (~600 lines)
> - **Chat tool schemas:** `GutAI.Infrastructure/Services/ChatTools.cs`
> - **Shared helpers:** `GutAI.Application/Common/Helpers/TimeZoneHelper.cs`, `FoodDtoHelper.cs`

---

## Executive Summary

The MCP server and Chat service tools are now **at full parity**. Both expose the same 11 tools with the same output shapes and data fidelity. Code for shared logic (`BuildDto`, `GetUserTodayUtcRange`) has been extracted to `GutAI.Application.Common.Helpers` to prevent drift.

The MCP server also includes **one bonus tool** (`GetUserProfile`) that was recently added to the Chat service for parity.

---

## Tool Inventory

| #   | Tool Name                     | Chat | MCP |
| --- | ----------------------------- | ---- | --- |
| 1   | `search_foods`                | ✅   | ✅  |
| 2   | `get_food_safety`             | ✅   | ✅  |
| 3   | `get_fodmap_assessment`       | ✅   | ✅  |
| 4   | `log_meal`                    | ✅   | ✅  |
| 5   | `log_symptom`                 | ✅   | ✅  |
| 6   | `get_todays_meals`            | ✅   | ✅  |
| 7   | `get_trigger_foods`           | ✅   | ✅  |
| 8   | `get_symptom_history`         | ✅   | ✅  |
| 9   | `get_nutrition_summary`       | ✅   | ✅  |
| 10  | `get_elimination_diet_status` | ✅   | ✅  |
| 11  | `get_user_profile`            | ✅   | ✅  |

**Total: 11 tools, 11/11 parity.**

---

## Key Parity Details

### Timezone Handling (✅ Both Correct)

Both MCP and Chat use `TimeZoneHelper.GetUserTodayUtcRange()` for "today" queries:
- `GetTodaysMeals`
- `GetNutritionSummary`

Both use user-timezone-aware ranges for date-back queries:
- `GetTriggerFoods` — uses user's timezone via `TimeZoneHelper`
- `GetSymptomHistory` — uses user's timezone via `TimeZoneHelper`

Reads `user.TimezoneId`, computes local midnight boundaries, converts back to UTC.

### Output Shapes (✅ Full Parity)

| Tool | Chat fields | MCP fields | Match |
|------|-------------|------------|-------|
| `search_foods` | 10 results, 12 fields + `matchConfidence` + `ingredients` + `fiber100g` | Same | ✅ |
| `get_food_safety` | FODMAP + gut risk + personalized score | Same | ✅ |
| `get_fodmap_assessment` | Score, rating, trigger count, triggers with explanations, summary | Same | ✅ |
| `log_meal` response | Full macros per item: calories, protein, carbs, fat, fiber | Same | ✅ |
| `get_todays_meals` response | Full macros per item + meal totals | Same | ✅ |
| `get_nutrition_summary` | Actuals + goals, includes `totalFiberG` | Same | ✅ |
| `get_trigger_foods` | food, symptoms, totalOccurrences, avgSeverity | Same | ✅ |
| `get_user_profile` | DisplayName, allergies, conditions, preferences, goals | Same | ✅ |

### Input Handling (✅ Full Parity)

| Tool | Capability | Chat | MCP |
|------|-----------|------|-----|
| `search_foods` | Query sanitization via `QuerySanitizer` | ✅ | ✅ |
| `log_meal` | Structured `items[]` with `food_product_id` | ✅ | ✅ |
| `log_meal` | Free-text `description` fallback | ✅ | ✅ |
| `log_meal` | `food_product_id` resolves from DB for accurate nutrition | ✅ | ✅ |
| `log_symptom` | Severity clamping (1–10) | ✅ | ✅ |
| `log_symptom` | Unknown symptom type returns available options | ✅ | ✅ |

### Error Handling (✅ Both Robust)

- Chat: Every tool call wrapped in try/catch returning `$"Error executing {name}: {message}"`
- MCP: Every tool call wrapped in try/catch throwing `McpException` (proper JSON-RPC `IsError=true` response rather than a string that appears successful)

### Shared Code (✅ Extracted to Prevent Drift)

The following were previously duplicated between Chat (in `AzureOpenAIChatService`) and MCP (in `GutAiMcpTools`). Both now reference the shared helper:

- `FoodDtoHelper.BuildFoodProductDto()` in `GutAI.Application.Common.Helpers`
- `TimeZoneHelper.GetUserTodayUtcRange()` in `GutAI.Application.Common.Helpers`

### MCP Best Practices (✅ Applied)

- **SDK version:** `ModelContextProtocol.AspNetCore` **1.3.0** (latest, upgraded from 1.0.0)
- **Transport:** Streamable HTTP with `Stateless = true` (replaces legacy SSE which is obsolete per MCP spec 2025-11-25)
- **Tool naming:** snake_case with `gutai_` prefix per SEP-986 convention (e.g., `gutai_search_foods`)
- **Tool metadata:** `[McpServerTool(ReadOnly = true)]` on all query-only tools
- **Error handling:** `McpException` throws → SDK returns proper `CallToolResult { IsError = true }`
- **DI:** Instance methods with constructor injection for `ILogger<T>` and service dependencies
- **Auth:** `AddAuthorizationFilters()` configured in MCP pipeline
- **Organization:** Split into 3 domain-grouped classes (`FoodTools`, `MealSymptomTools`, `ProfileTools`) registered individually via `.WithTools<T>()`

### Tool Registration

- Chat: 11 tools registered in `ChatTools.All`, injected into the OpenAI Assistant at creation time
- MCP: 11 tools via 3 `[McpServerToolType]` classes, registered individually via `.WithTools<T>()` (not assembly scanning)

---

## Remaining Differences (Intentional)

| Difference | Reason |
|-----------|--------|
| MCP tools use `McpException` for errors; Chat returns error strings | Chat's `AzureOpenAIChatService` returns strings to the OpenAI Assistant (which reads error messages from tool result text). MCP clients use JSON-RPC error protocol — `McpException` gives proper `IsError=true` semantics. |
| MCP has `gutai_` prefix on tool names; Chat tool names are bare (`search_foods`) | Chat tools are namespaced by the OpenAI Assistant — the `ChatTools.All` names don't need a prefix. MCP tools live in a global namespace alongside other servers' tools, so the prefix prevents collisions. |
| MCP uses instance methods with constructor DI; Chat uses instance methods with constructor DI | Both now use the same pattern. MCP injects `ILogger<T>` per class. Chat injects a single `ILogger<AzureOpenAIChatService>`. |

## Conclusion

**Full parity achieved.** All 11 tools are present in both MCP and Chat with identical output shapes, input handling, timezone awareness, error handling, and data fidelity. The shared helper pattern (`FoodDtoHelper`, `TimeZoneHelper`) ensures future changes stay in sync.

**MCP-specific upgrades applied:**
- SDK upgraded from v1.0 to v1.3 (latest)
- Transport switched from legacy SSE to Streamable HTTP (stateless)
- Tools use `gutai_` prefix + snake_case naming per SEP-986
- Read-only tools marked with `[McpServerTool(ReadOnly = true)]`
- Error handling uses `McpException` for proper JSON-RPC error propagation
- Logging via constructor-injected `ILogger<T>`
- Auth pipeline uses `AddAuthorizationFilters()`
