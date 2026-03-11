# Data Files Comprehensive Audit Report

**Date**: 2025-01-XX
**Scope**: All `*Data.cs` files + inline data in `FodmapService.cs` + consuming services + tests
**Status**: Research/Discovery Complete — Ready for Implementation Planning

---

## Executive Summary

The GutAI backend has three data domains powering food safety scoring: **FODMAP triggers**, **Glycemic Index**, and **Gut Risk additives/ingredients**. The data is spread across 3 dedicated `*Data.cs` files plus ~600 lines of inline data in `FodmapService.cs`. After thorough analysis of the data files, consuming services, test suites, and the 50-food scoring analysis report, this audit identifies **critical structural issues, data gaps, severity mismatches, and scoring formula problems** that collectively produce only **53% FODMAP accuracy, 64% GI accuracy, and 68% Gut Risk accuracy**.

### Key Findings at a Glance

| Category     | Severity    | Finding                                                                                                      |
| ------------ | ----------- | ------------------------------------------------------------------------------------------------------------ |
| Architecture | 🔴 Critical | FodmapData is split: `FodmapData.cs` (unused) + inline arrays in `FodmapService.cs`                          |
| Duplication  | 🔴 Critical | FodmapService has massive entry duplication (IngredientTriggers + WholeFood_Triggers repeat ~80% of entries) |
| Scoring      | 🔴 Critical | Single High FODMAP trigger → score 55 "Moderate" (garlic, onion, honey all misrated)                         |
| GI Gaps      | 🟡 Major    | Only 3 vegetables in GI database; estimation fallback produces GI ~63 for all veggies                        |
| Data Gaps    | 🟡 Major    | Missing whole-food patterns for bread, kimchi (now fixed in WholeFood_Triggers)                              |
| Consistency  | 🟡 Major    | WholeFood_Triggers severity disagrees with IngredientTriggers for same foods                                 |
| Tests        | 🟢 Good     | 550+ unit tests, good coverage of detection; scoring threshold tests reflect current (flawed) formula        |

---

## 1. File Inventory & Architecture

### Data Files

| File               | Lines | Location                   | Actually Used?                                                                                                           |
| ------------------ | ----- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `FodmapData.cs`    | 1,520 | `Infrastructure/Services/` | ⚠️ **PARTIALLY** — `FodmapData` static class exists but `FodmapService.cs` has its OWN inline copy of all trigger arrays |
| `GlycemicData.cs`  | 352   | `Infrastructure/Services/` | ✅ Yes — consumed by `GlycemicIndexService.cs` via `GlycemicData.GiDatabase`                                             |
| `GutRiskData.cs`   | 957   | `Infrastructure/Services/` | ✅ Yes — consumed by `GutRiskService.cs` via static imports                                                              |
| `FodmapService.cs` | 1,313 | `Infrastructure/Services/` | ✅ Contains the ACTUAL data (~800 lines of inline trigger arrays)                                                        |

### Critical Architecture Issue: FodmapData.cs vs FodmapService.cs

`FodmapData.cs` defines static collections (`IngredientTriggers`, `WholeFoodTriggers`, `Additives`, `AdditiveNameTriggers`, `GenericWholeFoodPatterns`) populated in a static constructor.

**However**, `FodmapService.cs` defines its own `private static readonly` versions of ALL these same arrays inline (lines 280-1313). The service does NOT reference `FodmapData` at all — it uses its own local copies.

**Impact**: `FodmapData.cs` is effectively dead code. Any edits to `FodmapData.cs` will have NO effect on scoring. All FODMAP data changes must be made in `FodmapService.cs`.

**Evidence**: `FodmapService.cs` declares:

- `static readonly (string, Regex?, FodmapTriggerDto)[] IngredientTriggers` (lines 280-830)
- `static readonly (string, FodmapTriggerDto)[] WholeFood_Triggers` (lines 835-1230)
- `static readonly Dictionary<string, FodmapTriggerDto> FodmapAdditives` (lines 1232-1280)
- `static readonly (string, FodmapTriggerDto)[] AdditiveNameTriggers` (lines 1282-1303)
- `static readonly HashSet<string> GenericWholeFoodPatterns` (lines 1305-1313)

### Shared Utilities

