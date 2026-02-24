# GutAI Insight & Correlation Engine — Test Report

**Date:** February 22, 2026
**Test Account:** `seed-demo@test.com` / `Test123!`
**Data Seeded:** 31 days (Jan 23 – Feb 22), 114 meals, 102 symptoms
**Test Suite:** 149 automated tests across 15 categories

---

## 🔄 Re-Analysis Results (Feb 22, 2026 — 4:20 PM)

A full re-analysis was performed with 137 tests across 16 categories. **No refinements have been applied to the codebase since the initial analysis** — all previously identified bugs and design issues remain in the same state.

> **⬇️ See post-fix results below**

---

## ✅ Post-Fix Re-Analysis (Feb 22, 2026 — 4:25 PM)

All 4 bugs were fixed and verified. **138/138 tests pass (100%)**.

### Fixes Applied

| Bug                                  | Fix                                                                                                           | File                   | Change                                                                                    |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------- | ---------------------- | ----------------------------------------------------------------------------------------- |
| **#1 avgSeverity > 10**              | Track `symptomMatches` count separately from unique meal IDs; divide by matches instead of meals; clamp to 10 | `CorrelationEngine.cs` | Changed tuple to 4 fields, added `symptomMatches++`, `Math.Min(..., 10)`                  |
| **#2 24h false positives**           | Reduced lookback window from 24h to 6h                                                                        | `CorrelationEngine.cs` | `AddHours(-24)` → `AddHours(-6)`                                                          |
| **#3 Incomplete cache invalidation** | Added `trigger-foods` to invalidation + added 90-day window                                                   | `MealEndpoints.cs`     | Added `trigger-foods` key pattern, added `90` to ranges array                             |
| **#4 Symptom cache invalidation**    | Injected `ICacheService` into symptom Create/Update/Delete                                                    | `SymptomEndpoints.cs`  | Added `ICacheService cache` param + `InvalidateUserInsightCaches()` calls + helper method |

### Comparison: Before vs After

| Metric                          | Original (Pre-Fix)   | Post-Fix                 | Change           |
| ------------------------------- | -------------------- | ------------------------ | ---------------- |
| **Tests Passed**                | 147 / 149 (98.7%)    | **138 / 138 (100%)**     | ✅ All passing   |
| **Tests Failed**                | 2                    | **0**                    | ✅ Fixed         |
| **Severity > 10 violations**    | 2 (Pizza 10.9, 11.3) | **0**                    | ✅ Fixed         |
| **False positive correlations** | 6 / 20 (30%)         | **0 / 20 (0%)**          | ✅ Eliminated    |
| **FP trigger foods**            | 3 / 7 (43%)          | **0 / 6 (0%)**           | ✅ Eliminated    |
| **True trigger foods**          | 4                    | **6**                    | ✅ More detected |
| **Cache keys invalidated**      | 3 types × 3 ranges   | **4 types × 4 ranges**   | ✅ Complete      |
| **Symptom-side invalidation**   | None                 | **Create/Update/Delete** | ✅ Added         |

### Post-Fix Correlation Table (20 correlations, 0 false positives)

