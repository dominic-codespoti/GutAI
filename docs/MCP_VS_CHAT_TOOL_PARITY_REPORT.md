# MCP vs Chat Tool Parity Analysis

> **Date:** 2026-03-13
> **Files compared:**
>
> - **MCP:** `backend/src/GutAI.Api/Mcp/GutAiMcpTools.cs` (313 lines)
> - **Chat:** `backend/src/GutAI.Infrastructure/Services/AzureOpenAIChatService.cs` (668 lines)
> - **Chat tool schemas:** `backend/src/GutAI.Infrastructure/Services/ChatTools.cs`

---

## Executive Summary

The MCP server tools are **significantly behind** the Chat service tools. There are **15+ discrete gaps** spanning missing tools, broken timezone handling, absent personalization, reduced data fidelity, and missing input/output fields. An external AI app using the MCP server today gets a materially degraded experience compared to the internal AI Coach.

---

## A) Feature Parity — Missing Tools

### Tools in Chat but NOT in MCP

| Tool              | Impact                                                                                                                              | Severity        |
| ----------------- | ----------------------------------------------------------------------------------------------------------------------------------- | --------------- |
| `get_food_safety` | Personalized safety report combining FODMAP + gut risk + personalized scoring (user allergies/conditions). MCP only has raw FODMAP. | 🔴 **Critical** |

### Tools in MCP but NOT in Chat

None. MCP is a strict subset of Chat.

### Tool inventory

| #   | Tool Name                     | Chat | MCP                    |
| --- | ----------------------------- | ---- | ---------------------- |
| 1   | `search_foods`                | ✅   | ✅                     |
| 2   | `get_food_safety`             | ✅   | ❌                     |
| 3   | `get_fodmap_assessment`       | ✅   | ✅                     |
| 4   | `log_meal`                    | ✅   | ✅ (degraded)          |
| 5   | `log_symptom`                 | ✅   | ✅                     |
| 6   | `get_todays_meals`            | ✅   | ✅ (broken)            |
| 7   | `get_trigger_foods`           | ✅   | ✅ (degraded)          |
| 8   | `get_symptom_history`         | ✅   | ✅                     |
| 9   | `get_nutrition_summary`       | ✅   | ✅ (broken + degraded) |
| 10  | `get_elimination_diet_status` | ✅   | ✅                     |

---

## B) Timezone Handling — 🔴 BROKEN in MCP

The Chat service was correctly fixed to use `GetUserTodayUtcRange(user)` which:

1. Reads `user.TimezoneId`
2. Converts `DateTime.UtcNow` to the user's local timezone
3. Computes local midnight→midnight boundaries
4. Converts those boundaries back to UTC for querying
5. Post-filters results within the precise UTC range

**MCP still uses the broken pattern `DateOnly.FromDateTime(DateTime.UtcNow)` in 3 places:**

| MCP Method            | Line | Broken Pattern                                                   |
| --------------------- | ---- | ---------------------------------------------------------------- |
| `GetTodaysMeals`      | ~166 | `var today = DateOnly.FromDateTime(DateTime.UtcNow)`             |
| `GetNutritionSummary` | ~230 | `var today = DateOnly.FromDateTime(DateTime.UtcNow)`             |
| `GetTriggerFoods`     | ~186 | `var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(...))` |
| `GetSymptomHistory`   | ~205 | `var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(...))` |

**Impact:** For a user in `Australia/Sydney` (UTC+10/+11), calling `GetTodaysMeals` at 8am local time returns **yesterday's meals** because UTC is still the previous date. Conversely at 11pm local, meals from the next UTC day bleed in.

The Chat service's `GetTodaysMeals` and `GetNutritionSummary` also use `GetMealLogsByDateRangeAsync` + post-filter, while MCP's `GetTodaysMeals` uses the simpler `GetMealLogsByDateAsync(userId, today)` which lacks the sub-day precision.

---

## C) User Profile / Personalization Gaps

