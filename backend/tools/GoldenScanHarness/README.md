# Golden Scan Regression Harness

The harness runs one of three evaluation surfaces against `golden-images/manifest.json` and writes a versioned JSON report. It uses the production scan implementation where applicable; every nutrition value is server-computed from a food database basis and grams.

## Modes

```bash
dotnet run --project backend/tools/GoldenScanHarness -- --images golden-images --mode stage-a
dotnet run --project backend/tools/GoldenScanHarness -- --images golden-images --mode in-process
dotnet run --project backend/tools/GoldenScanHarness -- --images golden-images --mode e2e
```

- `stage-a` (default) evaluates decomposition only: component precision/recall/F1, gram error, interval coverage, repeated-run variation, and Stage-A timing/usage. Grounding, nutrition accuracy, selection, and cost are reported NotEvaluated because this mode does not run those stages.
- `in-process` calls the production `MealScanService` and draft pipeline, with embedded USDA/Australian/offline food sources only. This gives deterministic grounding and avoids external food-search requests. The Stage-A cache is replayed when warm; grounded items and nutrition are recomputed every run. Batched selection and agent review are disabled unless `--live-selection` is supplied (which requires a configured selection workload).
- `e2e` sends a multipart scan request to `GUTAI_GOLDEN_API_URL` (default `http://localhost:5000`). It evaluates the live API response; add `--confirm` to commit each returned draft through the API and verify the saved meal readback. The API must accept registration and scan requests; `--confirm` additionally requires working draft commit and saved-meal readback endpoints.

Options: `--gate` returns 1 when any evaluated configured manifest threshold fails; `--refresh` ignores Stage-A cache entries; `--repeat N` repeats each case; `--report <path>` selects the report destination (default `golden-images/.cache/last-report.json`). Invalid arguments/configuration return 2. A normal non-gated run returns 0. `--confirm` is valid only in `e2e` mode. Exit status 0 means pass/ok, 1 means a gate failure, and 2 means usage/configuration errors or no produced results (Stage-A has no scored cases; in-process/e2e have no evaluations). Runtime failures also return nonzero.

## Stage-A cache policy

The local cache is only an iteration aid, never evidence for a live gate. Entries contain raw Stage-A JSON, visible components, inferred components, and their schema/prompt version. Keys are based only on image SHA-256, `MealScanService.EffectiveVisionPromptVersion(configuration)`, vision deployment, and reasoning effort; grounding policy and portion calibration do not invalidate a Stage-A decomposition because all grounded output is rebuilt on every in-process run. Each result also stores nullable `model_id`, the served model reported by the Responses API; older cache entries without this field are treated as having a null model id. Reports include `model_ids`, listing distinct served model ids by stage (vision and selection in in-process mode; vision in e2e mode).

The report separately records `GroundingPolicy.PolicyVersion`, `PortionCalibrator.Version`, effective prompt version, deployment/effort, and active flags. Thus a change in grounding or calibration is visible in the report and cannot silently reuse grounded results.

## Thresholds and ratchet procedure

Live refreshed in-process baseline from 5 runs on 2026-09-26: `--mode in-process --refresh`, 12 cases, no live selection, and web grounding off. The runs used the live gate configuration: deployment `gpt-5.4-mini` (serving `gpt-5.6-luna`, version `2026-07-09`, per the Azure resource), reasoning effort `default`, prompt `2026-08-26.v11-serving-hint`, grounding policy `2026-09-24.v1`, and portion calibration off. The harness records `gpt-5.4-mini` in `model_ids` because Azure's Responses API echoes the deployment name, not the underlying model. The runs were executed in parallel. Each run made 12 vision calls and took about 113 seconds; estimated cost was about $0.019 per run at $0.20 input / $1.20 output per 1M tokens (gpt-5.6-luna list price).