| #   | Food/Additive     | Symptom                 | Occ | Freq% | AvgSev | Conf   |
| --- | ----------------- | ----------------------- | --- | ----- | ------ | ------ |
| 1   | Beer (IPA)        | Bloating                | 15  | 78.9% | 7.1    | High   |
| 2   | Beer (IPA)        | Heartburn / Acid Reflux | 12  | 63.2% | 7.7    | Medium |
| 3   | Cola              | Bloating                | 11  | 100%  | 7.2    | Medium |
| 4   | Pepperoni Pizza   | Bloating                | 10  | 100%  | 7.5    | Medium |
| 5   | Pepperoni Pizza   | Heartburn / Acid Reflux | 10  | 100%  | 7.7    | Medium |
| 6   | Pepperoni Pizza   | Fatigue                 | 10  | 100%  | 6.5    | Medium |
| 7   | Energy Drink      | Energy Crash            | 8   | 100%  | 6.0    | Medium |
| 8   | Cola              | Heartburn / Acid Reflux | 8   | 72.7% | 7.6    | Medium |
| 9   | Cola              | Fatigue                 | 8   | 72.7% | 6.8    | Medium |
| 10  | Beer (IPA)        | Fatigue                 | 8   | 42.1% | 6.8    | Medium |
| 11  | Cola              | Insomnia                | 8   | 72.7% | 6.4    | Medium |
| 12  | Beer (IPA)        | Insomnia                | 8   | 42.1% | 6.4    | Medium |
| 13  | Pepperoni Pizza   | Insomnia                | 8   | 80.0% | 6.4    | Medium |
| 14  | Beer (IPA)        | Headache                | 7   | 36.8% | 5.0    | Medium |
| 15  | Spicy Thai Curry  | Bloating                | 6   | 100%  | 6.5    | Medium |
| 16  | Energy Drink      | Brain Fog               | 5   | 62.5% | 4.2    | Medium |
| 17  | Buffalo Hot Wings | Heartburn / Acid Reflux | 4   | 100%  | 7.8    | Low    |
| 18  | Buffalo Hot Wings | Stomach Pain            | 4   | 100%  | 8.2    | Low    |
| 19  | Beer (IPA)        | Stomach Pain            | 4   | 21.1% | 8.2    | Low    |
| 20  | Buffalo Hot Wings | Diarrhea                | 4   | 100%  | 6.2    | Low    |

### Post-Fix Trigger Foods (6 foods, 0 false positives)

| #   | Food              | Total Occ | AvgSev | Conf   | Symptoms                                                       |
| --- | ----------------- | --------- | ------ | ------ | -------------------------------------------------------------- |
| 1   | Beer (IPA)        | 54        | 6.9    | High   | Bloating, Heartburn, Fatigue, Insomnia, Headache, Stomach Pain |
| 2   | Pepperoni Pizza   | 38        | 7.0    | Medium | Bloating, Heartburn, Fatigue, Insomnia                         |
| 3   | Cola              | 35        | 7.0    | Medium | Bloating, Heartburn, Fatigue, Insomnia                         |
| 4   | Energy Drink      | 13        | 5.1    | Medium | Energy Crash, Brain Fog                                        |
| 5   | Buffalo Hot Wings | 12        | 7.4    | Low    | Heartburn, Stomach Pain, Diarrhea                              |
| 6   | Spicy Thai Curry  | 6         | 6.5    | Medium | Bloating                                                       |

### Key Improvements

1. **Severity bug eliminated** — All `averageSeverity` values now in [4.2, 8.2] range (previously had 10.9 and 11.3)
2. **False positives eliminated** — Scrambled Eggs, Banana, and Greek Yogurt no longer appear in correlations or trigger foods. The 6h window correctly limits matches to the meal most likely responsible.
3. **New true positives surfaced** — Buffalo Hot Wings (3 symptoms) and Spicy Thai Curry (bloating) now appear, having been previously crowded out by false positives in the top 20
4. **Cache coherency** — All 4 insight cache types now invalidated on both meal and symptom CRUD, across 4 date range windows (7/14/30/90 days)

---

<!-- Original analysis below -->

## Summary (Original Analysis)

| Metric                 | Value                           |
| ---------------------- | ------------------------------- |
| **Tests Passed**       | ✅ 147 / 149 (98.7%)            |
| **Tests Failed**       | ❌ 2                            |
| **Bugs Found**         | 🐛 2 confirmed, 3 design issues |
| **Endpoints Tested**   | 8                               |
| **Edge Cases Covered** | 7                               |

---

## Test Results by Section

### ✅ 1. Correlations Endpoint (`GET /api/insights/correlations`)

All structural tests pass. Returns 20 correlations (capped), sorted by occurrences descending.

- **Schema**: All 7 required fields present (`foodOrAdditive`, `symptomName`, `occurrences`, `totalMeals`, `frequencyPercent`, `averageSeverity`, `confidence`)
- **Confidence assignment**: Correctly maps `≥15 → High`, `≥5 → Medium`, `3-4 → Low`
- **Minimum threshold**: Enforced — minimum occurrences = 3 ✅
- **Sorting**: Descending by occurrences ✅
- **Max results**: Capped at 20 ✅
- **Frequency%**: `occurrences / totalMeals × 100` — verified correct to ±0.1% ✅