### C1. `GetNutritionSummary` — Missing `totalFiberG`

| Field           | Chat                                                   | MCP            |
| --------------- | ------------------------------------------------------ | -------------- |
| `totalCalories` | ✅                                                     | ✅             |
| `totalProteinG` | ✅                                                     | ✅             |
| `totalCarbsG`   | ✅                                                     | ✅             |
| `totalFatG`     | ✅                                                     | ✅             |
| `totalFiberG`   | ✅ `meals.SelectMany(m => m.Items).Sum(i => i.FiberG)` | ❌ **Missing** |
| `goals.fiberG`  | ✅                                                     | ✅             |

MCP returns a fiber _goal_ but not the actual fiber _intake_, making the goal useless.

### C2. `GetFoodSafety` — Entirely Missing from MCP

Chat's `get_food_safety` tool calls three services:

- `FodmapService.Assess()` — FODMAP score
- `GutRiskService.Assess()` — Gut health risk (additives, NOVA, sodium, etc.)
- `PersonalizedScoringService.ScoreAsync()` — Composite score factoring in user's **allergies, gut conditions, and meal history**

**Neither `GutRiskService` nor `PersonalizedScoringService` are injected or referenced anywhere in the MCP tools file.** MCP only exposes raw FODMAP, which is unpersonalized.

### C3. `LogMeal` — No Structured Items / `food_product_id` Support

| Capability                        | Chat                                         | MCP            |
| --------------------------------- | -------------------------------------------- | -------------- |
| Accept `items[]` array            | ✅                                           | ❌             |
| Accept `food_product_id` per item | ✅ (resolves from DB for accurate nutrition) | ❌             |
| Accept `servings` per item        | ✅                                           | ❌             |
| Free-text `description` fallback  | ✅                                           | ✅ (only mode) |
| Link `FoodProductId` on MealItem  | ✅                                           | ❌             |

**Impact:** MCP's `LogMeal` only accepts a free-text `description` and delegates entirely to the external Nutritionix API for nutrition estimation. Chat's version can resolve `food_product_id` from a prior `search_foods` call, computing nutrition directly from the local food database (which has curated data including FODMAP-relevant fields). This means MCP-logged meals:

- Have **less accurate nutrition data**
- Cannot be traced back to specific food products
- Cannot benefit from the local branded/USDA database

---

## D) Data Completeness — Output Shape Differences

### D1. `SearchFoods`

| Field              | Chat                           | MCP          |
| ------------------ | ------------------------------ | ------------ |
| `index`            | ✅ (1-based)                   | ❌           |
| `id`               | ✅                             | ✅           |
| `name`             | ✅                             | ✅           |
| `brand`            | ✅                             | ✅           |
| `dataSource`       | ✅                             | ❌           |
| `calories100g`     | ✅                             | ✅           |
| `protein100g`      | ✅                             | ✅           |
| `carbs100g`        | ✅                             | ✅           |
| `fat100g`          | ✅                             | ✅           |
| `fiber100g`        | ✅                             | ❌           |
| `servingSize`      | ✅                             | ✅           |
| `matchConfidence`  | ✅                             | ❌           |
| `ingredients`      | ✅ (truncated to 120 chars)    | ❌           |
| Result limit       | **10**                         | **5**        |
| Wrapper object     | `{ results: [...] }`           | bare array   |
| Query sanitization | ✅ `QuerySanitizer.Sanitize()` | ❌ raw input |

**6 missing fields + half the results + no input sanitization.**

### D2. `GetFodmapAssessment`

| Field                    | Chat | MCP |
| ------------------------ | ---- | --- |
| `FodmapScore`            | ✅   | ✅  |
| `FodmapRating`           | ✅   | ✅  |
| `TriggerCount`           | ✅   | ✅  |
| `triggers[].Name`        | ✅   | ✅  |
| `triggers[].Category`    | ✅   | ✅  |
| `triggers[].Severity`    | ✅   | ✅  |
| `triggers[].Explanation` | ✅   | ❌  |
| `Summary`                | ✅   | ✅  |

