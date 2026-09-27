# AGENTS.md — GutAI Master Guide & Guardrails

You are an expert full-stack AI developer working on **GutAI**.
Your goal is to write clean, correct, and bug-free code. Think step-by-step and DO NOT guess implementations.
The tech stack is **React Native (Expo)** on the frontend and **.NET 10 Minimal APIs** (with Azure Table Storage) on the backend.

## 📚 Knowledge Base Routing (Progressive Disclosure)

DO NOT guess architectural details, domain logic, or test arrangements. If your task involves any of the following topics, you **MUST** read the corresponding documentation file using your file reading tool BEFORE writing code or making system changes:

- **System Architecture & Setup**: `docs/ARCHITECTURE.md`
- **Meal Photo Scan Pipeline & Grounding**: `docs/meal-scan-detailed-design.md`
- **AI Calorie Estimation & Database Research**: `docs/ai-meal-scan-upgrade-research.md`
- **End-to-End Testing (Playwright)**: `docs/PLAYWRIGHT_E2E_ANALYSIS.md`
- **FODMAP Database Hardening Plan**: `docs/FODMAP_DATABASE_HARDENING_PLAN.md`
- **AI Calorie Pipeline Remediation Plan (confirm, drafts, grounding policy, evals, meal generation)**: `docs/AI_CALORIE_PIPELINE_REMEDIATION_PLAN.md`
- **Deployment & Production Config**: `docs/DEPLOYMENT.md`
- **Evaluation tooling:** `backend/tools/GoldenScanHarness/README.md`, `backend/tools/AgentEvalHarness/README.md`, `backend/tools/CorrectionAnalytics/README.md`, and `backend/tools/ScanMealRepair/README.md`

## 📝 Documentation Maintenance

As GutAI evolves, it is your responsibility to keep the system knowledge base current. If you implement a new architectural pattern, add a new service, change deployment steps, or discover a new pattern/bug:

- You **MUST** update the relevant file in the `docs/` folder.
- If you add a completely new category of documentation, you **MUST** update this `AGENTS.md` file to add the new doc to the **Knowledge Base Routing** list above.
- If you establish a new universal rule to prevent a category of bugs, you **MUST** add it to the **Strict Project Guardrails** section below.

---

## 🚨 Strict Project Guardrails

This document codifies the rules that prevent recurring bug categories discovered during audit passes. Every contributor (human or AI) MUST follow these rules.

## 1. Entity ↔ Table Storage Roundtrip

Every field on a Domain entity MUST appear in **both** `UpsertXxx` and `MapToXxx` in `TableStorageStore.cs`.

- **Symptom:** Data silently lost on save or load (e.g., `DisplayName` overwritten with null, `SafetyRating` not persisted, `alertEnabled` hardcoded).
- **Rule:** When adding a field to an entity, grep for `UpsertXxx` and `MapToXxx` and add the field to both. Write a roundtrip integration test in `GutAI.IntegrationTests`.

## 2. DTO ↔ Frontend Type Contract

Every field on a backend DTO or anonymous response object MUST match the corresponding frontend TypeScript interface (camelCase).

- **Symptom:** Frontend crashes or shows `undefined` because the backend sends `usRegulatoryStatus` but the frontend expects `usStatus`.
- **Rule:** Run `make check-contracts` before merging. The script `scripts/check-contracts.js` parses both files and flags mismatches.
- **Rule:** When adding a field to a DTO, add it to `frontend/src/types/index.ts` too.

## 3. Anonymous Response Objects

Endpoint handlers that return `Results.Ok(new { ... })` MUST have a contract test in `GutAI.Api.Tests` that asserts every field exists with the correct JSON type.

- **Symptom:** SafetyReport returned wrong shape, additives list missing `eNumber`/`safetyRating`, NutritionTrend field names wrong.
- **Rule:** Every endpoint with an anonymous return type gets a `[Fact]` that deserializes the response and calls `AssertHasStringProperty` / `AssertHasNumberProperty` etc. for every field.

## 4. Input Validation at API Boundary

All endpoints MUST validate inputs before processing. Return `400 Bad Request` or `422 Unprocessable Entity` for invalid data.

- **Symptom:** Invalid emails accepted, empty meal items stored, severity > 10 accepted.
- **Rule:** Add validation tests for every endpoint that accepts user input:
  - Auth: email format, password strength (≥8 chars, digit + lowercase), null/empty fields
  - Meals: non-empty items array (1–50), non-negative nutrition values, valid servings
  - Symptoms: severity 1–10, valid symptomTypeId, optional notes max 1000 chars, duration 0–7 days
  - Food: name required (max 300 chars), valid additive IDs
  - Alerts: valid additiveId
  - Chat: message required, max 2000 chars (validated in StreamChat)