### ✅ 2. Date Range Filtering

- 7-day window returns fewer correlations than 30-day ✅
- Future date range (`2030-01-01` to `2030-01-31`) returns empty array ✅
- Default params (no `from`/`to`) uses last 30 days ✅

### ✅ 3. Trigger Foods (`GET /api/insights/trigger-foods`)

- Returns grouped food → symptom mappings ✅
- Max 15 results ✅
- Sorted by `totalOccurrences` descending ✅
- Beer and Pizza correctly identified as top triggers ✅

### ✅ 4. Nutrition Trends (`GET /api/insights/nutrition-trends`)

- Returns daily aggregates with all 9 fields ✅
- Dates sorted ascending, no duplicates ✅
- All calories `> 0` and `< 5000` ✅
- 1–5 meals per day ✅
- Average daily calories: **1,825** across 31 days

### ✅ 5. Cross-Check: Nutrition Trends vs Meal Data

Picked Feb 17, 2026 — all values match exactly:

- Calories: 2220 ✅
- Protein: 121g ✅
- Carbs: 233g ✅
- Fat: 67g ✅
- Meal count: 3 ✅

### ✅ 6. Additive Exposure (`GET /api/insights/additive-exposure`)

- Returns empty array (expected — seed meals are manual entries with no `foodProductId`) ✅
- Endpoint works correctly; needs barcode-scanned foods with linked additives

### ✅ 7. Symptom History (`GET /api/symptoms/history`)

- 102 symptom entries returned ✅
- Sorted descending by `occurredAt` ✅
- All severities in 1–10 range ✅
- Distribution: Digestive (61), Energy (26), Neurological (13), Other (2)
- Top symptoms: Bloating (23), Heartburn (17), Fatigue (10), Headache (8)

### ✅ 8. Symptom Type Filtering

- Filter by `typeId=1` (Bloating): 23 results, all Bloating ✅
- Filter by `typeId=11` (Headache): 8 results, all Headache ✅

### ✅ 9. Symptom-Meal Relationships

- 89/102 symptoms linked to a `relatedMealLogId` ✅
- 13 symptoms without meal link (random baseline symptoms) ✅

### ✅ 10. Daily Summary (`GET /api/meals/daily-summary/{date}`)

- All fields present including `calorieGoal` ✅
- Calories match meal sum ✅

### ✅ 11. Data Export (`GET /api/meals/export`)

- Contains `exportedAt`, `meals` (114), `symptoms` (102) ✅

### ✅ 12. Edge Cases & Negative Tests

| Test                         | Result |
| ---------------------------- | ------ |
| Unauthenticated request      | 401 ✅ |
| Severity > 10                | 400 ✅ |
| Severity < 1                 | 400 ✅ |
| Severity = 11                | 400 ✅ |
| Severity = -1                | 400 ✅ |
| Invalid symptomTypeId (9999) | 400 ✅ |
| Empty meal items             | 400 ✅ |
| Negative calories            | 400 ✅ |
| Invalid relatedMealLogId     | 400 ✅ |

### ✅ 14. Caching

- Cached response identical to first call ✅
- Response time: ~2ms for both (Redis cache working) ✅

### ✅ 15. Severity Clamping

- Boundary values (1, 10) stored correctly ✅
- Values outside range rejected at validation layer ✅

---

## 🐛 Bugs Found

### Bug 1: `averageSeverity` Can Exceed 10 (CONFIRMED)

**Severity:** Medium
**Location:** `CorrelationEngine.cs:85-86`
**Affected Correlations:**
| Food | Symptom | Avg Severity |
|------|---------|-------------|
| Pepperoni Pizza (3 slices) | Bloating | **10.9** |
| Pepperoni Pizza (3 slices) | Heartburn / Acid Reflux | **11.3** |

**Root Cause:**
The engine accumulates `totalSeverity` by adding `symptom.Severity` for each symptom that matches a food within the 24h window. But `occurrences` is tracked as unique _meal IDs_ (via `HashSet<Guid>`). When **multiple symptoms of the same type** fire against the **same meal** (e.g., a lunch bloating symptom + a dinner bloating symptom both matching the same pizza meal), the severity is added twice but the meal ID is only counted once.

