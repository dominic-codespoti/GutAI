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
- **Deployment & Production Config**: `docs/DEPLOYMENT.md`
- **UI Polish & Wow-Factor Roadmap**: `docs/POLISH_ROADMAP.md`

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

- **LLM Never Produces Nutrition Numbers:** The vision model outputs ONLY component identity, gram ranges (low, midpoint, high), and confidence. Nutrition macros (calories, P, C, F, sodium, etc.) are ALWAYS computed deterministically from verified database per-100g values multiplied by detected grams.
- **Semantic Validation Outside the Model:** Structured JSON from LLMs guarantees syntactic shape, not semantic validity. Always run `MealVisionValidator.Validate` to enforce gram ordering ($low \le midpoint \le high$), physiological sanity caps ($\le 5\text{ kg}$ per item), and component count limits.
- **Grounding Through Existing Resolver:** Every detected component MUST be grounded via `IFoodSearchService.ResolveAsync`. Auto-select only when status is `Exact` or `Probable` AND `MatchConfidence >= 0.85`. Ambiguous items MUST abstain to human review with candidates exposed.
- **Stage-A Gram Immutability:** Portion estimates attach to the detected component, NEVER to the database product's default serving size. Grounding must not mutate Stage-A grams.
- **Health Signal Isolation (Safety Rule):** `MealScanHealthSignals` (FODMAP status, triggers, gut rating) attach ONLY to items with a verified `FoodProductId`. Web-scraped and AI-estimated items physically cannot receive FODMAP signals.
- **Regression Gating:** Prompt, schema, or model changes to Stage A MUST be versioned (`VisionPromptVersion`) and pass the `GoldenScanHarness` regression gate (`make golden-gate`).

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
- Model, reasoning effort, prompt/schema version, token usage, latency, and
  repeated-run variance MUST be covered by the applicable regression harness.
- Agent Framework scan tools MUST be typed, read-only projections of server-owned
  grounding snapshots. Never expose raw provider search or accept model-supplied
  user IDs, candidate IDs, image bytes, deployments, temperatures, or token limits.
- Reanalysis tools MUST have a server-enforced call count and effort cap. Every
  returned candidate still passes deterministic compatibility, confidence, and
  human-review gates.

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

---

## ⚙️ Development Workflow & Commands

### CI Pipeline (`make ci`)

The full CI pipeline runs these checks in order:

1. `dotnet build` — zero errors
2. `dotnet test GutAI.Infrastructure.Tests` — 550+ unit tests (services, scoring, FODMAP, GI, substitutions, NLP)
3. `dotnet test GutAI.Api.Tests` — API contract tests (WebApplicationFactory + Testcontainers Azurite)
4. `node scripts/check-contracts.js` — frontend↔backend DTO field matching (27 interface↔DTO pairs)
5. `npx tsc --noEmit` — frontend TypeScript type check

All must pass before merging.

---

### Test Organization

| Project                      | What it tests                                                    | Framework                                                 |
| ---------------------------- | ---------------------------------------------------------------- | --------------------------------------------------------- |
| `GutAI.Infrastructure.Tests` | Services, scoring, correlation, FODMAP, GI, substitutions, NLP   | xUnit v3, Moq                                             |
| `GutAI.IntegrationTests`     | Table Storage CRUD, end-to-end API flows, food product endpoints | xUnit v2, Testcontainers (Azurite)                        |
| `GutAI.Api.Tests`            | HTTP endpoint response shapes, validation, auth, roundtrips      | xUnit v2, WebApplicationFactory, Testcontainers (Azurite) |

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
