# Frontend ↔ Backend Data Alignment Audit

**Generated:** 2025-07-14
**Scope:** All 13 frontend files cross-referenced against 6 backend endpoint files + 4 DTO files
**Status:** READ-ONLY AUDIT — no changes made

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [API Client & Auth Infrastructure](#2-api-client--auth-infrastructure)
3. [Per-File Audit](#3-per-file-audit)
   - [3.1 `src/types/index.ts`](#31-srctypesindexts)
   - [3.2 `src/api/client.ts`](#32-srcapiclientts)
   - [3.3 `src/api/index.ts`](#33-srcapiindexts)
   - [3.4 `src/stores/auth.ts`](#34-srcstoresauthts)
   - [3.5 `app/(tabs)/index.tsx` — Dashboard](#35-apptabsindextsx--dashboard)
   - [3.6 `app/(tabs)/meals.tsx`](#36-apptabsmealstsx)
   - [3.7 `app/(tabs)/symptoms.tsx`](#37-apptabssymptomstsx)
   - [3.8 `app/(tabs)/insights.tsx`](#38-apptabsinsightstsx)
   - [3.9 `app/(tabs)/scan.tsx`](#39-apptabsscantsx)
   - [3.10 `app/(tabs)/profile.tsx`](#310-apptabsprofiletsx)
   - [3.11 `app/food/[id].tsx`](#311-appfoodidtsx)
   - [3.12 `app/settings.tsx`](#312-appsettingstsx)
   - [3.13 `app/onboarding.tsx`](#313-apponboardingtsx)
4. [Summary of All Issues](#4-summary-of-all-issues)
5. [Risk Assessment](#5-risk-assessment)

---

## 1. Executive Summary

| Category                                                            | Count |
| ------------------------------------------------------------------- | ----- |
| 🔴 **Critical mismatches** (will cause data loss or runtime errors) | 5     |
| 🟠 **Moderate issues** (wrong data displayed or sent)               | 9     |
| 🟡 **Minor issues** (cosmetic / robustness)                         | 8     |
| ✅ **Fully aligned endpoints**                                      | ~25   |

The frontend and backend are **substantially aligned** on routes, HTTP methods, and core DTO shapes. However, there are several field-level mismatches where the backend returns fields the frontend doesn't declare (silently dropped) or the frontend declares fields the backend never sends (always `undefined`). The most critical issues involve the `GetFoodAdditives` endpoint omitting `eNumber` and `safetyRating` (used in the profile UI), and the `MealLog` type declaring a `userId` field never returned by the backend.

---

## 2. API Client & Auth Infrastructure

### File: `src/api/client.ts`

**Base URL Configuration:**

```typescript
const BASE_URL =
  process.env.EXPO_PUBLIC_API_URL ||
  Constants.expoConfig?.extra?.apiUrl ||
  Platform.select({
    android: "http://10.0.2.2:5000",
    ios: "http://localhost:5000",
    default: "http://localhost:5000",
  });
```

- ✅ Uses environment variable / Expo config before falling back to hardcoded defaults
- ✅ Android emulator correctly uses `10.0.2.2` (maps to host loopback)
- 🟡 **No production URL default** — production deploys must set `EXPO_PUBLIC_API_URL` or the app will try to hit `localhost:5000`
- ✅ Timeout set to 15000ms (reasonable)
- ✅ Content-Type `application/json` set globally

**Auth Token Handling:**

- ✅ Request interceptor reads `accessToken` from async storage and sets `Authorization: Bearer <token>`
- ✅ Response interceptor handles 401 with token refresh queue pattern
- ✅ Refresh uses raw `axios.post` (not the intercepted `api` instance) — prevents infinite refresh loops
- ✅ On refresh failure, clears both tokens from storage
- 🟡 **No redirect to login on refresh failure** — the promise is rejected but the user may see a generic error rather than being sent to login. The auth store's `hydrate` handles this on app start, but mid-session 401 after failed refresh has no global handler.

**Hardcoded URLs:**

- The only hardcoded URLs are the development fallbacks in `client.ts` (`http://localhost:5000`, `http://10.0.2.2:5000`). All API calls use relative paths via the axios instance. ✅ No hardcoded URLs elsewhere.

---

## 3. Per-File Audit

### 3.1 `src/types/index.ts`

This file defines all TypeScript interfaces. Cross-referenced against backend DTOs:

#### `UserProfile` vs `UserProfileDto` (AuthDtos.cs)

| Frontend Field                 | Backend Field                              | Status                                                                                                                                                    |
| ------------------------------ | ------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id: string`                   | `Id: Guid`                                 | ✅                                                                                                                                                        |
| `email: string`                | `Email: string`                            | ✅                                                                                                                                                        |
| `displayName: string`          | `DisplayName: string?`                     | 🟠 Frontend is non-optional but backend can be null (fallback logic in `MapProfile` ensures it's never null in practice)                                  |
| `dailyCalorieGoal`             | `DailyCalorieGoal`                         | ✅                                                                                                                                                        |
| All goal fields                | All goal fields                            | ✅                                                                                                                                                        |
| `allergies: string[]`          | `Allergies: string[]`                      | ✅                                                                                                                                                        |
| `dietaryPreferences: string[]` | `DietaryPreferences: string[]`             | ✅                                                                                                                                                        |
| `onboardingCompleted: boolean` | `OnboardingCompleted: bool`                | ✅                                                                                                                                                        |
| `timezoneId?: string`          | `TimezoneId: string?`                      | ✅                                                                                                                                                        |
| _(missing)_                    | `createdAt` on GET/PUT `/profile` response | 🟠 Backend anonymous object in `UserEndpoints.GetProfile` and `UpdateProfile` returns `createdAt` — frontend type doesn't declare it (harmlessly ignored) |

#### `MealLog` vs `MealLogDto` (MealDtos.cs)

| Frontend Field       | Backend Field          | Status                                                                                                                    |
| -------------------- | ---------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `id: string`         | `Id: Guid`             | ✅                                                                                                                        |
| **`userId: string`** | _(not in MealLogDto)_  | 🔴 **PHANTOM FIELD** — Frontend declares `userId` but backend `MealLogDto` never includes it. Will always be `undefined`. |
| `mealType`           | `MealType: string`     | ✅                                                                                                                        |
| `loggedAt: string`   | `LoggedAt: DateTime`   | ✅                                                                                                                        |
| `notes`              | `Notes`                | ✅                                                                                                                        |
| `originalText`       | `OriginalText`         | ✅                                                                                                                        |
| `items: MealItem[]`  | `Items: MealItemDto[]` | ✅                                                                                                                        |
| `totalCalories`      | `TotalCalories`        | ✅                                                                                                                        |
| `totalProteinG`      | `TotalProteinG`        | ✅                                                                                                                        |
| `totalCarbsG`        | `TotalCarbsG`          | ✅                                                                                                                        |
| `totalFatG`          | `TotalFatG`            | ✅                                                                                                                        |
| _(missing)_          | `PhotoUrl: string?`    | 🟡 Backend returns `PhotoUrl` but frontend type doesn't declare it (harmlessly dropped)                                   |

#### `MealItem` vs `MealItemDto`

| Frontend Field           | Backend Field            | Status                                                                                                                               |
| ------------------------ | ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| All common fields        | All common fields        | ✅                                                                                                                                   |
| `cholesterolMg?: number` | `CholesterolMg: decimal` | 🟠 Frontend marks optional, backend always sends (default 0). Not a runtime error, but frontend may skip displaying when value is 0. |
| `saturatedFatG?: number` | `SaturatedFatG: decimal` | 🟠 Same as above                                                                                                                     |
| `potassiumMg?: number`   | `PotassiumMg: decimal`   | 🟠 Same as above                                                                                                                     |

#### `CreateMealItemRequest` (Frontend) vs `CreateMealItemRequest` (Backend)

| Frontend Field           | Backend Field            | Status                                                                                                                                                                                                                                                                |
| ------------------------ | ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `fiberG?: number`        | `FiberG: decimal`        | 🟠 Frontend marks optional — backend requires these as non-nullable decimals. If frontend omits them, JSON deserialization will default to `0` in C#. Not a runtime error but **potential silent data loss** if the frontend has the data but conditionally omits it. |
| `sugarG?: number`        | `SugarG: decimal`        | 🟠 Same                                                                                                                                                                                                                                                               |
| `sodiumMg?: number`      | `SodiumMg: decimal`      | 🟠 Same                                                                                                                                                                                                                                                               |
| `cholesterolMg?: number` | `CholesterolMg: decimal` | 🟠 Same                                                                                                                                                                                                                                                               |
| `saturatedFatG?: number` | `SaturatedFatG: decimal` | 🟠 Same                                                                                                                                                                                                                                                               |
| `potassiumMg?: number`   | `PotassiumMg: decimal`   | 🟠 Same                                                                                                                                                                                                                                                               |

#### `NaturalLanguageMealRequest` (Frontend) vs Backend

| Frontend           | Backend               | Status                                                                                                                                                                                        |
| ------------------ | --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `text: string`     | `Text: string`        | ✅                                                                                                                                                                                            |
| `mealType: string` | `MealType: string`    | ✅                                                                                                                                                                                            |
| _(missing)_        | `LoggedAt: DateTime?` | 🟡 Backend accepts optional `LoggedAt`, frontend doesn't send it. Backend `LogNatural` endpoint doesn't use it for the response (just returns parsed items), but the field exists on the DTO. |

#### `ParsedFoodItem` (Frontend) vs `ParsedFoodItemDto` (Backend)

| Frontend             | Backend                     | Status                                                                                                                                                                                                                                          |
| -------------------- | --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| All nutrition fields | All nutrition fields        | ✅                                                                                                                                                                                                                                              |
| _(missing)_          | `ServingSize: string?`      | 🟡 Backend DTO has `ServingSize` and `ServingQuantity`, frontend only has `servingWeightG`. Minor — the `LogNatural` endpoint returns whatever `INutritionApiService.ParseNaturalLanguageAsync` returns, which are `ParsedFoodItemDto` objects. |
| _(missing)_          | `ServingQuantity: decimal?` | 🟡 Same                                                                                                                                                                                                                                         |

#### `FoodProduct` (Frontend) vs `FoodProductDto` (Backend)

| Frontend                      | Backend                            | Status                                                                                                 |
| ----------------------------- | ---------------------------------- | ------------------------------------------------------------------------------------------------------ |
| All per-100g nutrition fields | All per-100g fields                | ✅                                                                                                     |
| `additives: FoodAdditive[]`   | `Additives: List<FoodAdditiveDto>` | ✅                                                                                                     |
| _(missing)_                   | `NutritionInfo: NutritionInfo?`    | 🟡 Backend returns full `NutritionInfo` object, frontend doesn't use it (uses per-100g fields instead) |
| _(missing)_                   | `AdditivesTags: List<string>`      | 🟡 Backend always returns `[]` (hardcoded empty in MapToDto), frontend doesn't need it                 |
| _(missing)_                   | `IsDeleted: bool`                  | 🟡 Irrelevant to frontend display                                                                      |

#### `FoodAdditive` (Frontend) vs `FoodAdditiveDto` (Backend)

| Frontend           | Backend                         | Status                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ------------------ | ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `usStatus: string` | `UsRegulatoryStatus: string`    | 🟠 **FIELD NAME MISMATCH** — Frontend uses `usStatus`, backend returns `usRegulatoryStatus`. In the `GetFoodAdditives` list endpoint, backend returns `usStatus` (anonymous object). In the `MapToDto` for `FoodAdditiveDto`, it's `UsRegulatoryStatus`. The food detail page uses `FoodAdditiveDto` via `FoodProductDto.Additives` → field name is `usRegulatoryStatus` in JSON. Frontend type says `usStatus`. **This means `additive.usStatus` will be `undefined` when coming from `FoodProductDto.Additives`, but will work from `GetFoodAdditives` list.** |
| `euStatus: string` | `EuRegulatoryStatus: string`    | 🟠 **Same mismatch** — `euStatus` vs `euRegulatoryStatus`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| _(missing)_        | `EfsaLastReviewDate: DateTime?` | 🟡 Dropped                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| _(missing)_        | `EpaCancerClass: string?`       | 🟡 Dropped                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| _(missing)_        | `FdaAdverseEventCount: int?`    | 🟡 Dropped                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| _(missing)_        | `FdaRecallCount: int?`          | 🟡 Dropped                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| _(missing)_        | `LastUpdated: DateTime?`        | 🟡 Dropped                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |

**Critical detail on `GetFoodAdditives` (list) endpoint:**
The backend `GetFoodAdditives` returns an anonymous object with these fields:

```csharp
{ id, name, category, cspiRating, usStatus, euStatus, healthConcerns, bannedInCountries, description, alternateNames, efsaAdiMgPerKgBw }
```

**Missing from this anonymous object:** `eNumber`, `safetyRating`

The `GetFoodAdditive` (single) endpoint returns: `{ id, eNumber, name, category, cspiRating, usStatus, euStatus, healthConcerns, bannedInCountries, description, alternateNames, efsaAdiMgPerKgBw }`
**Missing from this:** `safetyRating`

| Field          | List endpoint | Single endpoint | FoodProductDto.Additives    | Frontend type expects? |
| -------------- | ------------- | --------------- | --------------------------- | ---------------------- |
| `eNumber`      | ❌ Missing    | ✅ Present      | ✅ Present                  | ✅ Yes                 |
| `safetyRating` | ❌ Missing    | ❌ Missing      | ✅ Present (`SafetyRating`) | ✅ Yes                 |
| `usStatus`     | ✅ `usStatus` | ✅ `usStatus`   | ❌ `usRegulatoryStatus`     | Uses `usStatus`        |
| `euStatus`     | ✅ `euStatus` | ✅ `euStatus`   | ❌ `euRegulatoryStatus`     | Uses `euStatus`        |

#### `SymptomLog` vs `SymptomLogDto`

✅ Perfect match — all fields align.

#### `CreateSymptomRequest` (Frontend) vs Backend

| Frontend            | Backend               | Status                                                                                                                                                                                                                                                             |
| ------------------- | --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `duration?: string` | `Duration: TimeSpan?` | 🔴 **TYPE MISMATCH** — Frontend sends a plain string like `"30 minutes"` or `"2 hours"`. Backend expects a `TimeSpan` serialization (e.g., `"00:30:00"`). If the user types `"30 minutes"`, C# `TimeSpan` deserialization will fail or produce unexpected results. |

#### `DailyNutritionSummary` — ✅ Full match

#### `Correlation` vs `CorrelationDto` — ✅ Full match

#### `NutritionTrend` vs Backend anonymous object

Backend returns: `{ date, calories, protein, carbs, fat, fiber, sugar, sodium, mealCount }`
Frontend expects: `{ date, calories, protein, carbs, fat, fiber, sugar, sodium, mealCount }`
✅ Full match

#### `AdditiveExposure` vs Backend anonymous object

Backend returns: `{ additive, cspiRating, count }`
Frontend expects: `{ additive, cspiRating, count }`
✅ Full match

#### `TriggerFood` vs Backend anonymous object

Backend returns: `{ food, symptoms, totalOccurrences, avgSeverity, worstConfidence }`
Frontend expects: `{ food, symptoms, totalOccurrences, avgSeverity, worstConfidence }`
✅ Full match

#### `SafetyReport` vs Backend anonymous object

Backend returns: `{ product, additives, safetyScore, safetyRating, novaGroup, nutriScore, gutRisk, fodmap, substitutions, glycemic }`
Frontend expects: `{ product, additives, safetyScore, safetyRating, novaGroup, nutriScore, gutRisk, fodmap, substitutions, glycemic }`
✅ Full match — all nested DTOs also match.

#### `GutRiskAssessment`, `FodmapAssessment`, `SubstitutionResult`, `GlycemicAssessment`, `PersonalizedScore`, `FoodDiaryAnalysis`, `EliminationDietStatus`

✅ All fully match their backend DTO counterparts.

#### `ChangePasswordRequest` — ✅ Full match

#### `DataExport` vs Backend anonymous object

Backend returns: `{ exportedAt, from, to, meals, symptoms }` where `from`/`to` are `DateOnly`.
Frontend expects: `{ exportedAt: string, from: string, to: string, meals: MealLog[], symptoms: {...}[] }`
✅ Match — `DateOnly` serializes as `"YYYY-MM-DD"` string in JSON.

---

### 3.2 `src/api/client.ts`

Covered in [Section 2](#2-api-client--auth-infrastructure).

---

### 3.3 `src/api/index.ts`

#### Route-by-route verification:

| Frontend Call                      | Method | Route                                         | Backend Route                         | Match? |
| ---------------------------------- | ------ | --------------------------------------------- | ------------------------------------- | ------ |
| `authApi.register`                 | POST   | `/api/auth/register`                          | POST `/register` (group: `/api/auth`) | ✅     |
| `authApi.login`                    | POST   | `/api/auth/login`                             | POST `/login`                         | ✅     |
| `authApi.refresh`                  | POST   | `/api/auth/refresh`                           | POST `/refresh`                       | ✅     |
| `authApi.logout`                   | POST   | `/api/auth/logout`                            | POST `/logout`                        | ✅     |
| `authApi.changePassword`           | POST   | `/api/auth/change-password`                   | POST `/change-password`               | ✅     |
| `mealApi.list`                     | GET    | `/api/meals?date=`                            | GET `/` (group: `/api/meals`)         | ✅     |
| `mealApi.get`                      | GET    | `/api/meals/{id}`                             | GET `/{id:guid}`                      | ✅     |
| `mealApi.create`                   | POST   | `/api/meals`                                  | POST `/`                              | ✅     |
| `mealApi.update`                   | PUT    | `/api/meals/{id}`                             | PUT `/{id:guid}`                      | ✅     |
| `mealApi.delete`                   | DELETE | `/api/meals/{id}`                             | DELETE `/{id:guid}`                   | ✅     |
| `mealApi.parseNatural`             | POST   | `/api/meals/log-natural`                      | POST `/log-natural`                   | ✅     |
| `mealApi.dailySummary`             | GET    | `/api/meals/daily-summary/{date}`             | GET `/daily-summary/{date}`           | ✅     |
| `mealApi.export`                   | GET    | `/api/meals/export?from=&to=`                 | GET `/export`                         | ✅     |
| `foodApi.search`                   | GET    | `/api/food/search?q=`                         | GET `/search`                         | ✅     |
| `foodApi.lookupBarcode`            | GET    | `/api/food/barcode/{code}`                    | GET `/barcode/{barcode}`              | ✅     |
| `foodApi.get`                      | GET    | `/api/food/{id}`                              | GET `/{id:guid}`                      | ✅     |
| `foodApi.safetyReport`             | GET    | `/api/food/{id}/safety-report`                | GET `/{id:guid}/safety-report`        | ✅     |
| `foodApi.gutRisk`                  | GET    | `/api/food/{id}/gut-risk`                     | GET `/{id:guid}/gut-risk`             | ✅     |
| `foodApi.fodmap`                   | GET    | `/api/food/{id}/fodmap`                       | GET `/{id:guid}/fodmap`               | ✅     |
| `foodApi.substitutions`            | GET    | `/api/food/{id}/substitutions`                | GET `/{id:guid}/substitutions`        | ✅     |
| `foodApi.glycemic`                 | GET    | `/api/food/{id}/glycemic`                     | GET `/{id:guid}/glycemic`             | ✅     |
| `foodApi.personalizedScore`        | GET    | `/api/food/{id}/personalized-score`           | GET `/{id:guid}/personalized-score`   | ✅     |
| `foodApi.listAdditives`            | GET    | `/api/food/additives`                         | GET `/additives`                      | ✅     |
| `foodApi.getAdditive`              | GET    | `/api/food/additives/{id}`                    | GET `/additives/{id:int}`             | ✅     |
| `symptomApi.list`                  | GET    | `/api/symptoms?date=`                         | GET `/`                               | ✅     |
| `symptomApi.history`               | GET    | `/api/symptoms/history?from=&to=&typeId=`     | GET `/history`                        | ✅     |
| `symptomApi.create`                | POST   | `/api/symptoms`                               | POST `/`                              | ✅     |
| `symptomApi.update`                | PUT    | `/api/symptoms/{id}`                          | PUT `/{id:guid}`                      | ✅     |
| `symptomApi.delete`                | DELETE | `/api/symptoms/{id}`                          | DELETE `/{id:guid}`                   | ✅     |
| `symptomApi.types`                 | GET    | `/api/symptoms/types`                         | GET `/types`                          | ✅     |
| `symptomApi.get`                   | GET    | `/api/symptoms/{id}`                          | GET `/{id:guid}`                      | ✅     |
| `insightApi.correlations`          | GET    | `/api/insights/correlations?from=&to=`        | GET `/correlations`                   | ✅     |
| `insightApi.nutritionTrends`       | GET    | `/api/insights/nutrition-trends?from=&to=`    | GET `/nutrition-trends`               | ✅     |
| `insightApi.additiveExposure`      | GET    | `/api/insights/additive-exposure?from=&to=`   | GET `/additive-exposure`              | ✅     |
| `insightApi.triggerFoods`          | GET    | `/api/insights/trigger-foods?from=&to=`       | GET `/trigger-foods`                  | ✅     |
| `insightApi.foodDiaryAnalysis`     | GET    | `/api/insights/food-diary-analysis?from=&to=` | GET `/food-diary-analysis`            | ✅     |
| `insightApi.eliminationDietStatus` | GET    | `/api/insights/elimination-diet/status`       | GET `/elimination-diet/status`        | ✅     |
| `userApi.getProfile`               | GET    | `/api/user/profile`                           | GET `/profile`                        | ✅     |
| `userApi.updateProfile`            | PUT    | `/api/user/profile`                           | PUT `/profile`                        | ✅     |
| `userApi.updateGoals`              | PUT    | `/api/user/goals`                             | PUT `/goals`                          | ✅     |
| `userApi.getAlerts`                | GET    | `/api/user/alerts`                            | GET `/alerts`                         | ✅     |
| `userApi.addAlert`                 | POST   | `/api/user/alerts`                            | POST `/alerts`                        | ✅     |
| `userApi.removeAlert`              | DELETE | `/api/user/alerts/{additiveId}`               | DELETE `/alerts/{additiveId:int}`     | ✅     |
| `userApi.deleteAccount`            | DELETE | `/api/user/account`                           | DELETE `/account`                     | ✅     |

**All 42 routes match perfectly.** ✅

#### Local type definitions in `index.ts`:

- `UpdateProfileRequest` — ✅ matches backend `UpdateProfileRequest`
- `UpdateGoalsRequest` — ✅ matches backend `UpdateGoalsRequest`

---

### 3.4 `src/stores/auth.ts`

- ✅ `login()` — calls `authApi.login`, stores tokens, sets user
- ✅ `register()` — calls `authApi.register`, stores tokens, sets user
- ✅ `logout()` — calls `authApi.logout` (swallows errors), clears tokens
- ✅ `hydrate()` — checks for existing token, calls `userApi.getProfile` to restore session
- 🟡 `hydrate()` uses dynamic import `await import('../api')` for `userApi` — works but adds unnecessary latency on cold start

---

### 3.5 `app/(tabs)/index.tsx` — Dashboard

**API Calls:**

1. `mealApi.list(today)` → `GET /api/meals?date=YYYY-MM-DD` → ✅
2. `mealApi.dailySummary(today)` → `GET /api/meals/daily-summary/YYYY-MM-DD` → ✅
3. `symptomApi.list({ date: today })` → `GET /api/symptoms?date=YYYY-MM-DD` → ✅
4. `userApi.getAlerts()` → `GET /api/user/alerts` → ✅

**Data Usage:**

- Displays `meal.loggedAt`, `meal.mealType`, `meal.totalCalories`, `meal.items` — all valid ✅
- Displays daily summary calorie/macro goals — all valid ✅
- Uses alerts for notification badges — ✅

**Error Handling:**

- Uses React Query's built-in loading/error states ✅
- No explicit error boundaries for individual sections 🟡

**Race Conditions:**

- All queries use `today` as the date key and run independently — no race conditions ✅

---

### 3.6 `app/(tabs)/meals.tsx`

**API Calls:**

1. `mealApi.list(selectedDate)` → ✅
2. `mealApi.dailySummary(selectedDate)` → ✅
3. `mealApi.parseNatural({ text, mealType })` → ✅
4. `mealApi.create(data)` → ✅
5. `mealApi.update(id, data)` → ✅
6. `mealApi.delete(id)` → ✅

**Issues:**

🔴 **Natural language meal creation missing fields:** When creating a meal from parsed natural language items (line ~147-158), the frontend maps `parsed.map(p => ({ ... }))` but **omits `cholesterolMg`, `saturatedFatG`, `potassiumMg`** even though `ParsedFoodItem` has them. These fields will default to `0` on the backend, losing the parsed nutrition data.

🟠 **`parsedItems` field name:** The `LogNatural` endpoint returns `{ originalText, mealType, parsedItems }`. The frontend types this as `NaturalLanguageResponse.parsedItems`. The backend returns whatever `INutritionApiService.ParseNaturalLanguageAsync` returns — if that returns `ParsedFoodItemDto` objects, the field names (`servingWeightG` etc.) should match. However, the `ParsedFoodItemDto` has `servingSize` and `servingQuantity` fields that `ParsedFoodItem` frontend type doesn't include.

**Error Handling:**

- ✅ `onError` callbacks on all mutations with toast messages
- ✅ Nested `.catch()` on the create-after-parse flow

**Loading States:**

- ✅ `isLoading` used for meal list query
- ✅ `isPending` used for mutation states (disable buttons during submit)

---

### 3.7 `app/(tabs)/symptoms.tsx`

**API Calls:**

1. `symptomApi.types()` → `GET /api/symptoms/types` → ✅
2. `symptomApi.list({ date })` → `GET /api/symptoms?date=` → ✅
3. `mealApi.list(selectedDate)` (for linking meals) → ✅
4. `symptomApi.create(data)` → ✅
5. `symptomApi.update(id, data)` → ✅
6. `symptomApi.delete(id)` → ✅

**Issues:**

🔴 **Duration type mismatch:** Frontend sends `duration` as a free-text string (e.g., `"30 minutes"`, `"2 hours"` — from a text input with placeholder `"Duration (e.g., 30 minutes, 2 hours)"`). Backend `CreateSymptomRequest.Duration` is `TimeSpan?`. C# `TimeSpan` deserialization from JSON expects format like `"00:30:00"` (hours:minutes:seconds), NOT natural language strings. **This will cause a 400 Bad Request or the duration will be silently lost/zeroed.**

🟠 **Update mutation sends `editingSymptom?.duration`** which comes from the backend as a `TimeSpan` serialized string (e.g., `"00:30:00"`). When editing, the displayed duration in the text field would show this format, not the user-friendly `"30 minutes"`. If the user edits it to natural language, it breaks again.

**Error Handling:**

- ✅ `onError` on log, update, delete mutations
- ✅ Severity validation (1-10) in UI

---

### 3.8 `app/(tabs)/insights.tsx`

**API Calls:**

1. `insightApi.nutritionTrends(period)` → ✅
2. `insightApi.additiveExposure(period)` → ✅
3. `insightApi.correlations(period)` → ✅
4. `insightApi.foodDiaryAnalysis(period)` → ✅
5. `insightApi.triggerFoods(period)` → ✅
6. `insightApi.eliminationDietStatus()` → ✅

**Date Calculation:**

- Frontend computes `from`/`to` in `insightApi` functions. Backend also computes defaults if not provided. Both default to 30 days. ✅
- 🟡 **Redundant date logic:** The `insightApi` functions in `src/api/index.ts` always compute and send `from`/`to`, even though the backend has its own defaults. This is fine but means the backend defaults are dead code.

**Response Types:**

- All response types match their backend DTOs ✅
- `FoodDiaryAnalysis.fromDate`/`toDate` — backend returns `DateOnly`, serialized as `"YYYY-MM-DD"`, frontend expects `string` ✅

**Loading States:**

- ✅ Individual `isLoading` per section
- ✅ Skeleton loaders / empty states

---

### 3.9 `app/(tabs)/scan.tsx`

**API Calls:**

1. `foodApi.lookupBarcode(barcode)` → `GET /api/food/barcode/{barcode}` → ✅
2. `foodApi.search(query)` → `GET /api/food/search?q=` → ✅
3. `foodApi.safetyReport(id)` → `GET /api/food/{id}/safety-report` → ✅
4. `userApi.getAlerts()` → ✅
5. `userApi.addAlert(additiveId)` → ✅
6. `mealApi.create(data)` → ✅

**Issues:**

🟠 **Add-to-meal from scan page:** When adding a scanned food to a meal (line ~138), the frontend constructs a `CreateMealRequest` but uses `safetyReport.data?.product` properties. The nutrition values are calculated from `calories100g * servingQuantity / 100`. This works but **doesn't include `cholesterolMg`, `saturatedFatG`, `potassiumMg`** in the created meal item, defaulting them to 0 on the backend.

**Error Handling:**

- ✅ Toast on alert add error
- ✅ Toast on meal add error
- ✅ Barcode lookup returns 404 → handled via React Query error state

**Loading States:**

- ✅ Loading state for barcode lookup
- ✅ Loading state for search
- ✅ Loading state for safety report

---

### 3.10 `app/(tabs)/profile.tsx`

**API Calls:**

1. `userApi.getProfile()` (via auth store) → ✅
2. `userApi.updateProfile(data)` → ✅
3. `userApi.updateGoals(data)` → ✅
4. `foodApi.listAdditives()` → `GET /api/food/additives` → ✅
5. `userApi.getAlerts()` → ✅
6. `userApi.addAlert(additiveId)` → ✅
7. `userApi.removeAlert(additiveId)` → ✅
8. `insightApi.correlations(30)` → ✅

**Issues:**

🔴 **`eNumber` missing from additive list:** The profile page renders `add.eNumber` (line 881: `{add.name} {add.eNumber ? \`(\${add.eNumber})\` : ""}`). The additive list is fetched via `foodApi.listAdditives()`which calls`GET /api/food/additives`. **The backend `GetFoodAdditives`endpoint does NOT return`eNumber`in its anonymous object.** Result:`add.eNumber`will always be`undefined`, and the `(E-number)` suffix will never display.

🔴 **`safetyRating` missing from additive list:** The `FoodAdditive` type expects `safetyRating`, but `GetFoodAdditives` doesn't return it. If the profile page filters or displays by safety rating, it will be `undefined`.

🟠 **Profile update response shape mismatch:** `userApi.updateProfile` returns `UserProfile` type, but backend returns an anonymous object that includes `createdAt`. Frontend type doesn't have `createdAt`, so it's harmlessly ignored. However, after updating profile, the code calls `userApi.getProfile().then(({data}) => setUser(data))` (line 124) — the `GetProfile` response also includes `createdAt` which is silently dropped. ✅ No runtime error, just unused data.

**Additive search/filter:**

- Line 853-854 searches by `a.eNumber` — will always return false (eNumber is undefined from list endpoint). **Users cannot find additives by E-number in the profile additive picker.**

**Error Handling:**

- ✅ Toast on profile update error
- ✅ Toast on alert add error

---

### 3.11 `app/food/[id].tsx`

**API Calls:**

1. `foodApi.get(id)` → ✅
2. `foodApi.safetyReport(id)` → ✅
3. `foodApi.personalizedScore(id)` → ✅
4. `userApi.getProfile()` → ✅
5. `mealApi.create(data)` → ✅

**Issues:**

🟠 **Additive field name mismatch in detail view:** This page displays additives from `FoodProductDto.Additives` (via `foodApi.get` or `safetyReport`). The `FoodAdditiveDto` in the backend returns `usRegulatoryStatus` and `euRegulatoryStatus`, but the frontend `FoodAdditive` type expects `usStatus` and `euStatus`. **Any display of US/EU regulatory status from the food detail page will show `undefined`.**

🟠 **Add-to-meal missing fields:** Similar to scan.tsx, when creating a meal from the food detail page (line ~132-156), `cholesterolMg`, `saturatedFatG`, `potassiumMg` are not included in the meal item creation.

**Serving Size:**

- ✅ Correctly reads `product.servingQuantity` to set default serving size
- ✅ Correctly calculates nutrition per serving from per-100g values

**Error Handling:**

- ✅ Toast on meal add error
- ✅ Loading/error states for product and safety report queries

---

### 3.12 `app/settings.tsx`

**API Calls:**

1. `userApi.getProfile()` → ✅
2. `userApi.updateProfile({ timezoneId })` → ✅
3. `authApi.changePassword(data)` → ✅
4. `mealApi.export(from, to)` → ✅
5. `userApi.deleteAccount()` → ✅

**Issues:**

🟡 **Change password error handling:** The backend returns `Results.ValidationProblem` (RFC 7807 Problem Details) for password errors. The frontend uses `try/catch` with:

```typescript
try {
  await authApi.changePassword(data);
} catch (err) {
  // displays generic error
}
```

The validation problem response has `status: 400` and `errors` object with keys like `"PasswordMismatch"` and `"PasswordTooWeak"`. The frontend may not parse these specific error types and would show a generic error message instead of the specific validation message.

🟡 **Delete account flow:** Correctly calls `userApi.deleteAccount()` then `logout()`. Backend soft-deletes all data. ✅

🟡 **Export data:** Uses `mealApi.export(from, to)`. The response includes both meals and symptoms — correctly typed as `DataExport`. ✅

**Timezone update:**

- ✅ `onError` toast for timezone update failure
- ✅ Invalidates profile query on success

---

### 3.13 `app/onboarding.tsx`

**API Calls:**

1. `userApi.updateProfile(data)` → ✅
2. `userApi.updateGoals(data)` → ✅

**Issues:**

🟡 **Onboarding completion:** The onboarding flow sets `onboardingCompleted: true` via `updateProfile`. Backend correctly handles this with `if (request.OnboardingCompleted.HasValue) user.OnboardingCompleted = request.OnboardingCompleted.Value`. ✅

🟡 **Error handling:** Uses try/catch blocks for both profile and goals updates. Generic error display. ✅

**Data flow:**

- ✅ Collects allergies, dietary preferences, daily goals
- ✅ Sends as separate `updateProfile` and `updateGoals` calls
- ✅ Sets `onboardingCompleted: true` in the profile update

---

## 4. Summary of All Issues

### 🔴 Critical Issues

| #   | File(s)                                  | Issue                                                                                                               | Impact                                                                           |
| --- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| C1  | `types/index.ts`                         | `MealLog.userId` field doesn't exist in backend `MealLogDto`                                                        | Always `undefined`. If any code depends on `meal.userId`, it will fail silently. |
| C2  | `symptoms.tsx`                           | `duration` sent as natural language string (`"30 minutes"`) but backend expects `TimeSpan` format (`"00:30:00"`)    | 400 Bad Request or silent data loss on symptom creation/update with duration     |
| C3  | `profile.tsx`                            | `GetFoodAdditives` endpoint omits `eNumber` from response                                                           | E-number search filter always returns no results; E-number display always empty  |
| C4  | `profile.tsx`                            | `GetFoodAdditives` endpoint omits `safetyRating` from response                                                      | Safety rating display/filter broken for additive list                            |
| C5  | `meals.tsx`, `scan.tsx`, `food/[id].tsx` | Natural language and add-to-meal flows omit `cholesterolMg`, `saturatedFatG`, `potassiumMg` from created meal items | These micronutrients always stored as 0, losing parsed/known data                |

### 🟠 Moderate Issues

| #   | File(s)          | Issue                                                                                                      | Impact                                                                                        |
| --- | ---------------- | ---------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| M1  | `types/index.ts` | `FoodAdditive.usStatus` vs backend `FoodAdditiveDto.UsRegulatoryStatus`                                    | US regulatory status shows `undefined` on food detail pages (works on additive list page)     |
| M2  | `types/index.ts` | `FoodAdditive.euStatus` vs backend `FoodAdditiveDto.EuRegulatoryStatus`                                    | EU regulatory status shows `undefined` on food detail pages                                   |
| M3  | `types/index.ts` | `MealItem.cholesterolMg`, `saturatedFatG`, `potassiumMg` marked optional but backend always sends non-null | Frontend may conditionally hide non-zero values thinking they're absent                       |
| M4  | `types/index.ts` | `CreateMealItemRequest` marks `fiberG`, `sugarG`, `sodiumMg`, etc. as optional                             | Silent 0-defaulting in backend when frontend omits these                                      |
| M5  | `types/index.ts` | Backend profile response includes `createdAt` not in `UserProfile` type                                    | Data silently dropped (harmless but wasteful)                                                 |
| M6  | `types/index.ts` | `FoodAdditive` single endpoint (`GetFoodAdditive`) also omits `safetyRating`                               | `safetyRating` only available via `FoodProductDto.Additives`, not from direct additive lookup |
| M7  | `symptoms.tsx`   | Edit symptom pre-fills `duration` with `TimeSpan` string (e.g., `"00:30:00"`) in a free-text field         | Poor UX — user sees `00:30:00` instead of `30 minutes`                                        |
| M8  | `types/index.ts` | `NaturalLanguageMealRequest` missing optional `loggedAt` field                                             | Cannot specify meal time when using natural language logging                                  |
| M9  | `settings.tsx`   | Change-password error handling doesn't parse RFC 7807 `errors` object                                      | User sees generic error instead of "Current password is incorrect" or "Password too weak"     |

### 🟡 Minor Issues

| #   | File(s)          | Issue                                                                                                                  | Impact                                  |
| --- | ---------------- | ---------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| L1  | `types/index.ts` | `FoodProduct` missing `nutritionInfo`, `additivesTags`, `isDeleted` fields from backend                                | Harmlessly dropped                      |
| L2  | `types/index.ts` | `FoodAdditive` missing `efsaLastReviewDate`, `epaCancerClass`, `fdaAdverseEventCount`, `fdaRecallCount`, `lastUpdated` | Data not displayed (could be useful)    |
| L3  | `types/index.ts` | `ParsedFoodItem` missing `servingSize`, `servingQuantity`                                                              | Parsed serving info lost                |
| L4  | `types/index.ts` | `MealLog` missing `photoUrl` from backend                                                                              | Photo URLs not displayed                |
| L5  | `client.ts`      | No production URL default                                                                                              | Must be configured via env var          |
| L6  | `client.ts`      | No global redirect to login after refresh token failure                                                                | User sees error instead of login prompt |
| L7  | `auth.ts`        | Dynamic import in `hydrate()`                                                                                          | Minor startup latency                   |
| L8  | `api/index.ts`   | Insight date computation duplicates backend defaults                                                                   | Redundant but harmless                  |

---

## 5. Risk Assessment

### Immediate Action Required

1. **Fix `GetFoodAdditives` endpoint** to include `eNumber` and `safetyRating` fields — or change to return `FoodAdditiveDto` instead of anonymous object. (C3, C4)

2. **Fix `duration` handling** — either:
   - Convert frontend free-text duration to `TimeSpan` format before sending, OR
   - Accept string duration on backend and parse it, OR
   - Use a structured duration picker (hours/minutes dropdowns) in the UI (C2)

3. **Fix `FoodAdditive` field name mapping** — either rename frontend `usStatus`/`euStatus` to `usRegulatoryStatus`/`euRegulatoryStatus`, or change `FoodAdditiveDto` property names to match the anonymous object names. (M1, M2)

4. **Include missing micronutrients** in natural-language and add-to-meal flows (`cholesterolMg`, `saturatedFatG`, `potassiumMg`). (C5)

5. **Remove phantom `userId` field** from `MealLog` type. (C1)

### Low Priority

- Add `photoUrl` to frontend `MealLog` type (L4)
- Add regulatory/safety fields to `FoodAdditive` type (L2)
- Parse RFC 7807 validation errors for better UX on password change (M9)
- Add global 401-after-refresh handler to redirect to login (L6)