## 5. Null / Default Safety

Never overwrite existing entity fields with null when the update request omits them.

- **Symptom:** `UpdateProfile` overwrote `DisplayName` with null when request didn't include it.
- **Rule:** Use null-coalescing (`request.Field ?? existing.Field`) in all update endpoints.

## 6. Error Handling

`ExceptionMiddleware` MUST catch all exceptions and return structured JSON. Never leak stack traces in production.

- **Symptom:** Raw exception text returned to client.
- **Rule:** ExceptionMiddleware returns `{ error: "message" }` in Development, `{ error: "An error occurred" }` in Production.

## 7. Lazy Initialization

Never use `Lazy<Task<T>>` for faulting resources. A faulted `Lazy` permanently caches the exception.

- **Rule:** Use `SemaphoreSlim` + null check for async lazy initialization, or reset the lazy on failure.

## 8. AI Meal Photo Scanning & Grounding Invariants

Every AI meal scanning feature MUST adhere to the following deterministic boundaries:

- **Nutrition authority:** Stage A emits identity and gram estimates; inferred components are a separate opt-in list. All persisted nutrition with a basis is computed server-side from per-100 g values × grams (see N1 below).
- **Semantic Validation Outside the Model:** Structured JSON from LLMs guarantees syntactic shape, not semantic validity. Always run `MealVisionValidator.Validate` to enforce gram ordering ($low \le midpoint \le high$), physiological sanity caps ($\le 5\text{ kg}$ per item), and component count limits.
- **Grounding through the shared policy (N2):** Every surface — scan, NLP, Coach, MCP, and describe-food — MUST use `GroundingPolicy` for automatic selection. Auto-select only when status is `Exact` or `Probable`, `MatchConfidence >= 0.85`, calories are present, `NutritionSanity` passes, and no compatibility or food-form veto applies. Otherwise return candidates with `needs_choice`; draft commit MUST reject a needs-choice item without an explicit candidate key (including the previewed candidate), replacement food, or log-without-calories choice, returning HTTP 422.
- **Stage-A grams are immutable:** reanalysis may change identity/grounding only and MUST preserve Stage-A grams.
- **Batched B2 selection:** one selection call handles the batch, but every choice MUST still pass `MealScanCandidateSelector`; when `MealScan:RequireCompatibilityAgreement` is enabled it MUST also agree with the compatibility top candidate.
- **Hidden calories are opt-in:** inferred items MUST be disabled unless `Features:HiddenCalories` is true. Use its separate inferred-components wire list and prompt-version suffix; the flag-off Stage-A schema MUST remain byte-identical.
- **Health Signal Isolation (Safety Rule):** `MealScanHealthSignals` (FODMAP status, triggers, gut rating) attach ONLY to items with a verified `FoodProductId`. Web-scraped and AI-estimated items physically cannot receive FODMAP signals.
- **Symptom:** reanalysis silently changes portion estimates; batched selection bypasses item gates; a feature flag changes the default wire contract or introduces inferred items when off.
- **Regression gate:** Prompt, schema, model or calibration changes MUST use the applicable harness. For Stage-A changes run `make golden-gate`; run the refreshed in-process gate manually with `--mode in-process --refresh --gate`.
## 9. Reasoning Model Transport & Prompt Roles

All Azure OpenAI reasoning-model inference MUST use the Responses API. App-owned
inference MUST use the shared `IChatClient` adapter; Foundry-managed agents MAY use
their first-party Responses client. Direct Chat Completions calls and transport
switches are prohibited for production inference.

- Reasoning requests MUST omit unsupported sampling parameters (`temperature`,
  `top_p`, penalties, `max_tokens`).
- Stable behavioral instructions MUST be sent in one developer-role message.
  Do not send both system and developer messages. Dynamic user/profile data remains
  delimited user content.
- Tool-calling workflows MUST remain on Responses; do not set `reasoning_effort`
  to `none` merely to make Chat Completions tools work.
- Multi-call tool loops (Coach) MUST NOT depend on server-stored responses. Create
  their options with `MealScanReasoningOptions.Create(effort, storeOutput: false)`, so
  each call resends its own history with encrypted reasoning content and never chains
  `previous_response_id`. Chained stored responses intermittently fail with
  `previous_response_not_found`, and the user gets an empty reply.
