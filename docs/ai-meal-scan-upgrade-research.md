# AI Calorie Tracking / Meal Photo Scanning — Research & Upgrade Proposal

*2026-08 · research synthesis for GutAI/GutLens*

---

## Part 1 — What exists today (code audit)

### Current AI surface
| Feature | Endpoint | Backend | Notes |
|---|---|---|---|
| Nutrition-label photo parse | `POST /api/food/parse-label` | Azure AI Content Understanding (`prebuilt-documentFields`) → **LLM vision fallback** (Azure OpenAI, structured JSON) | `ContentUnderstandingService.ParseNutritionLabelAsync`; fallback prompt is a single-shot "extract the label" system prompt |
| Text description → food | `POST /api/food/describe` | Foundry agent `nutrition-estimation-agent` → direct chat completions fallback | Pro-gated, `aiExtraction` rate-limit partition |
| Natural-language meal log | `POST /api/meals/log-natural` | NLP pipeline + `ServingEstimator` (unit→grams tables: cup/bowl/slice/glass…) | Text only — no image input |
| Food search / barcode | `/api/food/search`, `/barcode/{code}` | `ExternalFoodProviderAggregator`: USDA FDC, Open Food Facts, Australian + branded-food APIs | Solid multi-source foundation |

### Key structural facts
- **There is no whole-meal photo scanner.** The camera today is used only for nutrition labels (and barcode entry). This is the gap.
- The stack is already Azure-native: `AzureOpenAIClient`, Foundry agents, Content Understanding — an upgrade can stay in that ecosystem.
- `FoodProduct` already carries `dataSource`, `sourceUrl`, `externalId`, `matchConfidence` — i.e., **the schema is ready for web-sourced provenance**, nothing new needed.
- Macro/FODMAP/gut-risk scoring is deterministic and local (`FodmapData`, `GutRiskData`, `GlycemicData` + matching services). Good: AI should feed *inputs* into this engine, not replace it.
- Frontend has a mature review-before-log pattern (`AddToMealSheet`, editable form in `food/create.tsx`, confidence badges High/Medium/Low ≥85%/≥60%). Reusable for scan results.
- `docs/free-food-databases-analysis.md` already ranks next databases (CNF recommended first).

---

## Part 2 — What the research says

