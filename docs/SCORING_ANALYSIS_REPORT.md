# GutAI Scoring Analysis Report

**Date**: June 2025
**Method**: Automated Playwright testing of 50 foods against the live API (Docker)
**Endpoint tested**: `/api/food/search` → `/api/food/{id}/safety-report`

---

## Executive Summary

50 foods (30 whole foods, 20 store-bought/prepared) were tested through the GutAI search and scoring pipeline. The results reveal **5 critical systemic issues** and **8 moderate issues** that significantly impact scoring accuracy. Overall accuracy by service:

| Service        | Accurate    | Inaccurate  | Misleading | N/A       |
| -------------- | ----------- | ----------- | ---------- | --------- |
| FODMAP Score   | 25/47 (53%) | 15/47 (32%) | 7/47 (15%) | 3 timeout |
| Gut Risk Score | 32/47 (68%) | 12/47 (26%) | 3/47 (6%)  | 3 timeout |
| Glycemic Index | 30/47 (64%) | 12/47 (26%) | 5/47 (10%) | 3 timeout |

---

## Complete Results Table

| #   | Search Term    | Matched Product                              | Gut Score | FODMAP Score | GI      | Issues                            |
| --- | -------------- | -------------------------------------------- | --------- | ------------ | ------- | --------------------------------- |
| 1   | banana         | Banana and chocolate bar raw                 | 97 Good   | 100 Low      | 46 Low  | Wrong match                       |
| 2   | apple          | Appletiser - 100% Apple Juice                | 82 Good   | 50 High      | 41 Low  | Wrong match                       |
| 3   | avocado        | Avocados, raw, Florida                       | 100 Good  | 88 Low       | 45 Low  | ✅                                |
| 4   | blueberries    | Blueberries, raw                             | 100 Good  | 100 Low      | 53 Low  | ✅                                |
| 5   | strawberries   | Strawberries, raw                            | 100 Good  | 100 Low      | 40 Low  | ✅                                |
| 6   | mango          | Mangos, raw                                  | 100 Good  | 88 Low       | 51 Low  | ✅                                |
| 7   | watermelon     | Watermelon, raw                              | 100 Good  | 75 Mod       | 76 High | Gut/FODMAP mismatch               |
| 8   | orange         | Orange peel, raw                             | 100 Good  | 100 Low      | 43 Low  | Wrong match                       |
| 9   | grapes         | Grapes, muscadine, raw                       | 100 Good  | 100 Low      | 46 Low  | ✅                                |
| 10  | cherries       | Cherries, sweet, raw                         | 100 Good  | 100 Low      | 64 Med  | **FODMAP miss**, GI wrong         |
| 11  | broccoli       | Broccoli, raw                                | 100 Good  | 100 Low      | 49 Low  | ✅                                |
| 12  | spinach        | Spinach, raw                                 | 100 Good  | 100 Low      | 49 Low  | ✅                                |
| 13  | sweet potato   | Sweet potato leaves, raw                     | 100 Good  | 88 Low       | 63 Med  | Wrong match                       |
| 14  | garlic         | Garlic, raw                                  | 100 Good  | 75 Mod       | 45 Low  | **Should be High FODMAP**         |
| 15  | onion          | Onions, raw                                  | 100 Good  | 75 Mod       | 63 Med  | **Should be High FODMAP**         |
| 16  | mushroom       | ❌ TIMEOUT                                   | -         | -            | -       | API timeout                       |
| 17  | cauliflower    | Cauliflower, raw                             | 100 Good  | 88 Low       | 63 Med  | ✅                                |
| 18  | kale           | Kale, raw                                    | 100 Good  | 100 Low      | 49 Low  | ✅                                |
| 19  | carrot         | Carrots, raw                                 | 100 Good  | 100 Low      | 57 Med  | GI wrong (should be ~16)          |
| 20  | celery         | Celery, raw                                  | 100 Good  | 88 Low       | 63 Med  | GI too high                       |
| 21  | chicken breast | Chicken breast, roll, oven-roasted           | 96 Good   | 100 Low      | 44 Low  | Wrong match                       |
| 22  | salmon         | Salmon nuggets, cooked                       | 100 Good  | 100 Low      | 41 Low  | Wrong match                       |
| 23  | eggs           | Eggs, Grade A, Large, egg yolk               | 100 Good  | 100 Low      | 41 Low  | ✅                                |
| 24  | ground beef    | Beef, grass-fed, ground, raw                 | 100 Good  | 100 Low      | None    | ✅ (no GI for pure protein)       |
| 25  | tofu           | Tofu yogurt                                  | 100 Good  | 88 Low       | 36 Low  | Wrong match, false lactose        |
| 26  | white rice     | White Rice Vinegar                           | 100 Good  | 100 Low      | 73 High | Wrong match                       |
| 27  | oatmeal        | Bread, oatmeal                               | 100 Good  | 100 Low      | 55 Low  | Wrong match                       |
| 28  | quinoa         | Quinoa, cooked                               | 100 Good  | 100 Low      | 53 Low  | ✅                                |
| 29  | lentils        | Lentils, raw                                 | 100 Good  | 75 Mod       | 32 Low  | **Should be High FODMAP**         |
| 30  | chickpeas      | Chickpea flour (besan)                       | 100 Good  | 75 Mod       | 28 Low  | **Should be High FODMAP**         |
| 31  | greek yogurt   | Brooklea Authentic Greek Yogurt              | 100 Good  | 75 Mod       | 32 Low  | ✅                                |
| 32  | cheddar cheese | ❌ TIMEOUT                                   | -         | -            | -       | API timeout                       |
| 33  | whole milk     | Yogurt, plain, whole milk                    | 100 Good  | 75 Mod       | 32 Low  | Wrong match                       |
| 34  | cottage cheese | Cottage Cheese                               | 92 Good   | 75 Mod       | 27 Low  | ✅                                |
| 35  | bread          | Bread, egg                                   | 100 Good  | 100 Low      | 42 Low  | **FODMAP miss**, GI wrong         |
| 36  | pasta          | Pasta, dry, enriched                         | 100 Good  | 75 Mod       | 49 Low  | ✅                                |
| 37  | peanut butter  | Peanut butter, reduced sodium                | 100 Good  | 100 Low      | 37 Low  | ✅                                |
| 38  | honey          | Honey                                        | 96 Good   | 75 Mod       | 61 Med  | **Should be High FODMAP**         |
| 39  | olive oil      | Bertolli Mediterranean Olive Oil (margarine) | 59 Poor   | 88 Low       | 27 Low  | **Completely wrong product**      |
| 40  | protein bar    | Nakd Bar                                     | 73 Fair   | 75 Mod       | 42 Low  | ✅                                |
| 41  | granola        | Granola LU                                   | 88 Good   | 88 Low       | 60 Med  | ✅                                |
| 42  | almond milk    | Lidl unsweetened Almond milk                 | 80 Good   | 75 Mod       | 27 Low  | False lactose trigger             |
| 43  | kombucha       | Remedy Kombucha                              | 80 Good   | 71 Mod       | 46 Low  | ✅                                |
| 44  | hummus         | Hummus, home prepared                        | 100 Good  | 75 Mod       | 46 Low  | Gut 100 despite FODMAP triggers   |
| 45  | dark chocolate | Green & Black Dark Chocolate                 | 100 Good  | 100 Low      | 40 Low  | ✅                                |
| 46  | rice cakes     | Marmite Rice Cakes                           | 40 Poor   | 63 Mod       | 68 Med  | Wrong product (flavored)          |
| 47  | corn tortilla  | Stockwell Tortilla Chips                     | 100 Good  | 88 Low       | 39 Low  | Wrong match (chips, not tortilla) |
| 48  | salsa          | ❌ TIMEOUT                                   | -         | -            | -       | API timeout                       |
| 49  | kimchi         | Cabbage, kimchi                              | 100 Good  | 100 Low      | 63 Med  | **FODMAP miss**, GI wrong         |
| 50  | sauerkraut     | Sauerkraut                                   | 100 Good  | 100 Low      | 63 Med  | ✅                                |

