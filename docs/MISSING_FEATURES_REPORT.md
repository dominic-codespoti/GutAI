# GutAI — Missing Food & Symptom Features Report
## Session 13 Comprehensive Audit

> Generated from a full codebase audit of all backend endpoints, frontend screens, DTOs, entities, and data flow paths.

---

## Executive Summary

The core CRUD for meals, symptoms, food lookup, and barcode scanning is solid. The major gaps fall into **6 categories**:

| Category | Severity | Count |
|----------|----------|-------|
| 🔴 Critical bugs (data leaks, dead systems) | Critical | 7 |
| 🟠 Missing backend endpoints/logic | High | 8 |
| 🟡 Frontend UX gaps vs architecture | Medium | 25+ |
| 🔵 DTO/type mismatches | Medium | 11 |
| ⚪ Dead code / unused entities | Low | 15 |
| 🟣 Deferred (Tier 3) | Future | 4 |

---

## 🔴 CRITICAL — Must Fix

### 1. Soft-Delete Filtering Not Applied Anywhere
Every query that reads meals or symptoms **returns deleted records**:
- `GetMealsByDate` — returns soft-deleted meals
- `GetMeal` — returns soft-deleted meals
- `GetDailySummary` — counts deleted meals in calorie/macro totals
- `ExportData` — exports deleted data
- `GetSymptomsByDate` — returns deleted symptoms
- `GetHistory` — returns deleted symptoms
- `CorrelationEngine` — correlates against deleted records
- `InsightEndpoints` (all 3) — includes deleted data

**Impact**: Deleted meals inflate calorie counts. Deleted symptoms create false correlations.

### 2. UserFoodAlert System is Write-Only
Users can add additives to their watchlist, but **nothing ever checks the watchlist**:
- Barcode scan doesn't warn when scanned product contains a watched additive
- Meal logging doesn't warn when adding a food with a watched additive
- No notification/banner system exists

**Impact**: Users configure alerts that never fire. Feature is non-functional.

### 3. Safety Score Algorithm Oversimplified
- **Current**: `SafetyScore = 100 - (worstCspiRating × 25)` — uses only the single worst additive
- **Architecture spec**: Per-additive composite scoring with EU ban penalty (-15), FDA adverse events (-10), EPA cancer class (-20), NOVA adjustment, weighted average
- **Impact**: A product with 10 "Caution" additives scores the same as one with 1

### 4. 7 FoodAdditive Entity Fields Are Dead
These fields exist in the DB but are never populated or returned:
`AlternateNames`, `Description`, `EfsaAdiMgPerKgBw`, `EfsaLastReviewDate`, `EpaCancerClass`, `FdaAdverseEventCount`, `FdaRecallCount`

### 5. MealItemDto Drops 5 Stored Fields
The entity stores `CholesterolMg`, `SaturatedFatG`, `PotassiumMg`, `ServingWeightG`, `FoodProductId` but the DTO never returns them. Frontend declares `servingWeightG` and `foodProductId` but always receives `undefined`.

### 6. InsightReport Entity Is Entirely Unused
`DbSet<InsightReport>` exists, DB table configured, but no code ever reads or writes it. Completely orphaned.

### 7. DailyNutritionSummary Table Never Written
`DbSet<DailyNutritionSummary>` exists but the `GetDailySummary` endpoint computes everything live from `MealLogs` each request. The table is always empty.

---

## 🟠 MISSING BACKEND ENDPOINTS & LOGIC

### 8. Weekly/Monthly Insight Reports — Not Implemented
- `GET /api/insights/weekly-report` — ❌ Missing
- `GET /api/insights/report/{period}?date={date}` — ❌ Missing
- `InsightReport` entity has `ReportType`, `SummaryText`, `TopTriggersJson`, `CorrelationsJson` — all unused

### 9. Meal History Endpoint — Missing
- `GET /api/meals/history?from={date}&to={date}` — ❌ Not implemented
- `ExportData` is not a substitute (different format, meant for export not browsing)

### 10. Background Jobs — Entire System Missing
- No Hangfire or any job infrastructure
- `WeeklyReportJob.cs` — ❌ Missing (weekly insight generation)
- `AdditiveDbRefreshJob.cs` — ❌ Missing (EFSA monthly refresh)

### 11. OpenFDA Client — Not Implemented
- `OpenFdaClient.cs` — ❌ File doesn't exist
- FDA adverse event counts and recall data never fetched
- This is Tier 2, targeted for Week 9 in the architecture

### 12. Correlation Engine Thresholds Don't Match Architecture
| Parameter | Architecture | Implementation |
|-----------|-------------|----------------|
| Min occurrences | ≥3 | ≥2 |
| Frequency filter | >50% | None |
| Confidence bands | 5/15 | 5/10 |
| Statistical test | Fisher's exact | None |

