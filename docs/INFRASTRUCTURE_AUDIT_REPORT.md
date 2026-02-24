# Infrastructure Audit Report

**Date:** 2026-02-23
**Scope:** All infrastructure services, data store, identity, caching, middleware, interfaces

---

## CRITICAL ISSUES

### 1. FoodDiaryAnalysisService — CancellationToken Not Propagated

**File:** `backend/src/GutAI.Infrastructure/Services/FoodDiaryAnalysisService.cs`
**Lines:** 14, 16, 18, 20, 111, 113, 138, 140
**Severity:** Critical — can cause requests to hang indefinitely on shutdown/cancellation

Every call to `ITableStore` methods omits the `CancellationToken`:

```csharp
// Line 14: missing ct
var meals = await store.GetMealLogsByDateRangeAsync(userId, from, to);
// Line 16: missing ct
meal.Items = await store.GetMealItemsAsync(userId, meal.Id);
// Line 18: missing ct
var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, from, to);
// Line 20: missing ct
s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);
// Line 111: missing ct
var recentMeals = await store.GetMealLogsByDateRangeAsync(userId, from, to);
// Line 113: missing ct
meal.Items = await store.GetMealItemsAsync(userId, meal.Id);
// Line 138: missing ct
var recentSymptoms = await store.GetSymptomLogsByDateRangeAsync(...);
// Line 140: missing ct
s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);
```

The interface declares `CancellationToken ct = default` so these compile, but cancellation is silently ignored.

### 2. PersonalizedScoringService — CancellationToken Not Propagated

**File:** `backend/src/GutAI.Infrastructure/Services/PersonalizedScoringService.cs`
**Lines:** 108, 179, 192, 194
**Severity:** Critical — same issue as above

```csharp
// Line 108: missing ct
var user = await store.GetUserAsync(userId);
// Line 179: missing ct
var allSymptoms = await store.GetSymptomLogsByDateRangeAsync(userId, cutoffFrom, cutoffTo);
// Line 192: missing ct
var allMeals = await store.GetMealLogsByDateRangeAsync(userId, cutoffFrom, cutoffTo);
// Line 194: missing ct
meal.Items = await store.GetMealItemsAsync(userId, meal.Id);
```

### 3. TableStorageStore — `EnsureTableAsync()` Race Condition

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 22-24
**Severity:** Critical — `Lazy<Task>` is NOT safe when the inner task fails

```csharp
_ensureCreated = new Lazy<Task>(() => _table.CreateIfNotExistsAsync());
```

If `CreateIfNotExistsAsync()` fails on first call (e.g., Azurite not yet started), the `Lazy<Task>` caches the **faulted task** permanently. All subsequent calls will instantly throw the same cached exception forever — the store becomes permanently broken until the app is restarted. There is no retry mechanism.

### 4. TableStorageStore — `EnsureTableAsync()` Ignores CancellationToken

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 22-24
**Severity:** High

```csharp
_ensureCreated = new Lazy<Task>(() => _table.CreateIfNotExistsAsync());
```

`CreateIfNotExistsAsync()` is called without a `CancellationToken`. Because it's cached in a `Lazy<Task>`, there's no easy way to pass one. During app startup, if this call hangs, it blocks every request indefinitely.

---

## HIGH SEVERITY ISSUES

### 5. FoodProduct Entity — `NutritionInfo`, `FoodProductAdditiveIds`, `IsDeleted` Not Persisted

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 532-557 (UpsertFoodProductAsync), 569-597 (MapToFoodProduct)
**Severity:** High — data silently lost on round-trip

`FoodProduct` entity has these properties:

- `NutritionInfo? NutritionInfo` — never stored or loaded (ValueObject)
- `List<Guid> FoodProductAdditiveIds` — never stored or loaded
- `bool IsDeleted` — never stored or loaded

The `UpsertFoodProductAsync` and `MapToFoodProduct` methods don't handle these fields. A product's `IsDeleted` flag is always `false` after read, and deleted products are still returned by queries.