| Metric | Live mean ± SD | Range | Threshold |
|---|---:|---:|---:|
| min_recall | 0.9400 ± 0.0273 | 0.8944–0.9667 | 0.885 |
| min_precision | 0.7702 ± 0.0533 | 0.6835–0.8289 | 0.663 |
| max_median_gram_error_percent | 40.32 ± 3.62 | 36.73–46.41 | 47.6 |
| min_nutrition_backed_rate | 0.3152 ± 0.0166 | 0.3030–0.3333 | 0.265 |
| max_false_positive_rate | 0.3335 ± 0.0717 | 0.2439–0.4400 | 0.477 |
| max_abstention_rate | 0.7106 ± 0.0206 | 0.6829–0.7333 | 0.761 |
| min_interval_coverage | 0.5499 ± 0.0401 | 0.5000–0.6000 | 0.469 |
| max_p95_latency_seconds | 12.93 ± 1.28 | 11.67–14.71 | 16.16 |
| max_p95_cost_usd | 0.0025 ± 0.0002 | 0.0023–0.0027 | 0.0032 |

Thresholds use mean ± max(2×SD, margin), rounded outward: the margin is 0.05 for rates, 5 percentage points for gram error, and 25% of the mean for p95 latency and p95 cost. For minimum metrics the lower bound is used; for maximum metrics the upper bound is used. These are provisional regression tripwires, not accuracy targets: calorie-error/bias/CV and identity-precision/ECE thresholds remain unset because there is no weighed ground truth (D7).

### Baseline change (2026-09-26)

The previous thresholds came from one offline replay of cached results with unknown model provenance and approximately 5-point margins: min_recall 0.8444, max_median_gram_error_percent 40.90, min_nutrition_backed_rate 0.2530, max_false_positive_rate 0.4414, min_precision 0.6580, max_abstention_rate 0.8109, min_interval_coverage 0.5571, with latency and cost unset. A live refreshed run would have failed the old gram-error threshold (45.0) and interval-coverage threshold (0.533); the new thresholds come from 5 live refreshed runs. Gram-error, interval-coverage, and false-positive thresholds loosened to cover measured live run-to-run variance; recall, nutrition-backed rate, and abstention tightened. Latency and cost thresholds were added. These remain provisional regression tripwires, not accuracy targets: calorie-error/bias/CV and identity-precision/ECE thresholds remain unset because there is no weighed ground truth (D7).

Local offline replays use the gitignored `golden-images/.cache`, now refreshed from the live run closest to the baseline mean (run 4). They are not evidence for a live gate.

For future ratchets, re-baseline with at least 5 live refreshed runs whenever the prompt/schema, deployment, or the deployment's underlying model changes. Review per-case results and missing-data status, then derive threshold changes from reviewed measurements using the explicit rule above. Never loosen a threshold just to pass a run; investigate the regression or document a justified dataset/baseline change. Ratchet gradually as weighed ground truth grows; the collection target is at least 50 cases, at least 40 weighed; see `golden-images/COLLECTION_PROTOCOL.md`.

Cost thresholds are evaluated when the local `AzureOpenAI__Pricing__<deployment>__InputPer1M` and `AzureOpenAI__Pricing__<deployment>__OutputPer1M` variables are set. For this deployment, use 0.20 input and 1.20 output per 1M tokens. Without both pricing variables, the cost threshold is NotEvaluated. The latency gate uses whole-scan observations; per-stage latency, token, and cost percentiles remain separately reported.

## Running live gates manually

From the repository root, after `az login`, run the live photo-scan gate:

```bash
AzureOpenAI__Endpoint=<endpoint> AzureOpenAI__Workloads__vision__Deployment=gpt-5.4-mini AzureOpenAI__Pricing__gpt-5.4-mini__InputPer1M=0.20 AzureOpenAI__Pricing__gpt-5.4-mini__OutputPer1M=1.20 dotnet run --project backend/tools/GoldenScanHarness -c Release -- --images golden-images --mode in-process --refresh --gate --report golden-report.json
```

The harness authenticates with the Azure CLI credential. Both pricing variables are needed to evaluate the cost threshold; without them it is reported NotEvaluated. A full live run makes 12 vision calls, costs about $0.02, and takes about 2 minutes.

Local Make targets: `make golden-run`, `make golden-gate`, `make golden-inprocess`, and `make golden-e2e`.
