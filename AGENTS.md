# AGENTS.md — GutAI Master Guide & Guardrails

You are an expert full-stack AI developer working on **GutAI**.
Your goal is to write clean, correct, and bug-free code. Think step-by-step and DO NOT guess implementations.
The tech stack is **React Native (Expo)** on the frontend and **.NET 8 Minimal APIs** (with Azure Table Storage) on the backend.

## 📚 Knowledge Base Routing (Progressive Disclosure)

DO NOT guess architectural details, domain logic, or test arrangements. If your task involves any of the following topics, you **MUST** read the corresponding documentation file using your file reading tool BEFORE writing code or making system changes:

- **System Architecture & Setup**: `docs/ARCHITECTURE.md`
- **End-to-End Testing (Playwright)**: `docs/PLAYWRIGHT_E2E_ANALYSIS.md`
- **Deployment**: `docs/DEPLOYMENT.md`
- **Scoring & Algorithms**: `docs/SCORING_ANALYSIS_REPORT.md`
- **FODMAP Domain Logic**: `docs/FODMAP_SERVICE_ANALYSIS.md`
- **Glycemic Index Logic**: `docs/GLYCEMIC_INDEX_SERVICE_ANALYSIS.md`
- **GutRisk Integrations**: `docs/GUTRISK_SERVICE_ANALYSIS.md`
- **Meals & UX**: `docs/MEALS_UX_ANALYSIS.md` and `docs/MEAL_LOGGING_BUG_ANALYSIS.md`
- **Database & Data Files**: `docs/DATA_FILES_AUDIT_REPORT.md`
- **Food Database Integrations**: `docs/global-branded-food-data-analysis.md`
- **Historical Bugs & Analytics**: `docs/BUG_AUDIT_REPORT.md`

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

---

## ⚙️ Development Workflow & Commands

### CI Pipeline (`make ci`)

The full CI pipeline runs these checks in order:

1. `dotnet build` — zero errors
2. `dotnet test GutAI.Infrastructure.Tests` — 550+ unit tests (services, scoring, FODMAP, GI, substitutions, NLP)
3. `dotnet test GutAI.Api.Tests` — API contract tests (WebApplicationFactory + Testcontainers Azurite)
4. `node scripts/check-contracts.js` — frontend↔backend DTO field matching (26 interface↔DTO pairs)
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
