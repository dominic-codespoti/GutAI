# GutAI Food & FODMAP System Analysis Report

**Date**: March 29, 2026  
**Scope**: Comprehensive analysis of food handling, FODMAP implementation, and related systems  
**Sources**: 14 parallel subagent analyses covering backend, frontend, tests, and documentation

---

## Executive Summary

The GutAI codebase contains a sophisticated food management system with **7,000+ whole foods**, **5,000+ branded products**, and comprehensive FODMAP assessment capabilities. However, critical issues exist in the FODMAP scoring formula, significant data duplication, and documentation gaps that require immediate attention.

### Key Findings

| Area | Status | Critical Issues |
|------|--------|-----------------|
| **Domain Model** | ✅ Well-designed | 6 core entities with clear relationships |
| **FODMAP Service** | ⚠️ **Critical bug** | Single High trigger scores "Moderate" instead of "High" |
| **Food Search** | ✅ Excellent | Lucene.NET with 3-tier scoring, 50+ special cases |
| **External APIs** | ✅ Robust | OpenFoodFacts + USDA with fallback chains |
| **Food Databases** | ✅ Comprehensive | 12k+ items across 3 databases |
| **Tests** | ⚠️ Good coverage | 3,000+ lines but gaps in GI/GutRisk services |
| **Documentation** | ❌ **Major gaps** | FODMAP_SERVICE_ANALYSIS.md is empty |
| **Storage** | ✅ Proper | All fields in Upsert/MapTo per AGENTS.md |
| **Frontend** | ✅ Type-safe | 26 interface↔DTO mappings, contract checked |

---

## 1. Domain Entities & Models

### Core Food Entities

**FoodProduct** (`backend/src/GutAI.Domain/Entities/FoodProduct.cs`)
- 30 properties including nutrition (per 100g), NOVA group, Nutri-Score
- **Key fields**: `FoodKind` (WholeFood/Branded/Unknown), `DataSource`, `SafetyScore`, `SafetyRating`
- **Caching**: 24-hour TTL with `IsCacheExpired()` method
- **Soft delete**: `IsDeleted` flag for logical deletion

**CustomFood** (`backend/src/GutAI.Domain/Entities/CustomFood.cs`)
- User-scoped entity (per-user partition in Table Storage)
- Stores nutrition **per serving** (not per 100g like FoodProduct)
- Audit trail: `CreatedAt`, `UpdatedAt`

**FoodAdditive** (`backend/src/GutAI.Domain/Entities/FoodAdditive.cs`)
- 19 properties including multi-region regulation (US/EU)
- **Safety metrics**: CSPI rating, FDA adverse events/recall counts
- **Navigation**: Many-to-many via `FoodProductAdditive`

**MealItem** (`backend/src/GutAI.Domain/Entities/MealItem.cs`)
- Links meals to foods with serving calculations
- Stores **scaled nutrition** based on servings consumed
- Optional `FoodProductId` for linking to catalog

### Entity Relationships

```
User → CustomFood (1:N, user-scoped)
User → FavoriteFoodProduct (1:N, junction table)
User → UserFoodAlert (1:N, additive alerts)
FoodProduct ↔ FoodAdditive (N:M via FoodProductAdditive)
MealLog → MealItem (1:N)
MealItem → FoodProduct (N:1, optional)
```

### Key Design Patterns

1. **FoodKind enum** discriminates whole foods vs branded products
2. **DataSource tracking** (Manual, USDA, AUSNUT, OpenFoodFacts) for provenance
3. **Value objects**: `NutritionInfo` record for immutable nutrition data
4. **SafetyRating enum**: Unknown=0, Safe=1, Caution=2, Warning=3, Avoid=4

---

## 2. FODMAP Implementation

### Architecture

The `FodmapService` (`backend/src/GutAI.Infrastructure/Services/FodmapService.cs`) uses a **6-layer detection pipeline**:

