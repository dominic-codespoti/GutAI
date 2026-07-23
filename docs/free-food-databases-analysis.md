# Free Food Databases Analysis (for GutAI)

## Quick context

You already use:
- USDA FoodData Central
- Open Food Facts

This list focuses on **additional** free/open sources.

## Comparison table

| Database | Region/coverage | Access method | Free-use terms (high level) | Commercial fit | Data type fit for GutAI | Integration effort |
|---|---|---|---|---|---|---|
| **Canadian Nutrient File (CNF)** | Canada | REST API (JSON/XML) | Open Government Licence - Canada | High | Strong nutrient reference; less barcode depth | **Low** (API-first) |
| **CIQUAL (ANSES)** | France / EU-relevant foods | Download (XLS/XML/PDF via Ciqual site) | OpenData with attribution expectations | High | Strong nutrient depth and food-component quality | Medium (ETL) |
| **AFCD (Australian Food Composition Database)** | Australia | Download | CC BY 2.5 AU (data.gov.au metadata) | High | Broad nutrient panel (up to 256 nutrients/food) | Medium (ETL) |
| **AUSNUT** | Australia (survey-oriented) | Download | CC BY 2.5 AU (data.gov.au metadata) | High | “As-consumed” foods useful for real-world logs | Medium (ETL) |
| **CoFID (McCance & Widdowson)** | UK | GOV.UK XLS download | Public GOV.UK dataset (verify exact OGL terms at use time) | High (pending legal check) | Excellent UK nutrient coverage | Medium (ETL) |
| **FRIDA (DTU Denmark)** | Denmark / Nordic foods | Free spreadsheet download via form | Free download; attribution/citation practice | Medium-High | Useful regional nutrient enrichment | Medium (ETL + request flow) |
| **BEDCA (Spain)** | Spain | Public query/XML endpoint + docs | Custom BEDCA usage terms (not standard CC) | Medium (license review first) | API path + regional nutrient support | Medium |
| **FAO/INFOODS databases** | Global / multi-region tables | Download (Excel/PDF) | Open access datasets; check dataset-level commercial permissions | Medium (varies by dataset) | Great global gap-filler, especially niche foods | Medium-High |
| **NEVO (RIVM)** | Netherlands | Dataset request/download | Usage agreement + attribution/citation requirements | Medium (term review needed) | Useful Benelux nutrient normalization | Medium-High |

## Ranked recommendation for GutAI

1. **CNF (Canada)** — easiest API win and clean licensing posture.
2. **CoFID + CIQUAL + AFCD/AUSNUT** — best next ETL bundle for UK/EU/AU nutrient coverage.
3. **BEDCA** — good API potential for Spain, but perform legal/terms review first.
4. **FRIDA** — useful regional add-on if Nordic user base matters.
5. **FAO/INFOODS + NEVO** — best as supplemental sources after legal normalization.

## Why this ranking

- Your product needs both **consumer-product lookup** and **nutrient completeness**.
- Open Food Facts remains best for barcode/product labels globally.
- National composition tables (CNF/CoFID/CIQUAL/AFCD/AUSNUT) are strongest for nutrient quality and consistency.
- Terms complexity (custom agreements or non-standard rights) lowers priority even when data quality is strong.

## Suggested integration pattern

- Keep **Open Food Facts** as barcode/product front door.
- Add **CNF API** as first additional live provider.
- Build a periodic ETL pipeline for **CoFID + CIQUAL + AFCD/AUSNUT** into your canonical nutrient schema.
- Add lower-priority sources behind feature flags and legal approval.

## Source links used

- USDA API Guide: https://fdc.nal.usda.gov/api-guide/
- Open Food Facts API docs: https://openfoodfacts.github.io/openfoodfacts-server/api/
- Open Food Facts terms: https://ssl-api.openfoodfacts.org/terms-of-use
- CNF dataset page: https://open.canada.ca/data/en/dataset/90a31d6a-9131-4f31-a156-cd1f3b2717fe
- CNF API docs: https://produits-sante.canada.ca/api/documentation/cnf-documentation-en.html
- CIQUAL overview: https://www.anses.fr/en/content/ciqual-nutritional-composition-table
- AUSNUT files: https://www.foodstandards.gov.au/science-data/food-nutrient-databases/ausnut/data-files
- AFCD metadata API (license info): https://data.gov.au/data/api/3/action/package_show?id=a9159b56-487e-4897-ac45-ab62f7e8d232
- CoFID publication: https://www.gov.uk/government/publications/composition-of-foods-integrated-dataset-cofid
- FRIDA download: https://frida.fooddata.dk/data?lang=en
- FRIDA disclaimer: https://frida.fooddata.dk/disclaimer?lang=en
- BEDCA portal: https://www.bedca.net/bdpub/index_en.php
- FAO/INFOODS databases: https://www.fao.org/infoods/infoods/tables-and-databases/faoinfoods-databases/en/

