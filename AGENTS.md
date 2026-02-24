# AGENTS.md — GutAI Guardrail Rules

This document codifies the rules that prevent recurring bug categories discovered during audit passes. Every contributor (human or AI) MUST follow these rules.

---

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

## CI Pipeline (`make ci`)

The full CI pipeline runs these checks in order:

1. `dotnet build` — zero errors
2. `dotnet test GutAI.Infrastructure.Tests` — 550+ unit tests (services, scoring, FODMAP, GI, substitutions, NLP)
3. `dotnet test GutAI.Api.Tests` — API contract tests (WebApplicationFactory + Testcontainers Azurite)
4. `node scripts/check-contracts.js` — frontend↔backend DTO field matching (26 interface↔DTO pairs)
5. `npx tsc --noEmit` — frontend TypeScript type check

All must pass before merging.

---

## Test Organization

| Project                      | What it tests                                                    | Framework                                                 |
| ---------------------------- | ---------------------------------------------------------------- | --------------------------------------------------------- |
| `GutAI.Infrastructure.Tests` | Services, scoring, correlation, FODMAP, GI, substitutions, NLP   | xUnit v3, Moq                                             |
| `GutAI.IntegrationTests`     | Table Storage CRUD, end-to-end API flows, food product endpoints | xUnit v2, Testcontainers (Azurite)                        |
| `GutAI.Api.Tests`            | HTTP endpoint response shapes, validation, auth, roundtrips      | xUnit v2, WebApplicationFactory, Testcontainers (Azurite) |

---

## Adding a New Endpoint Checklist

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
4. Add field to DTO in the appropriate DTOs file (if exposed via API)
5. Add field to TypeScript interface in `frontend/src/types/index.ts`
6. Update `INTERFACE_TO_DTO` in `scripts/check-contracts.js` if new DTO
7. Add roundtrip test in `GutAI.IntegrationTests/TableStorageCrudTests.cs`
8. Run `make ci`