Missing `Explanation` on triggers means the MCP consumer can't explain _why_ a food is a FODMAP trigger.

### D3. `LogMeal` Response

| Field              | Chat | MCP |
| ------------------ | ---- | --- |
| `id`               | ✅   | ✅  |
| `mealType`         | ✅   | ✅  |
| `totalCalories`    | ✅   | ✅  |
| `totalProteinG`    | ✅   | ❌  |
| `totalCarbsG`      | ✅   | ❌  |
| `totalFatG`        | ✅   | ❌  |
| `totalFiberG`      | ✅   | ❌  |
| `items[].FoodName` | ✅   | ✅  |
| `items[].Calories` | ✅   | ✅  |
| `items[].ProteinG` | ✅   | ❌  |
| `items[].CarbsG`   | ✅   | ❌  |
| `items[].FatG`     | ✅   | ❌  |
| `items[].FiberG`   | ✅   | ❌  |

MCP only returns `FoodName` + `Calories` per item. Chat returns full macros.

### D4. `GetTodaysMeals`

| Field              | Chat | MCP |
| ------------------ | ---- | --- |
| `mealType`         | ✅   | ✅  |
| `loggedAt`         | ✅   | ✅  |
| `totalCalories`    | ✅   | ✅  |
| `totalProteinG`    | ✅   | ❌  |
| `totalCarbsG`      | ✅   | ❌  |
| `totalFatG`        | ✅   | ❌  |
| `items[].FoodName` | ✅   | ✅  |
| `items[].Calories` | ✅   | ✅  |
| `items[].ProteinG` | ✅   | ❌  |
| `items[].CarbsG`   | ✅   | ❌  |
| `items[].FatG`     | ✅   | ❌  |
| `items[].FiberG`   | ✅   | ❌  |

Same pattern: MCP strips all macro fields except calories.

### D5. `GetTriggerFoods`

| Field              | Chat | MCP |
| ------------------ | ---- | --- |
| `food`             | ✅   | ✅  |
| `symptoms`         | ✅   | ✅  |
| `totalOccurrences` | ✅   | ❌  |
| `avgSeverity`      | ✅   | ✅  |

Missing `totalOccurrences` — an important signal for how strong the correlation evidence is.

### D6. `BuildDto` (MCP) vs `BuildFoodProductDto` (Chat)

The MCP helper `BuildDto` populates `FoodAdditiveDto` with only **5 fields**:

```
Id, Name, CspiRating, Category, ENumber, HealthConcerns
```

The Chat helper `BuildFoodProductDto` populates **8 fields**:

```
Id, Name, CspiRating, UsRegulatoryStatus, EuRegulatoryStatus, SafetyRating, Category, ENumber, HealthConcerns
```

Missing from MCP DTO:

- `UsRegulatoryStatus`
- `EuRegulatoryStatus`
- `SafetyRating`

These fields are required by `GutRiskService` for accurate safety scoring (if it were ever added to MCP).

Additionally, the MCP `BuildDto` does not set:

- `Barcode` (set in Chat)
- `NutriScore` (set in Chat)

---

## E) Other Differences & Bugs

### E1. No `QuerySanitizer` in MCP's `SearchFoods`

Chat sanitizes search input via `QuerySanitizer.Sanitize()` which strips special characters and normalizes whitespace. MCP passes the raw query string directly to `foodApi.SearchAsync()`. This is both a **potential injection concern** and a **functional difference** (malformed queries may fail or return poor results).

### E2. Error Handling

