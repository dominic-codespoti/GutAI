# Golden-Image Regression Harness

Regression gate for meal-scan **Stage A** (vision decomposition). Runs the exact
production inference path (`IMealVisionStage` → `MealScanService`) against real
meal photos with hand-entered ground truth, and scores:

- **Component recall** — fraction of your hand-listed components the model found
  (fuzzy name matching: token overlap ≥ 0.5 or substring containment)
- **Gram error** — |model midpoint − your grams| / your grams, per matched component
- Token usage per image (cost tracking)

Any change to the vision prompt, JSON schema, or model deployment **must** pass
the gate before merging. The gate keys its result cache on image hash + prompt
version, so re-runs only bill for genuinely new work.

## 1. Capture photos (target: 25–50)

Shoot meals you actually eat, exactly as the app would see them. Aim for coverage:

| Category | Count | Why |
|---|---|---|
| Single-item plates (bowl of pasta) | ~8 | baseline identity + portion |
| Multi-component plates (protein + carb + veg) | ~12 | the core use case |
| With reference object in frame (fork/plate edge/hand) | ~6 | scale-cue path |
| No reference possible (overhead close-up) | ~5 | worst-case portion path |
| Drinks / glasses | ~4 | liquid volume |
| Mixed dishes you can't separate (casserole, soup, curry) | ~5 | "inseparable" rule |
| Edge cases (mostly-empty plate, shared platter) | a few | robustness |

Name files simply (`case01.jpg`, …) and put them in this directory.

## 2. Write ground truth — `golden-images/manifest.json`

```json
{
  "gate": { "min_recall": 0.80, "max_median_gram_error_percent": 35 },
  "cases": [
    {
      "image": "case01.jpg",
      "expected": [
        { "name": "rice",        "grams": 180 },
        { "name": "grilled chicken", "grams": 120 }
      ],
      "notes": "lunch plate, fork visible"
    }
  ]
}
```

Rules for truth entry:
- `grams` = what you'd honestly write in a food diary (±20% is fine; that's the
  realistic ceiling for single-photo estimation — the gate threshold accounts for it).
- List every component YOU consider distinct. Don't list condiments under ~10 g.
- For inseparable dishes, one entry for the dish ("chicken curry") at its total weight.

## 3. Run

```bash
# configure once (or export as env vars AzureOpenAI__Endpoint etc.)
export AzureOpenAI__Endpoint="https://<your-resource>.openai.azure.com"
export AzureOpenAI__VisionDeployment="gpt-4.1-mini"

dotnet run --project backend/tools/GoldenScanHarness -- --images golden-images          # report only
dotnet run --project backend/tools/GoldenScanHarness -- --images golden-images --gate   # exit 1 on regression
dotnet run -- ... --refresh                                                             # ignore cache, re-bill everything
```

Or via make: `make golden-run`, `make golden-gate`.

## 4. When to re-run

| Event | Action |
|---|---|
| Prompt/schema/deployment change on scan path | `--refresh --gate` — full re-run must pass |
| Adding new photos | plain run (only new images are billed) |
| Model upgrade candidate | run twice (old vs new deployment) and diff reports |

Cache lives in `.cache/results.json`; delete it if things look confusing.
Do **not** commit `.cache/`.