### 13. NutritionTrends Missing Micronutrients
`GET /insights/nutrition-trends` returns only calories/protein/carbs/fat. Missing: fiber, sugar, sodium.

### 14. Additive Seed Data — 25 of 150
Only 25 additives seeded vs architecture target of ~150. Missing entire categories: phosphates, sulfites, MSG family, flour improvers, many preservatives.

### 15. OFF Search Results Missing Fields
`OpenFoodFactsClient.SearchAsync` omits: `IngredientsText`, `AllergensTags`, `AdditivesTags`, `Fiber100g`, `Sugar100g`, `Sodium100g` — only populated on barcode lookup.

---

## 🟡 FRONTEND UX GAPS

### Meals Screen
| # | Missing Feature | Architecture Spec |
|---|----------------|-------------------|
| 16 | No meal type filtering | Selector only sets type for new meals, doesn't filter list |
| 17 | No time picker | Always logs at current time, can't adjust |
| 18 | No daily nutrition summary on meals page | User can't see daily totals while logging |
| 19 | No food search integration in manual entry | Must type all nutrition values by hand |
| 20 | No "Recent Foods" quick-add | Architecture specifies quick-add from history |
| 21 | No photo attachment | Architecture says "Add photo — optional meal photo" |
| 22 | No barcode scan shortcut from meal screen | Architecture says camera icon → scan tab |
| 23 | No weekly/monthly meal history view | Single-day view only |

### Symptoms Screen
| # | Missing Feature | Architecture Spec |
|---|----------------|-------------------|
| 24 | No time picker | Always logs at current time |
| 25 | No duration field in UI | `CreateSymptomRequest` has `duration` but UI never collects it |
| 26 | No symptom calendar/heatmap | Architecture lists `SymptomCalendar` component |
| 27 | No "View History" or "View Insights" buttons | Architecture specifies navigation links |
| 28 | Meal linking limited to same day | Can't link to previous-day meals |
| 29 | No symptom frequency sparkline | No trend visualization |

### Insights Screen
| # | Missing Feature | Architecture Spec |
|---|----------------|-------------------|
| 30 | No period selector | Hardcoded 14-day trends, 30-day correlations |
| 31 | No actual charts/graphs | Flat bar segments and text lists only |
| 32 | No symptom frequency bar chart | Architecture says "bar chart by symptom type" |
| 33 | No trigger foods ranking | `TopTriggersJson` exists but no UI |
| 34 | No additive exposure chart | Just a flat list with counts |
| 35 | Correlations not tappable | Can't drill into specific meals/symptoms |
| 36 | No safety score breakdown | Architecture says "how composite score calculated" |

### Dashboard (Home)
| # | Missing Feature | Architecture Spec |
|---|----------------|-------------------|
| 37 | No calorie ring (circular progress) | Uses flat progress bar |
| 38 | No symptom trend sparkline | Architecture says "7-day sparkline" |
| 39 | No additive alerts banner | Architecture says "alert card for 'Avoid' additives" |
| 40 | No quick action FABs | Architecture says "Log Meal / Log Symptom buttons" |
| 41 | No streak counter | Architecture says "days of consecutive logging" |
| 42 | Meals/symptoms not tappable | Can't navigate to edit |

### Scan / Food Detail
| # | Missing Feature | Architecture Spec |
|---|----------------|-------------------|
| 43 | No allergen cross-reference with user profile | Scanned product doesn't warn about user's allergies |
| 44 | No serving size adjustment when adding to meal | Always 1 serving at 100g nutrition |
| 45 | No scan history | No record of previously scanned products |
| 46 | No circular safety score visualization | Only shows letter rating |
| 47 | No score breakdown detail | Architecture says "how composite score calculated" |
| 48 | No EFSA ADI values shown | Dead fields, never populated |

### Profile / Settings
| # | Missing Feature | Architecture Spec |
|---|----------------|-------------------|
| 49 | No notification settings | Architecture says toggle for reminders/alerts |
| 50 | Allergies as text input, not chips | Architecture says "multi-select chips" |
| 51 | Data export non-functional | Shows alert with counts but no downloadable file |
| 52 | No timezone setting exposed | `TimezoneId` writable but never returned |

---

## 🔵 DTO / TYPE MISMATCHES

| # | Issue | Direction |
|---|-------|-----------|
| 53 | `MealLog.photoUrl` — backend returns it, frontend type missing | Backend → Frontend gap |
| 54 | `MealLog.userId` — frontend declares it, backend never sends it | Frontend phantom field |
| 55 | `FoodProduct.additivesTags` — backend returns it, frontend type missing | Backend → Frontend gap |
| 56 | `NaturalLanguageMealRequest.loggedAt` — backend accepts it, frontend doesn't send it | Unused backend field |
| 57 | `User.timezoneId` — settable but never returned in `UserProfileDto` | Set-only field |
| 58 | 6 endpoints return anonymous types (no DTOs) | Fragile API contract |
| 59 | `AuthResponse.displayName` nullable in backend, non-nullable in frontend | Minor type mismatch |