---

## CRITICAL Issues

### 1. 🔴 Search Relevance — Wrong Products Returned (14/50 foods)

**Impact**: Catastrophic. Users searching for basic whole foods get scored on entirely different products.

**Affected foods**:
| Search | Got Instead |
|--------|------------|
| banana | Banana and chocolate bar |
| apple | Appletiser 100% Apple Juice |
| orange | Orange peel, raw |
| sweet potato | Sweet potato leaves, raw |
| chicken breast | Chicken breast roll, oven-roasted |
| salmon | Salmon nuggets |
| tofu | Tofu yogurt |
| white rice | White Rice Vinegar |
| oatmeal | Bread, oatmeal |
| whole milk | Yogurt, plain, whole milk |
| olive oil | Bertolli Mediterranean Olive Oil (margarine!) |
| chickpeas | Chickpea flour (besan) |
| rice cakes | Marmite Rice Cakes |
| corn tortilla | Tortilla Chips |

**Root cause**: The Lucene search merges USDA FoodData Central results with OpenFoodFacts results and ranks by text relevance. OpenFoodFacts products with more metadata (ingredients, brands, nutri-score) often outrank simple whole food names from USDA. The ranking algorithm does not give a "whole food bonus" to unprocessed items.

**Fix**:

1. Add a `wholeFoodBoost` to the search ranking that boosts items with no brand, no additives, and NOVA group 1 (or null).
2. When a search term exactly matches a known whole food name (e.g., "banana", "apple", "chicken breast"), promote USDA entries that contain the term in position 0 of the product name.
3. Consider a `preferred_match` parameter that deprioritizes compound products (those containing commas or "and" in the name) when the search term is a single word.