| File                        | Lines | Purpose                                                                                                       |
| --------------------------- | ----- | ------------------------------------------------------------------------------------------------------------- |
| `SharedFodmapSeverities.cs` | 129   | Canonical severity map (116 entries) used by both FodmapService and GutRiskData to ensure consistent ratings  |
| `MatchUtils.cs`             | 22    | Shared regex utilities: `WordBoundary()`, `WordMatch()`, `IsLactoseFree()`, `IsGlutenFree()`, `IsDairyFree()` |
| `SubstitutionService.cs`    | 303   | Ingredient substitution suggestions (has own inline data, separate concern)                                   |

---

## 2. FodmapService.cs Data Analysis (The ACTUAL FODMAP Data)

### 2.1 IngredientTriggers Array (~220 entries)

Scans `product.Ingredients` text. Entries are `(pattern, regex?, FodmapTriggerDto)`.

**Categories covered:**

- **Fructans** (~65 entries): wheat, barley, rye, spelt, garlic, onion, shallot, leek, artichoke, asparagus, beetroot, brussels sprout, inulin, chicory, FOS, oligofructose, fennel, pistachio, cashew, couscous, semolina, bulgur, kamut, farro, freekeh, breadcrumb, panko, pasta, noodle, pita, naan, tortilla, cracker, pretzel, croissant, brioche, crouton, breadstick, rye bread, pumpernickel, muffin, scone, biscuit, wafer, stuffing, granola, muesli, cereal bar, protein bar, fiber supplement, spring onion, scallion, savoy cabbage, radicchio, dandelion, sun-dried tomato, dried date/fig/cranberry, persimmon, chicory coffee, prebiotic
- **GOS** (~25 entries): chickpea, lentil, kidney bean, black bean, baked bean, soybean, soy milk, hummus, lima bean, split pea, navy bean, pinto bean, cannellini, borlotti, broad bean, fava bean, mung bean, adzuki, edamame, tempeh, soy flour, soy protein, pea protein, lupin, haricot, refried bean, bean paste, black-eyed pea, butter bean, garbanzo, dal makhani, chana
- **Lactose** (~25 entries): whole/skim/low-fat/fat-free/reduced-fat milk, milk powder, whey powder, whey concentrate, cream cheese, ricotta, cottage cheese, ice cream, custard, condensed milk, evaporated milk, buttermilk, generic milk (with "milk thistle" exclusion), lactose, yogurt, mascarpone, paneer, fresh mozzarella, goat cheese, cream (with "cream of tartar" exclusion), sour cream, half and half, dulce de leche, milk/white chocolate, cheese spread, processed cheese, milk solid, whipping/clotted cream, infant formula
- **Excess Fructose** (~30 entries): HFCS, agave, honey, apple/pear juice, mango, fruit juice concentrate, crystalline fructose, glucose-fructose/fructose-glucose syrup, fruit sugar, date syrup, golden syrup, treacle, tamarillo, boysenberry, fig, guava, jackfruit, cider, pomegranate, grape juice, fruit nectar/paste/leather, dried mango/pineapple, raisin, sultana, currant, jam, marmalade, kombucha, coconut water, rum, dessert wine, port
- **Polyols** (~30 entries): sorbitol, glucitol, mannitol, maltitol, xylitol, isomalt, erythritol, lactitol, hydrogenated starch, apple, pear, apricot, nectarine, peach, plum, cherry (×2 — one with word boundary, one with `cherr(y|ies)` regex), watermelon, prune, mushroom, cauliflower, celery, sweet potato, snow pea, sugar snap, blackberry, lychee, longan, avocado, dried apple/pear/peach/cherry, sugar-free gum/candy/mint/chocolate, "sugar free", shiitake, portobello, oyster mushroom, porcini, enoki, truffle, chanterelle
- **Sauces & Condiments** (~25 entries): teriyaki, BBQ sauce, ketchup, pasta/pizza sauce, curry paste, stock cube, bouillon, gravy, seasoning mix, onion/garlic powder/salt, ranch/caesar dressing, salsa, pesto, chutney, relish, kimchi, tzatziki, guacamole, worcestershire, hoisin, oyster sauce, fish sauce, miso
- **Other**: carrageenan, trail mix

### 2.2 WholeFood_Triggers Array (~130 entries)

Scans `product.Name` using word-boundary regex matching. Entries are `(pattern, FodmapTriggerDto)`.

**Categories covered:**

