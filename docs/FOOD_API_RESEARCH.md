# Food & Nutrition API Research Analysis

> Comprehensive evaluation of free/freemium APIs for the GutAI .NET gut health tracking application.
> Last updated: July 2025

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current Stack Analysis](#current-stack-analysis)
3. [Category 1: Nutrition & Calorie APIs](#category-1-nutrition--calorie-apis)
4. [Category 2: Barcode & Product Database APIs](#category-2-barcode--product-database-apis)
5. [Category 3: Recipe & Ingredient APIs](#category-3-recipe--ingredient-apis)
6. [Category 4: Food Allergen & Additive Databases](#category-4-food-allergen--additive-databases)
7. [Category 5: Grocery & Branded Food APIs](#category-5-grocery--branded-food-apis)
8. [Category 6: FODMAP & Gut Health Specific APIs](#category-6-fodmap--gut-health-specific-apis)
9. [Comparison Matrix](#comparison-matrix)
10. [Integration Recommendations](#integration-recommendations)
11. [Implementation Roadmap](#implementation-roadmap)

---

## Executive Summary

After researching 15+ food and nutrition APIs, here are the **top findings for GutAI**:

### Key Takeaways

| Priority        | Recommendation                                                            | Why                                                                          |
| --------------- | ------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| 🔴 **Critical** | Keep **OpenFoodFacts** + **USDA FDC** as free fallbacks                   | Best free coverage. OFF has allergens + additives data you're underutilizing |
| 🟠 **High**     | Replace CalorieNinjas with **Edamam Food Database API** ($14/mo)          | NLP + barcode + **FODMAP_FREE label** + allergen labels in one API           |
| 🟡 **Medium**   | Add **Spoonacular** free tier for recipe analysis                         | Glycemic load, ingredient substitutes, intolerances — all gut-relevant       |
| 🟢 **Nice**     | Build internal **FODMAP scoring engine** using Monash data + OFF taxonomy | No FODMAP API exists; must be self-built                                     |
| 🔵 **Future**   | Evaluate **FatSecret Premier Free** for international expansion           | 56 countries, 24 languages, 90%+ barcode coverage                            |

### Cost Analysis (Monthly)

| Stack Configuration                                            | Cost       | Coverage                                                           |
| -------------------------------------------------------------- | ---------- | ------------------------------------------------------------------ |
| Current (CalorieNinjas + OFF + USDA)                           | ~$10-20/mo | NLP + barcode + search                                             |
| **Recommended** (Edamam Basic + OFF + USDA + Spoonacular Free) | $14/mo     | NLP + barcode + allergens + FODMAP label + recipes + glycemic load |
| Premium (Edamam Core + Spoonacular Cook)                       | $98/mo     | Full commercial + recipe analysis                                  |

---

## Current Stack Analysis

### What You Have Now

| Service                   | Role                              | Cost | Strengths                       | Gaps                                                                                 |
| ------------------------- | --------------------------------- | ---- | ------------------------------- | ------------------------------------------------------------------------------------ |
| **CalorieNinjas**         | Primary NLP nutrition parsing     | Paid | Natural language queries        | No barcode, no allergens, no FODMAP data, no diet labels                             |
| **OpenFoodFacts**         | Fallback product search + barcode | Free | Barcode lookup, huge product DB | Crowdsourced = inconsistent quality, rate-limited (100 req/min reads, 10/min search) |
| **USDA FoodData Central** | Fallback search by name           | Free | Authoritative nutrition data    | No NLP, no barcode, no allergens, US-focused                                         |

### Gaps in Current Stack for Gut Health

1. **No FODMAP data** — Critical for IBS users
2. **No allergen detection** — Important for food sensitivities
3. **No additive/ingredient parsing** — E-numbers, preservatives
4. **No glycemic index/load** — Relevant for gut inflammation
5. **No recipe analysis** — Can't break down meal compositions
6. **No ingredient substitution** — Can't suggest gut-friendly alternatives
7. **No diet label filtering** — Keto, low-FODMAP, DASH, etc.

---

## Category 1: Nutrition & Calorie APIs

### 1.1 Edamam Food Database API ⭐ RECOMMENDED

| Attribute       | Details                                                           |
| --------------- | ----------------------------------------------------------------- |
| **URL**         | https://developer.edamam.com/food-database-api                    |
| **Pricing**     | $14/mo (Basic), $69/mo (Core), $299/mo (Plus), Custom (Unlimited) |
| **Free Tier**   | No — but $14/mo Basic has 30-day trial                            |
| **Rate Limits** | Basic: 100K calls/mo, 50/min; Core: 750K calls/mo, 100/min        |
| **Auth**        | API Key (app_id + app_key)                                        |
| **Data Format** | JSON                                                              |

**Why It's Best for GutAI:**

- ✅ **FODMAP_FREE health label** — The only major API with built-in FODMAP labeling
- ✅ **70+ diet/allergy/nutrition filters** — Gluten-free, dairy-free, soy-free, nut-free, etc.
- ✅ **NLP for food logging** — Natural language parsing (replaces CalorieNinjas)
- ✅ **790,000 UPC/barcodes** — Replaces part of OpenFoodFacts role
- ✅ **130,000 branded restaurant items**
- ✅ **Vision API** — Image recognition for food (Basic: 500/mo free)
- ✅ **28+ nutrients** tracked including fiber, sugar alcohols, added sugar
- ✅ **MCP server available** — Edamam Food MCP is free with all plans

**Health Labels (gut-relevant ones highlighted):**

- **`FODMAP_FREE`** ⭐
- `GLUTEN_FREE` ⭐
- `DAIRY_FREE` ⭐
- `SOY_FREE`
- `WHEAT_FREE` ⭐
- `EGG_FREE`, `FISH_FREE`, `SHELLFISH_FREE`
- `TREE_NUT_FREE`, `PEANUT_FREE`
- `KETO_FRIENDLY`, `PALEO`
- `MEDITERRANEAN` ⭐
- `DASH` ⭐
- `KIDNEY_FRIENDLY`
- `SUGAR_CONSCIOUS`, `LOW_SUGAR`
- `HIGH_FIBER` ⭐
- `LOW_FAT`, `LOW_SODIUM`
- 30+ more

**Integration Effort:** Medium — Similar to CalorieNinjas, REST/JSON, well-documented.

---

### 1.2 Edamam Nutrition Analysis API

| Attribute       | Details                                               |
| --------------- | ----------------------------------------------------- |
| **URL**         | https://developer.edamam.com/edamam-nutrition-api     |
| **Pricing**     | $29/mo (Basic), $299/mo (Core), Custom                |
| **Rate Limits** | Basic: 2,500 recipes/mo, 10,000 text lines/mo         |
| **Languages**   | 10 languages (EN, ES, PT, AR, DE, FR, IT, TR, RU, NL) |

**Full recipe analysis** — submit entire recipe text, get back complete nutritional breakdown with NLP. Includes cooking adjustments (oil absorption, stock solids, marinades). Useful for the meal logging feature where users enter custom recipes.

**Not recommended as primary** — Food Database API covers most needs. Add this only if recipe analysis becomes a core feature.

---

### 1.3 Nutritionix API ❌ NOT RECOMMENDED

| Attribute     | Details                                                                  |
| ------------- | ------------------------------------------------------------------------ |
| **URL**       | https://www.nutritionix.com/api                                          |
| **Pricing**   | Free trial (2 MAU), $499/mo (Starter, 200 MAU), $999/mo (MVP), $1850+/mo |
| **Free Tier** | Effectively useless — 2 monthly active users max                         |
| **Barcode**   | >92% UPC match rate                                                      |
| **Owner**     | Syndigo LLC (acquired)                                                   |

**Why Not:**

- Free tier is limited to 2 MAU — unusable for any real app
- $499/mo minimum for production is 35x the Edamam Basic plan
- Premium add-on fees for detailed nutrients, wellness claims, taxonomy
- Website was partially down during research — stability concerns
- No FODMAP data
- Billed annually ($5,988/yr minimum)

**Only Consider If:** Budget is not a concern and you need their NLP engine specifically, or their 1M+ branded food database via bulk licensing.

---

### 1.4 USDA FoodData Central (Already in Stack)

| Attribute       | Details                                                           |
| --------------- | ----------------------------------------------------------------- |
| **URL**         | https://fdc.nal.usda.gov/api-guide                                |
| **Pricing**     | **Completely free** — Public domain (CC0 1.0)                     |
| **Rate Limits** | 1,000 req/hr per IP (can request higher)                          |
| **Data Types**  | Standard Reference Legacy, Branded Foods, Foundation Foods, FNDDS |

**Keep as fallback.** Most authoritative source for generic food nutrition data. No NLP, no barcode, no allergens — but the foundation data is best-in-class for nutrient accuracy.

**Enhancement Opportunity:** USDA Branded Foods dataset includes 300K+ branded products with full nutrition panels. Currently underutilized if only using search.

---

## Category 2: Barcode & Product Database APIs

### 2.1 OpenFoodFacts (Already in Stack) — ENHANCE USAGE ⭐

| Attribute       | Details                                                        |
| --------------- | -------------------------------------------------------------- |
| **URL**         | https://world.openfoodfacts.org/api/v2/                        |
| **Pricing**     | **Completely free** — Open Database License                    |
| **Rate Limits** | 100 req/min (product), 10 req/min (search), 2 req/min (facets) |
| **Products**    | 3M+ products globally                                          |
| **SDK**         | **.NET/C# SDK available** on GitHub                            |

**Data You're NOT Using Yet (Critical for Gut Health):**

```json
{
  "allergens_tags": ["en:peanuts", "en:sesame-seeds", "en:soybeans"],
  "additives_tags": ["en:e330"],
  "nova_group": 4,
  "nutriscore_grade": "d",
  "ingredients_analysis_tags": ["en:vegan", "en:vegetarian"],
  "traces_tags": ["en:peanuts"],
  "labels_tags": ["en:no-gluten", "en:vegetarian", "en:vegan"],
  "nutrient_levels": {
    "fat": "moderate",
    "salt": "moderate",
    "saturated-fat": "moderate",
    "sugars": "high"
  }
}
```

**Gut-Health Relevant Data in OpenFoodFacts:**

| Field                        | Description                       | Gut Health Relevance                                        |
| ---------------------------- | --------------------------------- | ----------------------------------------------------------- |
| `allergens_tags`             | Declared allergens                | Direct trigger identification                               |
| `allergens_from_ingredients` | NLP-extracted allergens           | Catches undeclared allergens                                |
| `traces_tags`                | May-contain traces                | Cross-contamination warnings                                |
| `additives_tags`             | E-number additives                | Emulsifiers (E471, E433) linked to gut inflammation         |
| `nova_group`                 | NOVA ultra-processing score (1-4) | Ultra-processed foods (NOVA 4) correlate with gut dysbiosis |
| `nutriscore_grade`           | Nutri-Score (A-E)                 | Overall nutritional quality                                 |
| `ingredients_hierarchy`      | Full ingredient tree              | Parse for FODMAP triggers                                   |
| `nutrient_levels`            | Fat/salt/sugar levels             | Quick gut-friendly assessment                               |
| `labels_tags`                | Product certifications            | Organic, gluten-free, etc.                                  |
| `categories_tags`            | Product categories                | Map to food groups for FODMAP scoring                       |

**Recommendation:** Extend your `OpenFoodFactsService` to parse and store these additional fields. This is the highest-value, zero-cost improvement you can make.

---

### 2.2 FatSecret Platform API

| Attribute   | Details                                        |
| ----------- | ---------------------------------------------- |
| **URL**     | https://platform.fatsecret.com                 |
| **Pricing** | See tiers below                                |
| **Scale**   | 2.3M+ foods, 700M+ API calls/mo, 56+ countries |
| **Barcode** | 90%+ UPC/EAN coverage globally                 |
| **Clients** | Samsung, Fitbit, Dexcom, Nestlé, Medtronic     |

**Tier Comparison:**

| Feature               | Basic (Free)        | Premier Free                 | Premier (Paid)     |
| --------------------- | ------------------- | ---------------------------- | ------------------ |
| **Cost**              | Free (self sign-up) | Free (verification required) | Upon request       |
| **API Calls**         | 5,000/day           | Unlimited                    | Unlimited          |
| **Datasets**          | US Only             | US Only                      | 56+ Countries      |
| **Barcode**           | ❌                  | ❌                           | ✅                 |
| **Allergens**         | ❌                  | ❌                           | ✅ (generic foods) |
| **NLP**               | ❌                  | Add-on\*                     | Add-on\*           |
| **Image Recognition** | ❌                  | Add-on\*                     | Add-on\*           |
| **Autocomplete**      | ❌                  | ❌                           | ✅                 |
| **Support**           | Community           | Community                    | Email/Phone/Video  |
| **SLA**               | ❌                  | ❌                           | ✅                 |
| **Caching**           | ❌                  | ❌                           | ✅                 |
| **Attribution**       | Required            | Required                     | White-label        |
| **Languages**         | English             | English                      | 24                 |

\*NLP and Image Recognition are billed as monthly add-ons in tiers of 25,000 inputs.

**Assessment:**

- **Basic (Free)**: 5,000 calls/day is decent for development/small apps, but US-only and no barcode/allergens
- **Premier Free**: Must apply and verify — unlimited calls but still US-only, no barcode
- **Premier (Paid)**: The real product — excellent for international apps but pricing is opaque (contact sales)

**Verdict:** Good future option for international expansion. For now, OpenFoodFacts + Edamam covers more ground at lower cost. FatSecret's Premier Free is worth applying for as a supplementary US food search.

---

## Category 3: Recipe & Ingredient APIs

### 3.1 Spoonacular API ⭐ RECOMMENDED (Free Tier)

| Attribute       | Details                                                          |
| --------------- | ---------------------------------------------------------------- |
| **URL**         | https://spoonacular.com/food-api                                 |
| **Pricing**     | Free ($0/mo), Cook ($29/mo), Culinarian ($79/mo), Chef ($149/mo) |
| **Free Tier**   | 50 points/day, 1 req/s, 2 concurrent                             |
| **Data Source** | USDA database + manual research                                  |
| **Auth**        | API Key                                                          |

**Free Tier Details:**

- 50 points/day (~50 simple API calls)
- 1 request/sec, 2 concurrent
- Forum support only, no SLA
- Must include backlink attribution
- No commercial use restriction mentioned for free tier

**Point System:** ~1 point per API call + 0.01 per result + extras for nutrition/instructions data.

**Gut-Health Relevant Endpoints:**

| Endpoint                                 | Points | Gut Health Value                                                       |
| ---------------------------------------- | ------ | ---------------------------------------------------------------------- |
| `GET /recipes/complexSearch`             | 1+     | Filter by diet (keto, paleo) + intolerances (gluten, dairy, egg, etc.) |
| `GET /food/ingredients/{id}/information` | 1      | Full nutrient data per ingredient                                      |
| `POST /recipes/parseIngredients`         | 1+     | Break recipe text into structured ingredients                          |
| `GET /food/ingredients/substitutes`      | 1      | **Suggest gut-friendly alternatives** ⭐                               |
| `GET /recipes/{id}/nutritionWidget.json` | 1      | Nutrition + calorie breakdown                                          |
| `POST /food/detect`                      | 1      | Detect food from text                                                  |
| `GET /recipes/{id}/information`          | 1      | Diet labels, intolerances, etc.                                        |
| `POST /recipes/cuisine`                  | 1      | Classify cuisine type                                                  |

**Unique Features Not in Other APIs:**

1. **Glycemic Load Computation** — Calculate glycemic load for meals/recipes
2. **Ingredient Substitutes** — e.g., "lactose-free milk for milk" — perfect for gut-sensitive users
3. **Intolerance Filtering** — Dairy, Egg, Gluten, Grain, Peanut, Seafood, Sesame, Shellfish, Soy, Sulfite, Tree Nut, Wheat
4. **Diet Filtering** — Gluten Free, Keto, Vegetarian, Vegan, Paleo, Primal, Whole30, Pescetarian, FODMAP
5. **Recipe Cost Estimation** — Budget-conscious meal planning

**Integration Strategy:**

- Use free tier (50 pts/day) for ingredient substitution suggestions + glycemic load
- Not a primary nutrition source — supplement Edamam/USDA
- Upgrade to Cook ($29/mo) only if recipe analysis becomes a core feature

---

### 3.2 Edamam Recipe Search API

| Attribute    | Details                                                           |
| ------------ | ----------------------------------------------------------------- |
| **Pricing**  | $9/mo (Basic), $99/mo (Core), $399/mo (Plus)                      |
| **Database** | 2M+ web recipes + 20K Edamam-owned                                |
| **Features** | 30+ diet/health/allergy filters, Glycemic Index, Carbon Footprint |

More expensive per feature than Spoonacular for recipe-only use. Consider only if you're already using Edamam Food Database API and want a unified vendor.

---

### 3.3 Zestful (Recipe Ingredient Parser)

| Attribute    | Details                                              |
| ------------ | ---------------------------------------------------- |
| **URL**      | https://zestfuldata.com                              |
| **Pricing**  | Free tier available (limited)                        |
| **Function** | Parse recipe ingredient strings into structured JSON |

Niche tool — parses "2 cups diced fresh tomatoes" into `{quantity: 2, unit: "cups", preparation: "diced, fresh", product: "tomatoes"}`. Useful but Spoonacular's `parseIngredients` does the same thing.

---

## Category 4: Food Allergen & Additive Databases

### 4.1 OpenFoodFacts Allergen & Additive Data ⭐ ALREADY AVAILABLE

**You already have access to this through your OpenFoodFacts integration.** The product API returns:

**Allergen Fields:**

- `allergens` — Declared allergens (comma-separated)
- `allergens_tags` — Structured allergen tags (e.g., `en:peanuts`, `en:gluten`)
- `allergens_from_ingredients` — NLP-extracted from ingredient text
- `allergens_hierarchy` — Full allergen taxonomy
- `traces` — "May contain" warnings
- `traces_tags` — Structured trace tags

**Additive Fields:**

- `additives_tags` — E-number additives (e.g., `en:e330` = citric acid)
- `additives_n` — Count of additives
- `additives_original_tags` — As listed on packaging

**Gut-Concerning Additives to Flag:**
| E-Number | Name | Gut Health Concern |
|----------|------|-------------------|
| E433 | Polysorbate 80 | Emulsifier — linked to gut inflammation and microbiome disruption |
| E466 | Carboxymethyl cellulose | Emulsifier — promotes intestinal inflammation in studies |
| E471 | Mono/diglycerides | Emulsifier — alters gut microbiota |
| E407 | Carrageenan | Thickener — triggers intestinal inflammation |
| E621 | MSG | Can trigger IBS symptoms in sensitive individuals |
| E951 | Aspartame | Artificial sweetener — may alter gut microbiome |
| E955 | Sucralose | Artificial sweetener — reduces beneficial gut bacteria |
| E967 | Xylitol | Sugar alcohol — FODMAP (polyol), causes bloating/diarrhea |
| E420 | Sorbitol | Sugar alcohol — FODMAP (polyol) |
| E421 | Mannitol | Sugar alcohol — FODMAP (polyol) |

**Implementation:** Create an `AdditiveRiskService` that maps known gut-concerning additives to risk scores and explanations.

---

### 4.2 Edamam Allergen Labels

Built into the Food Database API response. Provides **labels** rather than raw allergen data:

- `GLUTEN_FREE`, `WHEAT_FREE`, `DAIRY_FREE`, `EGG_FREE`
- `SOY_FREE`, `FISH_FREE`, `SHELLFISH_FREE`
- `TREE_NUT_FREE`, `PEANUT_FREE`
- `CELERY_FREE`, `MUSTARD_FREE`, `SESAME_FREE`, `LUPINE_FREE`
- `MOLLUSK_FREE`, `ALCOHOL_FREE`, `SULFITE_FREE`

More useful for **filtering** ("show me dairy-free foods") than for **detection** ("does this food contain dairy"). For detection, OpenFoodFacts is better.

---

### 4.3 FDA openFDA API

| Attribute    | Details                                  |
| ------------ | ---------------------------------------- |
| **URL**      | https://open.fda.gov                     |
| **Pricing**  | Free                                     |
| **Function** | Drug, device, and **food** data from FDA |

Includes food recall data, adverse event reports, and food labeling data. Niche use case — could power a "food safety alerts" feature for recalled products. Low priority.

---

## Category 5: Grocery & Branded Food APIs

### 5.1 Chomp API

| Attribute    | Details                                                   |
| ------------ | --------------------------------------------------------- |
| **URL**      | https://chompthis.com                                     |
| **Pricing**  | Free tier available                                       |
| **Function** | Grocery product data — UPC lookup, nutrition, ingredients |

Niche UPC database. OpenFoodFacts + Edamam already cover this space better.

---

### 5.2 UPC Database API

| Attribute   | Details               |
| ----------- | --------------------- |
| **URL**     | Listed on public-apis |
| **Pricing** | API key required      |
| **Scale**   | 1.5M+ barcodes        |

Smaller than OpenFoodFacts (3M+) or Edamam (790K UPCs). Skip.

---

### 5.3 Branded Food Coverage Comparison

| API                | Branded Products   | Barcodes    | Restaurant Items |
| ------------------ | ------------------ | ----------- | ---------------- |
| OpenFoodFacts      | 3M+ (crowdsourced) | 3M+         | Limited          |
| Edamam Food DB     | ~1M                | 790K        | 130K             |
| FatSecret Premier  | 2.3M+ (verified)   | 90%+ global | Limited          |
| USDA Branded Foods | 300K+              | Yes         | No               |
| Nutritionix        | 1M+ grocery        | >92% UPC    | 203K             |

**For a gut health app**, restaurant items matter less than grocery/packaged food. OpenFoodFacts + Edamam gives the best free/low-cost combo.

---

## Category 6: FODMAP & Gut Health Specific APIs

### 6.1 The FODMAP API Landscape: It Doesn't Exist ⚠️

**There is no public FODMAP API.** This is the single biggest gap in the food API ecosystem for gut health apps.

**Monash University** — the creators of the FODMAP diet — provide data exclusively through:

- Their **mobile app** ($9.99, iOS/Android/Amazon) — the gold standard
- Their **product certification program** — for food manufacturers
- Their **professional courses** — for dietitians

**No API, no database licensing, no bulk data export.** Their data is proprietary research data from lab-tested foods.

---

### 6.2 Building Your Own FODMAP Scoring Engine ⭐ MUST BUILD

Since no API exists, you must build this internally. Here's how:

**Approach 1: Ingredient-Based FODMAP Scoring**

Map common FODMAP trigger ingredients to categories:

```
Oligosaccharides (Fructans):
  - wheat, rye, barley, onion, garlic, leek, shallot, artichoke, asparagus, beetroot,
    Brussels sprouts, broccoli, cabbage, fennel, pea, chicory, pistachio, cashew,
    watermelon, peach, persimmon, nectarine, plum

Oligosaccharides (GOS):
  - legumes, chickpeas, lentils, kidney beans, baked beans, soybeans

Disaccharides (Lactose):
  - milk, yogurt, soft cheese, cream, ice cream, custard

Monosaccharides (Excess Fructose):
  - apple, pear, mango, watermelon, honey, high-fructose corn syrup, agave,
    asparagus, sugar snap peas, cherry, fig

Polyols (Sorbitol):
  - apple, pear, apricot, cherry, nectarine, peach, plum, prune,
    sorbitol (E420), mushroom, cauliflower, snow peas

Polyols (Mannitol):
  - mushroom, cauliflower, watermelon, sweet potato,
    mannitol (E421), celery
```

**Approach 2: Use OpenFoodFacts ingredients + Edamam FODMAP_FREE label**

1. If Edamam returns `FODMAP_FREE` → Safe
2. If OpenFoodFacts `ingredients_tags` contain known FODMAP triggers → Flag with category
3. Check `additives_tags` for sugar alcohols (E420, E421, E953, E965, E966, E967)
4. Cross-reference with your internal FODMAP trigger database

**Approach 3: Leverage Edamam's FODMAP_FREE filter**

Use `health=FODMAP_FREE` parameter in Edamam Food Database API searches to filter results to FODMAP-safe foods only.

**Data Sources for Building FODMAP Database:**

- Monash University app (manual extraction of publicly available food lists)
- Published FODMAP research papers (PubMed)
- OpenFoodFacts ingredient taxonomies
- Kings College London FODMAP research

---

### 6.3 Gut Health Scoring Composite

Build a **Gut Health Score** (1-10) per food item using data from multiple APIs:

| Factor          | Source                       | Weight | Logic                                    |
| --------------- | ---------------------------- | ------ | ---------------------------------------- |
| FODMAP Risk     | Internal DB + Edamam         | 30%    | High FODMAP ingredients = low score      |
| Additive Risk   | OpenFoodFacts additives      | 15%    | Emulsifiers, sweeteners = penalty        |
| NOVA Processing | OpenFoodFacts nova_group     | 15%    | NOVA 4 (ultra-processed) = low score     |
| Fiber Content   | Edamam/USDA nutrients        | 15%    | Higher fiber (to a point) = higher score |
| Allergen Match  | OpenFoodFacts + user profile | 15%    | Matches user's known triggers = score 0  |
| Sugar Alcohols  | Ingredients + additives      | 10%    | Polyols (sorbitol, mannitol) = penalty   |

---

### 6.4 FooDB (Food Component Database)

| Attribute    | Details                     |
| ------------ | --------------------------- |
| **URL**      | https://foodb.ca            |
| **Pricing**  | Free (open data)            |
| **Function** | Chemical compounds in foods |

FooDB contains detailed data about food constituents — flavonoids, phenols, amino acids, lipids, etc. Research-grade data from University of Alberta. Could theoretically be used to identify anti-inflammatory compounds in foods. Very niche, low priority. API returned HTTP 429 during research — may have availability issues.

---

## Comparison Matrix

### Feature Comparison: All APIs

| Feature                    | CalorieNinjas (current) | Edamam Food DB         | Spoonacular      | FatSecret Basic | OpenFoodFacts (current) | USDA FDC (current)   |
| -------------------------- | ----------------------- | ---------------------- | ---------------- | --------------- | ----------------------- | -------------------- |
| **Cost**                   | ~$10-20/mo              | $14/mo                 | Free (50pts/day) | Free (5K/day)   | Free                    | Free                 |
| **NLP Parsing**            | ✅                      | ✅                     | ✅               | ❌              | ❌                      | ❌                   |
| **Barcode/UPC**            | ❌                      | ✅ (790K)              | ✅               | ❌              | ✅ (3M+)                | ❌                   |
| **Allergen Labels**        | ❌                      | ✅ (15+)               | ✅               | ❌              | ✅ (raw data)           | ❌                   |
| **FODMAP Data**            | ❌                      | ✅ (FODMAP_FREE label) | ✅ (diet filter) | ❌              | ❌                      | ❌                   |
| **Additive Data**          | ❌                      | ❌                     | ❌               | ❌              | ✅ (E-numbers)          | ❌                   |
| **NOVA Score**             | ❌                      | ❌                     | ❌               | ❌              | ✅                      | ❌                   |
| **Nutri-Score**            | ❌                      | ❌                     | ✅               | ❌              | ✅                      | ❌                   |
| **Glycemic Load**          | ❌                      | ❌                     | ✅               | ❌              | ❌                      | ❌                   |
| **Ingredient Substitutes** | ❌                      | ❌                     | ✅               | ❌              | ❌                      | ❌                   |
| **Recipe Analysis**        | ❌                      | Separate API ($29+)    | ✅               | ❌              | ❌                      | ❌                   |
| **Diet Filtering**         | ❌                      | ✅ (30+)               | ✅ (12+)         | ❌              | Limited                 | ❌                   |
| **Image Recognition**      | ❌                      | ✅ (500/mo)            | ✅               | ❌              | ✅ (community)          | ❌                   |
| **Restaurant Menu**        | ❌                      | ✅ (130K)              | ✅               | ❌              | Limited                 | ❌                   |
| **Nutrient Count**         | ~20                     | 28+                    | 30+              | ~20             | Variable                | 150+                 |
| **Languages**              | English                 | 10                     | English          | English         | 30+                     | English              |
| **.NET SDK**               | ❌                      | ❌                     | ❌               | ❌              | ✅ (community)          | ❌                   |
| **Attribution**            | Unknown                 | Required               | Required (free)  | Required        | Required (ODbL)         | None (public domain) |

---

## Integration Recommendations

### Recommended Architecture: Multi-Source Nutrition Pipeline

```
User Input (text, barcode, image)
         │
         ▼
┌─────────────────┐
│  Input Router    │ ← Determines input type
└─────────────────┘
         │
    ┌────┼────────────┐
    ▼    ▼            ▼
 Text  Barcode     Image
    │    │            │
    ▼    ▼            ▼
┌──────┐ ┌──────┐ ┌──────┐
│Edamam│ │ OFF  │ │Edamam│
│ NLP  │ │  API │ │Vision│
└──────┘ └──────┘ └──────┘
    │    │            │
    └────┼────────────┘
         ▼
┌─────────────────────┐
│  Nutrition Resolver  │ ← Merges data from best source
│  (Priority Chain)    │
│  1. Edamam (NLP+UPC) │
│  2. OpenFoodFacts     │
│  3. USDA FDC          │
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│  Gut Health Enricher │ ← Adds gut-specific scores
│  • FODMAP scoring     │
│  • Additive risk      │
│  • Allergen matching  │
│  • NOVA processing    │
│  • Fiber assessment   │
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│  Spoonacular Extras  │ ← On-demand enrichment
│  • Glycemic load      │
│  • Substitutes        │
│  • Recipe breakdown   │
└─────────────────────┘
         │
         ▼
  Final Enriched Food Record
  + Gut Health Score (1-10)
```

### Priority Chain Logic

```csharp
// Pseudocode for the nutrition resolution chain
public async Task<FoodRecord> ResolveNutrition(FoodInput input)
{
    FoodRecord record = null;

    // 1. Try Edamam first (NLP + barcode + allergens + FODMAP label)
    record = await _edamamService.Search(input);

    // 2. Fallback to OpenFoodFacts (barcode + additives + NOVA)
    if (record == null || !record.HasNutrition)
        record = await _openFoodFactsService.Search(input);

    // 3. Fallback to USDA FDC (authoritative nutrition data)
    if (record == null || !record.HasNutrition)
        record = await _usdaService.Search(input);

    // 4. Always enrich with OpenFoodFacts additive/NOVA data if barcode available
    if (input.HasBarcode)
        record = await _enricher.AddAdditiveData(record, input.Barcode);

    // 5. Apply FODMAP scoring
    record.FodmapScore = await _fodmapEngine.Score(record.Ingredients);

    // 6. Calculate composite Gut Health Score
    record.GutHealthScore = _gutScorer.Calculate(record);

    return record;
}
```

---

## Implementation Roadmap

### Phase 1: Quick Wins (1-2 weeks) — Zero Cost

1. **Extend OpenFoodFacts parsing** to extract allergens, additives, NOVA score, Nutri-Score, traces
2. **Create `AdditiveRiskService`** mapping gut-concerning additives to risk levels
3. **Build basic FODMAP trigger database** (~200 common foods with FODMAP categories)
4. **Add `GutHealthScoreService`** combining existing data into a composite score

### Phase 2: Edamam Integration (2-3 weeks) — $14/mo

1. **Replace CalorieNinjas** with Edamam Food Database API
2. **Implement `EdamamNutritionService`** with NLP, barcode, and diet label support
3. **Use FODMAP_FREE label** from Edamam to enhance FODMAP scoring
4. **Add allergen detection** using Edamam health labels + OFF allergen data
5. **Update `CompositeNutritionService`** fallback chain: Edamam → OFF → USDA

### Phase 3: Recipe & Substitution Intelligence (3-4 weeks) — Free

1. **Integrate Spoonacular free tier** for:
   - Ingredient substitution suggestions ("Try lactose-free milk instead")
   - Glycemic load computation for logged meals
   - Recipe nutritional analysis
2. **Build `IngredientSubstitutionService`**
3. **Add glycemic load to meal insights**

### Phase 4: Advanced Gut Health Features (4-6 weeks) — Internal

1. **Expand FODMAP database** to 500+ foods with FODMAP category + severity
2. **Build elimination diet tracker** using FODMAP data + symptom correlations
3. **Create personalized food scoring** based on user's symptom history
4. **Add "Why this score?" explanations** for each food item
5. **Implement food diary pattern analysis** (correlate foods → symptoms over time)

### Phase 5: Scale & Internationalize (Future)

1. **Evaluate FatSecret Premier** for multi-country support
2. **Consider Edamam Core upgrade** if hitting Basic rate limits
3. **Spoonacular Cook tier** if recipe analysis drives significant engagement
4. **Build community-sourced FODMAP data** feature (user-submitted food ratings)

---

## API Quick Reference

### Registration Links

| API           | Sign Up                                         | Docs                                                      |
| ------------- | ----------------------------------------------- | --------------------------------------------------------- |
| Edamam        | https://developer.edamam.com/admin/applications | https://developer.edamam.com/food-database-api-docs       |
| Spoonacular   | https://spoonacular.com/food-api/console        | https://spoonacular.com/food-api/docs                     |
| OpenFoodFacts | No key needed (use User-Agent)                  | https://openfoodfacts.github.io/openfoodfacts-server/api/ |
| USDA FDC      | https://fdc.nal.usda.gov/api-key-signup         | https://fdc.nal.usda.gov/api-guide                        |
| FatSecret     | https://platform.fatsecret.com/register         | https://platform.fatsecret.com/docs/guides                |

### .NET Integration Notes

| API           | HTTP Client                        | Auth Pattern                                       | Response Format |
| ------------- | ---------------------------------- | -------------------------------------------------- | --------------- |
| Edamam        | `HttpClient` GET with query params | `app_id` + `app_key` as query params               | JSON            |
| Spoonacular   | `HttpClient` GET/POST              | `apiKey` query param or `x-api-key` header         | JSON            |
| OpenFoodFacts | `HttpClient` GET                   | `User-Agent: GutAI/1.0 (contact@gutai.com)` header | JSON            |
| USDA FDC      | `HttpClient` GET/POST              | `api_key` query param                              | JSON            |
| FatSecret     | `HttpClient` with OAuth 2.0        | OAuth 2.0 client credentials                       | JSON            |

---

## Appendix: API Response Examples

### Edamam Food Database — Banana Search

```
GET https://api.edamam.com/api/food-database/v2/parser?app_id=XXX&app_key=XXX&ingr=banana&health=FODMAP_FREE
```

### OpenFoodFacts — Product with Additives

```
GET https://world.openfoodfacts.org/api/v2/product/737628064502.json?fields=allergens_tags,additives_tags,nova_group,nutriscore_grade,ingredients_tags
```

### Spoonacular — Ingredient Substitutes

```
GET https://api.spoonacular.com/food/ingredients/substitutes?ingredientName=butter&apiKey=XXX
```

### USDA FDC — Food Search

```
GET https://api.nal.usda.gov/fdc/v1/foods/search?query=banana&api_key=XXX&dataType=Foundation,SR%20Legacy
```

---

_This document should be reviewed quarterly as API pricing and features change frequently._