| Layer | Detection Method | Patterns |
|-------|------------------|----------|
| 1 | Ingredient text scan | 900+ patterns in inline arrays |
| 2 | Additive tags check | E420-E968 (polyols) |
| 3 | Linked additive names | Sugar alcohols (sorbitol, mannitol, etc.) |
| 4 | High sugar detection | >30g sugar with fructose sources |
| 5 | Product name matching | 500+ whole food/dish patterns |
| 6 | Lactase mitigation | Downgrades lactose if lactase present |

### Data Structures

**FODMAP Categories:**
- **Oligosaccharide**: Fructan (wheat, garlic, onion), GOS (chickpeas, lentils)
- **Disaccharide**: Lactose (milk, soft cheese)
- **Monosaccharide**: Excess Fructose (apple, pear, honey)
- **Polyol**: Sorbitol (E420), Mannitol (E421), Maltitol (E965), Erythritol (E968)

### ⚠️ Critical Scoring Bug

**Current formula** (line ~200 in FodmapService.cs):
```csharp
Base score = 100
Multiplier = 1.0 × (severity multipliers)
Severity multipliers:
- High: ×0.55  ← BUG: Should be ×0.40
- Moderate: ×0.85
- Low: ×0.95
```

**Impact**: Single High FODMAP trigger (garlic, onion, honey) produces score 55 → **"Moderate FODMAP"** when it should score 40 → **"High FODMAP"**.

**Fix required**: Change High multiplier from 0.55 to 0.40.

### Data Quality Issues

1. **FodmapData.cs is dead code** - All FODMAP data lives in inline arrays within `FodmapService.cs`. Edits to `FodmapData.cs` have no effect.

2. **Massive duplication** - `IngredientTriggers` and `WholeFood_Triggers` share ~80% of entries:
   - "garlic" appears 4+ times
   - "onion", "wheat", "milk" similarly duplicated

3. **Severity inconsistencies**:
   | Food | IngredientTriggers | WholeFood_Triggers |
   |------|-------------------|-------------------|
   | Yogurt | Moderate | **High** |
   | Cream | Moderate | **High** |
   | Cream cheese | Moderate | **High** |

4. **Internal duplicates**: Apricot, peach, plum, nectarine each have TWO entries (first match wins). Cherry has THREE entries.

### Confidence Levels

| Condition | Confidence |
|-----------|------------|
| Detailed ingredients (>50 chars, commas) | **High** |
| Trusted whole food (USDA/AUSNUT) | **Medium** |
| No ingredients + branded product | **Low** |

---

## 3. Food Search & Scoring Infrastructure

### Technology Stack

- **Apache Lucene.NET 4.8** with `RAMDirectory` (in-memory)
- **Custom analyzer**: `FoodAnalyzer` with synonym support
- **Three-tier scoring**: BooleanQuery → CustomScoreQuery → Post-ranking

### Scoring Architecture

**Tier 1: Lucene BooleanQuery** (`FoodQueryBuilder.cs`)
- Phrase matching with slop
- Token-level exact, prefix, fuzzy matching
- Brand matching (25x boost)
- **51 multi-word synonyms** (e.g., "chicken breast" → ["chicken", "broilers", "breast", "meat"])

**Tier 2: Custom Score Query** (`FoodCustomScoreQuery.cs`)
```
Final Score = Lucene Score + (Static Quality × 8)
```
Quality multiplier tuned to let USDA foods compete with metadata-rich branded products.