### 6. FoodAdditive Entity — `SafetyRating` Not Persisted

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 619-640 (UpsertFoodAdditiveAsync), 642-660 (MapToFoodAdditive)
**Severity:** High — data loss

`FoodAdditive` entity has a `SafetyRating SafetyRating` property (non-nullable enum) that is not stored in `UpsertFoodAdditiveAsync` and not read in `MapToFoodAdditive`. The value is silently lost on save/load.

### 7. SearchFoodProductsAsync — Full Table Scan with Client-Side Filter

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 513-528
**Severity:** High — O(n) performance, scans ALL food products every search

```csharp
var filter = "PartitionKey eq 'FOOD'";
var entities = await QueryAsync(filter, ct);
// Then client-side .Where() filtering
```

This loads every single food product into memory before filtering. With a growing database this becomes a serious performance bottleneck and memory pressure issue. Azure Table Storage supports OData filters but not `contains` — this needs a different indexing approach (e.g., search index partition keys).

### 8. NaturalLanguageFallbackService — Circular DI Dependency Risk

**File:** `backend/src/GutAI.Infrastructure/DependencyInjection.cs` (lines 89-90), `NaturalLanguageFallbackService.cs` (line 11)
**Severity:** High

```csharp
// DI registration:
services.AddScoped<IFoodApiService, CompositeFoodApiService>();  // line 89
services.AddScoped<NaturalLanguageFallbackService>();             // line 90
```

`NaturalLanguageFallbackService` depends on `IFoodApiService` (→ `CompositeFoodApiService`).
`CompositeNutritionService` depends on `NaturalLanguageFallbackService`.
`CompositeFoodApiService` implements `IFoodApiService`.

If `NaturalLanguageFallbackService.ParseAsync()` calls `_foodApi.SearchAsync()` which internally calls back to the fallback service, you'd get infinite recursion. The architecture should be reviewed to ensure no such cycle exists at runtime.

### 9. ExceptionMiddleware — Response Already Started Check Missing

**File:** `backend/src/GutAI.Api/Middleware/ExceptionMiddleware.cs`
**Lines:** 24-50
**Severity:** High — can throw `InvalidOperationException` at runtime

```csharp
catch (Exception ex)
{
    // No check for context.Response.HasStarted
    context.Response.StatusCode = ...
```

If the response has already started streaming (e.g., in a streaming endpoint), modifying `StatusCode` and writing to the response throws a secondary `InvalidOperationException` which crashes the request processing. Should check `context.Response.HasStarted` before attempting to write the error response.

### 10. ExceptionMiddleware — `FormatException` Maps to 400 (Bad Request)

**File:** `backend/src/GutAI.Api/Middleware/ExceptionMiddleware.cs`
**Lines:** 28-33
**Severity:** Medium-High — leaks internal parsing errors to clients

`FormatException` is treated as a client error (400), but it commonly occurs due to internal bugs (e.g., `Guid.Parse` on bad data from the database). This would return a 400 to the client with the exception message when it should be a 500. Only `ArgumentException` thrown at the API boundary should be 400.

---

## MEDIUM SEVERITY ISSUES

### 11. JwtService — JWT Secret Length Not Validated

**File:** `backend/src/GutAI.Infrastructure/Identity/JwtService.cs`
**Lines:** 19-20
**Severity:** Medium — weak key allows brute-force attacks

```csharp
var secret = _config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
```

HMAC-SHA256 requires at minimum 256 bits (32 bytes). No validation ensures the configured secret meets this requirement. A short secret could be brute-forced. The production `appsettings.json` doesn't even have a `Jwt` section, so this would throw at runtime if `appsettings.Production.json` is not properly configured.

### 12. JwtService — ClockSkew = TimeSpan.Zero

**File:** `backend/src/GutAI.Infrastructure/Identity/JwtService.cs`
**Lines:** 62
**Severity:** Medium — tokens rejected for minor clock drift

```csharp
ClockSkew = TimeSpan.Zero
```

While this prevents token replay after expiry, it means any clock skew between the token issuer and validator will cause valid tokens to be rejected. A small skew (30 seconds to 2 minutes) is standard practice. With distributed systems this can cause intermittent auth failures.

