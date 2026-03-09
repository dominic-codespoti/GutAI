# Meal Logging Bug Analysis: Meals Created Successfully but Not Displayed

**Date:** 2026-03-08
**Severity:** High — core functionality broken for users not in UTC timezone

---

## Executive Summary

When a user logs a meal, the success toast fires but the meal does not appear in the list. **The root cause is a timezone mismatch between how `loggedAt` is constructed during meal creation and how the GET endpoint filters meals for display.**

The frontend constructs `loggedAt` using a **local date + UTC time suffix** (creating a hybrid timestamp that represents neither correct UTC nor correct local time). The backend stores this value as-is (interpreted as UTC). When the frontend then queries for meals, it passes `tzOffsetMinutes` and the backend converts the requested local date to a UTC range — but the stored `loggedAt` falls **outside** that UTC range because it was constructed with a local date instead of a UTC date.

---

## Problem Understanding and Scope

**Symptom:** User logs a meal → success toast appears → meal list shows "No meals logged for this date."
**Affected users:** Anyone whose local timezone is significantly offset from UTC (especially UTC+N positive offsets where the local date is ahead of the UTC date).
**Not affected:** Users in UTC or very close to UTC, or users logging meals during hours where local date == UTC date.

---

## Detailed Data Flow Analysis

### 1. Meal Creation (Write Path)

**File:** `frontend/components/meals/AddMealSheet.tsx`, lines 173, 197
**File:** `frontend/components/meals/RecentFoodsRow.tsx`, line 39

```typescript
loggedAt: selectedDate + "T" + new Date().toISOString().split("T")[1];
```

- `selectedDate` = local date string like `"2026-03-08"` (from `toLocalDateStr()`)
- `new Date().toISOString().split("T")[1]` = UTC time suffix like `"13:30:00.000Z"`
- **Result:** `"2026-03-08T13:30:00.000Z"` — this is a **hybrid**: local date + UTC time

**File:** `backend/src/GutAI.Api/Endpoints/MealEndpoints.cs`, line 60

```csharp
LoggedAt = request.LoggedAt ?? DateTime.UtcNow
```

The backend receives the `loggedAt` string and deserializes it. Because it ends with `Z`, .NET parses it as UTC. The value stored is: **local-date + UTC-time, treated as UTC**.

**Example for a user in UTC+11 (Sydney) at 1:30 AM local on March 9:**

- `selectedDate` = `"2026-03-09"` (local date: March 9)
- `new Date().toISOString()` = `"2026-03-08T14:30:00.000Z"` (UTC is still March 8)
- `loggedAt` sent = `"2026-03-09T14:30:00.000Z"` ← **March 9 14:30 UTC** (WRONG — actual UTC is March 8 14:30)

### 2. Meal Fetching (Read Path)

**File:** `frontend/app/(tabs)/meals.tsx`, lines 76-80

```typescript
useQuery({
  queryKey: ["meals", selectedDate],
  queryFn: () => mealApi.list(selectedDate).then((r) => r.data),
});
```

**File:** `frontend/src/api/index.ts`, lines 72-73

```typescript
list: (date?: string) =>
  api.get<MealLog[]>("/api/meals", { params: { date, tzOffsetMinutes: tz() } }),
```

The frontend sends: `GET /api/meals?date=2026-03-09&tzOffsetMinutes=-660` (for UTC+11)

**File:** `backend/src/GutAI.Api/Endpoints/MealEndpoints.cs`, lines 122-140

```csharp
var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);  // 2026-03-09
var offset = TimeSpan.FromMinutes(-tzOffsetMinutes.Value);        // +11 hours
var utcStart = targetDate.ToDateTime(TimeOnly.MinValue) - offset; // 2026-03-08T13:00:00 UTC
var utcEnd = targetDate.ToDateTime(TimeOnly.MaxValue) - offset;   // 2026-03-09T12:59:59 UTC
```

The backend looks for meals where `LoggedAt` is between **March 8 13:00 UTC** and **March 9 12:59 UTC**.

### 3. The Mismatch

The stored `LoggedAt` is `2026-03-09T14:30:00Z` — this is **outside** the query range (which ends at `2026-03-09T12:59:59Z`).

|                     | Value                  |
| ------------------- | ---------------------- |
| **Stored LoggedAt** | `2026-03-09T14:30:00Z` |
| **Query UTC Start** | `2026-03-08T13:00:00Z` |
| **Query UTC End**   | `2026-03-09T12:59:59Z` |
| **In range?**       | ❌ NO                  |

The meal is stored, the toast fires, but the query never returns it.

---

## Key Findings

### Finding 1: Hybrid `loggedAt` Construction (ROOT CAUSE)

**Files affected:**

- `frontend/components/meals/AddMealSheet.tsx` — lines 173, 197
- `frontend/components/meals/RecentFoodsRow.tsx` — line 39
- `frontend/src/hooks/useMealMutations.ts` — line 83 (copyMeal)

All three construct `loggedAt` the same broken way:

```typescript
selectedDate + "T" + new Date().toISOString().split("T")[1];
```

This concatenates a **local date** with a **UTC time**, creating a timestamp that is neither correct local time nor correct UTC.

### Finding 2: Query Key Invalidation is Correct

**File:** `frontend/src/hooks/useMealMutations.ts`, lines 23-31

```typescript
function invalidate(qc) {
  qc.invalidateQueries({ queryKey: ["meals"] }); // prefix match — invalidates ALL ["meals", *]
  qc.invalidateQueries({ queryKey: ["daily-summary"] });
  // ... other keys
}
```

The invalidation uses `queryKey: ["meals"]` which is a **prefix match** and correctly invalidates `["meals", selectedDate]`. This is NOT the bug. The refetch happens, but the backend returns an empty array due to the date mismatch.