**Tier 3: Post-Lucene Re-ranking** (`FoodScoring.FinalScore()`)
- Token coverage calculation (primary noun vs full name)
- 50+ category-specific rules (e.g., "corn" shouldn't return "corned beef")
- Nutrition plausibility validation

### Static Quality Factors (Index-Time)

| Factor | Points | Condition |
|--------|--------|-----------|
| Trusted Source | +0.4 | USDA or AUSNUT |
| Image | +0.1/+0.25 | Whole food vs branded |
| Ingredients | +0.05/+0.15 | Whole food vs branded |
| Whole Food | +0.5 | FoodKind.WholeFood |
| Short name | +0.3 | ≤40 characters |
| Hard penalty | -1.2 | "frozen", "canned", "baby food" |

### Key Features

- **Regional synonyms**: capsicum→pepper (AU/UK), prawns→shrimp, aubergine→eggplant
- **Depluralization**: Intelligent singular/plural matching
- **Nutrition plausibility**: Validates query-specific ranges (e.g., eggs should have protein >5g, carbs <20g)
- **Brand detection**: 5-minute cache of known brand tokens

---

## 4. External Food API Integrations

### Composite Provider Pattern

All APIs implement `IFoodApiService` with unified `FoodProductDto` output:

```csharp
public interface IFoodApiService
{
    string SourceName { get; }
    Task<FoodProductDto?> LookupBarcodeAsync(string barcode, ...);
    Task<List<FoodProductDto>> SearchAsync(string query, ...);
}
```

### API Sources

| API | Purpose | Data Types |
|-----|---------|------------|
| **OpenFoodFacts** | Global branded products | Barcodes, additives, NOVA, Nutri-Score |
| **USDA FoodData** | US whole foods + branded | Foundation Foods, SR Legacy, Branded |
| **AustralianFoodsDatabase** | Australian foods | AUSNUT 2011-13 (~114 items) |
| **WholeFoodsDatabase** | Offline whole foods | 7,261 USDA items |
| **BrandedFoodsDatabase** | Offline branded | 5,000 USDA items |

### Aggregation Strategy

**Search** (`CompositeFoodApiService.SearchPersonalizedAsync`):
1. Parallel execution across all clients
2. Deduplication by name (case-insensitive)
3. Lucene-powered merging with quality scoring
4. Personalization boosting (user's previously selected foods rank higher)

**Barcode Lookup** (`CompositeFoodApiService.LookupBarcodeAsync`):
- Sequential fallback: tries each client in order
- Returns first successful match
- Logs failures but continues

### Data Mapping

**OpenFoodFacts** → `FoodProductDto`:
- Brands formatted as `"Brand - Product Name"`
- `DataSource` = "OpenFoodFacts"
- `FoodKind` = `FoodKind.Branded`
- Nutrition per 100g

**USDA** → `FoodProductDto`:
- ALL CAPS names converted to Title Case
- Nutrient ID mapping (1008=calories, 1003=protein, etc.)
- `DataSource` = "USDA"
- Dual search: Foundation/SR Legacy + Branded in parallel

### Resilience

- **Safe wrappers**: Each search wrapped in try-catch, returns empty list on failure
- **Offline capability**: Local databases work without external APIs
- **Caching**: Search results cached (3 min for full, 30 sec for sparse)

---

## 5. Food Databases

### WholeFoodsDatabase.cs

- **7,261 entries** from USDA FoodData Central
- Categories: American Indian Foods (165), Baked Products (512), Vegetables, Fruits, etc.
- Auto-generated by `tools/UsdaFoodGenerator/generate.py`
- No ingredients (whole foods don't have ingredient lists)

### AustralianFoodsDatabase.cs

- **~114 hand-seeded entries** (expandable to ~3,700)
- Australian specialties: Tim Tams, Vegemite, Meat pies, Lamingtons
- Unique proteins: Kangaroo, crocodile, emu, barramundi
- Sources: AUSNUT 2011-13, FSANZ nutrient tables

### BrandedFoodsDatabase.cs

- **5,000 entries** from USDA Branded Foods
- **Full ingredient lists** included (crucial for FODMAP)
- Organized by product category (Alcohol, Bacon, Biscuits, etc.)
- Links to FDC IDs in comments
- Auto-generated by `tools/UsdaBrandedFoodGenerator/generate.py`

### Database Comparison

| Aspect | WholeFoods | AustralianFoods | BrandedFoods |
|--------|-----------|----------------|--------------|
| Entry count | 7,261 | ~114 | 5,000 |
| Includes brands | No | Mixed | Yes |
| Includes ingredients | No | No | Yes |
| Geographic focus | US/global | Australia | US/global |
| Generation | Auto | Hand-seeded + generator | Auto |

---

## 6. API Endpoints & DTOs

### User Endpoints (Require Authentication)

| Route | Method | Description |
|-------|--------|-------------|
| `/api/food/search` | GET | Search with personalization |
| `/api/food/barcode/{barcode}` | GET | Barcode lookup with fallback |
| `/api/food/additives` | GET | List all additives |
| `/api/food/{id}/safety-report` | GET | Comprehensive safety assessment |
| `/api/food/{id}/fodmap` | GET | FODMAP analysis |
| `/api/food/{id}/gut-risk` | GET | Gut risk assessment |
| `/api/food/{id}/substitutions` | GET | Substitution suggestions |
| `/api/food/{id}/glycemic` | GET | GI/GL assessment |
| `/api/food/{id}/personalized-score` | GET | User-specific score |
| `/api/food/custom` | GET/POST/PUT/DELETE | Custom food CRUD |
| `/api/food/parse-label` | POST | AI nutrition label parsing |

### Admin Endpoints (Require Admin Key)

| Route | Method | Description |
|-------|--------|-------------|
| `/api/food/` | POST | Create food product |
| `/api/food/{id}` | PUT | Update food product |
| `/api/food/{id}` | DELETE | Soft-delete product |

### Key DTOs

**FoodProductDto** (Response):
```csharp
Guid Id, string? Barcode, string Name, string? Brand, string? Ingredients,
int? NovaGroup, string? NutriScore, decimal? Calories100g, ...,
FoodKind FoodKind, string DataSource, int? SafetyScore, string? SafetyRating,
List<FoodAdditiveDto> Additives
```

**CustomFoodDto** (Request/Response):
```csharp
string Name, string? BrandName, decimal ServingSize, string ServingSizeUnit,
decimal Calories, decimal ProteinG, decimal CarbG, decimal FatG,
decimal? FiberG, decimal? SugarG, decimal? SodiumMg, string? Ingredients
```

**Validation Rules**:
- Name: required, max 300 chars (FoodProduct), max 200 (MealItem)
- Barcode: max 50 chars
- Ingredients: max 5000 chars
- Additives: max 100 per product
- Servings: 0-1000 range

---

## 7. Testing Coverage

### Comprehensive Tests (3,000+ lines)

**FoodSearchRankingTests.cs** (555 lines):
- Multi-source ranking (USDA beats branded for simple queries)
- Personalization boost
- Brand detection
- Synonym expansion (39 regional/colloquial synonyms)
- Full index integration (7,000+ foods)

**FoodSearchIndexIntegrationTests.cs** (2,727 lines):
- 100+ nutrition plausibility assertions
- Typo tolerance (brocoli→broccoli)
- Token order reversal (rice brown → brown rice)
- 50+ food categories (seafood, fruits, vegetables, grains, legumes)
- Regression tests (eggs not returning Alaska Native first)

**FodmapServiceTests.cs** (798 lines):
- Scoring logic (100→40 for single high trigger)
- All 5 FODMAP categories tested
- Lactase enzyme mitigation
- Deduplication logic
- Real-world products (Nutella, protein bars)

**FoodContractTests.cs** (252 lines):
- Response shape validation for all 15+ endpoints
- HTTP status codes
- JSON field assertions

### Test Coverage Gaps

| Area | Status | Priority |
|------|--------|----------|
| **GlycemicIndexService** | ❌ No tests | P0 - Critical |
| **GutRiskService** | ❌ No tests | P0 - Critical |
| **SubstitutionService** | ⚠️ Contract only | P1 - High |
| **Additive safety scoring** | ❌ No tests | P1 - High |
| **Nova Group assessment** | ❌ No tests | P2 - Medium |
| **NutriScore calculation** | ❌ No tests | P2 - Medium |
| **Barcode scanning flow** | ⚠️ Basic only | P2 - Medium |
| **Serving size normalization** | ❌ No tests | P2 - Medium |

---

## 8. Documentation Analysis

### Existing Documentation

| Document | Lines | Status | Key Content |
|----------|-------|--------|-------------|
| DATA_FILES_AUDIT_REPORT.md | 520 | ✅ Complete | Comprehensive data audit, scoring accuracy issues |
| free-food-databases-analysis.md | 279 | ✅ Complete | API-first integration strategy (CNF) |
| global-branded-food-data-analysis.md | 29 | ✅ Complete | Global provider architecture |
| fsanz-branded-food-database-integration.md | 176 | ✅ Complete | FSANZ integration implementation |
| ARCHITECTURE.md | N/A | ✅ Referenced | System architecture |
| **FODMAP_SERVICE_ANALYSIS.md** | **0** | ❌ **Empty** | **Critical gap** |

### Key Documentation Insights

**Phase-by-Phase Integration Plan**:
- **Phase 0**: Foundation (schema + legal gate)
- **Phase 1**: CNF API (Canadian Nutrient File) - quick win
- **Phase 2**: Batch ETL (CoFID + CIQUAL + AFCD + AUSNUT)
- **Phase 3**: Secondary sources (BEDCA + FRIDA)
- **Phase 4**: Long-tail enrichment (FAO/INFOODS + NEVO)

**Canonical Data Model** requires:
- `foods` - canonical identity + source references
- `food_names` - locale/language variants
- `nutrients` - canonical nutrient dictionary
- `food_nutrients` - per-100g values with provenance
- `food_provenance` - legal compliance metadata

### Scoring Accuracy (from DATA_FILES_AUDIT_REPORT.md)

| Service | Current Accuracy | Target |
|---------|-----------------|--------|
| FODMAP | 53% | >80% |
| GI | 64% | >80% |
| Gut Risk | 68% | >85% |

### P0 Issues Documented but Not Fixed

| # | Issue | Impact |
|---|-------|--------|
| 1 | Search ranking favors packaged products | UX degradation |
| 2 | Single High trigger → "Moderate" FODMAP | **Safety issue** |

---

## 9. Data Storage & Table Storage

### Entity Mappings (Per AGENTS.md Guardrails)

All food-related entities properly implement **both Upsert and MapTo methods**:

**FoodProduct** (30 properties mapped):
- PartitionKey: `FOOD` (global)
- RowKey: `Id` (GUID)
- Decimals stored as **strings** (invariant culture)
- Arrays stored as JSON
- Special BARCODE partition for lookups

**CustomFood** (13 properties mapped):
- PartitionKey: `userId.ToString()` (user-scoped)
- RowKey: `CUSTOMFOOD|{id}`
- Decimals stored as **doubles**
- ⚠️ **Issue**: `UpdatedAt` not set to `DateTime.UtcNow` before save

**UserFoodAlert** (4 properties):
- PartitionKey: `userId.ToString()`
- RowKey: `ALERT|{additiveId}`

**FavoriteFoodProduct** (4 properties):
- PartitionKey: `userId.ToString()`
- RowKey: `FAV|{foodProductId}`
- Redundant `FoodProductId` property (also in RowKey)

**FoodAdditive** (19 properties):
- PartitionKey: `ADDITIVE` (global)
- RowKey: `Id` (int)
- Complex regulatory enums stored as int
- JSON arrays for `AlternateNames`, `BannedInCountries`

### Query Patterns

User-scoped entities use prefix range queries:
```csharp
// GetUserFavoriteFoodsAsync
var filter = $"PartitionKey eq '{pk}' and RowKey ge 'FAV|' and RowKey lt 'FAV|~'";
```

### Decimal Storage Inconsistency

| Entity | Storage Type | Notes |
|--------|-------------|-------|
| FoodProduct | string | Uses `Str()`, `Dec()`, `DecN()` helpers |
| CustomFood | double | Cast to/from decimal |

**Recommendation**: Standardize on string storage (more precise for decimals).

---

## 10. Frontend Integration

### TypeScript Interfaces

**Core types** (`frontend/src/types/index.ts`):

```typescript
interface FoodProduct {
  id: string; barcode: string | null; name: string; brand: string | null;
  ingredients: string | null; imageUrl: string | null; /* ... 20+ fields ... */
  foodKind: FoodKind; dataSource: string | null; safetyScore: number | null;
  additives: FoodAdditive[]; matchConfidence: number;
}

interface FoodAdditive {
  id: number; eNumber: string | null; name: string; category: string;
  cspiRating: string; usStatus: string; euStatus: string; /* mapped from backend */
  safetyRating: string; healthConcerns: string; bannedInCountries: string[];
}

interface CustomFood {
  id?: string; name: string; brandName?: string | null;
  servingSize: number; servingSizeUnit: string;
  calories: number; proteinG: number; carbG: number; /* singular "carb" */
  fatG: number; fiberG?: number | null; sugarG?: number | null;
  sodiumMg?: number | null; ingredients?: string | null;
}
```

### API Client Patterns

**Axios-based client** (`frontend/src/api/client.ts`):
- 15-second timeout
- Automatic JWT injection
- Token refresh with request queuing
- Platform-specific base URL

**Food API methods** (`frontend/src/api/index.ts`):
```typescript
export const foodApi = {
  search: (q: string) => api.get<FoodProduct[]>("/api/food/search", { params: { q } }),
  lookupBarcode: (code: string) => api.get<FoodProduct>(`/api/food/barcode/${code}`),
  safetyReport: (id: string) => api.get<SafetyReport>(`/api/food/${id}/safety-report`),
  // ... 15+ methods
};
```

### Frontend/Backend DTO Mismatches

**Known mismatches** (handled by contract checker):

| Frontend | Backend | Mapping |
|----------|---------|---------|
| `usStatus` | `usRegulatoryStatus` | Manual endpoint mapping |
| `euStatus` | `euRegulatoryStatus` | Manual endpoint mapping |
| `carbG` | `CarbG` | Naming convention |
| `imageFrontUrl` | N/A | Frontend-only field |

### Contract Testing

**Automated checking** (`scripts/check-contracts.js`):
- 26 interface↔DTO pairs
- `KNOWN_EXCEPTIONS` documents intentional mismatches
- Run via `make check-contracts`

---

## 11. Food Diary Analysis

### Core Functionality

**FoodDiaryAnalysisService** (`backend/src/GutAI.Infrastructure/Services/FoodDiaryAnalysisService.cs`):

**Temporal Correlation**:
- Matches symptoms to meals 1-8 hours prior (`MinOnsetHours`, `MaxOnsetHours`)
- Groups by (FoodName, SymptomName) pairs
- Calculates average severity and onset time

**Confidence Scoring**:
- **High**: 5+ occurrences AND avg severity ≥5/10
- **Medium**: 3+ occurrences OR avg severity ≥6/10
- **Low**: Everything else

**Elimination Diet Tracking**:
- Analyzes 90 days of data
- Identifies "safe foods" (eaten 5+ times with no correlations)
- Tracks phases: Not Started → Assessment → Elimination → Reintroduction → Maintenance
- Detects reintroduction attempts (7+ days absent, then reappears)

### ⚠️ Critical Timezone Bug

**Problem**: Meals not displayed for non-UTC users

**Root cause** (`MEAL_LOGGING_BUG_ANALYSIS.md`):
```typescript
// Frontend constructs hybrid timestamp:
const loggedAt = `${localDate}T${UTCtime}Z`  // WRONG

// Example: User in UTC+11 at 1:30 AM local on March 9
// Creates: 2026-03-09T14:30:00Z (March 9 14:30 UTC)
// Actual UTC: March 8 14:30
// Query for March 9 looks for March 8 13:00 - March 9 12:59 UTC
// Meal at March 9 14:30 UTC is OUTSIDE range
```

**Fix**: Use `buildLoggedAtUTC()` helper to properly convert local date+time to UTC.

### UX Issues (from MEALS_UX_ANALYSIS.md)

1. **Undiscoverable long-press actions** - No visual affordance
2. **No swipe-to-delete** - Breaks muscle memory
3. **Bottom sheet stacking** - Multiple modals on Android
4. **33 useState hooks** - "God component" anti-pattern
5. **Inconsistent tap behavior** - Some items navigate, others open sheet

---

## 12. Issues Summary & Recommendations

### P0 - Critical (Fix Immediately)

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| 1 | **FODMAP High multiplier bug** | `FodmapService.cs:200` | Safety misrating |
| 2 | **FODMAP documentation empty** | `docs/FODMAP_SERVICE_ANALYSIS.md` | Knowledge loss |
| 3 | **Meal logging timezone bug** | `frontend/components/meals/` | Data loss |
| 4 | **FodmapData.cs is dead code** | `FodmapData.cs` vs `FodmapService.cs` | Maintenance confusion |
| 5 | **CustomFood UpdatedAt not set** | `TableStorageStore.cs:1069` | Audit trail broken |

### P1 - High Priority

| # | Issue | Recommendation |
|---|-------|---------------|
| 1 | FODMAP scoring accuracy 53% | Fix multiplier, add GI/GutRisk tests |
| 2 | 80% data duplication in FodmapService | Consolidate IngredientTriggers + WholeFood_Triggers |
| 3 | Severity inconsistencies | Standardize dairy severity |
| 4 | GI database gaps | Add 20+ vegetable entries |
| 5 | No GlycemicIndexService tests | Create test file with 50+ cases |
| 6 | No GutRiskService tests | Create test file with comprehensive coverage |
| 7 | Missing nuts/seeds in GI db | Add peanut, almond, walnut entries |

### P2 - Medium Priority

| # | Issue | Recommendation |
|---|-------|---------------|
| 1 | Decimal storage inconsistency | Standardize on string |
| 2 | Missing WholeFood patterns | Add to GutRiskData |
| 3 | SubstitutionService tests | Add behavioral tests |
| 4 | Nova Group assessment | Add tests |
| 5 | CNF API integration | Implement Phase 1 |
| 6 | FSANZ provider | Implement behind feature flag |
| 7 | Frontend validation | Add Zod for critical paths |

### Quick Wins

```bash
# 1. Fix FODMAP multiplier (5 minutes)
sed -i 's/HighSeverityMultiplier = 0.55/HighSeverityMultiplier = 0.40/' FodmapService.cs

# 2. Fix CustomFood UpdatedAt (2 minutes)
# Add: food.UpdatedAt = DateTime.UtcNow; at line 1049

# 3. Run contract check
make check-contracts

# 4. Run tests
make ci
```

### Long-Term Architecture

1. **Unified FODMAP data source** - Move from inline arrays to external JSON/YAML
2. **Provenance tracking** - Add `food_provenance` table for legal compliance
3. **GI database expansion** - Target 500+ items (currently ~272)
4. **Machine learning** - Learn from user corrections to improve scoring
5. **Real-time sync** - WebSocket updates for collaborative features

---

## Appendix: File Locations Reference

### Core Food Files

| File | Path |
|------|------|
| FoodProduct entity | `backend/src/GutAI.Domain/Entities/FoodProduct.cs` |
| CustomFood entity | `backend/src/GutAI.Domain/Entities/CustomFood.cs` |
| FoodAdditive entity | `backend/src/GutAI.Domain/Entities/FoodAdditive.cs` |
| FoodKind enum | `backend/src/GutAI.Domain/Enums/FoodKind.cs` |
| FodmapService | `backend/src/GutAI.Infrastructure/Services/FodmapService.cs` |
| FodmapData (dead code) | `backend/src/GutAI.Infrastructure/Services/FodmapData.cs` |
| FoodSearchIndex | `backend/src/GutAI.Infrastructure/Data/FoodSearchIndex.cs` |
| FoodScoring | `backend/src/GutAI.Infrastructure/Data/FoodScoring.cs` |
| FoodQueryBuilder | `backend/src/GutAI.Infrastructure/Data/FoodQueryBuilder.cs` |
| FoodAnalyzer | `backend/src/GutAI.Infrastructure/Data/FoodAnalyzer.cs` |
| WholeFoodsDatabase | `backend/src/GutAI.Infrastructure/Data/WholeFoodsDatabase.cs` |
| AustralianFoodsDatabase | `backend/src/GutAI.Infrastructure/Data/AustralianFoodsDatabase.cs` |
| BrandedFoodsDatabase | `backend/src/GutAI.Infrastructure/Data/BrandedFoodsDatabase.cs` |
| OpenFoodFactsClient | `backend/src/GutAI.Infrastructure/ExternalApis/OpenFoodFactsClient.cs` |
| UsdaFoodDataClient | `backend/src/GutAI.Infrastructure/ExternalApis/UsdaFoodDataClient.cs` |
| CompositeFoodApiService | `backend/src/GutAI.Infrastructure/ExternalApis/CompositeFoodApiService.cs` |
| FoodEndpoints | `backend/src/GutAI.Api/Endpoints/FoodEndpoints.cs` |
| FoodProductDto | `backend/src/GutAI.Application/Common/DTOs/Dtos.cs` |
| CustomFoodDto | `backend/src/GutAI.Application/Common/DTOs/CustomFoodDto.cs` |
| TableStorageStore | `backend/src/GutAI.Infrastructure/Data/TableStorageStore.cs` |
| FoodDiaryAnalysisService | `backend/src/GutAI.Infrastructure/Services/FoodDiaryAnalysisService.cs` |

### Test Files

| File | Path |
|------|------|
| FoodScoringUnitTests | `backend/tests/GutAI.Infrastructure.Tests/FoodScoringUnitTests.cs` |
| FoodSearchRankingTests | `backend/tests/GutAI.Infrastructure.Tests/FoodSearchRankingTests.cs` |
| FoodSearchIndexIntegrationTests | `backend/tests/GutAI.Infrastructure.Tests/FoodSearchIndexIntegrationTests.cs` |
| FoodDiaryAnalysisServiceTests | `backend/tests/GutAI.Infrastructure.Tests/FoodDiaryAnalysisServiceTests.cs` |
| FoodProductEndpointsTests | `backend/tests/GutAI.IntegrationTests/FoodProductEndpointsTests.cs` |
| FoodContractTests | `backend/tests/GutAI.Api.Tests/FoodContractTests.cs` |
| FodmapServiceTests | `backend/tests/GutAI.Infrastructure.Tests/FodmapServiceTests.cs` |

### Frontend Files

| File | Path |
|------|------|
| TypeScript interfaces | `frontend/src/types/index.ts` |
| API client | `frontend/src/api/client.ts` |
| Food API methods | `frontend/src/api/index.ts` |
| Nutrition utilities | `frontend/src/utils/nutrition.ts` |
| Meal mappers | `frontend/src/utils/mealMappers.ts` |
| Contract checker | `scripts/check-contracts.js` |

### Documentation

| Document | Path |
|----------|------|
| FODMAP analysis (empty) | `docs/FODMAP_SERVICE_ANALYSIS.md` |
| Data files audit | `docs/DATA_FILES_AUDIT_REPORT.md` |
| Free food databases | `docs/free-food-databases-analysis.md` |
| Global branded food | `docs/global-branded-food-data-analysis.md` |
| FSANZ integration | `docs/fsanz-branded-food-database-integration.md` |
| Meal logging bug | `docs/MEAL_LOGGING_BUG_ANALYSIS.md` |
| Meals UX analysis | `docs/MEALS_UX_ANALYSIS.md` |
| Architecture | `docs/ARCHITECTURE.md` |
| AGENTS.md guardrails | `AGENTS.md` |

---

*Report generated by OpenCode with 14 parallel subagents analyzing 50+ files across backend, frontend, tests, and documentation.*