### 13. RedisCacheService — Named "Redis" but Uses In-Memory Cache

**File:** `backend/src/GutAI.Infrastructure/Caching/RedisCacheService.cs`, `DependencyInjection.cs` (line 28-29)
**Severity:** Medium — misleading name, not horizontally scalable

```csharp
services.AddDistributedMemoryCache();
services.AddSingleton<ICacheService, RedisCacheService>();
```

The class is named `RedisCacheService` but backs onto `IDistributedCache` which is configured as in-memory. This works functionally but:

1. Cache is lost on every restart
2. In a multi-instance deployment, each instance has its own cache (no sharing)
3. The name misleads developers into thinking Redis is in use

### 14. RedisCacheService — No Cache Key Prefix/Namespace

**File:** `backend/src/GutAI.Infrastructure/Caching/RedisCacheService.cs`
**Lines:** all Get/Set/Remove methods
**Severity:** Medium — key collisions across different data types

Cache keys are passed raw with no namespace prefix. If two different parts of the codebase use the same key pattern (e.g., `"user:{id}"`) for different data types, they'd collide. When/if upgrading to a shared Redis instance, keys could also collide across environments.

### 15. TableStorageStore — MealLog Items Type Mismatch

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 231, 250
**Severity:** Medium — potential runtime issues with `ICollection<MealItem>` vs `List<MealItem>`

```csharp
// MapToMealLog doesn't set Items, then GetMealLogAsync does:
meal.Items = await GetMealItemsAsync(userId, mealId, ct); // Returns List<MealItem>
```

`MealLog.Items` is declared as `ICollection<MealItem>` which `List<MealItem>` satisfies. However, `FoodDiaryAnalysisService` and `CorrelationEngine` also do `meal.Items = await store.GetMealItemsAsync(...)` on the same entities. This works but relies on the `List<MealItem>` being assignable to `ICollection<MealItem>`.

### 16. MealLog GetByDateRange — No Pagination

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 232-248
**Severity:** Medium — memory pressure with large date ranges

```csharp
var filter = $"PartitionKey eq '{pk}' and RowKey ge 'MEAL|' and RowKey lt 'MEAL|~'";
var entities = await QueryAsync(filter, ct);
```

Loads ALL meal logs for a user into memory, then filters by date client-side. A user with thousands of meals would cause excessive memory allocation. The filter doesn't restrict by date at the storage level.

### 17. CorrelationEngine — N+1 Query Problem

**File:** `backend/src/GutAI.Infrastructure/Services/CorrelationEngine.cs`
**Lines:** 15-38
**Severity:** Medium — excessive database round-trips

For each meal, for each item with a `FoodProductId`, it makes separate queries:

- `GetFoodProductAsync` per item
- `GetAdditiveIdsForProductAsync` per product
- `GetFoodAdditiveAsync` per additive ID

With 50 meals × 3 items × 2 additives = ~350 separate Table Storage round-trips. Should batch or cache these lookups.

### 18. PersonalizedScoringService — N+1 Query Problem

**File:** `backend/src/GutAI.Infrastructure/Services/PersonalizedScoringService.cs`
**Lines:** 188-195
**Severity:** Medium

Loads all meals in a 90-day range, then for each meal calls `GetMealItemsAsync` individually. Same N+1 issue.

### 19. TableStorageStore — Refresh Token Stored in Plaintext

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 758-769
**Severity:** Medium — security issue

```csharp
{ "Token", token.Token },
```

The refresh token value is stored as plaintext in Table Storage. While the lookup uses a SHA256 hash, the actual token entity stores the raw value. If Table Storage is compromised, all refresh tokens are exposed. Only the hash should be stored.

### 20. UpsertMealItemsAsync — Delete-Then-Reinsert Not Atomic

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 310-329
**Severity:** Medium — data loss window

```csharp
await DeleteMealItemsAsync(userId, mealLogId, ct);
// If crash/timeout occurs here, items are gone
foreach (var item in items)
    await UpsertAsync(e, ct);
```