- Prepared dishes: garlic bread, onion ring, bean soup, dal, falafel, pizza, burrito, lasagna, ramen, udon, gyoza, dumpling, samosa, spring roll, empanada, quiche, carbonara, alfredo, bolognese, minestrone, french onion, cream of mushroom, mac and cheese, grilled cheese, tabbouleh, bruschetta, focaccia, sourdough, bagel, pancake, waffle, french toast, protein shake, milkshake, chili con carne, tikka masala, butter chicken, korma, vindaloo, biryani, shepherd/cottage pie, clam chowder, pad thai, fried rice, fish and chips, chicken nugget, pot pie, calzone, quesadilla, enchilada, nachos, risotto, ravioli, tortellini, sausage/meat roll/pie, wrap, sandwich, taco, hummus, baba ganoush
- Whole foods repeated from IngredientTriggers: garlic, onion, shallot, leek, spring onion, scallion, wheat, wheat flour, whole wheat, wheat starch, rye, barley, spelt, kamut, farro, freekeh, bulgur, semolina, couscous, pasta, noodle, naan, pita, tortilla, cracker, rye bread, pumpernickel, inulin, chicory root/fibre, FOS, oligofructose, prebiotic, chicory coffee, all GOS legumes, all dairy items, all fructose items, all polyol items
- Special patterns: kimchi, sauerkraut, bread (generic), almond/oat/coconut/rice/hemp/soy milk (dairy-free safeguards), cherries (plural form), blackberries (plural form), pistachio

### 2.3 FodmapAdditives Dictionary (7 entries)

Only polyol E-numbers: E420 (Sorbitol), E421 (Mannitol), E953 (Isomalt), E965 (Maltitol), E966 (Lactitol), E967 (Xylitol), E968 (Erythritol)

### 2.4 AdditiveNameTriggers (10 entries)

Name-based matching for: sorbitol, mannitol, maltitol, xylitol, isomalt, erythritol, lactitol, lactose, inulin, fructooligosaccharide

### 2.5 DUPLICATION ANALYSIS

**The IngredientTriggers and WholeFood_Triggers arrays overlap massively.** Nearly every food in IngredientTriggers is duplicated in WholeFood_Triggers. This is by design (ingredients text vs product name scanning), but creates maintenance burden and **SEVERITY INCONSISTENCIES**.

**Severity mismatches between IngredientTriggers and WholeFood_Triggers for the SAME food:**

| Food                           | IngredientTriggers Severity                              | WholeFood_Triggers Severity | Note                               |
| ------------------------------ | -------------------------------------------------------- | --------------------------- | ---------------------------------- |
| Yogurt                         | Moderate                                                 | **High**                    | WholeFoodTrigger escalates         |
| Cream                          | Moderate                                                 | **High**                    | WholeFoodTrigger escalates         |
| Buttermilk                     | Moderate                                                 | **High**                    | WholeFoodTrigger escalates         |
| Cream cheese                   | Moderate                                                 | **High**                    | WholeFoodTrigger escalates         |
| Sour cream                     | Moderate                                                 | **High**                    | WholeFoodTrigger escalates         |
| Apricot (IngredientTriggers)   | Has TWO entries: High (first) and Moderate (second)      | —                           | Internal duplication in same array |
| Peach (IngredientTriggers)     | Has TWO entries: High (first) and Moderate (second)      | —                           | Internal duplication in same array |
| Plum (IngredientTriggers)      | Has TWO entries: High (first) and Moderate (second)      | —                           | Internal duplication in same array |
| Nectarine (IngredientTriggers) | Has TWO entries: High (first) and Moderate (second)      | —                           | Internal duplication in same array |
| Cherry (IngredientTriggers)    | Has THREE entries (word boundary, cherries regex, plain) | —                           | Internal duplication               |
| Spelt (IngredientTriggers)     | Listed twice                                             | —                           | Exact duplicate                    |

**These duplicates within IngredientTriggers don't cause runtime issues** because `HasTrigger()` deduplicates by SubCategory+Category. The first match wins, so the second entry is effectively dead code. But it makes maintenance confusing.

**The WholeFood severity escalation for dairy IS intentional** — whole dairy products (e.g., a cup of yogurt) have more lactose impact than yogurt as one ingredient among many. But this should be documented.

---

## 3. GlycemicData.cs Analysis

### 3.1 Structure

- 272 entries in a single `GiDatabase` list, sorted by pattern length descending (longest matches first)
- Each `GiEntry` has: Pattern, pre-compiled Regex (word boundary), GI value, Category, Source, Notes, optional Exclusions array
- Exclusions prevent false matches (e.g., "rice" excludes "licorice", "rice milk", "rice cake", etc.)

### 3.2 Coverage by Category