Chat wraps every tool execution in a `try/catch` that returns a structured error message:

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Tool execution failed: {Tool}", functionName);
    return $"Error executing {functionName}: {ex.Message}";
}
```

MCP tools have **no error handling**. A `Guid.Parse` failure, null reference, or DB exception will propagate as an unhandled exception, likely returning a 500 to the MCP client.

### E3. `LogMeal` / `LogSymptom` use `DateTime.UtcNow` directly

Both MCP and Chat set `LoggedAt = DateTime.UtcNow` and `OccurredAt = DateTime.UtcNow`. This is **technically correct** (storing UTC) but interacts badly with MCP's broken date-range queries (see §B). Chat mitigates this because its queries use the timezone-aware range.

### E4. MCP `GetTodaysMeals` uses `GetMealLogsByDateAsync` (single date)

Chat uses `GetMealLogsByDateRangeAsync` spanning two UTC dates (to handle timezone offsets that cross date boundaries). MCP uses `GetMealLogsByDateAsync(userId, today)` which queries a single UTC date, meaning it can miss meals logged near midnight in the user's timezone.

### E5. Services not injected in MCP

MCP has no access to:

- `GutRiskService` — gut health risk assessment
- `PersonalizedScoringService` — composite personalized safety scoring
- `QuerySanitizer` — input sanitization

These would need to be registered and injected to achieve parity.

### E6. No logging in MCP

Chat has `_logger.IsEnabled(LogLevel.Debug)` conditional debug logging for `log_meal` arguments. MCP has no logging at all, making debugging tool calls difficult.

---

## Priority Summary

| #   | Issue                                                    | Severity    | Effort                               |
| --- | -------------------------------------------------------- | ----------- | ------------------------------------ |
| 1   | Timezone bug in `GetTodaysMeals` + `GetNutritionSummary` | 🔴 Critical | Medium — port `GetUserTodayUtcRange` |
| 2   | Missing `get_food_safety` tool                           | 🔴 Critical | Medium — inject services, add method |
| 3   | `LogMeal` lacks `items[]` / `food_product_id` support    | 🔴 Critical | Medium — port structured item logic  |
| 4   | `SearchFoods` missing 6 fields + limit 5 vs 10           | 🟡 High     | Small                                |
| 5   | `GetNutritionSummary` missing `totalFiberG`              | 🟡 High     | Trivial                              |
| 6   | `GetFodmapAssessment` missing trigger `Explanation`      | 🟡 High     | Trivial                              |
| 7   | `LogMeal` response missing macro breakdown               | 🟡 High     | Small                                |
| 8   | `GetTodaysMeals` response missing macros                 | 🟡 High     | Small                                |
| 9   | No `QuerySanitizer` on search input                      | 🟡 High     | Trivial                              |
| 10  | No try/catch error handling                              | 🟡 High     | Small                                |
| 11  | `GetTriggerFoods` missing `totalOccurrences`             | 🟢 Medium   | Trivial                              |
| 12  | `BuildDto` missing additive regulatory fields            | 🟢 Medium   | Small                                |
| 13  | `BuildDto` missing `Barcode` + `NutriScore`              | 🟢 Low      | Trivial                              |
| 14  | No debug logging                                         | 🟢 Low      | Trivial                              |

---

## Recommended Fix Order

1. **Port `GetUserTodayUtcRange`** into MCP (or extract to a shared static utility and reference it from both). Fix `GetTodaysMeals` and `GetNutritionSummary` to use it.
2. **Add `GetFoodSafety`** tool — inject `GutRiskService` + `PersonalizedScoringService`, add `[McpServerTool]` method.
3. **Port structured `LogMeal`** — add `items[]` parameter with `food_product_id` resolution, keep `description` as fallback.
4. **Enrich output shapes** — add missing fields to `SearchFoods`, `GetTodaysMeals`, `LogMeal` response, `GetFodmapAssessment`, `GetTriggerFoods`, and `GetNutritionSummary`.
5. **Add `QuerySanitizer`** to `SearchFoods`.
6. **Wrap all tools in try/catch** with structured error returns.
7. **Align `BuildDto`** with `BuildFoodProductDto` — add missing additive and product fields.