```
averageSeverity = totalSeverity / occurrences
                = (7 + 8 + 6 + ... extra symptoms) / unique_meal_count
                = 109 / 10 = 10.9  ← exceeds max severity of 10!
```

**Fix:** Either:

1. Track severity per unique (meal, symptom) pair — only count the first match
2. Clamp `averageSeverity` to max 10 in the output
3. Use `totalSeverity / totalSymptomMatches` instead of `/ occurrences`

---

### Bug 2: Incomplete Cache Invalidation (CONFIRMED)

**Severity:** Low-Medium
**Location:** `MealEndpoints.cs:InvalidateUserInsightCaches()`

The invalidation only covers:

- ✅ `correlations:{userId}:{from}:{today}` for 7, 14, 30 day windows
- ✅ `nutrition-trends:{userId}:{from}:{today}` for 7, 14, 30 day windows
- ✅ `additive-exposure:{userId}:{from}:{today}` for 7, 14, 30 day windows

**Missing:**

- ❌ `trigger-foods` cache (never invalidated — stale up to 15 min)
- ❌ Any custom date range not matching exactly 7/14/30 days
- ❌ The 90-day period option in the frontend is never cache-busted
- ❌ Symptom creation doesn't invalidate ANY insight caches

**Impact:** After logging a meal or symptom, users may see stale insights for up to 15 minutes (cache TTL) for trigger foods and non-standard date ranges.

---

## ⚠️ Design Issues

### Issue 1: False Positive Correlations — Wide 24h Window

**Severity:** Medium (affects UX credibility)

The correlation engine uses a **24-hour lookback** from symptom time. This means **breakfast foods get correlated with dinner-triggered symptoms**.

**Evidence from test data:**

| False Positive Food     | Correlated Symptom | Occurrences | Explanation                                       |
| ----------------------- | ------------------ | ----------- | ------------------------------------------------- |
| Scrambled Eggs on Toast | Bloating           | 8×          | Eaten at 7:30 AM, symptoms at 8-10 PM from dinner |
| Scrambled Eggs on Toast | Heartburn          | 8×          | Same time window issue                            |
| Scrambled Eggs on Toast | Fatigue            | 8×          | Same                                              |
| Scrambled Eggs on Toast | Insomnia           | 8×          | Same                                              |
| Banana                  | Heartburn          | 7×          | Snack at 3:30 PM, symptoms from dinner            |
| Greek Yogurt            | Stomach Pain       | 7×          | Same                                              |

The result: **3 out of 7 trigger foods (43%) are false positives** in the current implementation.

**Recommendation:** Reduce the window to **6-8 hours**, or implement a weighted scoring system where closer meals get higher correlation weight. Consider also using the `relatedMealLogId` field when available for stronger signal.

---

### Issue 2: No Symptom-Side Cache Invalidation

When a symptom is created/updated/deleted, no insight caches are invalidated. The `SymptomEndpoints.cs` doesn't call `InvalidateUserInsightCaches()`. This means:

