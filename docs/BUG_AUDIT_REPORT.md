# Bug Audit Report — GutAI / Gut Lens

**Date**: 2025-01-25
**Scope**: Full codebase — frontend (React Native/Expo) + backend (.NET Minimal API)
**Methodology**: Static analysis of all source files, data flow tracing, contract verification
**Exclusions**: The `buildLoggedAt`/`redateLoggedAt` fix already applied to `AddMealSheet.tsx`, `EditMealSheet.tsx`, `CopyMealSheet.tsx`, and `RecentFoodsRow.tsx` is NOT re-reported.

---

## Bug 1 — `scan.tsx` uses `new Date().toISOString()` instead of `buildLoggedAt`

| Field        | Value                                                                                                                                                                                                                     |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Severity** | 🔴 High                                                                                                                                                                                                                   |
| **Category** | Timestamp / Timezone                                                                                                                                                                                                      |
| **File**     | `frontend/app/(tabs)/scan.tsx` line ~190                                                                                                                                                                                  |
| **Impact**   | Meals added from the barcode scanner always log to UTC "now" instead of the user's selected local date. A user in UTC+10 scanning food at 8 AM local (10 PM previous day UTC) will have the meal attributed to yesterday. |

**Code**:

```tsx
const addToMealMutation = useMutation({
  mutationFn: (product: FoodProduct) => {
    // ...
    return mealApi.create({
      mealType: addToMealType,
      loggedAt: new Date().toISOString(), // ← BUG: should use buildLoggedAt(selectedDate)
      items: [
        /* ... */
      ],
    });
  },
});
```

**Fix**: Import `buildLoggedAt` from `@/src/utils/date` and use it with the current selected date, matching the pattern in `AddMealSheet.tsx`.

---

## Bug 2 — `food/[id].tsx` uses `new Date().toISOString()` instead of `buildLoggedAt`

| Field        | Value                                                                                                                |
| ------------ | -------------------------------------------------------------------------------------------------------------------- |
| **Severity** | 🔴 High                                                                                                              |
| **Category** | Timestamp / Timezone                                                                                                 |
| **File**     | `frontend/app/food/[id].tsx` line ~240                                                                               |
| **Impact**   | Identical to Bug 1. Meals added from the Food Detail screen are assigned UTC "now" instead of the user's local date. |

**Code**:

```tsx
const addToMealMutation = useMutation({
  mutationFn: () => {
    // ...
    return mealApi.create({
      mealType: addToMealType,
      loggedAt: new Date().toISOString(), // ← BUG
      items: [
        /* ... */
      ],
    });
  },
});
```

**Fix**: Same as Bug 1 — use `buildLoggedAt()`.

---

## Bug 3 — Chat history endpoint omits `id` field → broken FlatList keys

| Field               | Value                                                                                                                                                                                                                                                                                                                                                                                                          |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Severity**        | 🔴 High                                                                                                                                                                                                                                                                                                                                                                                                        |
| **Category**        | Data Contract Mismatch                                                                                                                                                                                                                                                                                                                                                                                         |
| **File (backend)**  | `backend/src/GutAI.Api/Endpoints/ChatEndpoints.cs` lines 68-72                                                                                                                                                                                                                                                                                                                                                 |
| **File (frontend)** | `frontend/app/(tabs)/chat.tsx` lines 74, 389                                                                                                                                                                                                                                                                                                                                                                   |
| **Impact**          | The `GetHistory` endpoint returns `{ Role, Content, CreatedAt }` but the frontend `ChatMessage` type expects `id: string`. The chat screen maps `m.id` (which is `undefined`) into local state, then uses `keyExtractor={(item) => item.id}` — every item gets key `"undefined"`, causing React to render only the last item or produce deduplication warnings. Chat history will appear broken on app reload. |

**Backend returns**:

```csharp
return Results.Ok(messages.Select(m => new
{
    m.Role,
    m.Content,
    m.CreatedAt
    // ← Missing: m.Id
}));
```

**Frontend expects**:

```typescript
export interface ChatMessage {
  id: string; // ← never populated from API
  role: "user" | "assistant";
  content: string;
  createdAt: string;
}
```

**Fix**: Add `m.Id` to the backend projection, or generate a synthetic key on the frontend (e.g., `id: m.id ?? \`msg-${index}\``).

---

## Bug 4 — Nutrition trends grouped by UTC date, not user's local date