### Finding 3: Backend Date Filtering Logic is Correct

The backend's timezone-aware filtering in `GetMealsByDate` is sound. Given a local date and `tzOffsetMinutes`, it correctly computes the UTC window for that local day. The issue is purely that the stored `LoggedAt` doesn't match reality.

### Finding 4: `staleTime` Masks the Issue on Subsequent Navigation

**File:** `frontend/src/queryClient.ts`

```typescript
staleTime: 1000 * 60 * 5,  // 5 minutes
```

With a 5-minute stale time, even after invalidation triggers a refetch, the result (empty array) gets cached for 5 minutes. Pull-to-refresh works because it calls `refetch()` directly, but it still returns empty because the backend date filter excludes the meal.

### Finding 5: Toast Fires Before Data is Visible

**File:** `frontend/src/hooks/useMealMutations.ts`, lines 39-45

```typescript
onSuccess: () => {
  invalidate(queryClient); // triggers async refetch
  mealSheet.close();
  toast.success("Meal logged!"); // fires immediately
};
```

The toast fires in `onSuccess` which means the API POST succeeded. The `invalidateQueries` call triggers an async background refetch. Even if the refetch completes fast, the backend filters it out — so the list stays empty. The toast is a red herring because it correctly indicates the creation succeeded.

---

## Reproduction Scenario

1. Set device to any timezone significantly ahead of UTC (e.g., UTC+10 AEST)
2. Log a meal at any time where local date ≠ UTC date (e.g., between midnight and 10:00 AM local)
3. Observe: success toast appears, but meal list shows empty
4. In the database, the meal exists with a `LoggedAt` that falls outside the expected UTC range for that local date

**Worst case:** For UTC+13 (Samoa), the mismatch window is 13 hours — meals logged between midnight and 1:00 PM local won't display.

**Users in negative UTC offsets (Americas):** The bug manifests differently — meals logged in the evening (when UTC date is already tomorrow) would show up on tomorrow's list instead of today's.

---

## Alternative Solution Approaches

### Option A: Fix `loggedAt` construction to use proper UTC (RECOMMENDED)

Convert the local date+time to a correct UTC timestamp:

```typescript
// Build a local DateTime from selectedDate + current local time, then let the
// Date constructor handle UTC conversion via toISOString().
const now = new Date();
const [y, m, d] = selectedDate.split("-").map(Number);
const local = new Date(
  y,
  m - 1,
  d,
  now.getHours(),
  now.getMinutes(),
  now.getSeconds(),
);
const loggedAt = local.toISOString();
```

**Pros:** Correct UTC timestamp; minimal backend changes; all existing queries work.
**Cons:** Need to update 3 frontend locations.

### Option B: Send `loggedAt` without timezone suffix (let backend interpret)

```typescript
loggedAt: selectedDate + "T" + now.toTimeString().split(" ")[0];
// e.g., "2026-03-09T01:30:00" (no Z suffix)
```

Then have backend parse it as local time using the user's timezone profile.

**Pros:** Semantically clean.
**Cons:** Requires backend changes; ambiguous without timezone context; breaking change.

### Option C: Don't send `loggedAt` at all — let backend use `DateTime.UtcNow`

Remove the `loggedAt` field from create requests and let the backend default to `DateTime.UtcNow`.

**Pros:** Simplest; always correct UTC.
**Cons:** Loses the ability to log meals for past dates/times; the `selectedDate` becomes irrelevant for creation timestamp.

---

## Recommended Solution

**Option A** — Fix the `loggedAt` construction in 3 locations:

### Files to change:

1. **`frontend/components/meals/AddMealSheet.tsx`** — lines 173, 197
2. **`frontend/components/meals/RecentFoodsRow.tsx`** — line 39
3. **`frontend/src/hooks/useMealMutations.ts`** — line 83

### Recommended helper function (add to `frontend/src/utils/date.ts`):

```typescript
/** Build a correct UTC ISO string for a local date + current local time. */
export function buildLoggedAtUTC(localDateStr: string): string {
  const now = new Date();
  const [y, m, d] = localDateStr.split("-").map(Number);
  const local = new Date(
    y,
    m - 1,
    d,
    now.getHours(),
    now.getMinutes(),
    now.getSeconds(),
  );
  return local.toISOString();
}
```

### Replace in all 3 files:

```typescript
// BEFORE (broken):
loggedAt: selectedDate + "T" + new Date().toISOString().split("T")[1],

// AFTER (correct):
loggedAt: buildLoggedAtUTC(selectedDate),
```

---

## Risks and Edge Cases

| Risk                                                                                  | Mitigation                                                                                                                               |
| ------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Meals logged for past/future dates (via date nav) should use that date + current time | `buildLoggedAtUTC` already handles this — it takes the selected date and applies current local time, then converts to UTC                |
| Existing meals in DB with wrong `loggedAt` values                                     | These are already persisted incorrectly; a one-time data migration could fix them, but it's low priority since the offset is predictable |
| Users who switch timezones between logging and viewing                                | Already handled by `tzOffsetMinutes` being sent on every request based on current device timezone                                        |
| The `Z` suffix in `.toISOString()` could be stripped by some JSON serializers         | Standard behavior in JS/JSON; .NET handles this correctly                                                                                |

---

## Verification Steps

After the fix:

1. Set device to UTC+11 timezone
2. Log a meal at 1:00 AM local (UTC date = previous day)
3. Verify the `loggedAt` in the POST request body is a correct UTC timestamp (previous day + correct UTC hours)
4. Verify the meal appears in today's list immediately after creation
5. Verify pull-to-refresh also shows the meal
6. Test in UTC-5, UTC+0, and UTC+13 timezones for full coverage
