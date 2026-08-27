# GutAI — Project Plan

> **Meal logging · Calorie tracking · Gut health symptom correlation · Food safety insights**
> **.NET 10 Backend + React Native Expo Frontend (iOS / Android / Web)**

> **⚠️ Documentation note:** Sections below this point are the original pre-implementation
> planning document. The build diverged from the plan in a few load-bearing ways the plan
> text was never updated to reflect:
> - **Persistence is Azure Table Storage (`TableStorageStore.cs`), not PostgreSQL 16 / EF Core.**
>   There is no `AppDbContext`, no migrations, no `Configurations/` folder — see
>   `AGENTS.md`'s "Entity ↔ Table Storage Roundtrip" rule, which is the actual governing
>   convention for persistence changes.
> - **Backend unit tests use xUnit + Moq**, not xUnit + NSubstitute.
> Treat the "Tech Stack" and "Architecture" sections below as historical intent, not current
> fact, for anything persistence-related. Everything else (endpoints, correlation engine,
> external API fan-out, frontend structure) matches the shipped implementation reasonably
> closely as of this note.

## Current Implementation Status (2026-07)
Current meal logging supports natural-language parsing, search/scan selection, manual entry, meal notes, local draft recovery, reusable templates, correction tracking, and end-to-end uncertainty disclosure.

### Current evidence architecture

The shipped system now draws a hard line between four different evidence types and surfaces them separately instead of conflating them:

- **Food resolution:** every search/parse path resolves to `Exact` / `Probable` / `Ambiguous` / `Unresolved` with `MatchConfidence`. Zero-overlap queries abstain instead of returning a confident wrong guess.
- **Selection-only persistence:** external candidates are not authoritative products until selected or committed. Repeated selections converge through canonical identity (`barcode → source+externalId → name+brand`) so one real product does not grow duplicate rows.
- **Parsed meal provenance:** parsed items carry independent identity confidence, portion confidence, and nutrition provenance (`Sourced` / `Estimated` / `Unknown`). The frontend review step exposes those signals separately.
- **FODMAP:** rule-based ingredient/name screening with three statuses — `PotentialTriggersDetected`, `NoKnownTriggersDetected`, `InsufficientInformation`. It is intentionally an **ingredient screen**, not a serving-level laboratory classification; unsupported cases remain unknown instead of showing a misleading green “Low FODMAP”. Trigger records dedupe on **canonical Name** (synonym patterns like "wheat flour"/"wheat" share one Name; distinct foods in the same FODMAP class stay individually visible so multi-source load is counted). Single-token ingredient patterns match with plural-tolerant word boundaries ("breaded" ≠ "bread"); free-from claims ("lactose-free") are ignored when negated. Severity ratings derive **by construction** from the canonical `SharedFodmapSeverities` map via `Resolve(key, fallback)` — ~85% of patterns are keyed (composite dishes stay list-local by policy), so drift is unrepresentable rather than merely detected; whole-food name matching tolerates plurals (`Pistachios` matches `pistachio`); and the stacking bonus counts distinct Monash chemistry families parsed from dual-class entries (`Excess Fructose + Sorbitol` = two families). Meal-scan health chips render all three statuses distinctly (warning / no-known-triggers / muted not-enough-info).
- **Product-resolution parity:** every consumer of a stored product (REST detail, MCP tools, Coach chat tools) goes through one shared `FoodProductResolver` that lazy-enriches OFF products missing ingredient text (offline table → barcode API) before assessment — a product's FODMAP screening no longer depends on which surface asked. USDA live search maps the FDC `ingredients` field for branded foods.
- **Gut risk / glycemic / personalized score:** additive screening, glycemic reference matching, and the personalized composite score intentionally remain separate systems. They are allowed to disagree because they represent different evidence; the food detail page notes explicitly that low GI does not imply low FODMAP for fructose/polyol-rich foods. The personalized composite documents its weights in code (FODMAP 35 / additive-only 20 / NOVA 15 / fiber 15 / allergen 15, diagnostic sugar-alcohol component at weight 0, personal-history penalty capped at −25) and shifts emphasis deterministically for declared profiles: FODMAP-sensitivity conditions or a low-fodmap preference move FODMAP to 45% (NOVA/fiber drop to 10%), and celiac/gluten conditions add an ingredients-text gluten scan on top of official allergen data. Profiles without those declarations produce exactly the default weights.
- **Food-symptom analysis:** one shared `FoodSymptomAssociationService` now powers Correlations, Food Diary, Elimination Diet, and the Coach chat. It uses a 1–6 hour onset window, respects explicit `RelatedMealLogId` links first, splits inferred evidence across competing candidate meals instead of double-counting it, compares exposed-vs-baseline symptom rates, and flags co-consumption limitations.
- **Frontend uncertainty propagation:** the UI exposes uncertainty as text, not color alone — e.g. “Multiple possible matches”, “Nutrition estimated”, “Portion assumed”, muted “Not Enough Info” FODMAP badges, baseline percentages on correlation cards, and missing-evidence disclosures.
- **Static dataset governance:** the curated datasets (`FodmapData`, `GutRiskData`, `GlycemicData`) and embedded catalogs (`WholeFoodsDatabase` ~7.2k USDA foods, `BrandedFoodsDatabase` 5k with ingredients, `AustralianFoodsDatabase` — hand-seeded rather than generator-produced) are compile-time snapshots regenerated only via `backend/tools/*Generator` — `scripts/refresh-food-data.sh` and the manual `refresh-food-data` workflow orchestrate downloads; nothing refreshes automatically. Embedded snapshot records currently stamp `DataSource="USDA"` for identity stability with persisted rows; live-API vs snapshot distinction is carried by record provenance fields, not source name. Integrity is test-enforced: dataset pattern lists contain no duplicate entries.

Known evidence limits remain explicit: ingredient order is not dose, proprietary FODMAP concentration thresholds are not embedded, GI reference matches do not establish an exact branded product’s measured GI, and observational food-symptom patterns still do not prove causation.

### AI Coach & Meal Photo Scanning Architecture (2026-08)

The system features two modern, production-hardened AI sub-systems:

#### 1. Conversational Coach (`CoachChatService`)
- **Transport:** Built on `Microsoft.Extensions.AI` (`IChatClient`) over Azure OpenAI **Responses** transport (replaces the retired Assistants API). The Azure client network timeout is configurable and defaults to 300 seconds for reasoning workloads.
- **Tool Calling:** `UseFunctionInvocation` middleware executes the 12 domain tools (`search_foods`, `log_meal`, `get_food_safety`, `get_todays_meals`, etc.) via `AIFunctionFactory` adapters. User identity is captured server-side from JWT claims and is NEVER a model-supplied argument.
- **Current-day and local-range parity:** User-local date requests carry the browser/device IANA timezone when available; the server falls back to the persisted profile timezone, then UTC. All local ranges use DST-aware UTC boundaries while stored/exported timestamps remain UTC instants.
- **Structured tool results (2026-08):** after each tool completes the stream emits `{ tool_result, summary }` where `summary` is a compact typed payload built by `GutAI.Application.Chat.ChatToolSummaries` — `meal_logged {mealType, calories, items[≤3]}`, `meals_today {count, calories}`, `triggers {count, top}`; unknown tools send `summary: null` and clients render a neutral chip. Shapes are locked by `ChatToolSummariesTests` (AGENTS.md #3).
- **State & History:** App-owned conversation history stored in Azure Table Storage (`COACHMSG|` partition rows, inverted-tick ordering) instead of cloud-managed threads.
- **Developer Instructions:** Stable coach instructions live in `CoachPrompts.cs` and are sent as one developer-role message. User profile data is delimited user content, not elevated instructions.

#### 2. Meal Photo Scanning & Grounding Pipeline (`MealScanService`)
- **Stage A (Vision Decomposition):** `POST /api/meals/scan/image` accepts a meal photo (preprocessed via ImageSharp for EXIF stripping, 2000px downscale, JPEG q80) and calls `IChatClient.GetResponseAsync<MealVisionResult>()` with strict schema output and configurable `VisionReasoningEffort` (`none` through `max`). Output carries component names, gram ranges ($low, midpoint, high$), scale notes, and confidence. The LLM **never produces calories or nutrition numbers**.
- **Stage-B Agent Review:** Ambiguous grounding snapshots are exposed to a bounded Agent Framework `ChatClientAgent` through typed read-only tools. The agent may inspect the authoritative candidate snapshot and request one server-capped reanalysis with a different effort. Final selection still passes through `MealScanCandidateSelector`, compatibility checks, and human-review thresholds.
- **Deterministic Semantic Validation:** `MealVisionValidator` checks $low \le midpoint \le high$, caps portions at $\le 5\text{ kg}$, enforces component count limits, and drops invalid items.
- **Stage B (Database Grounding):** `ComponentGroundingEngine` resolves each component through `IFoodSearchService.ResolveAsync`. Auto-select occurs ONLY when status is `Exact` or `Probable` AND `MatchConfidence >= 0.85`. Ambiguous items abstain to human review with top-3 candidates exposed. Quantities attach to the detected component, never database default servings.
- **Stage B3 (Zero-Cost Web Cascade):** `WebNutritionCascade` runs for unresolved items behind `Features:WebGrounding` (Table cache $\rightarrow$ DuckDuckGo HTML search $\rightarrow$ Jina Reader markdown $\rightarrow$ LLM extraction $\rightarrow$ physiological plausibility gate $\rightarrow$ cache write).
- **Stage C & Health Signals:** Macros are computed deterministically from verified per-100g database values $\times$ grams. `MealScanHealthSignals` enriches grounded items with FODMAP status/triggers and gut ratings. Web-scraped and AI-estimated items physically cannot receive FODMAP signals.
- **Frontend Review Sheet:** `MealScanReviewSheet.tsx` provides an interactive bottom sheet with confidence badges, provenance chips (`USDA`, `OFF`, `AU`, `Web ↗`, `AI`), FODMAP/gut badges, portion steppers with live macro scaling, 1-tap candidate swapper, and meal type logging.
- **Regression Gating:** `backend/tools/GoldenScanHarness` tests Stage A across a golden test suite of meal photos (`golden-images/manifest.json`), reporting component recall, gram error, and token usage with an automated `--gate` exit rule.

### External AI-consumer access via pairing-code linking (2026-08)

External AI apps (Claude Desktop, Cursor, any MCP client) connect to GutAI through the
Streamable HTTP MCP endpoint at the pinned route **`/mcp`** (the SDK's default route is
site root — `app.MapMcp("/mcp")` makes it explicit) without ever handling a user password:

- **Pairing flow:** the user generates a single-use code in-app (`POST /api/user/pairing-codes`,
  `PairingService`); the assistant calls the anonymous MCP tool `gutai_link_account(code)`,
  which redeems it for a read-only `PersonalAccessToken` (`gutai_pat_…`). Plaintext exists
  exactly once — only SHA-256 hashes are stored (`PLOOKUP`/`PATLOOKUP` hash rows mirror the
  refresh-token pattern).
- **Auth surface:** one policy scheme (`MultiAuth`) forwards `gutai_pat_`-prefixed
  Authorization headers to `PatAuthenticationHandler` and everything else to JwtBearer.
  PATs emit `sub`, `email`, `token_type=pat`, and per-scope `scope` claims, so all existing
  tools resolve identity unchanged. PATs are valid **only on `/mcp`** — a leaked pairing
  token cannot drive the REST API.
- **Per-tool authorization:** `/mcp` has NO route-level authorization;
  `AddAuthorizationFilters` enforces `[Authorize]` on every data tool while
  `gutai_link_account` is `[AllowAnonymous]`. Mutating tools (`gutai_log_meal`,
  `gutai_log_symptom`) additionally call `McpAccess.EnsureWrite(user)` — PAT links are
  read-only today; JWT sessions keep full access.
- **Tool parameter contract (hard-won, verified end-to-end by `McpLinkFlowTests`):**
  SDK 1.2.0 binds ONLY these request-context parameter types: `ClaimsPrincipal`,
  `McpServer`, `RequestContext<CallToolRequestParams>`, `IProgress<ProgressNotificationValue>`,
  and `CancellationToken`. An `HttpContext` parameter silently becomes a required JSON
  argument ("missing required parameter 'httpContext'") — every tool that used one was
  broken over real transports until converted to `ClaimsPrincipal`. Optional tool parameters
  MUST carry explicit defaults (`= null` / `= default`), otherwise M.E.AI marks them
  required in the schema and rejects calls that omit them.
- **Rate limiting:** dedicated `mcp` policy — 120/min token bucket per user, 20/min fixed
  window per IP for unauthenticated traffic (the pairing tool is the brute-force surface;
  codes add a 5-attempt burn-in on top).
- **Lifecycle:** users list/revoke connections at `/api/user/tokens`; account deletion
  removes tokens and pairing codes. `LastUsedAt` is throttled to one write per minute per
  token to bound table-write amplification from chatty agents.

Guardrail (AGENTS.md #10): every future MCP tool MUST carry `[Authorize]` unless it is an
explicit link/auth exception, MUST take `ClaimsPrincipal?` (never `HttpContext`) for
identity, and MUST call `McpAccess.EnsureWrite` before any side effect.

### Health-platform import/export (2026-08)

GutAI exchanges meal data with Apple HealthKit and Google Health Connect through a
device-side abstraction, not a third-party aggregator:

- **HealthBridge (`frontend/src/services/health/`)** — one TypeScript interface
  (`isAvailable` / `requestPermissions` / `readNutrition` / `writeMeal` / `deleteMeal`)
  with platform files over per-platform libraries: `react-native-health-connect`
  (Android) and `@kingstinct/react-native-healthkit` (iOS, Nitro module — swapped in
  because the legacy ObjC `react-native-health` bridge cannot return saved UUIDs,
  cannot delete correlations, and hides sample sources, which made correct
  write/update/delete semantics impossible). No unified wrapper library — pairing the
  two best-of-breed platform bridges plus a thin shared interface is the settled
  industry pattern.
- **Import path:** incremental watermark reads → normalized rows → `POST /api/meals/import`
  (JWT, ≤2000 items/request). The endpoint derives meal type from local hour when absent,
  clamps values via `MealValidation`, persists with `NutritionProvenance="Estimated"`
  (imported nutrition is never treated as verified), and dedupes on
  `(ExternalSource, ExternalId)` — re-imports are idempotent. Name-only rows still feed
  trigger tracking via normalized-name grouping.
  Android reads follow Health Connect pagination tokens so windows larger than the
  native 1000-record default page are never silently truncated.
- **Export path:** opt-in per platform (default OFF, Apple 5.1.3-compliant contextual
  permission request). Meal create/update/delete hooks fire after successful API commits;
  failures never block the meal flow. Android writes use HC `clientRecordId = mealId`
  (upsert semantics); iOS tracks saved sample UUIDs locally for deletes.
- **Availability honesty:** `SDK_UNAVAILABLE_PROVIDER_UPDATE_REQUIRED` is reported as
  "Google Health Connect needs an update" with a deep link to its Play Store listing,
  never mislabeled as "not installed".
- **Dual-direction safety:** when reading, records whose originating app is GutAI itself
  are skipped (HC `metadata.dataOrigin`; HealthKit source bundle id). Without this filter,
  enabling both directions duplicates every meal once per sync cycle.
- **Fidelity honesty:** Health Connect carries real meals (name + mealType); HealthKit is
  sample-shaped — the iOS bridge prefers food correlations and otherwise clusters macro
  samples into meal windows, yielding coarser meals. Imported rows display estimated
  provenance in the existing evidence UI.
- **Store declarations:** Play Console health-apps declaration for READ/WRITE_NUTRITION
  (moved into Play Console proper as of 2026-09-03; expect ≥7-day review + propagation);
  iOS needs the HealthKit entitlement plus NSHealthShareUsageDescription /
  NSHealthUpdateUsageDescription and passes App Review 5.1.3.
- **Device verification (manual, requires EAS dev build):** health libraries carry native
  code, so this surface cannot be exercised by unit/contract suites. Release gate:
  ① `eas build --profile development` on both platforms; ② Health Connect Toolbox app
  seeded with Nutrition records → import pulls them with `Estimated` provenance and
  correct meal-type derivation; ③ log a GutAI meal with export enabled → record appears
  in HC/Apple Health attributed to GutAI; ④ delete that meal → platform record removed;
  ⑤ enable BOTH directions → run import twice → zero duplicate meals (self-origin filter);
  ⑥ revoke permission at OS level → app surfaces unavailable state without crashing.

### Verification hardening (Phase 7 closeout)

A gap audit of the behavioral test suite found the tests themselves were the weak point in
several places — not the runtime behavior. Each was closed with a real fix, not a relaxed
assertion:

- **Search regression suite tested a fake pipeline.** The integration search-quality suite
  used to hand-reimplement canonicalization/ranking/persistence instead of calling
  `FoodEndpoints.SearchFoodProducts`, so it could pass while the real endpoint was broken. It
  now calls the real handler directly (embedded/offline providers only, for determinism — no
  live OFF/USDA network calls in tests) and exercises the actual
  `FoodCandidateCanonicalizer` → `IFoodRanker` → `FoodProductPersistence.ResolveOrPersistAsync`
  chain.
- **Match-confidence was computed but discarded before it reached association evidence.**
  `ParsedFoodItemDto.MatchConfidence` was shown in the meal-review UI, then silently dropped
  when the request was built for `POST /api/meals` — `CreateMealItemRequest` had nowhere to
  carry it. It is now threaded end to end (`CreateMealItemRequest` → `MealItem` →
  `FoodSymptomAssociationService`), and a food/symptom association whose contributing meals
  have low average identity confidence (`< 0.6`, the same threshold the frontend uses for its
  "Estimated match" warning) is capped at "Low" confidence regardless of how strong the raw
  occurrence/rate statistics look.
- **Parser corpus gaps.** Added coverage for ambiguous top-two-candidate resolution
  (`FoodResolutionStatus.Ambiguous` still returns usable sourced nutrition, not a withheld
  result), multi-word regional dish names, and preparation-modifier prefixes ("grilled",
  "steamed", "roasted").
- **MCP/chat projection parity was architecturally true but unverified.** `GetTriggerFoods`
  and `GetEliminationDietStatus` already called the same `ICorrelationEngine`/
  `IFoodDiaryAnalysisService` instances Insights/Food Diary use; this is now locked in with a
  same-seeded-data behavioral comparison instead of relying on code inspection.
- **Niche unresolved foods** (no catalog match, no `FoodProductId`) are proven to still group
  correctly across meals by normalized name, so a resolution failure never fragments or
  excludes a food from trigger tracking.

### Backend — Fully Implemented ✅

| Component              | Status | Details                                                                                                        |
| ---------------------- | ------ | -------------------------------------------------------------------------------------------------------------- |
| **Auth**               | ✅     | Register (with transaction safety), Login, Refresh token rotation, Logout, **Change Password**                 |
| **Meal Endpoints**     | ✅     | CRUD, natural language parsing, daily summary, **data export**, negative value validation                      |
| **Insight Endpoints**    | ✅     | Correlations, nutrition trends, additive exposure, food diary analysis, elimination-diet status — cached projections over the shared evidence engine |
| **User Endpoints**       | ✅     | Profile CRUD, goals, alerts watchlist, **account deletion**                                                    |
| **Middleware**           | ✅     | ExceptionMiddleware (ProblemDetails), RateLimiting (3 policies), Serilog request logging                       |
| **External APIs**        | ✅     | Composite food fan-out (OFF + USDA + embedded providers), CalorieNinjas — OFF has full Polly resilience (retry + circuit breaker); USDA runs on a typed HttpClient (8s timeout); embedded catalogs are local |
| **Caching**              | ✅     | In-memory cache for projections/history lookups                                                                 |
| **Security**             | ✅     | JWT (no hardcoded fallback — throws on missing secret), Identity, rate limiting, input validation              |
| **Database**             | ✅     | Azure Table Storage via `TableStorageStore` — single-table entity persistence with explicit `Upsert`/`MapTo` roundtrip rules |
| **Symptom Association**  | ✅     | Shared `FoodSymptomAssociationService` (1-6h onset window, exposed-vs-baseline rate contrast, `RelatedMealLogId` precedence, co-consumption caveats) — powers Correlations, Food Diary, and Elimination Diet identically |
| **Health Checks**      | ✅     | Health endpoints                                                                                               |

### Frontend — Fully Implemented ✅

| Screen             | Status | Details                                                                                                  |
| ------------------ | ------ | -------------------------------------------------------------------------------------------------------- |
| **Dashboard**      | ✅     | Calorie progress ring, macro bars, today's meals/symptoms, date navigation                               |
| **Meals**          | ✅     | Manual entry (with negative validation), natural language parsing, edit/delete, date navigation          |
| **Symptoms**       | ✅     | Type selection, severity picker, notes, meal linking, edit/delete, date navigation                       |
| **Scan**           | ✅     | Barcode input + camera, food search, safety report, add-to-meal, **product images**                      |
| **Insights**       | ✅     | Nutrition trends, additive exposure, correlations (shared utils), symptom timeline                       |
| **Profile**        | ✅     | User info, daily goals, alerts, correlations preview, **settings link**                                  |
| **Food Detail**    | ✅     | Safety badges, nutrition, ingredients, allergens, additives, **product images**, add-to-meal             |
| **Login/Register** | ✅     | **Email validation**, password visibility toggle, proper error typing (`catch (e: unknown)`)             |
| **Onboarding**     | ✅     | 4-step wizard with **goal validation** (calorie range 1-10000, no negatives)                             |
| **Settings**       | ✅     | Change password, data export, app info, **account deletion** (danger zone)                               |
| **Components**     | ✅     | ErrorBoundary, ErrorState, SkeletonLoader, **EmptyState**, Toast, **CountUpText**, **CelebrationOverlay**, **ShareCardPortal + share/ templates (off-screen composed cards → PNG via react-native-view-shot → expo-sharing; brand-fixed palette `shareCardColors`)**, pull-to-refresh. Motion rule: celebratory/entering animation gates on `useReducedMotion()` |
| **Illustrations** | ✅     | unDraw SVGs recolored to brand (`assets/onboarding/`), inlined at build time via `react-native-svg-transformer` (see `metro.config.js`, `svg.d.ts`). Onboarding steps + meals empty state use them; `EmptyState` takes an optional `illustration` node. Brand sweep: all user-facing "GutLens" copy is now "GutAI" |
| **Store Artwork** | ✅     | Real UI captures in `assets/store/captures/raw/`; ChatGPT-generated backdrop assets in `assets/store/backgrounds/`; reproducible ImageMagick compositor at `frontend/scripts/compose-store-art.sh` emits Apple phone (1290×2796), Apple iPad (2048×2732), and Google phone (1080×1920) sets under `assets/store/final/`. Captions are composited outside untouched UI pixels. |
| **API Layer**      | ✅     | All endpoints including **changePassword, export, deleteAccount**                                        |
| **Retention**     | ✅     | Local-only reminders (`src/utils/notifications.ts`): opt-in daily logging reminder + one-shot evening streak nudge resynced on foreground/meal-log (no server push, no tokens — see privacy §2). Settings → Reminders with contextual permission priming. Dashboard "Your consistency" 28-day calendar (`StreakCalendar`). Requires the EAS dev build carrying `expo-notifications` |
| **Types**          | ✅     | All DTOs typed including **servingWeightG, foodProductId, ChangePasswordRequest, DataExport**            |

### Infrastructure ✅

| Component             | Status | Details                                                        |
| --------------------- | ------ | -------------------------------------------------------------- |
| **Docker Compose**    | ✅     | API + Azurite + Seq (log viewer), health checks, develop.watch |
| **Makefile**          | ✅     | up/down/nuke/logs/test/build/migrate/fresh targets             |
| **GitHub Actions CI** | ✅     | Backend build/lint, frontend type check, Docker build          |
| **EditorConfig**      | ✅     | Consistent formatting rules (2-space TS, 4-space C#)           |
| **VS Code Config**    | ✅     | settings.json + extensions.json with recommended extensions    |

### Middleware Pipeline (Order)

```
Request → ExceptionMiddleware → SerilogRequestLogging → CORS → RateLimiter → Auth → Authorization → Endpoints
```

### Rate Limiting Policies

| Policy          | Type           | Limit         |
| --------------- | -------------- | ------------- |
| `authenticated` | Token bucket   | 100/min       |
| `auth`          | Fixed window   | 20/min per-IP |
| `search`        | Sliding window | 30/min        |

### In-Memory Cache Strategy

- Keys: `{type}:{userId}:{fromDate}:{toDate}`
- TTL: 15min (correlations), 10min (trends/exposure)
- Invalidation: On meal create/update/delete — clears 7/14/30-day windows

### External API Resilience

- OpenFoodFacts client: Polly standard resilience (1 retry, 8s attempt / 12s total budget, circuit breaker) via typed HttpClient (12s timeout)
- USDA client: typed HttpClient with 8s timeout and User-Agent (no Polly); bounded by the search endpoint's 10s overall CTS
- API key guards: USDA + CalorieNinjas skip if keys empty (graceful degradation)

---

## 1. Project Overview & Vision

**GutAI** helps users understand what they eat and how it affects their body. Three pillars:

1. **Meal Logging & Calorie Tracking** — Log meals via natural language ("ate 2 eggs and toast"), barcode scan, or manual search. Track calories, macros, and micros against personalized daily goals.
2. **Gut Health Symptom Tracking** — Log symptoms (bloating, cramping, brain fog, skin issues, etc.) with severity and timing. The app correlates symptoms with recently consumed foods and additives to surface patterns over time.
3. **Food Safety Insights** — Scan any product barcode for an instant safety report: flagged additives (Red 40, BHT, sodium nitrite, titanium dioxide), CSPI safety ratings, EU vs US regulatory status, NOVA ultra-processing score, Nutri-Score, and health concern summaries.

### Target Users

- Health-conscious consumers wanting to avoid harmful additives
- People with IBS, food sensitivities, or unexplained gut issues
- Parents wanting to screen foods for children (dyes, preservatives)
- Anyone tracking calories/macros for fitness or weight goals

### Key Differentiators

- **Additive safety scoring** combining CSPI + EFSA + EU ban status + FDA adverse event data — no other consumer app does this
- **Symptom-food correlation engine** that learns from user data over time
- **US vs EU regulatory comparison** — highlights additives banned in EU but still allowed in US
- **Single app** unifying calorie tracking + food safety + gut health (competitors only do 1-2 of these)

---

## 2. Tech Stack

### Backend

| Layer            | Technology                                |
| ---------------- | ----------------------------------------- |
| Runtime          | .NET 10 (C# 14)                           |
| Framework        | ASP.NET Core Minimal APIs                 |
| ORM              | None — Azure.Data.Tables SDK directly     |
| Database         | Azure Table Storage (see doc note above)  |
| Cache            | In-memory (IDistributedCache)             |
| Auth             | ASP.NET Core Identity + JWT Bearer tokens |
| API Docs         | Scalar                                    |
| Logging          | Serilog → Seq                             |
| Testing          | xUnit + Moq + Testcontainers (Azurite)    |
| Containerization | Docker + Docker Compose                   |

### Frontend

| Layer          | Technology                          |
| -------------- | ----------------------------------- |
| Framework      | React Native 0.81+ with Expo SDK 54 |
| Navigation     | Expo Router v6 (file-based)         |
| Charts          | victory-native v42 (ex-"Victory Native XL") on @shopify/react-native-skia ^2.11.1 for native; platform-specific `.web.tsx` RN fallbacks keep Expo web export functional without CanvasKit. Axis labels require bundled fonts (`@expo-google-fonts/inter` → `useChartFonts`). |
| Camera/Barcode  | expo-camera                         |
| Server State    | TanStack Query v5 (React Query)     |
| Client State    | Zustand                             |
| Storage         | expo-secure-store (tokens)          |
| HTTP           | Axios with interceptors             |
| Testing        | Jest + React Native Testing Library |

### Infrastructure

| Concern         | Tool                                       |
| --------------- | ------------------------------------------ |
| Local Dev       | Docker Compose (API + Azurite + Seq)       |
| CI/CD           | GitHub Actions                             |
| Backend Hosting | Fly.io or Railway                          |
| Mobile Builds   | Expo EAS Build + EAS Submit                |
| Monitoring      | Sentry (errors), Expo Insights (analytics) |

---

## 3. Architecture

### Backend — Clean Architecture

```
backend/
├── src/
│   ├── GutAI.Api/                       # Presentation layer
│   │   ├── Endpoints/                   # Minimal API endpoint groups
│   │   │   ├── AuthEndpoints.cs
│   │   │   ├── MealEndpoints.cs
│   │   │   ├── FoodEndpoints.cs
│   │   │   ├── SymptomEndpoints.cs
│   │   │   ├── InsightEndpoints.cs
│   │   │   └── UserEndpoints.cs
│   │   ├── Middleware/
│   │   │   ├── ExceptionMiddleware.cs
│   │   │   └── RateLimitingMiddleware.cs
│   │   ├── appsettings.json
│   │   └── Program.cs
│   │
│   ├── GutAI.Application/              # Use cases & business logic
│   │   ├── Common/
│   │   │   ├── Interfaces/
│   │   │   │   ├── IFoodApiService.cs
│   │   │   │   ├── INutritionApiService.cs
│   │   │   │   ├── ICacheService.cs
│   │   │   │   └── ICorrelationEngine.cs
│   │   │   ├── DTOs/
│   │   │   └── Mappings/
│   │   ├── Meals/
│   │   │   ├── Commands/               # LogMeal, UpdateMeal, DeleteMeal
│   │   │   ├── Queries/                # GetDailySummary, GetMealHistory
│   │   │   └── Services/MealService.cs
│   │   ├── Foods/
│   │   │   ├── Commands/               # CacheFoodProduct
│   │   │   ├── Queries/                # SearchFood, GetSafetyReport, LookupBarcode
│   │   │   └── Services/FoodSafetyService.cs
│   │   ├── Symptoms/
│   │   │   ├── Commands/               # LogSymptom, UpdateSymptom
│   │   │   ├── Queries/                # GetSymptomHistory, GetSymptomTypes
│   │   │   └── Services/SymptomService.cs
│   │   ├── Insights/
│   │   │   ├── Queries/                # GetCorrelations, GetWeeklyReport
│   │   │   └── Services/CorrelationEngine.cs
│   │   └── Users/
│   │       ├── Commands/               # UpdateProfile, SetGoals, ConfigAlerts
│   │       └── Queries/                # GetProfile
│   │
│   ├── GutAI.Domain/                   # Core domain (zero dependencies)
│   │   ├── Entities/
│   │   │   ├── User.cs
│   │   │   ├── MealLog.cs
│   │   │   ├── MealItem.cs
│   │   │   ├── FoodProduct.cs
│   │   │   ├── FoodAdditive.cs
│   │   │   ├── SymptomLog.cs
│   │   │   ├── SymptomType.cs
│   │   │   ├── DailyNutritionSummary.cs
│   │   │   ├── UserFoodAlert.cs
│   │   │   └── InsightReport.cs
│   │   ├── Enums/
│   │   │   ├── MealType.cs             # Breakfast, Lunch, Dinner, Snack
│   │   │   ├── SafetyRating.cs         # Safe, CutBack, Caution, Avoid
│   │   │   ├── RegulatoryStatus.cs     # Approved, Restricted, Banned
│   │   │   ├── SymptomSeverity.cs
│   │   │   └── NovaGroup.cs            # 1-4
│   │   └── ValueObjects/
│   │       ├── NutritionInfo.cs        # Calories, Protein, Carbs, Fat, etc.
│   │       └── ServingSize.cs
│   │
│   └── GutAI.Infrastructure/           # External concerns
│       ├── Data/
│       │   ├── AppDbContext.cs
│       │   ├── Migrations/
│       │   └── Configurations/         # EF entity configs
│       ├── ExternalApis/
│       │   ├── OpenFoodFactsClient.cs
│       │   ├── UsdaFoodDataClient.cs
│       │   ├── CompositeFoodApiService.cs
│       │   ├── CalorieNinjasClient.cs
│       │   ├── OpenFdaClient.cs
│       │   ├── EdamamClient.cs
│       │   └── SpoonacularClient.cs
│       ├── Identity/
│       │   ├── JwtService.cs
│       │   └── AuthService.cs
│       ├── Caching/
│       │   └── InMemoryCacheService.cs
│       └── BackgroundJobs/
│           ├── AdditiveDbRefreshJob.cs
│           └── WeeklyReportJob.cs
│
├── tests/
│   ├── GutAI.UnitTests/
│   └── GutAI.IntegrationTests/
│
├── docker-compose.yml
├── Dockerfile
└── GutAI.sln
```

### Frontend — Expo Router File-Based

```
frontend/
├── app/
│   ├── _layout.tsx                      # Root layout (providers, fonts, auth guard)
│   ├── (auth)/
│   │   ├── _layout.tsx
│   │   ├── login.tsx
│   │   ├── register.tsx
│   │   └── onboarding.tsx               # Goals, allergies, watchlist setup
│   ├── (tabs)/
│   │   ├── _layout.tsx                  # Bottom tab navigator (5 tabs)
│   │   ├── index.tsx                    # 🏠 Home / Dashboard
│   │   ├── log.tsx                      # 🍽️ Log Meal
│   │   ├── scan.tsx                     # 📷 Barcode Scanner
│   │   ├── symptoms.tsx                 # 🩺 Symptom Logger
│   │   └── profile.tsx                  # 👤 Profile & Settings
│   ├── food/
│   │   ├── [id].tsx                     # Food detail / safety report
│   │   └── search.tsx                   # Food search results
│   ├── insights/
│   │   ├── index.tsx                    # Insights dashboard
│   │   ├── correlations.tsx             # Symptom-food correlation details
│   │   └── report/[period].tsx          # Weekly/monthly report
│   └── meals/
│       └── [id].tsx                     # Meal detail / edit
├── components/
│   ├── ui/                              # Button, Card, Input, Modal, Badge, Skeleton
│   ├── meals/                           # MealCard, MealItemRow, NutritionRing, MacroBar
│   ├── food/                            # SafetyBadge, AdditiveRow, NutriScoreBadge, NovaGroup
│   ├── symptoms/                        # SymptomButton, SeveritySlider, SymptomCalendar
│   └── charts/                          # CalorieRingChart, MacroChart, SymptomTimeline, TrendLine
├── hooks/
│   ├── useAuth.ts
│   ├── useMeals.ts                      # TanStack Query hooks for meals
│   ├── useFood.ts
│   ├── useSymptoms.ts
│   └── useInsights.ts
├── services/
│   ├── api.ts                           # Axios instance with auth interceptor
│   ├── authService.ts
│   ├── mealService.ts
│   ├── foodService.ts
│   ├── symptomService.ts
│   └── insightService.ts
├── stores/
│   ├── authStore.ts                     # Zustand: user, tokens
│   └── appStore.ts                      # Zustand: theme, onboarding state
├── types/
│   ├── meal.ts
│   ├── food.ts
│   ├── symptom.ts
│   ├── insight.ts
│   └── user.ts
├── constants/
│   ├── colors.ts
│   └── symptomTypes.ts
├── utils/
│   ├── formatters.ts
│   └── dateHelpers.ts
├── assets/
├── app.json
├── tailwind.config.js
├── tsconfig.json
└── package.json
```

---

## 4. API Integration Strategy

### Tier 1 — Core (Always Used)

#### Open Food Facts (FREE — Primary barcode/product lookup)

- **Used for**: Barcode scan → product name, brand, ingredients list, additives tags, NOVA group, Nutri-Score, nutrition facts, allergens
- **Endpoint**: `GET https://world.openfoodfacts.org/api/v2/product/{barcode}`
- **Key fields**: `product_name`, `ingredients_text`, `additives_tags`, `nova_group`, `nutriscore_grade`, `nutriments`, `allergens_tags`
- **Caching**: Cache product data in PostgreSQL with **24-hour TTL**. On cache miss, fetch from API and store.
- **Rate limit handling**: 100 req/min reads, 10 req/min search — enforce via Polly rate-limit policy
- **Integration point**: `OpenFoodFactsClient.cs` → called by `FoodSafetyService.cs`

#### USDA FoodData Central (FREE — Nutrient composition enrichment)

- **Used for**: Detailed nutrient data (150+ nutrients) when Open Food Facts data is insufficient, or for generic/unbranded foods
- **Endpoint**: `GET https://api.nal.usda.gov/fdc/v1/foods/search?query={food}&api_key={key}`
- **Key fields**: `fdcId`, `description`, `foodNutrients[]` (energy, protein, fat, carbs, fiber, sugar, sodium, vitamins, minerals)
- **Caching**: Cache nutrient profiles with **7-day TTL** (nutrient data rarely changes)
- **Rate limit**: 1,000 req/hr — use Polly circuit breaker
- **Integration point**: `UsdaFoodDataClient.cs` → fallback/enrichment for meal logging

#### CalorieNinjas (FREE tier — NLP meal logging)

- **Used for**: Natural language meal input parsing. User types "ate a chicken burrito and a coke" → API returns structured per-item nutrition (calories, protein, carbs, fat, etc.)
- **Endpoint**: `GET https://api.calorieninjas.com/v1/nutrition?query={text}`
- **Response**: Array of items, each with `name`, `calories`, `protein_g`, `carbohydrates_total_g`, `fat_total_g`, `fiber_g`, `sugar_g`, `sodium_mg`, `cholesterol_mg`
- **Caching**: Cache query → result pairs with **24-hour TTL**
- **Integration point**: `CalorieNinjasClient.cs` → called from `POST /api/meals/log-natural` endpoint

#### CSPI Chemical Cuisine (Static Import — Additive safety ratings)

- **Used for**: The core additive safety rating system. Each additive gets a CSPI rating: Safe / Cut Back / Caution / Avoid / Certain People Should Avoid
- **Data source**: Scrape/manually build from https://www.cspinet.org/eating-healthy/chemical-cuisine
- **Storage**: Static seed data in `FoodAdditives` table. Fields: `Name`, `ENumber`, `CspiRating` (enum), `HealthConcerns` (text), `Description`
- **Refresh**: Manual quarterly review for new CSPI updates (Hangfire reminder job)
- **Key additives to seed**: Red 40, Yellow 5, Yellow 6, Blue 1, Green 3, Red 3, BHA, BHT, TBHQ, sodium nitrite, sodium nitrate, potassium bromate, brominated vegetable oil, propylparaben, titanium dioxide, carrageenan, aspartame, sucralose, saccharin, acesulfame-K, caramel color (4-MEI), partially hydrogenated oils, mycoprotein, and ~100 more

### Tier 2 — Safety Enrichment (Static Imports + Periodic Refresh)

#### EFSA / OpenEFSA (EU additive safety data)

- **Used for**: EU scientific safety opinions, Acceptable Daily Intake (ADI) values, and re-evaluation status for each additive
- **Data source**: Download from https://open.efsa.europa.eu/ — structured scientific output data
- **Storage**: Enrich `FoodAdditives` table with `EfsaAdi`, `EfsaStatus`, `EfsaLastReviewDate`
- **Highlight**: Titanium dioxide (E171) banned in EU 2022, still allowed in US — this is a key talking point in the app
- **Refresh**: Hangfire monthly job to check for new EFSA opinions

#### EC Food Additives Database (EU regulatory status)

- **Used for**: Cross-referencing what's authorized/restricted/banned in EU vs allowed in US
- **Storage**: `FoodAdditives` table fields: `UsStatus` (Approved/Restricted/Banned), `EuStatus` (Approved/Restricted/Banned)
- **Key value**: "This additive is **banned in the EU** but still permitted in the US" — powerful for user trust

#### EPA IRIS (Toxicological reference)

- **Used for**: Reference Doses (RfD) and cancer classifications for food-relevant chemicals
- **Storage**: Enrich `FoodAdditives` with `EpaRfd`, `EpaCancerClassification` where applicable
- **Scope**: Only ~20-30 food-relevant chemicals in IRIS — small static import

#### EWG Food Scores (Ingredient hazard reference)

- **Used for**: Product-level ingredient concern scoring as secondary validation
- **Data source**: Manual reference from https://www.ewg.org/foodscores
- **Usage**: Inform our composite safety score algorithm, not direct data import

#### openFDA Food APIs (Adverse events & recalls)

- **Used for**: Querying adverse event reports and recalls related to specific additives
- **Endpoints**:
  - `GET https://api.fda.gov/food/event.json?search=products.industry_name:"Red+40"` — adverse events
  - `GET https://api.fda.gov/food/enforcement.json?search=reason_description:"red+40"` — recalls
- **Integration**: Background job queries openFDA weekly for additives in our DB, stores `FdaAdverseEventCount` and `FdaRecallCount` per additive
- **Caching**: Results stored in DB, refreshed weekly by Hangfire job

### Tier 3 — Premium/Optional (Freemium APIs — add as budget allows)

#### Edamam (Recipe nutrition analysis + food DB)

- **Used for**: Recipe text → full nutrition breakdown, broader food database search (900K foods)
- **Free tier**: Limited requests/day — use as enrichment, not primary
- **Future**: Meal planning feature could leverage their Meal Planner API

#### Spoonacular (Allergen detection, diet classification)

- **Used for**: Diet classification (vegan, paleo, keto, Whole30), allergen detection, recipe search
- **Free tier**: ~150 requests/day — use for diet/allergen tagging on food products

#### Nutritionix (Premium NLP food logging)

- **Used for**: Upgrade from CalorieNinjas if premium NLP accuracy needed. Superior branded food database.
- **Cost**: Paid/enterprise — defer to Phase 5+ if revenue supports it

---

## 5. Database Schema

### Entity Relationship Overview

```
User ──1:M──> MealLog ──1:M──> MealItem
User ──1:M──> SymptomLog ──M:1──> SymptomType
User ──1:M──> DailyNutritionSummary
User ──1:M──> UserFoodAlert ──M:1──> FoodAdditive
User ──1:M──> InsightReport
FoodProduct ──M:M──> FoodAdditive (via FoodProductAdditive)
MealItem ──M:1──> FoodProduct (optional)
MealLog ──1:M──> SymptomLog (optional association)
```

### Entities Detail

#### Users

```
Id                  : Guid (PK)
Email               : string (unique, required)
PasswordHash        : string
DisplayName         : string
CreatedAt           : DateTime
DailyCalorieGoal    : int (default 2000)
DailyProteinGoalG   : int (default 50)
DailyCarbGoalG      : int (default 250)
DailyFatGoalG       : int (default 65)
DailyFiberGoalG     : int (default 25)
Allergies           : string[] (e.g., ["dairy", "gluten", "shellfish"])
DietaryPreferences  : string[] (e.g., ["vegetarian", "low-fodmap"])
OnboardingCompleted : bool
```

#### MealLogs

```
Id                  : Guid (PK)
UserId              : Guid (FK → Users)
MealType            : enum (Breakfast=0, Lunch=1, Dinner=2, Snack=3)
LoggedAt            : DateTime
Notes               : string?
PhotoUrl            : string?
TotalCalories       : decimal (computed)
TotalProteinG       : decimal (computed)
TotalCarbsG         : decimal (computed)
TotalFatG           : decimal (computed)
OriginalText        : string? (raw NLP input text, e.g., "2 eggs and toast")
```

#### MealItems

```
Id                  : Guid (PK)
MealLogId           : Guid (FK → MealLogs)
FoodProductId       : Guid? (FK → FoodProducts, null for NLP-parsed generic items)
FoodName            : string (display name)
Barcode             : string?
Servings            : decimal (default 1.0)
ServingUnit         : string (e.g., "g", "oz", "cup", "piece")
ServingWeightG      : decimal?
Calories            : decimal
ProteinG            : decimal
CarbsG              : decimal
FatG                : decimal
FiberG              : decimal
SugarG              : decimal
SodiumMg            : decimal
CholesterolMg       : decimal
SaturatedFatG       : decimal
PotassiumMg         : decimal
```

#### FoodProducts (cached from APIs)

```
Id                  : Guid (PK)
Barcode             : string? (unique index)
Name                : string
Brand               : string?
IngredientsText     : string?
ImageUrl            : string?
NovaGroup           : int? (1-4)
NutriScore          : string? ("a"-"e")
AllergensTags       : string[]
Calories100g        : decimal?
Protein100g         : decimal?
Carbs100g           : decimal?
Fat100g             : decimal?
Fiber100g           : decimal?
Sugar100g           : decimal?
Sodium100g          : decimal?
DataSource          : string ("OpenFoodFacts", "USDA", "Manual")
ExternalId          : string? (OpenFoodFacts code or USDA fdcId)
CachedAt            : DateTime
CacheTtlHours       : int (default 24)
SafetyScore         : int? (0-100 computed composite)
SafetyRating        : enum (Safe=0, Caution=1, Warning=2, Avoid=3)
```

#### FoodAdditives

```
Id                  : int (PK)
ENumber             : string? (e.g., "E129")
Name                : string (e.g., "Red 40 / Allura Red AC")
AlternateNames      : string[] (e.g., ["FD&C Red No. 40", "CI 16035", "Allura Red"])
Category            : string (e.g., "Color", "Preservative", "Sweetener", "Emulsifier")
CspiRating          : enum (Safe=0, CutBack=1, Caution=2, CertainPeopleShouldAvoid=3, Avoid=4)
UsRegulatoryStatus  : enum (Approved=0, GRAS=1, Restricted=2, Banned=3)
EuRegulatoryStatus  : enum (Approved=0, Restricted=1, Banned=2, NotAuthorized=3)
EfsaAdiMgPerKgBw    : decimal? (Acceptable Daily Intake)
EfsaLastReviewDate  : DateTime?
EpaCancerClass      : string?
HealthConcerns      : string (text description of known risks)
Description         : string
FdaAdverseEventCount: int (from openFDA)
FdaRecallCount      : int (from openFDA)
BannedInCountries   : string[] (e.g., ["EU", "UK", "Japan", "Canada"])
LastUpdated         : DateTime
```

#### FoodProductAdditives (junction)

```
FoodProductId       : Guid (FK)
FoodAdditiveId      : int (FK)
```

#### SymptomTypes (seed data)

```
Id                  : int (PK)
Name                : string (e.g., "Bloating")
Category            : string (e.g., "Digestive", "Neurological", "Skin", "Energy")
Icon                : string (emoji or icon name)
```

Seed data:

- **Digestive**: Bloating, Gas, Cramping, Diarrhea, Constipation, Heartburn/Acid Reflux, Nausea, Stomach Pain, Indigestion
- **Neurological**: Brain Fog, Headache, Migraine, Dizziness
- **Skin**: Skin Rash, Hives, Acne Flare-up, Eczema Flare-up
- **Energy**: Fatigue, Energy Crash, Insomnia
- **Other**: Joint Pain, Mood Changes, Anxiety, Inflammation

#### SymptomLogs

```
Id                  : Guid (PK)
UserId              : Guid (FK → Users)
SymptomTypeId       : int (FK → SymptomTypes)
Severity            : int (1-10)
OccurredAt          : DateTime
RelatedMealLogId    : Guid? (FK → MealLogs, optional user-selected association)
Notes               : string?
Duration            : TimeSpan?
```

#### DailyNutritionSummary (aggregated daily)

```
Id                  : Guid (PK)
UserId              : Guid (FK → Users)
Date                : DateOnly (unique per user)
TotalCalories       : decimal
TotalProteinG       : decimal
TotalCarbsG         : decimal
TotalFatG           : decimal
TotalFiberG         : decimal
TotalSugarG         : decimal
TotalSodiumMg       : decimal
MealCount           : int
CalorieGoal         : int (snapshot of user's goal that day)
```

#### UserFoodAlerts (custom watchlist)

```
Id                  : Guid (PK)
UserId              : Guid (FK → Users)
FoodAdditiveId      : int (FK → FoodAdditives)
AlertEnabled        : bool (default true)
CreatedAt           : DateTime
```

#### InsightReports

```
Id                  : Guid (PK)
UserId              : Guid (FK → Users)
GeneratedAt         : DateTime
PeriodStart         : DateOnly
PeriodEnd           : DateOnly
ReportType          : enum (Weekly=0, Monthly=1)
CorrelationsJson    : string (JSON blob of computed correlations)
SummaryText         : string (human-readable summary)
AdditiveExposureJson: string (JSON blob of additive frequency counts)
TopTriggersJson     : string (JSON blob of top suspected trigger foods)
```

---

## 6. Backend API Endpoints

### Auth

| Method | Path                 | Description                  |
| ------ | -------------------- | ---------------------------- |
| POST   | `/api/auth/register` | Register with email/password |
| POST   | `/api/auth/login`    | Login → JWT + refresh token  |
| POST   | `/api/auth/refresh`  | Refresh expired JWT          |
| POST   | `/api/auth/logout`   | Revoke refresh token         |

### Meals

| Method | Path                                       | Description                        |
| ------ | ------------------------------------------ | ---------------------------------- |
| POST   | `/api/meals`                               | Log a meal (manual item entry)     |
| POST   | `/api/meals/log-natural`                   | Log meal via natural language text |
| GET    | `/api/meals?date={date}`                   | Get meals for a date               |
| GET    | `/api/meals/{id}`                          | Get meal detail with items         |
| PUT    | `/api/meals/{id}`                          | Update a meal log                  |
| DELETE | `/api/meals/{id}`                          | Delete a meal log                  |
| GET    | `/api/meals/daily-summary/{date}`          | Get daily nutrition summary        |
| GET    | `/api/meals/history?from={date}&to={date}` | Meal history range                 |

### Food

| Method | Path                           | Description                                       |
| ------ | ------------------------------ | ------------------------------------------------- |
| GET    | `/api/food/search?q={query}`   | Search foods by name                              |
| GET    | `/api/food/barcode/{code}`     | Lookup product by barcode                         |
| GET    | `/api/food/{id}`               | Get cached food product detail                    |
| GET    | `/api/food/{id}/safety-report` | Full safety report (additives, ratings, concerns) |
| GET    | `/api/food/additives`          | List all tracked additives with ratings           |
| GET    | `/api/food/additives/{id}`     | Single additive detail                            |

### Symptoms

| Method | Path                                                        | Description             |
| ------ | ----------------------------------------------------------- | ----------------------- |
| POST   | `/api/symptoms`                                             | Log a symptom           |
| GET    | `/api/symptoms?date={date}`                                 | Get symptoms for a date |
| GET    | `/api/symptoms/{id}`                                        | Get symptom detail      |
| PUT    | `/api/symptoms/{id}`                                        | Update a symptom log    |
| DELETE | `/api/symptoms/{id}`                                        | Delete a symptom log    |
| GET    | `/api/symptoms/types`                                       | List all symptom types  |
| GET    | `/api/symptoms/history?from={date}&to={date}&type={typeId}` | Filterable history      |

### Insights

| Method | Path                                                    | Description                   |
| ------ | ------------------------------------------------------- | ----------------------------- |
| GET    | `/api/insights/weekly-report`                           | Current week's insight report |
| GET    | `/api/insights/report/{period}?date={date}`             | Weekly or monthly report      |
| GET    | `/api/insights/correlations`                            | Food-symptom correlations     |
| GET    | `/api/insights/additive-exposure?from={date}&to={date}` | Additive exposure summary     |
| GET    | `/api/insights/nutrition-trends?from={date}&to={date}`  | Nutrition trend data          |

### User

| Method | Path                            | Description               |
| ------ | ------------------------------- | ------------------------- |
| GET    | `/api/user/profile`             | Get current user profile  |
| PUT    | `/api/user/profile`             | Update profile info       |
| PUT    | `/api/user/goals`               | Update dietary goals      |
| GET    | `/api/user/alerts`              | Get additive watchlist    |
| POST   | `/api/user/alerts`              | Add additive to watchlist |
| DELETE | `/api/user/alerts/{additiveId}` | Remove from watchlist     |

---

## 7. Frontend Screens & Navigation

### Tab Navigation (5 tabs)

#### 🏠 Tab 1: Home / Dashboard (`(tabs)/index.tsx`)

- **Daily calorie ring** — circular progress showing consumed vs goal
- **Macro breakdown** — protein / carbs / fat progress bars with grams & percentages
- **Today's meals** — list of logged meals with calorie subtotals, tap to expand items
- **Symptom trend mini-chart** — small sparkline showing symptom frequency over past 7 days
- **Additive alerts banner** — if any scanned products today contained "Avoid" additives, show alert card
- **Quick actions** — "Log Meal" and "Log Symptom" floating buttons
- **Streak counter** — days of consecutive logging

#### 🍽️ Tab 2: Log Meal (`(tabs)/log.tsx`)

- **Natural language input** — large text field with placeholder "What did you eat? (e.g., 2 eggs, toast with butter, coffee)"
  - On submit → hits CalorieNinjas API → shows parsed items with nutrition
  - User can adjust servings, remove items, confirm
- **Meal type selector** — Breakfast / Lunch / Dinner / Snack (pill buttons)
- **Time picker** — defaults to now, adjustable
- **Manual search** — search bar to find foods from USDA/OpenFoodFacts DB
- **Barcode scan shortcut** — camera icon button → navigates to scan tab
- **Recent foods** — quick-add from previously logged items
- **Add photo** — optional meal photo via camera or gallery

#### 📷 Tab 3: Scan Food (`(tabs)/scan.tsx`)

- **Camera viewfinder** — full-screen barcode scanner using `expo-camera`
- **On scan** → lookup barcode via Open Food Facts API
- **Result card overlay** slides up with:
  - Product name, brand, image
  - **Safety score badge** — large color-coded circle (🟢 85/100 Safe | 🟡 55/100 Caution | 🔴 25/100 Avoid)
  - **NOVA group** indicator (1-4 with explanation)
  - **Nutri-Score** badge (A-E)
  - **Flagged additives** — list with color-coded safety ratings
  - Quick buttons: "View Full Report" → navigates to `food/[id]`, "Add to Meal" → adds to current meal log
- **Manual barcode entry** — type barcode number if camera fails
- **Search fallback** — if barcode not found, offer text search

#### 🩺 Tab 4: Symptoms (`(tabs)/symptoms.tsx`)

- **Quick-log grid** — 3×4 grid of common symptoms with icons/emojis:
  - 🫧 Bloating, 💨 Gas, 😖 Cramping, 🚽 Diarrhea, 🧱 Constipation, 🔥 Heartburn
  - 🤢 Nausea, 🧠 Brain Fog, 😴 Fatigue, 🤕 Headache, 🌡️ Skin Rash, 😫 Stomach Pain
- **On tap** → expands to:
  - **Severity slider** (1-10 with emoji faces)
  - **Time selector** (defaults to now)
  - **Associate with meal** — optional dropdown of today's meals
  - **Notes** field
  - **Save** button
- **Today's symptom timeline** — chronological list below the grid
- **"View History"** button → navigates to symptom history/calendar view
- **"View Insights"** button → navigates to correlation analysis

#### 👤 Tab 5: Profile (`(tabs)/profile.tsx`)

- **Profile info** — name, email, avatar
- **Daily goals** — editable calorie, protein, carbs, fat, fiber goals
- **Allergies & sensitivities** — multi-select chips (dairy, gluten, nuts, soy, shellfish, eggs, etc.)
- **Dietary preferences** — vegetarian, vegan, keto, paleo, low-FODMAP, etc.
- **Additive watchlist** — manage custom list of additives to flag (pre-populated with common "Avoid" ones)
- **Notification settings** — toggle push notifications for:
  - Daily logging reminders
  - Weekly insight reports
  - Scanned product alerts
- **Data & privacy** — export data, delete account
- **About / FAQ**

### Detail Screens

#### Food Safety Report (`food/[id].tsx`)

- **Header**: Product image, name, brand, barcode
- **Safety score** — large circular score (0-100) with color gradient
- **Score breakdown** — how the composite score was calculated:
  - CSPI additive ratings contributing factor
  - NOVA ultra-processing group factor
  - Number of flagged additives factor
  - FDA adverse event history factor
- **Additives section** — each additive in a card:
  - Name + E-number
  - **Color-coded badge**: 🟢 Safe | 🟡 Cut Back | 🟠 Caution | 🔴 Avoid
  - One-line health concern summary
  - **US status** vs **EU status** side-by-side (with ⚠️ if banned in EU)
  - EFSA ADI value if available
  - Tap to expand for full detail paragraph
- **Nutrition facts** — standard nutrition label format
- **Allergens** — highlighted allergen tags
- **NOVA group** — with explanation ("Group 4: Ultra-processed food")
- **Nutri-Score** — with explanation
- **"Add to Meal"** button

#### Insights Dashboard (`insights/index.tsx`)

- **Period selector** — This Week / This Month / Custom
- **Nutrition trends** — line charts for calories, protein, carbs, fat over time vs goals
- **Symptom frequency** — bar chart of symptom counts by type for the period
- **Top correlations** — cards showing discovered correlations:
  - "Bloating occurred 4/5 times within 6 hours of consuming products with **Carrageenan**"
  - "Brain fog reported 3/4 times after meals containing **Red 40**"
  - "Stomach pain severity averaged 7/10 after high-sodium meals (>1500mg)"
- **Additive exposure summary** — pie/bar chart of most-consumed additives, color-coded by safety rating
- **Trigger foods** — ranked list of foods most associated with symptoms

---

## 8. Key Features & Algorithms

### 8.1 Additive Safety Scoring (Composite Score 0-100)

Each food product gets a computed `SafetyScore` based on its additives:

```
For each additive in product:
  additiveScore = baseScore(cspiRating)    // Safe=100, CutBack=70, Caution=45, Avoid=10
  if (bannedInEU) additiveScore -= 15
  if (fdaAdverseEvents > 50) additiveScore -= 10
  if (epaCancerClass == "likely") additiveScore -= 20

productSafetyScore = weightedAverage(all additiveScores)
  adjusted by: novaGroup penalty (Group4 = -15, Group3 = -5)
  clamped to 0-100

Rating:
  90-100 → 🟢 Safe
  60-89  → 🟡 Caution
  30-59  → 🟠 Warning
  0-29   → 🔴 Avoid
```

### 8.2 Food-Symptom Correlation Engine

Time-window analysis correlating meals with subsequently logged symptoms:

```
1. For each symptom log S:
   - Find all meals M logged 1-24 hours before S.occurredAt
   - Extract all ingredients and additives from those meals
   - Record association: (ingredient/additive, symptom_type, severity)

2. After N data points (minimum 10 symptom logs):
   - For each (ingredient, symptom_type) pair:
     - Calculate frequency: times symptom followed ingredient / total meals with ingredient
     - Calculate average severity when correlated
     - Calculate p-value using Fisher's exact test (optional, for confidence)
   - Surface correlations where frequency > 50% and occurrences >= 3

3. Output:
   - "You logged [symptom] after consuming [ingredient] in X out of Y instances (Z%)"
   - Ranked by frequency × severity
   - Confidence indicator: Low (<5 data points) / Medium (5-15) / High (>15)
```

### 8.3 NLP Meal Logging Flow

```
1. User types: "had a chicken burrito, chips and guac, and a dr pepper"
2. Frontend sends POST /api/meals/log-natural { text, mealType, loggedAt }
3. Backend calls CalorieNinjas: GET /v1/nutrition?query={text}
4. API returns array of parsed items with nutrition per item
5. Backend returns parsed items to frontend for confirmation
6. Frontend shows editable list: user can adjust servings, remove items
7. User confirms → backend creates MealLog + MealItems, updates DailyNutritionSummary
```

### 8.4 Smart Alerts

- **Scan alert**: When barcode scan returns a product with any additive on user's watchlist → show prominent red warning banner with additive name and concern
- **Daily reminder**: Push notification at user-configured time if no meals logged today
- **Weekly insight**: Push notification on Sunday with link to weekly report
- **New correlation**: When a new food-symptom correlation is discovered, notify user

### 8.5 Barcode Scan Flow

```
1. expo-camera captures barcode (EAN-13, UPC-A)
2. Frontend sends GET /api/food/barcode/{code}
3. Backend checks PostgreSQL cache:
   a. Cache hit + fresh → return cached FoodProduct
   b. Cache miss or stale → call OpenFoodFacts API
4. Backend processes response:
   - Parse additives_tags → match against FoodAdditives table
   - Compute SafetyScore
   - Cache product in FoodProducts table
5. Return to frontend: product info + safety report + matched additives with ratings
6. Frontend renders result card overlay on camera view
```

---

## 9. Development Phases & Milestones

### Phase 1 — MVP: Meal Logging & Calorie Tracking (Weeks 1-6)

**Backend:**

- [ ] Project scaffolding (solution, projects, Docker Compose)
- [ ] PostgreSQL + EF Core setup, initial migration
- [ ] User registration & login (Identity + JWT)
- [ ] Meal CRUD endpoints
- [ ] CalorieNinjas integration for NLP meal logging
- [ ] USDA FoodData Central integration for food search
- [ ] Daily nutrition summary aggregation
- [ ] In-memory caching layer

**Frontend:**

- [ ] Expo project init with Router, NativeWind, TanStack Query, Zustand
- [ ] Auth screens (login, register)
- [ ] Dashboard screen (calorie ring, macro bars, today's meals)
- [ ] Log Meal screen (NLP input + manual search + meal type + time)
- [ ] Meal detail/edit screen
- [ ] Profile screen (goals, basic info)
- [ ] API service layer with auth interceptor

**Deliverable:** Users can register, log meals via text or search, see daily calorie/macro tracking.

### Phase 2 — Food Safety: Barcode Scanning & Additive Database (Weeks 7-10)

**Backend:**

- [ ] Seed FoodAdditives table (CSPI data, EU status, EFSA ADI, ~150 additives)
- [ ] Open Food Facts integration (barcode lookup)
- [ ] Food product caching with TTL
- [ ] Safety score computation algorithm
- [ ] Safety report endpoint
- [ ] openFDA integration (adverse events background job)
- [ ] Food search combining OFF + USDA results

**Frontend:**

- [ ] Barcode scanner screen (expo-camera)
- [ ] Scan result card overlay (product info, safety score, flagged additives)
- [ ] Food safety report detail screen (full additive breakdown, US vs EU)
- [ ] Safety score badge component (color-coded)
- [ ] Additive detail expansion cards
- [ ] "Add to Meal" flow from scan result
- [ ] Food search results screen

**Deliverable:** Users can scan barcodes, see food safety reports with additive ratings, and add scanned items to meals.

### Phase 3 — Gut Health: Symptom Tracking & History (Weeks 11-14)

**Backend:**

- [ ] Seed SymptomTypes table
- [ ] Symptom CRUD endpoints
- [ ] Symptom history endpoint with filtering
- [ ] Basic correlation query (meals before symptoms in time window)

**Frontend:**

- [ ] Symptom logger screen (quick-log grid, severity slider, time, meal association)
- [ ] Symptom history/calendar view
- [ ] Symptom timeline component
- [ ] Today's symptoms on dashboard

**Deliverable:** Users can log symptoms, view history, and see symptoms alongside meals on a timeline.

### Phase 4 — Insights & Correlation Engine (Weeks 15-18)

**Backend:**

- [ ] Correlation engine implementation (time-window analysis, frequency calculation)
- [ ] Weekly/monthly report generation (Hangfire job)
- [ ] Insight report endpoints
- [ ] Additive exposure tracking across logged meals
- [ ] Nutrition trend aggregation endpoints

**Frontend:**

- [ ] Insights dashboard (nutrition trends, symptom frequency, correlations)
- [ ] Correlation cards with visual explanations
- [ ] Additive exposure summary chart
- [ ] Trigger foods ranked list
- [ ] Weekly/monthly report view
- [ ] Dashboard additive alert banner
- [ ] Symptom trend mini-chart on dashboard

**Deliverable:** Users see food-symptom correlations, additive exposure analysis, and actionable weekly reports.

### Phase 5 — Polish, Onboarding & Launch (Weeks 19-22)

**Backend:**

- [ ] Push notification service (Expo Push)
- [ ] Rate limiting and abuse protection
- [ ] Performance optimization (query tuning, caching review)
- [ ] API documentation finalization
- [ ] Security audit (OWASP checklist)

**Frontend:**

- [ ] Onboarding flow (goals, allergies, dietary prefs, additive watchlist setup)
- [ ] Push notification integration (reminders, weekly reports, scan alerts)
- [ ] Empty states, loading skeletons, error states
- [ ] Offline support (queue meal logs when offline, sync on reconnect)
- [ ] Animations and micro-interactions
- [ ] Dark mode support
- [ ] Accessibility pass
- [ ] App Store screenshots and metadata

**Infrastructure:**

- [ ] CI/CD pipeline (GitHub Actions → EAS Build → backend deploy)
- [ ] Production environment setup (Fly.io + managed Postgres)
- [ ] Sentry error tracking integration
- [ ] App Store / Play Store submission
- [ ] Web deployment (Expo Web → Vercel or Fly.io)

**Deliverable:** Production-ready app submitted to App Store, Play Store, and deployed on web.

---

## 10. Third-Party API Caching Strategy

| API Source                 | Storage                    | TTL             | Refresh Mechanism                  |
| -------------------------- | -------------------------- | --------------- | ---------------------------------- |
| Open Food Facts products   | PostgreSQL `FoodProducts`  | 24 hours        | On-demand (cache miss → fetch)     |
| USDA nutrient data         | PostgreSQL `FoodProducts`  | 7 days          | On-demand                          |
| CalorieNinjas NLP results  | In-memory cache            | 24 hours        | On-demand (same query = cache hit) |
| CSPI additive ratings      | PostgreSQL `FoodAdditives` | ∞ (static seed) | Manual quarterly review            |
| EFSA ADI / safety opinions | PostgreSQL `FoodAdditives` | 30 days         | Hangfire monthly job               |
| EU regulatory status       | PostgreSQL `FoodAdditives` | ∞ (static seed) | Manual quarterly review            |
| openFDA adverse events     | PostgreSQL `FoodAdditives` | 7 days          | Hangfire weekly job                |
| Edamam / Spoonacular       | In-memory cache            | 24 hours        | On-demand                          |

### Rate Limit Handling

- All external API clients use **Microsoft.Extensions.Http.Resilience** standard resilience handler:
  - **Retry**: 2 retries with 500ms delay
  - **Circuit breaker**: Opens after failures in 30s sampling window, half-open recovery
  - **Attempt timeout**: 5s per request attempt
  - **Total timeout**: 15s including all retries
- Rate limiting middleware uses ASP.NET Core built-in `AddRateLimiter`:
  - `authenticated` policy: 100 requests/minute per-user (token bucket)
  - `auth` policy: 20 requests/minute per-IP (fixed window) — prevents brute-force
  - `search` policy: 30 requests/minute per-user (sliding window) — protects external API quotas

---

## 11. Security

| Concern                | Implementation                                                                                                   | Status              |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------- | ------------------- |
| Authentication         | JWT access tokens (60min expiry) + refresh tokens (7-day rotation)                                               | ✅ Implemented      |
| Password               | ASP.NET Core Identity with bcrypt hashing                                                                        | ✅ Implemented      |
| Transport              | HTTPS everywhere (TLS 1.3)                                                                                       | ✅ Via Docker/proxy |
| Input validation       | Inline validation in all endpoints (type, range, required checks)                                                | ✅ Implemented      |
| Rate limiting          | ASP.NET Core rate limiting — `authenticated` (100/min), `auth` (20/min per-IP), `search` (30/min sliding window) | ✅ Implemented      |
| CORS                   | AllowAnyOrigin for local dev (lock down for prod)                                                                | ✅ Implemented      |
| SQL injection          | Parameterized queries via EF Core (no raw SQL)                                                                   | ✅ Implemented      |
| API keys               | External API keys stored in env vars, never exposed to frontend                                                  | ✅ Implemented      |
| Global error handling  | ExceptionMiddleware returns ProblemDetails JSON, no stack traces in production                                   | ✅ Implemented      |
| HTTP client resilience | Polly standard resilience handler on all external API clients (retry, circuit breaker, timeout)                  | ✅ Implemented      |

---

## 12. Docker Compose (Local Development)

```yaml
services:
  api:
    build: ./backend
    ports: ["5000:8080"]
    environment:
      - ConnectionStrings__AzureStorage=UseDevelopmentStorage=true
      - Jwt__Secret=dev-secret-key-change-in-prod
      - ExternalApis__UsdaApiKey=${USDA_API_KEY}
      - ExternalApis__CalorieNinjasApiKey=${CALORIENINJAS_API_KEY}
    depends_on: [azurite]

  azurite:
    image: mcr.microsoft.com/azure-storage/azurite
    ports:
      - "10000:10000"
      - "10001:10001"
      - "10002:10002"
    volumes: [azurite-data:/data]

  seq:
    image: datalust/seq
    ports: ["5341:80"]
    environment:
      ACCEPT_EULA: "Y"

volumes:
  azurite-data:
```

---

## 13. Key Technical Decisions & Rationale

| Decision                                          | Rationale                                                                                                                                                       |
| ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **CalorieNinjas over Nutritionix** for NLP        | Free tier, sufficient accuracy for MVP. Upgrade to Nutritionix later if needed.                                                                                 |
| **Open Food Facts as primary product DB**         | Free, open-source, 3M+ products, includes additives data. No API key needed.                                                                                    |
| **Static additive safety DB** (not real-time API) | CSPI, EFSA, EPA data doesn't change frequently. Static seeding with periodic refresh is more reliable and faster than runtime API calls to scrape-only sources. |
| **Composite safety score** (not single-source)    | No single source rates all additives comprehensively. Combining CSPI + EU status + EFSA ADI + FDA adverse events gives the most complete picture.               |
| **PostgreSQL over SQLite/Cosmos**                 | Relational model fits (users, meals, items, additives, junctions). Great JSON support for flexible fields. Robust, free, widely hosted.                         |
| **Minimal APIs over Controllers**                 | Less boilerplate, faster development, good enough for this scope.                                                                                               |
| **Expo over bare RN**                             | Barcode scanner, camera, notifications, push, OTA updates, EAS builds — all easier with Expo. Web support included.                                             |
| **TanStack Query for server state**               | Automatic caching, background refetching, optimistic updates, devtools. Eliminates custom loading/error state management.                                       |
| **Zustand over Redux**                            | Minimal boilerplate, tiny bundle, sufficient for auth/app state. Server state handled by TanStack Query.                                                        |

---

## 14. Future Enhancements (Post-Launch)

- **AI-powered meal photo recognition** — snap a photo → AI identifies foods → auto-log (use Google Vision or custom model)
- **Meal planning** — suggest meals based on nutrition goals and symptom triggers to avoid (Edamam Meal Planner API)
- **Social features** — share food safety reports, compare symptom patterns
- **Wearable integration** — sync with Apple Health / Google Fit for activity + HRV data
- **Premium tier** — advanced AI insights, unlimited scans, detailed reports
- **Restaurant menu scanner** — OCR menu items, rate safety of dishes
- **Elimination diet tracker** — guided elimination diet protocol with automated food reintroduction tracking
- **Healthcare provider sharing** — export symptom/diet reports to share with GI doctors
- **Community additive database** — user-submitted additive research and ratings

---

## 15. Getting Started — First Steps

```bash
# 1. Create the monorepo
mkdir gut-ai && cd gut-ai
git init

# 2. Backend setup
dotnet new sln -n GutAI -o backend
cd backend
dotnet new web -n GutAI.Api -o src/GutAI.Api
dotnet new classlib -n GutAI.Application -o src/GutAI.Application
dotnet new classlib -n GutAI.Domain -o src/GutAI.Domain
dotnet new classlib -n GutAI.Infrastructure -o src/GutAI.Infrastructure
dotnet sln add src/GutAI.Api src/GutAI.Application src/GutAI.Domain src/GutAI.Infrastructure

# Add project references (Clean Architecture dependency flow)
cd src/GutAI.Api && dotnet add reference ../GutAI.Application ../GutAI.Infrastructure
cd ../GutAI.Application && dotnet add reference ../GutAI.Domain
cd ../GutAI.Infrastructure && dotnet add reference ../GutAI.Application
cd ../../..

# 3. Frontend setup
npx create-expo-app@latest frontend --template tabs
cd frontend
npx expo install nativewind tailwindcss expo-camera expo-barcode-scanner expo-secure-store expo-notifications
npm install @tanstack/react-query zustand axios react-hook-form @hookform/resolvers zod
npm install react-native-mmkv victory-native

# 4. Start Docker services
cd ../backend
docker compose up -d

# 5. Run migrations
cd src/GutAI.Api
dotnet ef migrations add InitialCreate
dotnet ef database update

# 6. Start developing!
dotnet run  # Backend on :5000
cd ../../frontend && npx expo start  # Frontend on :8081
```

---

**Total estimated timeline: 20-22 weeks for a solo developer, 10-12 weeks with a 2-person team.**

**Priority order for external API integration:**

1. CalorieNinjas (Week 1 — enables core meal logging)
2. USDA FoodData Central (Week 2 — food search)
3. CSPI static seed (Week 7 — additive safety foundation)
4. Open Food Facts (Week 7 — barcode scanning)
5. openFDA (Week 9 — adverse event enrichment)
6. EFSA/EU data (Week 9 — regulatory comparison)
7. Edamam/Spoonacular (Week 15+ — enrichment features)

That's the complete plan. Copy it from above into `/home/dom/projects/gut-ai/PLAN.md`, or if you'd like me to try creating the file another way, let me know. I can also start scaffolding the actual project code if you're ready to begin building.