| Field        | Value                                                                                                                                                                                                                                                                                                                                                                                               |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Severity** | 🟡 Medium                                                                                                                                                                                                                                                                                                                                                                                           |
| **Category** | Timezone / Backend Logic                                                                                                                                                                                                                                                                                                                                                                            |
| **File**     | `backend/src/GutAI.Api/Endpoints/InsightEndpoints.cs` lines 44-52                                                                                                                                                                                                                                                                                                                                   |
| **Impact**   | `meals.GroupBy(m => m.LoggedAt.Date)` groups by the UTC date component of `LoggedAt`. For users in timezones far from UTC (e.g., UTC+10, UTC-8), meals logged near midnight local time will be attributed to the wrong day in the nutrition trends chart. The frontend sends a `tzOffsetMinutes` header on meal creation, but the insights endpoint ignores it and uses raw UTC dates for grouping. |

**Code**:

```csharp
var grouped = meals.GroupBy(m => m.LoggedAt.Date)  // ← UTC date, not local
    .Select(g => new {
        date = DateOnly.FromDateTime(g.Key),
        calories = g.Sum(m => m.TotalCalories),
        // ...
    });
```

**Fix**: Accept a `tzOffsetMinutes` query parameter and apply it before grouping: `m.LoggedAt.AddMinutes(-tzOffset).Date`.

---

## Bug 5 — Symptom `occurredAt` construction can shift to wrong day during DST

| Field        | Value                                                                                                                                                                                                                                                                                                                                                                                                               |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Severity** | 🟡 Medium                                                                                                                                                                                                                                                                                                                                                                                                           |
| **Category** | Timestamp / Timezone                                                                                                                                                                                                                                                                                                                                                                                                |
| **File**     | `frontend/app/(tabs)/symptoms.tsx` lines 170-178                                                                                                                                                                                                                                                                                                                                                                    |
| **Impact**   | The code creates a Date from the current time, then mutates it with `setFullYear(year, month-1, day)`. During DST transitions (e.g., "spring forward" at 2 AM), the local time hours on the original Date may not exist for the target date. This is different from `buildLoggedAt()` which constructs a fresh `new Date(y, m-1, d, h, m, s)`. Edge case but can produce an `occurredAt` that's on an adjacent day. |

**Code**:

```tsx
const date = new Date();
date.setFullYear(year, month - 1, day); // ← mutates existing Date; DST-unsafe
const isoString = date.toISOString();
```

**Fix**: Use the same `new Date(year, month-1, day, hours, mins, secs)` constructor pattern as `buildLoggedAt`, or use `buildLoggedAt` directly.

---

## Bug 6 — `saveEdit` for symptoms passes stored TimeSpan back through parser, duration not editable

| Field        | Value                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Severity** | 🟢 Low                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| **Category** | UX / Data Integrity                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| **File**     | `frontend/app/(tabs)/symptoms.tsx` line ~281                                                                                                                                                                                                                                                                                                                                                                                                              |
| **Impact**   | When editing a symptom, the `saveEdit` function passes `editingSymptom.duration` (already a TimeSpan string like `"01:30:00"`) back through `parseDurationToTimeSpan()`. While this happens to work (the regex matches `HH:MM:SS` and returns it unchanged), the user has no UI to actually modify the duration — it's always sent back as the original value. If the stored format ever changes, the round-trip through the parser could break silently. |

**Code**:

```tsx
const saveEdit = () => {
  updateMutation.mutate({
    // ...
    duration: parseDurationToTimeSpan(editingSymptom?.duration || ""),
    //        ^^^ re-parsing an already-formatted value; also not editable by user
  });
};
```

**Fix**: Either (a) pass `editingSymptom.duration` directly without re-parsing, or (b) add an editable duration field in the edit modal, or (c) add a `editDuration` state variable that's initialized from the symptom.

---

## Summary

| #   | Bug                                          | Severity  | Category          | File(s)                                  |
| --- | -------------------------------------------- | --------- | ----------------- | ---------------------------------------- |
| 1   | `scan.tsx` — `loggedAt` uses UTC now         | 🔴 High   | Timestamp         | `scan.tsx:190`                           |
| 2   | `food/[id].tsx` — `loggedAt` uses UTC now    | 🔴 High   | Timestamp         | `food/[id].tsx:240`                      |
| 3   | Chat history missing `id` field              | 🔴 High   | Contract mismatch | `ChatEndpoints.cs:70`, `chat.tsx:74,389` |
| 4   | Nutrition trends grouped by UTC date         | 🟡 Medium | Timezone          | `InsightEndpoints.cs:44`                 |
| 5   | Symptom `occurredAt` DST-fragile             | 🟡 Medium | Timestamp         | `symptoms.tsx:170`                       |
| 6   | `saveEdit` duration not editable / re-parsed | 🟢 Low    | UX                | `symptoms.tsx:281`                       |

**Total: 3 High, 2 Medium, 1 Low**