## Phase-by-phase integration plan

### Phase 0 — Foundation (schema + legal gate)

**Goal:** Create one canonical ingestion model before adding new sources.

**Canonical model (minimum):**
- `foods` (canonical food identity + source references)
- `food_names` (locale + language variants + synonyms)
- `nutrients` (canonical nutrient dictionary: code, unit, display name)
- `food_nutrients` (food_id, nutrient_id, value_per_100g, raw_unit, source, source_version)
- `food_portions` (household measures, grams conversion)
- `food_provenance` (source URL, license type, attribution text, retrieval date, checksum)

**Legal gate checklist:**
- Confirm commercial use rights for each source/version.
- Store mandatory attribution text per source.
- Store share-alike or redistribution constraints in metadata.

**Exit criteria:**
- Canonical schema approved.
- Source-by-source legal matrix approved.

### Phase 1 — API-first quick win (CNF)

**Goal:** Add CNF as the first incremental provider with minimal ETL friction.

**Work:**
- Build `CNFClient` adapter for Food/Nutrient/NutrientAmount/ServingSize endpoints.
- Map CNF nutrient identifiers and `tagname` into canonical nutrient dictionary.
- Normalize all values to per-100g canonical fields.
- Add source attribution rendering in API responses where needed.

**Exit criteria:**
- CNF-backed search and nutrient hydration works in staging.
- Unit tests for nutrient mapping/normalization pass.

### Phase 2 — Batch ETL bundle (CoFID + CIQUAL + AFCD + AUSNUT)

**Goal:** Add high-value regional nutrient coverage through scheduled ingestion.

**Work:**
- Build reusable ETL framework: extract -> map -> normalize -> load -> validate.
- Create source-specific mappers for each dataset’s nutrient IDs/columns.
- Implement dedupe and merge rules:
  - Region-aware preference (e.g., UK users -> CoFID first).
  - Source-priority fallback chain when values conflict.
- Version datasets and support re-import without destructive overwrite.

**Exit criteria:**
- All four datasets ingested with reproducible runs.
- Data-quality checks (nulls, ranges, unit consistency) pass.

### Phase 3 — Secondary sources (BEDCA + FRIDA)

**Goal:** Expand regional depth where demand exists.

**Work:**
- BEDCA: implement adapter with strict terms-compliance controls.
- FRIDA: add periodic download/import workflow (form/request operational step).
- Add locale weighting logic so these sources only outrank when regionally relevant.

**Exit criteria:**
- Regional source routing works by locale/profile.
- Compliance notes for BEDCA/FRIDA embedded in provenance metadata.

### Phase 4 — Long-tail enrichment (FAO/INFOODS + NEVO)

**Goal:** Fill niche nutrient gaps after legal confirmation.

**Work:**
- Import selected FAO/INFOODS tables where licensing is compatible.
- Import NEVO after agreement checks and citation requirements are encoded.
- Use these sources as fallback-only unless explicitly region-selected.

**Exit criteria:**
- Long-tail sources available behind feature flags.
- Legal compliance evidence attached to each imported dataset version.

## Source precedence and merge policy

1. **Barcode/product identity:** Open Food Facts first.
2. **Core nutrient authority:** region-first national table (CNF/CoFID/CIQUAL/AFCD/AUSNUT), then USDA fallback.
3. **Conflict handling:** prefer latest dataset version in same source; otherwise use source-priority matrix by locale.
4. **Transparency:** return `source`, `sourceVersion`, and `retrievedAt` with nutrient payloads.

### Current implementation note

Food products now persist `SourceVersion`, `LicenseType`, `Attribution`, and `RetrievedAt` through the API and Azure Table Storage roundtrip. Search merging uses barcode or source/external ID identity before falling back to brand/name. Additives expose `EvidenceSources`; seeded references are category-level background sources and must not be treated as claim-specific citations.
The canonical sodium field is `SodiumMg100g` / `sodiumMg100g`: sodium is stored and exposed as milligrams per 100 g, matching meal-item `SodiumMg`. The Table Storage mapper reads legacy `Sodium100g` values as grams and converts them during read compatibility handling.
Provider mappings now carry source version, license, attribution, and retrieval timestamps instead of replacing them with generic API labels. Food search accepts an optional `region=AU|US` hint; Australian whole-food searches prefer AUSNUT, US/default whole-food searches prefer USDA, and branded searches prefer Open Food Facts. The region is part of the cache key.