### 2.1 Realistic accuracy expectations (important)
- Independent 2026 testing of six commercial AI photo-calorie apps found **systematic underestimation**, with individual meals off by **hundreds of kcal** (worst reported cases ~345 kcal low). No commercial app is reliably accurate at the single-meal level.
- Single-photo portion/volume estimation remains the weakest link regardless of model — typical errors are ±20–50% on mixed meals. Multi-angle/video capture (Cal AI's pitch) improves this but with diminishing returns and worse UX.
- General-purpose multimodal models (GPT-4o/4.1-class vision, Gemini Flash-class) are **comparable to purpose-built apps' recognizers** on dish identification; their advantage is flexibility (mixed/home-cooked meals, cultural foods).
- **Design implication:** build for fast user correction and honest confidence display rather than pretending full automation. GutLens already has the right UX instincts here.

### 2.2 Purpose-built food-vision APIs vs frontier LLM
| Option | Strengths | Weaknesses | Verdict for GutAI |
|---|---|---|---|
| SnapCalorie API | Trained on Nutrition5K-class datasets, dish segmentation + kcal directly | Per-call cost, another vendor, generic (no FODMAP angle) | Not worth it — you'd still need DB matching + gut scoring |
| LogMeal / FoodAI / Passio | Similar niche focus | Pricing opaque, lock-in, same limits | Skip |
| Frontier LLM vision (GPT-4o/4.1-mini, Gemini Flash) via your existing Azure/OpenAI path | Best-in-class identification of *components*, flexible structured output, one vendor you already run | Portion estimates need engineering guardrails | **Winner** — use it as the recognizer, not the calorie oracle |

### 2.3 Prompting/engineering patterns that measurably help
1. **Never ask the LLM for calories directly.** Two-stage: model outputs *component identities + gram estimates + confidence*; macros computed deterministically from database values × grams. This kills the largest error source (LLM nutrition-number hallucination) and matches your existing "deterministic scoring engine" architecture.
2. **Give the model a shortlist, not a blank page**: after Stage A, run each component through `FoodSearchService`; if ambiguous, re-ask the model to choose among the top-N DB candidates (with images/names). Selection-from-shortlist beats free recall.
3. **Structured JSON output with a strict schema** (you already do this for label parsing) — extend it: array of components, each with name, estimatedGrams range, confidence, cooking-method note (oil added? sauces?), and reference cues ("fork at 8 o'clock ≈ 18cm").
4. **Reference objects**: explicitly instruct the model to use plate/utensil/hand size for scale, and to say when none is visible (→ lower confidence).
5. **Portion midpoint + range**: store grams as a range, prefill the editor with the midpoint, one-tap ±25% adjustments. Counteracts documented systematic underestimation.
6. Optional later: self-consistency (2 samples, average grams) for high-value scans; multi-photo capture flow.

### 2.4 Incorporating web results
Three tiers, cheapest-first:

1. **Structured DB lookup (you have it)** — USDA/OFF/AU aggregator *is* the best "web result" for packaged & common foods. Free, licensed, structured. Keep as primary.
2. **Grounded web search for the long tail** — restaurant menu items, regional dishes, recipes, local brands not in OFF:
   - **Gemini API `google_search` grounding**: model auto-generates queries, returns answer + inline `url_citation` annotations (start/end index → source URL/title). Billing: **per executed search query** on Gemini 3 models; per prompt on ≤2.5 models (2.5 Flash historically had a daily free allowance then ~$35/1k prompts — verify current pricing page before committing). Works with URL-context tool to pin specific domains.
   - **OpenAI Responses API `web_search` tool**: equivalent capability inside the Azure OpenAI ecosystem you're already in — likely the lowest-friction option given `AzureOpenAIClient` is wired.
   - Either way: constrain to authoritative domains (usda.gov, openfoodfacts.org, manufacturer sites, major chain menus), require citations, and flag results `dataSource="web"` so the existing `sourceUrl` field shows provenance in-app.
   - **Guardrail:** web-sourced values should be marked visually differently in the confirmation sheet (they're the least trustworthy tier).
3. **DIY pipeline** (LLM generates query → Serper/Tavily/Bing → fetch page → LLM extracts): most control, most code, per-search cost similar to grounding — only worth it if you want to cache/scrape aggressively. Not recommended initially.

### 2.5 Cost envelope (order of magnitude)
Per scan at ~30 scans/day: 1–2 vision calls (small-model vision ≈ $0.002–0.01/image) + occasional grounded search ($0.0015–0.035/query depending on model/tier) ⇒ **~$0.005–0.04 per scan, well under $1/day** even at heavy use. Not a cost problem; the `aiExtraction` rate limiter already protects abuse.

---

## Part 3 — Proposed pipeline (v1)

```
Camera (multi-item meal photo)
   │  POST /api/meals/scan-image  (multipart, Pro-gated, aiExtraction limit)
   ▼
Stage A · Vision model (existing AzureOpenAIClient)
   prompt: identify every distinct food component; estimate grams
   (range + midpoint); use visible references for scale; report
   confidence + missing-reference flag; STRICT JSON schema
   ▼
Stage B · Ground to data (per component)
   B1: FoodSearchService / ExternalFoodProviderAggregator (USDA·OFF·AU)
       → best candidate per component (+matchConfidence)
   B2: low match → grounded web search (authoritative domains,
       citations required) → dataSource="web", sourceUrl set
   ▼
Stage C · Deterministic computation
   per-100g DB values × estimated grams → calories/macros per item;
   run EXISTING Fodmap/GutRisk/Glycemic scoring unchanged
   ▼
Draft meal → frontend confirmation sheet (reuses AddToMealSheet +
confidence badges + editable fields, ±25% portion nudge, per-item
provenance chip: USDA / OFF / web+citation / AI-only)
   ▼
User confirms → CreateMeal (unchanged downstream)
```

**Why this shape:** the LLM does what it's good at (seeing what's on the plate), your existing infrastructure does everything else. Every number the user sees traces to a named source; AI guesses are confined to *identity* and *grams*, which the user can correct in two taps.

### Implementation checklist (rough order)
1. Backend: `IMealScanService` + endpoint + JSON schema DTOs (mirror label-parse patterns incl. fallback + `FinalizeGeneratedFood` hygiene).
2. Stage B matcher: reuse aggregator; add "top-3 candidates back to model" disambiguation call.
3. Web-grounding fallback behind config flag (`Features:WebGrounding`) — start with OpenAI Responses web_search; store citation in `sourceUrl`.
4. Frontend: scan screen camera flow → results sheet with per-item provenance chips + portion stepper.
5. Store corrections (edited name/grams) — future training/few-shot signal.
6. Later: multi-photo capture, restaurant mode, self-consistency sampling, CNF provider per the databases doc.

### Explicit non-goals (v1)
- Volume reconstruction / AR estimation (poor ROI, poor UX).
- Replacing Monash FODMAP data with anything scraped from the web — web results must never touch FODMAP flags (safety-critical, keep the curated local dataset authoritative).