---

### 2. 🔴 FODMAP Single-Trigger Scoring Floor — "High FODMAP" Foods Rated as "Moderate"

**Impact**: Critical. The most dangerous FODMAP foods are being underrated, giving users false confidence.

**The math problem**:

- A single "High" trigger deducts 25 points: 100 → 75
- Score 75 falls in the "Moderate FODMAP" band (60-79)
- The "High FODMAP" threshold requires score < 60, meaning 2+ High triggers

**Affected foods** (all definitively High FODMAP per Monash University):

| Food      | Monash Rating | GutAI Score | GutAI Rating | Triggers Found       |
| --------- | ------------- | ----------- | ------------ | -------------------- |
| Garlic    | High FODMAP   | 75          | Moderate     | 1 High (Fructan)     |
| Onion     | High FODMAP   | 75          | Moderate     | 1 High (Fructan)     |
| Honey     | High FODMAP   | 75          | Moderate     | 1 High (Fructose)    |
| Lentils   | High FODMAP   | 75          | Moderate     | 1 High (GOS)         |
| Chickpeas | High FODMAP   | 75          | Moderate     | 1 High (GOS)         |
| Hummus    | High FODMAP   | 75          | Moderate     | 1 High (GOS+Fructan) |

**Fix options**:

1. **Increase High trigger penalty**: Change from -25 to -35 or -40 for High severity. A single High trigger should land in the High FODMAP band (score < 60).
2. **Adjust thresholds**: Change "High FODMAP" threshold from <60 to ≤75, so score 75 = High FODMAP.
3. **Best approach**: Combine both. Use `-30` per High trigger and set thresholds to: ≥85 Low, ≥65 Moderate, ≥40 High, <40 Very High. This way a single High trigger (100-30=70) falls in "Moderate" but two high triggers (100-60=40) fall in "High", while still allowing the summary text to flag the food clearly.
4. **Alternative**: Add a rule: "If ANY High trigger's subcategory matches a known 'pure food' trigger (garlic, onion, honey), automatically cap the rating at 'High FODMAP' regardless of score."

---

### 3. 🔴 GutRiskService Blind to Whole Foods — Perfect Scores for High-FODMAP Foods

**Impact**: Critical. GutRiskService scans ONLY ingredient text and additive E-numbers. Whole foods from USDA have neither. Result: garlic, onion, lentils, chickpeas, honey, watermelon all score 100/100 "Good" on gut risk despite being well-known IBS triggers.

**The gap**: FodmapService has `WholeFood_Triggers` for product-name-based matching. GutRiskService has NO equivalent. It has `IngredientPatterns` (for text scanning) and `GutHarmfulAdditives` (for E-numbers), but nothing for matching against the product name itself.

**Affected foods** (Gut Score 100, but known gut irritants):

| Food       | Gut Score | FODMAP Score | Reality                           |
| ---------- | --------- | ------------ | --------------------------------- |
| Garlic     | 100       | 75 (Mod)     | Top IBS trigger worldwide         |
| Onion      | 100       | 75 (Mod)     | Top IBS trigger worldwide         |
| Watermelon | 100       | 75 (Mod)     | High FODMAP (fructose + mannitol) |
| Lentils    | 100       | 75 (Mod)     | High GOS                          |
| Chickpeas  | 100       | 75 (Mod)     | High GOS                          |
| Honey      | 96        | 75 (Mod)     | High fructose                     |
| Hummus     | 100       | 75 (Mod)     | Chickpeas + garlic                |
| Kimchi     | 100       | 100 (Low!)   | Contains garlic + onion           |

**Fix**:
Add a `WholeFoodRiskPatterns` dictionary to `GutRiskService` that matches product names, similar to `WholeFood_Triggers` in `FodmapService`. For whole foods without ingredient text, fall back to this name-based matching. This should flag:

- Garlic/onion → Fructan Source (High risk)
- Lentils/chickpeas/beans → GOS Source (Medium risk)
- Honey → Fructose Source (Medium risk)
- Mushrooms → Mannitol Source (Low risk)
- Watermelon → Fructose + Mannitol (Medium risk)

---

### 4. 🔴 Cherries FODMAP Pattern Bug — Plural Form Mismatch

**Impact**: High. The WholeFood_Triggers pattern `"cherry"` does not match product name `"cherries, sweet, raw"` because `String.Contains("cherry")` fails on `"cherries"` — the substring `"cherry"` is not present in `"cherries"` (letters diverge at position 6: 'i' vs 'y').

**Product**: "Cherries, sweet, raw" → FODMAP Score 100 (Low) — **should be High FODMAP (Fructose + Sorbitol)**

**Also affected GI**: Got estimated GI of 64 instead of the database value of 22. The GI pattern `"cherry"` also fails on `"cherries"` for the same reason, causing fallback to `EstimateFromNutrition()`.

**Fix**: Add plural patterns or use regex. Simplest fix — add `"cherries"` as a separate pattern entry in both `WholeFood_Triggers` and `GiDatabase`. Better fix — change the pattern matching to use regex or stemming so `cherr` prefix matches both forms.

**Other potentially affected plurals to audit**:

- "strawberries" → pattern `"strawberr"` ✅ (already handles this via prefix)
- "blueberries" → pattern `"blueberr"` ✅
- "grapes" → pattern `"grape"` ✅ ("grapes" contains "grape")
- "lentils" → pattern `"lentil"` ✅ ("lentils" contains "lentil")

---

### 5. 🔴 GI Estimation Fallback Produces Nonsensical Results for Low-Carb Foods

**Impact**: High. When no GI database pattern matches, `EstimateFromNutrition()` starts from a base of 55 and adjusts based on macronutrient ratios. This works okay for starchy foods but fails badly for low-carb vegetables and proteins.

**How it works**:

```
Base = 55
+ 8-15 if high sugar ratio (sugar/carbs > 0.3 or 0.6)
- 6-12 if high fiber
- 4-8 if high protein
- 3-6 if high fat
Clamped to [20, 95]
```

**Problem cases**:

| Food       | Estimated GI | Actual GI | Why Wrong                                                        |
| ---------- | ------------ | --------- | ---------------------------------------------------------------- |
| Carrot     | 57           | ~16 raw   | Sugar ratio 8.3/9.6=0.86 → +15, but raw carrots have very low GI |
| Celery     | 63           | ~15       | Almost no carbs (3g), sugar ratio high → +15, nonsensical        |
| Cherries   | 64           | 22        | Pattern "cherry" didn't match "cherries" → fell to estimation    |
| Kimchi     | 63           | ~15       | No pattern match, low carbs → base estimate dominates            |
| Sauerkraut | 63           | ~15       | Same issue as kimchi                                             |

**Fix**:

1. Add a carb floor check: if `carbs < 10g per 100g`, cap estimated GI at 40 (low-carb foods rarely have meaningful glycemic impact).
2. Add more patterns to the GI database for common vegetables: carrot, celery, cabbage, lettuce, tomato, cucumber, etc.
3. Return `null` / "Unknown" instead of a misleading estimate when carbs are very low (< 5g/100g) since GI is clinically meaningless for these foods.

---

## MODERATE Issues

### 6. 🟡 Kimchi FODMAP Completely Missed

Kimchi scores 100 (Low FODMAP) despite being made from cabbage + garlic + onion + chili. The product "Cabbage, kimchi" has no ingredients text, and the WholeFood_Triggers list has no "kimchi" pattern. Garlic and onion are present but only detectable via ingredient text or explicit name patterns.

**Fix**: Add `("kimchi", ...)` to WholeFood_Triggers with Severity "High" — kimchi inherently contains garlic and onion (fructans).

### 7. 🟡 Almond Milk False Lactose Trigger

Almond milk (a dairy-free product) triggered a "Lactose" FODMAP flag, scoring 75 Moderate. The WholeFood_Triggers pattern `"milk"` matches `"almond milk"` and adds a Lactose trigger. The pattern list should exclude known dairy-free milks.

**Fix**: Add negative patterns or specific entries for dairy alternatives:

- `("almond milk", new() { ... Severity = "Low", Explanation = "Almond milk is naturally lactose-free..." })`
- Or add a pre-check: if product name contains "almond milk", "oat milk", "coconut milk", "soy milk", "rice milk" → skip the generic `"milk"` lactose trigger.

### 8. 🟡 Bread FODMAP Miss — "Bread, egg" Shows Low FODMAP