## Operational guardrails

- Add per-source freshness SLAs (e.g., monthly check for new releases).
- Keep raw source snapshots for reproducibility/audit.
- Block promotion to production if attribution or legal metadata is missing.
- Add data-contract tests for canonical nutrient fields and units.

## Repo-ready backlog (files + tests + order)

### 1) Add source constants and trust policy (start here)

**Change**
- `backend/src/GutAI.Domain/Constants/DataSources.cs` (add: `CNF`, `CIQUAL`, `COFID`, `AFCD`, `BEDCA`, `FRIDA`, `INFOODS`, `NEVO`)
- `backend/src/GutAI.Infrastructure/Data/FoodScoring.cs` (replace hardcoded `"USDA"/"AUSNUT"` trust checks with shared policy)
- **Add:** `backend/src/GutAI.Infrastructure/Data/FoodSourcePolicy.cs` (source trust, locale priority, fallback order)

**Tests**
- `backend/tests/GutAI.Infrastructure.Tests/FoodScoringUnitTests.cs`
- `backend/tests/GutAI.Infrastructure.Tests/FoodSearchRankingTests.cs`

### 2) Implement CNF provider (first shipping increment)

**Add**
- `backend/src/GutAI.Infrastructure/ExternalApis/CanadianNutrientFileClient.cs` (implements `IFoodApiService`)
- `backend/src/GutAI.Infrastructure/ExternalApis/CanadianNutrientFileModels.cs` (DTOs for CNF responses)

**Change**
- `backend/src/GutAI.Infrastructure/DependencyInjection.cs` (register CNF HttpClient + provider ordering)
- `backend/src/GutAI.Api/appsettings.Development.json` (CNF base URL + enable flag)
- `backend/src/GutAI.Api/appsettings.Production.json` (same keys)

**Tests**
- **Add:** `backend/tests/GutAI.Infrastructure.Tests/CanadianNutrientFileClientTests.cs` (mapping/unit conversions)
- `backend/tests/GutAI.IntegrationTests/SearchQualityTests.cs` (add CNF-enabled scenario)

### 3) Add ETL generators for downloadable national datasets

**Add**
- `tools/CofidFoodGenerator/generate.py`
- `tools/CiqualFoodGenerator/generate.py`
- `tools/AfcdFoodGenerator/generate.py`

**Generate/commit outputs**
- `backend/src/GutAI.Infrastructure/Data/CofidFoodsDatabase.cs`
- `backend/src/GutAI.Infrastructure/Data/CiqualFoodsDatabase.cs`
- `backend/src/GutAI.Infrastructure/Data/AfcdFoodsDatabase.cs`

**Change**
- `backend/src/GutAI.Infrastructure/ExternalApis/CofidFoodApiService.cs` (new)
- `backend/src/GutAI.Infrastructure/ExternalApis/CiqualFoodApiService.cs` (new)
- `backend/src/GutAI.Infrastructure/ExternalApis/AfcdFoodApiService.cs` (new)
- `backend/src/GutAI.Infrastructure/DependencyInjection.cs` (register and order providers)

**Tests**
- `backend/tests/GutAI.Infrastructure.Tests/FoodSearchIndexIntegrationTests.cs`
- `backend/tests/GutAI.IntegrationTests/SearchQualityTests.cs`

### 4) Add provenance fields for legal/compliance traceability

**Change**
- `backend/src/GutAI.Domain/Entities/FoodProduct.cs` (add `SourceVersion`, `LicenseType`, `Attribution`, `RetrievedAt`)
- `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs` (**both** `UpsertFoodProductAsync` and `MapToFoodProduct`)
- `backend/src/GutAI.Application/Common/DTOs/Dtos.cs` (`FoodProductDto`)
- `backend/src/GutAI.Api/Endpoints/FoodEndpoints.cs` (`MapToDto`)
- `frontend/src/types/index.ts` (`FoodProduct`, `MealFood` if exposed)

**Tests (required by your guardrails)**
- `backend/tests/GutAI.IntegrationTests/TableStorageCrudTests.cs` (roundtrip)
- `backend/tests/GutAI.Api.Tests/FoodContractTests.cs` (JSON shape)
- `scripts/check-contracts.js` mapping updates if needed

### 5) Expand secondary sources (BEDCA, FRIDA) behind flags

**Add**
- `backend/src/GutAI.Infrastructure/ExternalApis/BedcaFoodApiService.cs`
- `backend/src/GutAI.Infrastructure/ExternalApis/FridaFoodApiService.cs`