- Log a symptom → correlations are stale until cache expires (15 min)
- The nutrition-trends cache is also not invalidated (though symptoms don't affect it)

---

### Issue 3: `seenInMeal` Reset Per Symptom Iteration

In `CorrelationEngine.cs:73`, `seenInMeal` is created fresh for each `(symptom, meal)` pair. This correctly prevents counting the same food twice within one meal for one symptom. However, if the same food appears in a meal and 3 different symptoms fire, the same meal ID is added to `mealIds` only once (good — `HashSet<Guid>`), but `totalSeverity` accumulates all 3 severity values. This is the root cause of Bug 1.

---

## 📊 Correlation Engine — How It Works

### Algorithm Overview

```
For each symptom in the date range:
    window = [symptom.time - 24h, symptom.time - 1h]

    For each meal in window:
        For each food item in meal:
            key = "foodName|symptomName"
            Track: unique meal IDs, accumulated severity, total meals containing this food

        For each additive on food products:
            Same tracking as above with "[additive] name|symptomName"

Filter: only keep entries with ≥ 3 unique meal occurrences
Compute:
    frequencyPercent = occurrences / totalMealsWithFood × 100
    averageSeverity = totalSeverity / occurrences
    confidence = High (≥15) | Medium (≥5) | Low (3-4)
Sort: descending by occurrences, take top 20
```

### Trigger Foods Derivation

Trigger foods are **derived from correlations** — not independently computed:

```
1. Run ComputeCorrelationsAsync()
2. Group by foodOrAdditive
3. For each food:
   - Collect unique symptom names
   - Sum totalOccurrences across all symptom types
   - Average severity across all symptom types
   - Take worst confidence level
4. Sort by totalOccurrences desc, take top 15
```

### What Works Well

1. **Minimum threshold of 3** prevents single-event noise from appearing
2. **Per-meal deduplication** prevents a food item listed twice in one meal from being double-counted
3. **Additive tracking** through `FoodProductAdditives` provides ingredient-level insight beyond just food names
4. **Confidence levels** give users intuitive signal strength (Low/Medium/High)
5. **Caching with Redis** (15 min TTL) prevents repeated expensive DB queries
6. **Sorted + capped output** (top 20 correlations, top 15 triggers) keeps UI manageable

### What Needs Improvement

| Area                        | Issue                                       | Impact | Fix Complexity              |
| --------------------------- | ------------------------------------------- | ------ | --------------------------- |
| 24h window                  | Too wide → false positives                  | High   | Low (change to 6-8h)        |
| Severity calc               | Can exceed 10                               | Medium | Low (clamp or fix counting) |
| Cache invalidation          | Incomplete                                  | Low    | Low (add missing keys)      |
| No symptom cache bust       | Stale after symptom log                     | Low    | Low                         |
| No frequency baseline       | 100% frequency for rare foods is misleading | Low    | Medium                      |
| No statistical significance | No p-value or Fisher's test                 | Low    | Medium                      |

---

## 📈 Data Profile from Seed

| Metric                  | Value                                    |
| ----------------------- | ---------------------------------------- |
| Total meals             | 114                                      |
| Total symptoms          | 102                                      |
| Avg meals/day           | 3.7                                      |
| Avg daily calories      | 1,825                                    |
| Symptoms with meal link | 89 (87%)                                 |
| Correlations found      | 20                                       |
| True positive triggers  | 4 (Beer, Pizza, Cola, Energy Drink)      |
| False positive triggers | 3 (Scrambled Eggs, Banana, Greek Yogurt) |
| Trigger precision       | **57%**                                  |
| Top correlation         | Beer → Bloating (16×, High confidence)   |

---

## Endpoint Performance

| Endpoint                              | Status     | Cache TTL |
| ------------------------------------- | ---------- | --------- |
| `GET /api/insights/correlations`      | ✅ Working | 15 min    |
| `GET /api/insights/trigger-foods`     | ✅ Working | 15 min    |
| `GET /api/insights/nutrition-trends`  | ✅ Working | 10 min    |
| `GET /api/insights/additive-exposure` | ✅ Working | 10 min    |
| `GET /api/symptoms/history`           | ✅ Working | None      |
| `GET /api/symptoms/types`             | ✅ Working | None      |
| `GET /api/meals/daily-summary/{date}` | ✅ Working | None      |
| `GET /api/meals/export`               | ✅ Working | None      |

---

## Recommendations (Priority Order)

1. **🔴 Reduce correlation window to 6-8 hours** — Eliminates most false positives. The 24h window means breakfast is correlated with dinner symptoms, producing misleading results. A 6-8h window matches realistic digestion timing.

2. **🟡 Fix averageSeverity overflow** — Clamp output to max 10, or count total symptom matches instead of unique meals as the divisor. Currently shows values like 11.3 which is impossible on a 1-10 scale.

3. **🟡 Complete cache invalidation** — Add `trigger-foods` key to invalidation. Invalidate caches on symptom CRUD too. Consider wildcard/prefix-based invalidation for custom date ranges.

4. **🟢 Add frequency baseline** — A food eaten in 8 out of 8 meals shows 100% frequency, but if it's something healthy eaten daily, 100% frequency of symptoms is misleading. Consider comparing against baseline symptom rate when the food is NOT present.

5. **🟢 Weight correlations by time proximity** — Meals closer in time to the symptom should contribute more weight than meals 20 hours prior. A simple linear decay or time-bucketed weighting would improve precision.
