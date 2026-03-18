# Implementing FSANZ Branded Food Database in GutAI

## Objective

Integrate FSANZ Branded Food Database data into GutAI for better Australian branded-food coverage (GTIN/barcode, brand, product, nutrition panel, ingredients).

## Important context

- FSANZ states the Branded Food Database is being developed and publication is subset/permission-based.
- Treat FSANZ as a **feature-flagged provider** initially.
- Keep existing sources (Open Food Facts, USDA, AUSNUT) active during rollout.

---

## 1) Access and ingestion mode

Choose one mode first:

1. **Public subset mode** (recommended initial): ingest published downloadable files when available.
2. **Partner mode**: ingest data available through FSANZ/GS1 partner pathways.

For both modes, store:
- source version
- retrieval date
- attribution/license metadata

---

## 2) Code changes (file-by-file)

### A. Add source constant

**Change**
- `backend/src/GutAI.Domain/Constants/DataSources.cs`

Add:
- `public const string FsanxBfd = "FSANZ_BFD";`

### B. Add config flags

**Change**
- `backend/src/GutAI.Api/appsettings.Development.json`
- `backend/src/GutAI.Api/appsettings.Production.json`

Add:
- `ExternalApis:FsanxEnabled` (bool)
- `ExternalApis:FsanxDataPath` (for file ingestion mode)
- `ExternalApis:FsanxBaseUrl` (if API endpoint becomes available)

### C. Add FSANZ provider implementation

**Add**
- `backend/src/GutAI.Infrastructure/ExternalApis/FsanzBrandedFoodApiService.cs`
- `backend/src/GutAI.Infrastructure/ExternalApis/FsanzBrandedFoodModels.cs`

`FsanzBrandedFoodApiService` should implement `IFoodApiService`:
- `SearchAsync(query)` -> return mapped `FoodProductDto` list
- `LookupBarcodeAsync(barcode)` -> return mapped `FoodProductDto?` when GTIN exists

### D. Add data loader (file-based mode)

**Add**
- `backend/src/GutAI.Infrastructure/Data/FsanzBrandedFoodsDatabase.cs`

Pattern to follow:
- `AustralianFoodsDatabase.cs`
- `BrandedFoodsDatabase.cs`

### E. Register in DI + provider order

**Change**
- `backend/src/GutAI.Infrastructure/DependencyInjection.cs`

Register FSANZ provider and include in `CompositeFoodApiService` list.  
Suggested order for AU-focused behavior:

1. OpenFoodFacts
2. FSANZ_BFD
3. USDA
4. WholeFood/AUSNUT/Branded static providers

Gate FSANZ registration with `ExternalApis:FsanxEnabled`.

### F. Search ranking trust updates

**Change**
- `backend/src/GutAI.Infrastructure/Data/FoodScoring.cs`

Current trust logic boosts USDA/AUSNUT only.  
Add FSANZ_BFD to trusted whole/branded source handling (carefully, with tests).

---

## 3) Field mapping (FSANZ -> GutAI)

| FSANZ field | GutAI target |
|---|---|
| GTIN | `FoodProductDto.Barcode` / `FoodProduct.Barcode` |
| Brand owner + brand name | `Brand` |
| Product name | `Name` |
| Ingredient statement | `Ingredients` |
| Nutrition panel values | `Calories100g`, `Protein100g`, `Carbs100g`, `Fat100g`, `Sugar100g`, `Sodium100g` |
| Serve size | `ServingSize`, `ServingQuantity` |
| Source URL/reference | `SourceUrl` |
| Source ID/version | `ExternalId` + provenance metadata |

Normalization rules:
- convert all nutrient values to per-100g where needed
- keep sodium in the codebase convention (grams in existing FoodProduct fields)

---

## 4) Provenance/compliance metadata (recommended)

To support legal traceability, add these fields on `FoodProduct`:
- `SourceVersion`
- `LicenseType`
- `Attribution`
- `RetrievedAt`

Then update:
- `TableStorageStore.UpsertFoodProductAsync`
- `TableStorageStore.MapToFoodProduct`
- `FoodProductDto` and `FoodEndpoints.MapToDto`
- frontend types if exposed

---

## 5) Tests to add/update

### Infrastructure tests

**Add**
- `backend/tests/GutAI.Infrastructure.Tests/FsanzBrandedFoodApiServiceTests.cs`

Cover:
- barcode lookup mapping
- search mapping
- unit conversion and null handling

### Integration tests

**Update**
- `backend/tests/GutAI.IntegrationTests/SearchQualityTests.cs`
- `backend/tests/GutAI.IntegrationTests/TableStorageCrudTests.cs` (if provenance fields added)

### API contract tests

**Update**
- `backend/tests/GutAI.Api.Tests/FoodContractTests.cs` (if response shape changes)

### Frontend type checks

Run:
- `node scripts/check-contracts.js`
- `cd frontend && npx tsc --noEmit`

---

## 6) Rollout plan

1. Implement provider + mapping behind `FsanxEnabled=false`.
2. Run CI + targeted FSANZ tests.
3. Enable in staging for AU test accounts only.
4. Compare search/barcode hit rate vs OFF-only baseline.
5. Enable for production AU traffic progressively.

---

## 7) Validation commands

- `cd backend && dotnet build --verbosity quiet`
- `cd backend && dotnet test tests/GutAI.Infrastructure.Tests --verbosity minimal`
- `cd backend && dotnet test tests/GutAI.Api.Tests --verbosity minimal`
- `node scripts/check-contracts.js`
- `cd frontend && npx tsc --noEmit`
