# Agent evaluation harness

`AgentEvalHarness` evaluates the production Coach, describe-food, and nutrition-label services against versioned local cases. From the repository root, `make evals-agents` is the primary live-run command; `make evals` runs the photo gate first, then these suites.

```sh
make evals-agents
# Advanced direct invocation, from backend/:
dotnet run --project tools/AgentEvalHarness -c Release -- --suite coach --report coach-report.json
dotnet run --project tools/AgentEvalHarness -c Release -- --suite describe --gate --report describe-report.json
dotnet run --project tools/AgentEvalHarness -c Release -- --suite all --repeat 3 --gate --report all-report.json
```

The wrapper loads `AzureOpenAI` from the API's `appsettings.json` plus `appsettings.Development.json`, then applies caller environment overrides; `AZURE_OPENAI_ENDPOINT` overrides the endpoint. Pricing defaults to $0.20 input / $1.20 output per 1M tokens (`EVAL_INPUT_PER_1M` / `EVAL_OUTPUT_PER_1M`, gpt-5.6-luna list prices); set `EVAL_SUITE` (`all` by default) and `EVAL_REPEAT` (`1` by default) to choose agent suites and repeats. Run `az login`; Docker is required, and the wrapper starts/removes temporary Azurite if needed. Reports go under `eval-reports/<UTC timestamp>/`; `scripts/run-ai-evals.sh` exits 0 for passing gates, 1 for gate failures, and 2 for missing prerequisites/configuration (through `make`, any failure shows as exit status 2; the summary names the failing gate). Agent evals take about 4 minutes and cost about $0.05 per repeat.

Direct runs compose production services through `AddInfrastructure`; food search is replaced only with the embedded Whole Foods, branded, and Australian providers, matching the deterministic provider setup in `GutAI.IntegrationTests.SearchQualityTests`. No external food APIs are called. Coach cases use a fresh Azurite user and real `IChatService` per conversation. Configure `GUTAI_EVAL_STORAGE` for the storage connection string; the default is `UseDevelopmentStorage=true`. Direct AI calls require `az login` and Azure OpenAI endpoint/deployment settings (including workload-specific deployments) through environment variables or `appsettings.harness.json`. Never run against a paid model without authorization.

## Suites and case format

- `cases/coach/cases.json` contains at least 20 independent scripted conversations. `turns` are sequential user messages; each gets a new user. `expectedToolOrder` is an ordered subsequence. Optional fields describe expected draft/commit behavior, medical redirects, and feature-flag-only cases. `feature: "MealSuggestions"` cases are reported as skipped, with a reason, unless the feature is enabled.
- `cases/describe/cases.json` contains text descriptions with expected per-serving calories/macros/grams and USDA FoodData Central identifiers. The suite reports nutrient absolute and percent errors plus provenance and latency.
- `cases/label/manifest.json` lists synthetic image fixtures and known per-serving nutrient values. Regenerate the committed set with:

  ```sh
  dotnet run --project tools/AgentEvalHarness -c Release -- --generate-labels --root tools/AgentEvalHarness
  ```

  Generation uses the bundled DejaVu Sans font and license in `fonts/`, so the fixtures do not depend on installed system fonts.

Add cases to the suite's JSON and keep each suite's thresholds in its manifest. Coach assertions cover ordered tool use across the full conversation, same-turn commit prohibition, commit-after-confirmation, diary-write provenance, nutrition prose grounded in server draft items and totals, per-100 g nutrition returned by `search_foods`, and the requested refusal/redirect behavior. Graders are deterministic and isolated in Infrastructure's `Services/Evaluation` namespace.

## Gates and reports

`--report` writes per-case assertions/results, nutrient errors, tool calls/results, draft/meal counts, turn latency, suite assertion pass rates, p50/p95 latency, token p50/p95 when exposed by the service, and suite metadata: effective workload deployment, configured reasoning effort, and prompt/schema version. Token counts are per model request and capture chat spans only, not orchestration, agent, or tool spans. Token counts unavailable from the current streaming/content-service contracts are serialized as `null`, not estimated; if a suite configures `maximumTokenP95` without measuring token usage, its gate fails with `token usage not measured`. `--gate` uses the thresholds in each suite manifest and returns 1 when a suite misses a threshold; 0 means all requested suite gates passed. Invalid arguments (including `--repeat` outside 1..10), missing case/configuration files, and service failures return 2.
`--repeat N` runs every case N times (default 1). For repeats greater than one, each case reports the mean binary score, population variance, and pass count; gate assertion rates are calculated from the repeated runs. At `--repeat 1`, the report retains the ordinary single-run case shape without repeat statistics.

Threshold fields are `minimumAssertionPassRate`, `maximumKcalPercentError`, `maximumMacroPercentError`, `maximumLatencyP95Ms`, and `maximumTokenP95`. Coach safety assertions for same-turn commit and diary writes without commit have zero tolerance.