If the process crashes or cancellation occurs between the delete and the inserts, the meal items are permanently lost. Azure Table Storage supports batch transactions within the same partition — this should use `SubmitTransactionAsync`.

### 21. SetAdditiveIdsForProductAsync — Same Non-Atomic Delete-Reinsert

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 680-693
**Severity:** Medium — same issue as #20

### 22. DeleteRefreshTokensForUserAsync — Sequential Deletes

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 780-795
**Severity:** Medium — slow for users with many tokens

Each token is deleted one at a time with separate round-trips. Should use batch operations.

---

## LOW SEVERITY ISSUES

### 23. RateLimitingMiddleware — Hardcoded Rate Limits

**File:** `backend/src/GutAI.Api/Middleware/RateLimitingMiddleware.cs`
**Lines:** 12-48
**Severity:** Low

All rate limit values are hardcoded:

- `TokenLimit = 100` (line 16)
- `PermitLimit = 20` for auth (line 28)
- `PermitLimit = 30` for search (line 38)

These should come from configuration to allow tuning without redeployment.

### 24. RateLimitingMiddleware — IP-Based Fallback Behind Reverse Proxy

**File:** `backend/src/GutAI.Api/Middleware/RateLimitingMiddleware.cs`
**Lines:** 14, 27, 39
**Severity:** Low-Medium

```csharp
var userId = httpContext.User.FindFirst("sub")?.Value
    ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
```

Behind a reverse proxy (e.g., Docker, NGINX, Azure App Service), `RemoteIpAddress` is always the proxy IP. All unauthenticated requests from different users would share the same rate limit bucket. Need `X-Forwarded-For` header handling.

### 25. ExceptionMiddleware — Missing `TaskCanceledException` Handling

**File:** `backend/src/GutAI.Api/Middleware/ExceptionMiddleware.cs`
**Lines:** 28-33
**Severity:** Low

`TaskCanceledException` (base of `OperationCanceledException`) is handled, but `HttpRequestException`, `TimeoutException`, and `Azure.RequestFailedException` are not. These should return appropriate status codes rather than generic 500.

### 26. ExceptionMiddleware — Logs All Cancellations as Errors

**File:** `backend/src/GutAI.Api/Middleware/ExceptionMiddleware.cs`
**Line:** 26
**Severity:** Low

```csharp
_logger.LogError(ex, "Unhandled exception on {Method} {Path}", ...);
```

`OperationCanceledException` (client disconnected) is logged at `Error` level. This pollutes error logs with noise. Should be `LogInformation` or `LogDebug` for cancellations.

### 27. DependencyInjection — Scoped/Singleton Mismatch for Services Using ITableStore

**File:** `backend/src/GutAI.Infrastructure/DependencyInjection.cs`
**Lines:** 23, 33
**Severity:** Low (works because ITableStore is Singleton)

`ITableStore` is registered as `Singleton`. `ICorrelationEngine` is registered as `Scoped` (line 33) and depends on `ITableStore`. This works because a Scoped service can depend on a Singleton, but not vice versa. Just noting for awareness.

### 28. FoodDiaryAnalysisService — `ITableStore` Passed as Method Parameter

**File:** `backend/src/GutAI.Infrastructure/Services/FoodDiaryAnalysisService.cs`
**Lines:** 13, 100
**Severity:** Low — anti-pattern

```csharp
public async Task<FoodDiaryAnalysisDto> AnalyzeAsync(Guid userId, DateOnly from, DateOnly to, ITableStore store)
```

`ITableStore` is passed as a method parameter instead of being injected via constructor. This breaks DI best practices and makes the method harder to test. Same for `PersonalizedScoringService.ScoreAsync`.

### 29. TableStorageStore — `AdditivesTags` Field on FoodProduct Not Stored (OpenFoodFacts tags)

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 532-557
**Severity:** Low (may be reconstructed from linked additives)

The `FoodProduct` entity doesn't appear to have an `AdditivesTags` property separate from `AllergensTags`. The services (FodmapService, GutRiskService) check `product.AdditivesTags` on `FoodProductDto`, which comes from API responses, not from storage. Products loaded from storage won't have their additive tags populated.