---

## ⚪ DEAD CODE / UNUSED

| # | Item | Status |
|---|------|--------|
| 60 | `InsightReport` entity + table | Never read or written |
| 61 | `DailyNutritionSummary` table | Never written (computed live) |
| 62 | `FoodAdditive.AlternateNames` | Always `[]` |
| 63 | `FoodAdditive.Description` | Always `""` |
| 64 | `FoodAdditive.EfsaAdiMgPerKgBw` | Always `null` |
| 65 | `FoodAdditive.EfsaLastReviewDate` | Always `null` |
| 66 | `FoodAdditive.EpaCancerClass` | Always `null` |
| 67 | `FoodAdditive.FdaAdverseEventCount` | Always `0` |
| 68 | `FoodAdditive.FdaRecallCount` | Always `0` |
| 69 | `FoodAdditive.LastUpdated` | Never exposed |
| 70 | `MealLog.CreatedAt` / `UpdatedAt` | Never exposed |
| 71 | `SymptomLog.CreatedAt` / `UpdatedAt` | Never exposed |
| 72 | `SymptomLog.duration` | Frontend type exists, UI never collects it |
| 73 | `foodApi.listAdditives()` / `getAdditive()` | API functions exist but never called |
| 74 | `UserFoodAlert.AlertEnabled` toggle | No endpoint to toggle, always `true` |

---

## 🟣 DEFERRED (Tier 3 / Future)

| # | Feature | Notes |
|---|---------|-------|
| 75 | `EdamamClient.cs` | Recipe nutrition analysis — Tier 3 |
| 76 | `SpoonacularClient.cs` | Allergen/diet classification — Tier 3 |
| 77 | Elimination diet tracking | Listed as future enhancement |
| 78 | ML/smart correlation | Beyond simple time-window analysis |

---

## 📊 Prioritized Fix Plan

### Wave 1 — Critical Bugs (Estimated: 1-2 sessions)
1. **Soft-delete filtering** — Add `!IsDeleted` to all meal/symptom queries (9 locations)
2. **MealItemDto** — Add 5 missing fields to `MapToDto`
3. **DTO/type alignment** — Fix `MealLog.photoUrl`, `FoodProduct.additivesTags` on frontend

### Wave 2 — Core Feature Gaps (Estimated: 2-3 sessions)
4. **UserFoodAlert triggering** — Check watchlist on barcode scan + meal creation, show warning
5. **Allergen cross-reference** — Check user profile allergies against scanned products
6. **Time picker** — Add to meal and symptom logging
7. **Serving size adjustment** — When adding food to meal from scan/detail
8. **Meal type filtering** — Filter displayed meals by type
9. **Daily nutrition summary on meals screen**
10. **Correlation engine thresholds** — Match architecture spec

### Wave 3 — Enrichment (Estimated: 2-3 sessions)
11. **Safety score algorithm** — Implement full composite scoring
12. **FoodAdditive DTO enrichment** — Add 8 missing fields + populate from seed data
13. **Additive seed data expansion** — 25 → 150 additives
14. **NutritionTrends micronutrients** — Add fiber, sugar, sodium
15. **Meal history endpoint** — `GET /api/meals/history`
16. **Anonymous types → proper DTOs** — 6 endpoints
17. **OFF search field parity** — Parse all fields in search, not just barcode

### Wave 4 — UX Polish (Estimated: 3-4 sessions)
18. **Charts/graphs** — Install charting library, nutrition trend lines, symptom frequency bars
19. **Trigger foods ranking** — Surface `TopTriggersJson` data
20. **Symptom calendar/heatmap**
21. **Combined meal+symptom timeline**
22. **Dashboard enhancements** — Calorie ring, sparklines, quick actions, streak counter
23. **Tappable correlations** — Drill into individual meals/symptoms
24. **Data export** — Generate actual downloadable file
25. **Functional food search in manual meal entry**

### Wave 5 — Advanced (Estimated: 2-3 sessions)
26. **Weekly/monthly insight reports** — Endpoint + persistence + UI
27. **Background job infrastructure** — Even without Hangfire, periodic computation
28. **OpenFDA integration** — Populate adverse event/recall data
29. **Notification settings**
30. **Recent foods quick-add**

---

*Total identified gaps: 78 items across all categories.*
*Estimated total effort: 10-15 additional sessions to reach full architecture spec.*