- Model, reasoning effort, prompt/schema version, token usage, latency, and
  repeated-run variance MUST be covered by the applicable regression harness.
- Agent Framework scan tools MUST be typed, read-only projections of server-owned
  grounding snapshots. Never expose raw provider search or accept model-supplied
  user IDs, candidate IDs, image bytes, deployments, temperatures, or token limits.
- Reanalysis tools MUST have a server-enforced call count and effort cap. Every
  returned candidate still passes deterministic compatibility, confidence, and
  human-review gates.
- Use keyed per-workload `IChatClient`s and configure reasoning effort per workload.
  Every workload MUST use function invocation; Coach MUST cap iterations/errors and
  history size. Emit OpenTelemetry and logging telemetry while keeping sensitive data
  disabled in production.
- **Symptom:** workload settings are only labels, a workload bypasses tool safeguards, or
  Coach loops/history and cost/latency become unbounded.

## 10. MCP Tool Authorization

The `/mcp` endpoint has NO route-level authorization; access is enforced per-tool.
Every `[McpServerTool]` method MUST carry `[Authorize]` unless it is an explicit
anonymous linking/auth exception (currently only `gutai_link_account`). Every mutating
tool MUST call `McpAccess.EnsureWrite(user)` before its first side effect — PAT-linked
AI consumers are read-only by default.

- **Symptom:** a new MCP tool without `[Authorize]` silently exposes user health data to
  unauthenticated sessions, or a new write path forgets the scope gate and lets a
  read-only AI connection mutate records.
- **Rule:** when adding an MCP tool, copy the attribute stack of the nearest existing
  tool (`[McpServerTool]` + `[Authorize]` [+ `ReadOnly = true`]) and add the write gate
  for anything that persists. Contract tests must cover any new anonymous response shape.
- **Parameter rules (SDK 1.2.0 binding contract):** take `ClaimsPrincipal? user` for
  identity — NEVER `HttpContext` (it becomes a required JSON argument and the tool fails
  every real transport call). Optional parameters MUST declare explicit defaults
  (`string? x = null`, `CancellationToken ct = default`) or M.E.AI marks them required in
  the schema.
- **Rule:** MCP changes are proven by `GutAI.Api.Tests:McpLinkFlowTests`-style end-to-end
  calls over the real Streamable HTTP transport — parameter-binding bugs only surface
  over the wire, never in unit tests.

## 11. UTC DateTime Persistence

Every `DateTime` written through `TableStorageStore` MUST have
`DateTimeKind.Utc`. API clients MAY send ISO-8601 offsets; the persistence
boundary normalizes Local/Unspecified values before Azure Table serialization.

## 12. Reanimated Runtime Boundaries

Callbacks passed to `useAnimatedReaction`, `useAnimatedStyle`, `useAnimatedProps`,
and other automatically workletized APIs run on the UI runtime. Never call React
state setters or ordinary JavaScript functions directly from those callbacks.
Use `scheduleOnRN` from `react-native-worklets` for RN-runtime calls; `runOnJS`
is deprecated in Reanimated 4.


## 13. Server-Authoritative Nutrition (N1)

- **Symptom:** client or model values drift from the diary after a portion edit, or linked
  catalog items persist tampered calories.
- **Rule:** Derive persisted nutrition only with `NutritionCalculator` from a per-100 g
  basis × grams whenever a basis exists; never trust client/model nutrition for those items.
  Validate item grams in `(0, 5000]`. `frontend/src/utils/nutrition.ts` is display parity,
  not an authority.

## 14. Agent Writes Use MealDraft (N3)

- **Symptom:** an AI tool writes a meal before review, creates and commits it in one turn,
  or tells the user calorie numbers that do not match the server calculation.
- **Rule:** AI-originated diary writes MUST go through `IMealDraftService.CommitAsync`.
  Coach commits MUST pass the turn-start guard; MCP commits MUST pass the minimum-age
  guard configured by `Mcp:MinCommitDelaySeconds`. Coach prose MUST NOT invent nutrition
  numbers; the `AgentEvalHarness` grader enforces consistency with server draft/tool data.

## 15. Truthful Nutrition Provenance (N4)

- **Symptom:** unavailable nutrition appears as a measured zero or totals silently treat an
  unresolved item as zero calories.