### 30. CorrelationEngine — Magic Number Thresholds

**File:** `backend/src/GutAI.Infrastructure/Services/CorrelationEngine.cs`
**Lines:** 76-77, 109-110
**Severity:** Low

```csharp
var windowStart = symptom.OccurredAt.AddHours(-6);  // Magic number
var windowEnd = symptom.OccurredAt.AddHours(-1);     // Magic number
...
.Where(c => c.Value.mealIds.Count >= 3)              // Magic threshold
```

These are clinical heuristics but should be configurable constants. Different users may have different symptom onset windows.

### 31. PersonalizedScoringService — Magic Number Thresholds

**File:** `backend/src/GutAI.Infrastructure/Services/PersonalizedScoringService.cs`
**Lines:** 199-200
**Severity:** Low

```csharp
var windowStart = symptom.OccurredAt.AddHours(-6);
var windowEnd = symptom.OccurredAt.AddHours(-2);
```

Different window than CorrelationEngine (which uses -6 to -1 hours). This inconsistency means the two services may find different food-symptom associations for the same data.

### 32. TableStorageStore — Single Table for All Entities

**File:** `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs`
**Lines:** 20
**Severity:** Low (but limits scaling)

```csharp
_table = serviceClient.GetTableClient("gutai");
```

All entity types share a single table "gutai". This means:

- Partition key prefixes (USER, FOOD, SYMPTOMTYPE, etc.) share the same partition namespace
- Can't scale partitions independently
- Table-level operations (backup, delete) affect everything

### 33. JwtService — `ValidateToken` Swallows All Exceptions

**File:** `backend/src/GutAI.Infrastructure/Identity/JwtService.cs`
**Lines:** 60-68
**Severity:** Low

```csharp
catch { return null; }
```

All exceptions during token validation are silently swallowed. While returning `null` for invalid tokens is correct, logging at `Debug`/`Warning` level would help diagnose authentication issues.

### 34. ExceptionMiddleware — Missing `NotImplementedException` and `InvalidOperationException` Handling

**File:** `backend/src/GutAI.Api/Middleware/ExceptionMiddleware.cs`
**Lines:** 28-33
**Severity:** Low

`NotImplementedException` should return 501 (Not Implemented). `InvalidOperationException` from business logic (e.g., "can't delete a meal that's already deleted") should return 409 (Conflict) or 422 (Unprocessable Entity) rather than 500.

### 35. Caching — No Cache Invalidation Strategy

**File:** All services
**Severity:** Low-Medium

There is no cache invalidation anywhere in the codebase. The `ICacheService` is injected but:

- When a `FoodProduct` is updated, cached versions are not invalidated
- When user allergies change, cached personalized scores are stale
- No cache-aside pattern is consistently applied

The cache currently has a 24-hour default TTL which prevents stale data from persisting indefinitely, but within that window users may see outdated data.

---

## SUMMARY

| Severity  | Count  | Key Areas                                                          |
| --------- | ------ | ------------------------------------------------------------------ |
| Critical  | 4      | CancellationToken propagation, Lazy<Task> fault caching            |
| High      | 6      | Missing field persistence, full table scan, response started check |
| Medium    | 12     | N+1 queries, plaintext tokens, non-atomic ops, rate limiting       |
| Low       | 13     | Magic numbers, naming, missing exception types, configuration      |
| **Total** | **35** |                                                                    |

### Top 5 Fixes to Prioritize:

1. **Add CancellationToken propagation** to `FoodDiaryAnalysisService` and `PersonalizedScoringService`
2. **Fix Lazy<Task> fault caching** in `TableStorageStore.EnsureTableAsync()` — use `AsyncLazy` with retry
3. **Add `HasStarted` check** in `ExceptionMiddleware` before writing error response
4. **Persist missing FoodProduct fields** (`IsDeleted`, `NutritionInfo`, `FoodProductAdditiveIds`) and `FoodAdditive.SafetyRating`
5. **Fix SearchFoodProductsAsync** to avoid full table scan — consider a search index or at least limit results at query level