"Bread, egg" (a wheat-based bread) scored 100 Low FODMAP. It has no ingredients text (USDA product), and while `WholeFood_Triggers` has `"wheat"` patterns, the product name "Bread, egg" doesn't contain "wheat". There's no `"bread"` pattern in WholeFood_Triggers.

**Fix**: Add `("bread", ...)` to WholeFood_Triggers with Fructan trigger (most bread is wheat-based). GI was also wrong — 42 instead of ~75 for white bread, because the GI database pattern `"white bread"` doesn't match `"bread, egg"`.

### 9. 🟡 Gut Score / FODMAP Score Incoherence

Multiple foods show a perfect Gut Score (100) alongside a Moderate FODMAP score:

- Watermelon: Gut 100 / FODMAP 75
- Lentils: Gut 100 / FODMAP 75
- Greek Yogurt: Gut 100 / FODMAP 75
- Pasta: Gut 100 / FODMAP 75

Users see "100 — Good for your gut" and "Moderate FODMAP" simultaneously, which is confusing. The two services are independent and don't cross-reference.

**Fix**: Add a post-scoring reconciliation step: if FODMAP score is Moderate or worse, cap the Gut Score at 90 (or apply a multiplier). Alternatively, surface both scores clearly in the UI with explanation text like "Gut score is good for general health but may be problematic if you have IBS/FODMAP sensitivity."

### 10. 🟡 GI = 63 for All Estimated Vegetables

Multiple whole vegetables got GI ~63 from the estimation fallback: celery, cauliflower, sweet potato (leaves), kimchi, sauerkraut, onion. The estimation function's base of 55 + sugar ratio adjustment converges to ~63 for many vegetable nutritional profiles. This is misleading since most raw vegetables have GI under 30.

**Fix**: (Same as Critical Issue #5 — add carb floor check and more GI database entries for vegetables.)

### 11. 🟡 API Timeouts (3/50 foods)

Mushroom, cheddar cheese, and salsa all timed out (>30s). These are likely caused by slow OpenFoodFacts API calls or Lucene indexing delays. Should be investigated for performance issues.

### 12. 🟡 Tofu Gets False Lactose Trigger

"Tofu yogurt" matched the `"yogurt"` lactose pattern, giving tofu a false FODMAP trigger. This is a search relevance issue (wrong product) combined with a scoring issue (tofu yogurt is typically dairy-free).

### 13. 🟡 Cauliflower Rated Low FODMAP (Score 88)

Cauliflower contains mannitol (detected as Moderate severity, -12 points → score 88). Per Monash, cauliflower is High FODMAP in normal serving sizes. The Moderate severity rating for the cauliflower mannitol pattern may be too lenient.

---

## Summary of Recommended Fixes (Priority Order)

### P0 — Must Fix

1. **Search ranking**: Add whole-food boost to deprioritize packaged products when searching for basic food names
2. **FODMAP thresholds**: Increase High trigger penalty from -25 to -35, OR lower the "High FODMAP" threshold
3. **GutRiskService name matching**: Add WholeFood risk patterns for product-name-based scoring (garlic, onion, etc.)
4. **Cherry plural bug**: Add "cherries" pattern to both WholeFood_Triggers and GiDatabase

### P1 — Should Fix

5. **GI estimation carb floor**: Cap estimated GI at 40 when carbs < 10g/100g, or return null when < 5g/100g
6. **Add missing FODMAP patterns**: kimchi, bread, sauerkraut (if garlic-containing)
7. **Dairy-free milk safeguard**: Prevent false lactose triggers for almond milk, oat milk, etc.
8. **GI database gaps**: Add entries for carrot (16), celery (15), cabbage (10), tomato (15)

### P2 — Nice to Have

9. **Gut/FODMAP reconciliation**: Add cross-referencing between the two scores
10. **FODMAP confidence scoring**: Surface confidence level when scoring is based only on product name
11. **API timeout investigation**: Debug slow searches for mushroom, cheddar cheese, salsa
12. **Cauliflower severity**: Consider upgrading cauliflower mannitol from Moderate to High

---

## Methodology Notes

- All tests run against `localhost:5000` (Docker container `gut-ai-api-1`)
- Authentication: JWT bearer token from test account registration
- Search used first result from `/api/food/search?q={term}&limit=5`
- Safety report from `/api/food/{id}/safety-report`
- Comparison data from Monash University FODMAP Database, Sydney GI Tables
- Full raw results saved to `scripts/food-analysis-results.json`
- Test script: `scripts/food-analysis.py`