| Category                  | Count | Assessment          |
| ------------------------- | ----- | ------------------- |
| Breads & Bakery           | 29    | ✅ Excellent        |
| Rice & Grains             | 36    | ✅ Excellent        |
| Potatoes & Starchy Veg    | 18    | ✅ Good             |
| Fruits                    | 38    | ✅ Excellent        |
| Juices & Beverages        | 24    | ✅ Good             |
| Legumes                   | 18    | ✅ Excellent        |
| Sugars & Sweeteners       | 11    | ✅ Good             |
| Dairy                     | 6     | 🟡 Sparse           |
| Snacks & Processed        | 30    | ✅ Good             |
| Cereals & Breakfast       | 16    | ✅ Good             |
| Indian Staples            | 9     | ✅ Good             |
| Asian                     | 11    | ✅ Good             |
| Latin/African/European/ME | 16    | ✅ Adequate         |
| Meal Items                | 5     | 🟡 Could expand     |
| **Vegetables**            | **3** | **🔴 Critical gap** |

### 3.3 Critical GI Gaps

**Vegetables (only 3 entries!)**: carrot (39), beetroot (64), pea (48). Missing ALL of:

- Broccoli (~15), cauliflower (~15), cabbage (~10), lettuce (~15), tomato (~15), cucumber (~15), celery (~15), zucchini (~15), eggplant (~15), spinach (~15), kale (~15), bell pepper (~15), green beans (~15), asparagus (~15), onion (~10)

**Impact**: Any vegetable search without a database match falls to `EstimateFromNutrition()`, which starts at base 35-55 and adjusts, often producing GI ~40-63 for vegetables that should be ~10-15.

**The service now has a <5g carb floor returning "Not Applicable"**, which handles many vegetables correctly. But vegetables with 5-10g carbs (carrots, peas, corn) still need accurate entries.

**Other gaps:**

- No nuts/seeds section: almonds (~15), walnuts (~15), peanuts (~14), cashews (~22)
- No meat/protein section (GI ~0 for all meat/fish/eggs — the <5g carb floor handles these via "Not Applicable")
- Missing some common foods: French toast, bread pudding, rice porridge (congee IS there), tapioca pudding

### 3.4 GI Estimation Fallback

`EstimateFromNutrition()` (lines 315-356 of GlycemicIndexService.cs):

- Base = 35 if carbs < 10g, else 55
- Adjustments: +8/+15 for high sugar ratio, -3/-6 for fiber, -2/-4 for protein, -3/-6 for fat
- NOVA 4 bonus: +8
- Name heuristics: "whole grain" -8, "instant" +10, "raw" -5
- Clamped [20, 95]

**The <5g carb floor is already implemented** — returns `null` GI with "Not Applicable" for very low-carb foods. This was one of the P0 fixes from the scoring report and appears to be already fixed.

---

## 4. GutRiskData.cs Analysis

### 4.1 Structure

Most architecturally clean of the three files. Uses:

- `CategoryMap`: 32 categories → `(TriggerType, FodmapClass)` tuples for classification
- `GutHarmfulAdditives`: ~120 E-number additives with `AdditiveInfo` records
- `IngredientPatterns`: ~150 ingredient text patterns with `IngredientRiskEntry` records
- `WholeFoodRiskPatterns`: ~60 product name patterns (✅ this collection EXISTS and IS used — the scoring report's Issue #3 about GutRiskService being "blind to whole foods" appears to have been FIXED)
- Shared severity via `SharedFodmapSeverities.GetRiskLevel()`

### 4.2 GutHarmfulAdditives Coverage (~120 entries)

| Category              | Example E-numbers                                                        | Count |
| --------------------- | ------------------------------------------------------------------------ | ----- |
| Emulsifiers           | E433, E435, E436, E471, E472a-e, E476, E481, E491                        | ~20   |
| Thickeners            | E407 (carrageenan), E412, E415, E440, E460-466                           | ~15   |
| Emulsifier/Thickener  | E401-405 (alginates), E410, E414, E417, E418                             | ~15   |
| Sugar Alcohols        | E420, E421, E953, E965, E966, E967, E968                                 | 7     |
| Artificial Sweeteners | E950, E951, E952, E954, E955, E961, E962                                 | 7     |
| Preservatives         | E200-203, E210-213, E220-228, E249-252, E270, E280-283                   | ~25   |
| Colorants             | E102, E104, E110, E122, E124, E129, E132, E133, E150d, E155, E160b, E171 | ~12   |
| Phosphates            | E338-341, E450-452                                                       | ~8    |
| Acidity Regulators    | E330, E331, E332, E334, E262, E260                                       | ~6    |
| Antioxidants          | E300, E301, E302, E304, E306, E307, E310, E315, E316, E319-321           | ~11   |

### 4.3 WholeFoodRiskPatterns Coverage (~60 entries)

**Fructan Sources**: garlic, onion, shallot, leek, spring onion, scallion, artichoke, asparagus, wheat, barley, rye, inulin, chicory, FOS, oligofructose, pistachio, cashew, kimchi, sauerkraut, bread

**GOS Sources**: lentil, chickpea, black bean, kidney bean, soybean, baked bean, navy bean, pinto bean, split pea, hummus, falafel, edamame, bean flour, lentil flour, chickpea protein, faba bean

**Fructose Sources**: honey, watermelon, mango, fig, grape

**Polyol Sources**: mushroom, cauliflower, cherry (`cherr(y|ies)` regex), blackberry (regex), avocado, apple, pear, peach, apricot, plum, prune, nectarine, lychee, sweet potato, celery

**Dairy/Lactose**: milk (with "milk thistle" exclusion), yogurt (with `yogu?h?rt` regex), ice cream, cream (with "cream of tartar" exclusion), cheese

**Hidden FODMAP Risks**: dehydrated vegetables, vegetable extract, savory/savoury flavoring, yeast extract

### 4.4 GutRiskData Gaps

- **No "bread" pattern** in WholeFoodRiskPatterns (exists in FodmapService WholeFood_Triggers but missing from GutRiskData)
- **Missing fermented foods**: kombucha, tempeh, miso entries in WholeFoodRiskPatterns
- **Missing condiments**: ketchup, BBQ sauce, teriyaki, salsa, pesto as whole food patterns
- **No dried fruit entries** in WholeFoodRiskPatterns (dried mango, dried fig, raisins — these concentrate FODMAPs)

---

## 5. Scoring Formula Analysis

### 5.1 FODMAP Scoring (FodmapService.cs `CalculateFodmapScore`)

```
multiplier = 1.0
For each trigger:
  High    → ×0.55
  Moderate → ×0.85
  Low     → ×0.95
If 3+ distinct subcategories: ×0.92^(count-2)
Score = clamp(round(100 × multiplier), 0, 100)
```

**Thresholds:**

- ≥75 → Low FODMAP
- ≥55 → Moderate FODMAP
- ≥30 → High FODMAP
- <30 → Very High FODMAP

**Problem**: A single High trigger → 100 × 0.55 = **55** → "Moderate FODMAP". This means garlic, onion, honey, lentils, chickpeas (all definitively "High FODMAP" per Monash) are rated "Moderate".

**The scoring report recommended** changing High penalty to ×0.40 or adjusting thresholds. Neither has been implemented.

### 5.2 Gut Risk Scoring (GutRiskService.cs `CalculateGutScore`)

```
score = 100
For each flag:
  High   → -20 × multiplier
  Medium → -10 × multiplier
  Low    → -5 (or -2 for stacking) × multiplier
multiplier: Fodmap=1.0, Processing=0.8, Nutrient=0.8
+5 fiber bonus (reduced if FODMAP fibers present)
```

**Thresholds:** ≥80 Good, ≥60 Fair, ≥40 Poor, <40 Bad

### 5.3 GI Scoring (GlycemicIndexService.cs)

Direct lookup from database. Position-weighted averaging for multi-ingredient matches (60%/25%/10%/5%). Falls back to nutrition estimation. GL = GI × carbs × serving / 10000.

---

## 6. Test Coverage Analysis

### Test Files

| File                           | Lines | Tests                                                    |
| ------------------------------ | ----- | -------------------------------------------------------- |
| `FodmapServiceTests.cs`        | 730   | ~60 tests covering detection, scoring, dedup, edge cases |
| `GlycemicIndexServiceTests.cs` | 384   | ~30 tests covering known foods, estimation, edge cases   |
| `GutRiskServiceTests.cs`       | 2,843 | ~150+ tests (most comprehensive)                         |
| `FoodScoringUnitTests.cs`      | 306   | Cross-cutting scoring tests                              |

### Test Observations

**FodmapServiceTests**:

- Good detection coverage for all FODMAP categories
- Tests **enshrine current scoring formula** (e.g., `SingleHighTrigger_Drops25Points` asserts score=55 and rating="Moderate FODMAP" for garlic)
- Tests verify deduplication, lactase mitigation, generic whole food skipping, dairy-free milk safeguards
- Missing: tests for cherry plural matching, kimchi detection, bread detection

**GlycemicIndexServiceTests**:

- Good coverage of known food GI values
- Tests verify the <5g carb floor ("Not Applicable")
- Tests verify estimation heuristics
- Missing: tests for vegetable GI accuracy

**GutRiskServiceTests**:

- Most comprehensive test suite at 2,843 lines
- Covers additive detection, ingredient scanning, stacking penalties, amplifiers
- Missing: dedicated tests for WholeFoodRiskPatterns effectiveness

---

## 7. Gap Analysis — What's Missing from the Data

### 7.1 Missing FODMAP WholeFood_Triggers (P1)

These are now present (bread, kimchi, sauerkraut, cherries, dairy-free milks were added). The major remaining gaps:

| Missing Pattern           | Should Trigger                                              | Severity |
| ------------------------- | ----------------------------------------------------------- | -------- |
| `"broccoli"`              | Fructan (Moderate at ≤3/4 cup)                              | Moderate |
| `"cabbage"`               | Fructan/GOS (Moderate)                                      | Moderate |
| `"corn"` / `"sweet corn"` | Sorbitol (Moderate) per Monash                              | Moderate |
| `"persimmon"`             | Already in IngredientTriggers but NOT in WholeFood_Triggers | High     |
| `"jackfruit"`             | Already in IngredientTriggers but NOT in WholeFood_Triggers | Moderate |
| `"boysenberry"`           | Already in IngredientTriggers but NOT in WholeFood_Triggers | High     |
| `"tamarillo"`             | Already in IngredientTriggers but NOT in WholeFood_Triggers | High     |
| `"lychee"`                | Already in IngredientTriggers but NOT in WholeFood_Triggers | Moderate |
| `"longan"`                | Already in IngredientTriggers but NOT in WholeFood_Triggers | Moderate |

### 7.2 Missing GI Database Entries (P1)

**Vegetables (high priority — most common searches):**

| Food        | Actual GI | Source        |
| ----------- | --------- | ------------- |
| Broccoli    | 15        | Very low carb |
| Cauliflower | 15        | Very low carb |
| Cabbage     | 10        | Very low carb |
| Lettuce     | 15        | Very low carb |
| Tomato      | 15        | Very low carb |
| Cucumber    | 15        | Very low carb |
| Celery      | 15        | Very low carb |
| Zucchini    | 15        | Very low carb |
| Eggplant    | 15        | Very low carb |
| Spinach     | 15        | Very low carb |
| Kale        | 15        | Very low carb |
| Bell pepper | 15        | Very low carb |
| Green beans | 15        | Very low carb |
| Asparagus   | 15        | Very low carb |
| Onion       | 10        | Very low carb |
| Mushroom    | 15        | Very low carb |

Note: Many of these have <5g carbs/100g, so the existing "Not Applicable" floor will catch them. But for those in the 5-10g range (corn at 19g, peas at 14g, carrots at 10g), explicit entries matter. Carrots (39), peas (48), and corn (52) ARE already in the database. The issue is mainly with the estimation fallback for unmatched vegetables.

**Nuts & Seeds:**

| Food            | Actual GI | Source        |
| --------------- | --------- | ------------- |
| Peanut          | 14        | Sydney Uni    |
| Almond          | 15        | Estimated     |
| Walnut          | 15        | Estimated     |
| Cashew          | 22        | Sydney Uni    |
| Macadamia       | 10        | Estimated     |
| Pecan           | 10        | Estimated     |
| Pistachio       | 15        | Estimated     |
| Sunflower seeds | 20        | Estimated     |
| Pumpkin seeds   | 25        | Estimated     |
| Chia seeds      | 1         | Very low carb |
| Flax seeds      | 0         | Very low carb |
| Peanut butter   | 14        | Sydney Uni    |

### 7.3 Missing GutRiskData WholeFoodRiskPatterns (P1)

| Missing Pattern                     | Category                         | Risk Level  |
| ----------------------------------- | -------------------------------- | ----------- |
| `"bread"`                           | High-FODMAP Ingredient (Fructan) | High        |
| `"pasta"`                           | High-FODMAP Ingredient (Fructan) | High        |
| `"noodle"`                          | High-FODMAP Ingredient (Fructan) | High        |
| `"ramen"`                           | High-FODMAP Ingredient (Fructan) | High        |
| `"pizza"`                           | High-FODMAP Ingredient (Fructan) | Medium      |
| `"couscous"`                        | High-FODMAP Ingredient (Fructan) | High        |
| `"tortilla"`                        | High-FODMAP Ingredient (Fructan) | Medium      |
| Dried fruits                        | Fructose/Sorbitol Source         | Medium-High |
| Condiments (BBQ, ketchup, teriyaki) | Hidden FODMAP Risk               | Low-Medium  |

### 7.4 SharedFodmapSeverities Gaps

The shared severities map has 116 entries. Missing some entries that appear in both FodmapService and GutRiskData:

- `"bread"` — not in shared map
- `"pasta"` — not in shared map
- `"broccoli"` — not in shared map
- `"cabbage"` — not in shared map
- `"corn"` — not in shared map

---

## 8. Scoring Issues (from Scoring Analysis Report)

### P0 Issues

| #   | Issue                                   | Status    | Fix Location                                                                                           |
| --- | --------------------------------------- | --------- | ------------------------------------------------------------------------------------------------------ | ----------- |
| 1   | Search ranking favors packaged products | NOT FIXED | Search infrastructure (not data files)                                                                 |
| 2   | Single High trigger → "Moderate" FODMAP | NOT FIXED | `FodmapService.cs` `CalculateFodmapScore()` — change `0.55` multiplier to ~`0.40` or adjust thresholds |
| 3   | GutRiskService blind to whole foods     | **FIXED** | `GutRiskData.cs` has `WholeFoodRiskPatterns` and `GutRiskService.cs` step 3b uses them                 |
| 4   | Cherry plural bug                       | **FIXED** | `FodmapService.cs` has both `"cherry"` and `"cherries"` patterns; GutRiskData uses `cherr(y            | ies)` regex |

### P1 Issues

| #   | Issue                                               | Status              | Fix Location                                                                            |
| --- | --------------------------------------------------- | ------------------- | --------------------------------------------------------------------------------------- |
| 5   | GI estimation nonsensical for low-carb              | **PARTIALLY FIXED** | `<5g` carb floor implemented; `<10g` still uses base=35 which is better but not perfect |
| 6   | Missing FODMAP patterns (kimchi, bread, sauerkraut) | **FIXED**           | All three now in WholeFood_Triggers                                                     |
| 7   | Almond milk false lactose trigger                   | **FIXED**           | Dairy-free milk safeguards in WholeFood_Triggers (ordered before generic "milk")        |
| 8   | GI database gaps for vegetables                     | **PARTIALLY FIXED** | carrot, beetroot, pea added; still missing most vegetables                              |

---

## 9. Recommendations (Priority Order)

### P0 — Must Fix (Scoring Accuracy)

1. **Fix FODMAP scoring multiplier**: Change High from `×0.55` to `×0.40` (or `×0.35`). This makes single High trigger → score 40 → "High FODMAP". Two High triggers → 16 → "Very High FODMAP". Update all test assertions accordingly.

2. **Consolidate FodmapData.cs and FodmapService.cs**: Either delete `FodmapData.cs` (since it's unused) or refactor `FodmapService.cs` to consume `FodmapData` like the other two services do. Current state is confusing — two sources of truth where one is dead code.

3. **Fix severity inconsistencies between IngredientTriggers and WholeFood_Triggers**: Dairy products have Moderate in IngredientTriggers but High in WholeFood_Triggers. This should be explicitly documented or standardized. Recommendation: use `SharedFodmapSeverities` for both, with a documented reason for any overrides.

### P1 — Should Fix (Data Gaps)

4. **Add GI database entries for common vegetables**: Even if most have <5g carbs (handled by floor), adding explicit low-GI entries prevents the estimation fallback from producing wrong values for edge cases.

5. **Add GI database entries for nuts/seeds**: Peanuts (14), almonds (15), cashews (22), peanut butter (14).

6. **Add missing WholeFood patterns to GutRiskData**: bread, pasta, noodle, pizza, couscous, tortilla, dried fruits, condiments.

7. **Add missing WholeFood patterns to FodmapService**: broccoli, cabbage, corn, persimmon, boysenberry, tamarillo, lychee, longan.

8. **Clean up IngredientTriggers duplicates**: Remove the second entries for cherry, apricot, peach, plum, nectarine, spelt that are dead code.

### P2 — Nice to Have (Architecture & Maintenance)

9. **Refactor FodmapService data to external file**: Move the ~800 lines of inline data arrays to `FodmapData.cs` (properly this time), making the service code and data clearly separated like GlycemicData/GutRiskData.

10. **Add SharedFodmapSeverities entries**: bread, pasta, broccoli, cabbage, corn, and any other foods used by both services.

11. **Add data-specific tests**: Tests that validate WholeFood_Triggers coverage against a known list of "must detect" foods. Tests that validate IngredientTriggers have no severity conflicts.

12. **Gut/FODMAP score reconciliation**: Add post-scoring step that caps Gut Score when FODMAP score is poor (currently independent — food can have Gut 100 and FODMAP 55 simultaneously).

---

## 10. Implementation Notes

### Changing the FODMAP Scoring Multiplier

The multiplier change from 0.55 → 0.40 for High severity will affect these test assertions:

| Test                                 | Current Assert               | New Assert (×0.40)            |
| ------------------------------------ | ---------------------------- | ----------------------------- |
| `SingleHighTrigger_Drops25Points`    | score=55, "Moderate FODMAP"  | score=40, "High FODMAP"       |
| `TwoHighTriggers_Drops50Points`      | score=47                     | Recalculate: depends on dedup |
| `ManyHighTriggers_ClampedAt0`        | score=17                     | Recalculate                   |
| `Score40To64_IsHighFodmap`           | score=55, "Moderate"         | score=40, "High"              |
| `ScoreBelow40_IsVeryHighFodmap`      | score=37, "High"             | Recalculate                   |
| `GarlicAndOnionPasta_VeryHighFodmap` | score=55, dedup to 1 trigger | score=40                      |

**Key math**: With ×0.40 for High, ×0.85 for Moderate, ×0.95 for Low:

- Garlic alone (1 High Fructan): 100 × 0.40 = **40** → "High FODMAP" ✅
- Garlic + cream (1 High + 1 Moderate): 100 × 0.40 × 0.85 = **34** → "High FODMAP" ✅
- Garlic + onion (deduped to 1 High): 100 × 0.40 = **40** → "High FODMAP" ✅
- Two distinct High triggers: 100 × 0.40 × 0.40 = **16** → "Very High FODMAP" ✅

### CI Pipeline

Per AGENTS.md, `make ci` runs: build → Infrastructure.Tests (550+ tests) → Api.Tests → check-contracts → tsc. All test changes must pass this pipeline.

---

## Appendix A: File Cross-References

```
FodmapService.cs (1313 lines)
  ├── Imports: MatchUtils.cs (WordBoundary, IsLactoseFree, IsGlutenFree, IsDairyFree)
  ├── Does NOT import: FodmapData.cs
  ├── Inline data: IngredientTriggers[], WholeFood_Triggers[], FodmapAdditives{}, AdditiveNameTriggers[], GenericWholeFoodPatterns{}
  └── Tests: FodmapServiceTests.cs (730 lines, ~60 tests)

GlycemicIndexService.cs (356 lines)
  ├── Imports: GlycemicData.cs (GiDatabase)
  └── Tests: GlycemicIndexServiceTests.cs (384 lines, ~30 tests)

GutRiskService.cs (406 lines)
  ├── Imports: GutRiskData.cs (CategoryMap, GutHarmfulAdditives, IngredientPatterns, WholeFoodRiskPatterns, SugarAlcoholNames, ArtificialSweetenerNames)
  ├── Imports: MatchUtils.cs (IsLactoseFree, IsDairyFree)
  └── Tests: GutRiskServiceTests.cs (2843 lines, ~150 tests)

SharedFodmapSeverities.cs (129 lines)
  ├── Used by: GutRiskData.cs (GetRiskLevel() for severity lookup)
  └── Referenced concept in: FodmapService.cs (not actually imported — severities are hardcoded inline)

FodmapData.cs (1520 lines) — ⚠️ DEAD CODE (not imported by any service)
```

## Appendix B: Quick Entry Count Summary

| Collection                         | Count      | Location                  |
| ---------------------------------- | ---------- | ------------------------- |
| FodmapService IngredientTriggers   | ~220       | FodmapService.cs inline   |
| FodmapService WholeFood_Triggers   | ~130       | FodmapService.cs inline   |
| FodmapService FodmapAdditives      | 7          | FodmapService.cs inline   |
| FodmapService AdditiveNameTriggers | 10         | FodmapService.cs inline   |
| GlycemicData GiDatabase            | 272        | GlycemicData.cs           |
| GutRiskData GutHarmfulAdditives    | ~120       | GutRiskData.cs            |
| GutRiskData IngredientPatterns     | ~150       | GutRiskData.cs            |
| GutRiskData WholeFoodRiskPatterns  | ~60        | GutRiskData.cs            |
| SharedFodmapSeverities             | 116        | SharedFodmapSeverities.cs |
| **Total data entries**             | **~1,085** |                           |