**Change**
- `backend/src/GutAI.Infrastructure/DependencyInjection.cs` (feature flags + ordering)
- `backend/src/GutAI.Api/appsettings*.json` (enable/disable flags)

**Tests**
- `backend/tests/GutAI.Infrastructure.Tests/*` for parsing/mapping
- Keep integration tests deterministic by defaulting these flags off in test config

### 6) Frontend/legal disclosure updates (must ship with provider changes)

**Change**
- `frontend/app/privacy.tsx` (third-party services list)
- `frontend/app/sources.tsx` (new references)
- `README.md` (External APIs section + required env vars/flags)

### 7) Rollout order (practical release sequence)

1. Source constants + scoring policy refactor
2. CNF provider (flagged on in dev/staging)
3. CoFID/CIQUAL/AFCD ETL + provider registration
4. Provenance fields + contract/roundtrip tests
5. BEDCA/FRIDA flagged rollout
6. INFOODS/NEVO optional fallback stage

### 8) Verification commands per milestone

- `cd backend && dotnet build --verbosity quiet`
- `cd backend && dotnet test tests/GutAI.Infrastructure.Tests --verbosity minimal`
- `cd backend && dotnet test tests/GutAI.Api.Tests --verbosity minimal`
- `node scripts/check-contracts.js`
- `cd frontend && npx tsc --noEmit`

## Task board (ticketized backlog)

| Ticket | Title | Depends on | Primary files | Acceptance criteria |
|---|---|---|---|---|
| T1 | Source constants + policy abstraction | — | `DataSources.cs`, `FoodScoring.cs`, `FoodSourcePolicy.cs` (new) | New sources defined; no hardcoded USDA/AUSNUT trust checks remain; ranking tests pass |
| T2 | CNF API client implementation | T1 | `CanadianNutrientFileClient.cs` (new), `CanadianNutrientFileModels.cs` (new) | CNF search returns mapped `FoodProductDto` with normalized nutrients and `DataSource="CNF"` |
| T3 | CNF wiring + feature flags | T2 | `DependencyInjection.cs`, `appsettings.Development.json`, `appsettings.Production.json` | CNF enabled/disabled by config; provider order deterministic; app boots in both modes |
| T4 | CNF provider tests | T2,T3 | `CanadianNutrientFileClientTests.cs` (new), `SearchQualityTests.cs` | Client mapping tests pass; CNF-enabled integration scenario passes |
| T5 | ETL generator scaffolding (CoFID/CIQUAL/AFCD) | T1 | `tools/CofidFoodGenerator/generate.py`, `tools/CiqualFoodGenerator/generate.py`, `tools/AfcdFoodGenerator/generate.py` | Generators produce deterministic C# outputs from source files |
| T6 | Generated dataset providers | T5 | `CofidFoodsDatabase.cs` (new), `CiqualFoodsDatabase.cs` (new), `AfcdFoodsDatabase.cs` (new), provider services (new), `DependencyInjection.cs` | New datasets searchable via composite provider; source tags set correctly |
| T7 | Provenance fields on FoodProduct | T1 | `FoodProduct.cs`, `TableStorageStore.cs`, `Dtos.cs`, `FoodEndpoints.cs` | `SourceVersion/LicenseType/Attribution/RetrievedAt` persist + map both ways |
| T8 | Contract + roundtrip + type sync | T7 | `TableStorageCrudTests.cs`, `FoodContractTests.cs`, `frontend/src/types/index.ts`, `scripts/check-contracts.js` (if needed) | Roundtrip test passes; API shape assertions updated; TS contract check passes |
| T9 | Frontend/legal disclosure update | T3,T6,T7 | `frontend/app/privacy.tsx`, `frontend/app/sources.tsx`, `README.md` | Third-party list and source references match shipped providers |
| T10 | BEDCA/FRIDA secondary rollout | T1,T9 | `BedcaFoodApiService.cs` (new), `FridaFoodApiService.cs` (new), `DependencyInjection.cs`, appsettings flags | Both providers run behind flags; disabled-by-default in tests |
| T11 | CI hardening for data-source evolution | T4,T8 | test projects + CI commands | `make ci` passes; failures clearly identify contract/roundtrip/source-mapping breaks |

### Suggested execution batches

- **Batch A (ship fast):** T1 -> T2 -> T3 -> T4
- **Batch B (regional depth):** T5 -> T6
- **Batch C (compliance + UX):** T7 -> T8 -> T9
- **Batch D (optional expansion):** T10 -> T11