- **Rule:** Use `NutritionProvenance` according to source: `Sourced` for catalog data,
  `Estimated` for non-model estimates, `Web` for web results, `ModelEstimated` for model
  estimates, `UserEntered` for user-supplied values, and `Unknown` when no nutrition basis
  exists. Never fabricate 0 kcal. Count `Unknown`/basis-less items in `itemsWithoutNutrition`.

## 16. Conditional Draft Status Updates

- **Symptom:** concurrent commit, discard, or expiry operations overwrite a newer draft
  state and create duplicate or inconsistent diary records.
- **Rule:** Draft/shared-record status transitions MUST use ETag-conditional replace
  (`TryReplaceMealDraftAsync`); never implement them as read-then-upsert.

## 17. API Test Host and Telemetry Isolation

- **Symptom:** API tests hang or fail on sampled activities because multiple App Insights
  samplers are registered in one process.
- **Rule:** The shared test factory explicitly clears `APPLICATIONINSIGHTS_CONNECTION_STRING`, so no shared or derived host registers App Insights. Only `AppInsightsStartupSmokeTests` enables it, in its single keyed host. Derived hosts MUST come from the factory's keyed cache, are created once per fixture, and are disposed with that fixture. Never create hosts per test or mutate a host's `IConfiguration` in tests.
## 18. Measured Before Shipped (N5)

- **Symptom:** an unmeasured or unverified AI behavior is described or treated as production-ready because a configured gate appears to pass.
- **Rule:** Scan (`GoldenScanHarness`) and agent (`AgentEvalHarness`) gates MUST be run manually against the live deployment before shipping any change to prompts or schemas, model deployments or effort, grounding/search ranking, calibration, or Coach tools, and the report MUST be reviewed. Current thresholds are provisional baselines from 5 live refreshed runs of the unweighed 12-case set (plan decision D7), not measured production baselines. If a metric with a configured threshold is unmeasured, the harness MUST fail the gate or report it as not evaluated according to its rules; it MUST NOT be treated as passing.

---

## ⚙️ Development Workflow & Commands

### CI Pipeline

`make ci` runs backend build, Infrastructure/API/Integration tests, contract checks,
frontend TypeScript checking, and frontend unit tests (see `Makefile`). GitHub
`.github/workflows/ci.yml` runs the same backend test groups and contract check, plus
frontend type/unit tests and a Docker API image build. The AI regression gates are not
part of CI; they are run manually on demand (see the harness READMEs), and CI makes no
model calls.

---

### Test Organization

| Project / tool | What it tests | Framework / command |
| --- | --- | --- |
| `GutAI.Infrastructure.Tests` | Services, scoring, correlation, FODMAP, GI, substitutions, NLP, draft and suggestion behavior | xUnit; `make ci` / `.github/workflows/ci.yml` |
| `GutAI.IntegrationTests` | Table Storage and end-to-end persistence flows | xUnit, Testcontainers (Azurite); `make ci` / `.github/workflows/ci.yml` |
| `GutAI.Api.Tests` | HTTP contracts, validation, authorization and roundtrips | xUnit, `WebApplicationFactory`, Testcontainers (Azurite); `make ci` / `.github/workflows/ci.yml` |
| Frontend tests | Utility and store behavior, plus TypeScript contracts | `tsx --test` and `tsc --noEmit`; `make ci` / `.github/workflows/ci.yml` |
| `GoldenScanHarness` | Stage-A, in-process and API end-to-end scan evaluation | `make golden-run`, `make golden-gate`, `make golden-inprocess`, `make golden-e2e`; refreshed in-process gate run manually with `--mode in-process --refresh --gate` |
| `AgentEvalHarness` | Coach, describe-food and label evaluation suites | `--suite all --gate`; manual live runs |
| `CorrectionAnalytics` | Read-only correction report and calibration snippet | `backend/tools/CorrectionAnalytics/README.md` |
| `ScanMealRepair` | Dry-run historical scan-meal repair | `backend/tools/ScanMealRepair/README.md` |

---

### Adding a New Endpoint Checklist

1. Add the endpoint in `XxxEndpoints.cs`
2. If it returns data: add/update DTO in the appropriate DTOs file OR document the anonymous object shape
3. Add matching TypeScript interface in `frontend/src/types/index.ts`
4. Add contract test in `GutAI.Api.Tests/XxxContractTests.cs`
5. If it accepts input: add validation + validation test
6. Run `make ci` to verify everything passes

## Adding a New Entity Field Checklist

1. Add field to entity in `Domain/Entities/`
2. Add field to `UpsertXxx` in `TableStorageStore.cs`
3. Add field to `MapToXxx` in `TableStorageStore.cs`
