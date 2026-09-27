# Meal Photo Scan — Current Detailed Design

This document describes the implemented meal-photo pipeline and its current contracts. The endpoint wiring is in `backend/src/GutAI.Api/Program.cs`, `backend/src/GutAI.Api/Endpoints/MealScanEndpoints.cs`, and `backend/src/GutAI.Api/Endpoints/MealDraftEndpoints.cs`; scan orchestration is in `backend/src/GutAI.Infrastructure/Services/MealScanService.cs`.
Unless a path is explicitly rooted with `frontend/` or `backend/tools/`, source paths in this document are relative to `backend/src/`.

## 1. Runtime and routes

| Concern | Current implementation | Source |
|---|---|---|
| Photo request | `POST /api/meals/scan/image` accepts multipart `file` plus optional `note` (≤200 characters); returns a `MealDraftDto` with origin `photo`. The request is synchronous, with a 60-second default scan deadline. | `Api/Endpoints/MealScanEndpoints.cs`, `Infrastructure/Services/MealScanService.cs` |
| Draft lifecycle | `GET /api/meals/drafts/`, `GET /{id}`, `PUT /{id}`, `PUT /{id}/commit`, `DELETE /{id}` provide inbox, retrieval, review edits, commit and discard. Commit persists the meal only after server-side recomputation and draft claim. | `Api/Endpoints/MealDraftEndpoints.cs`, `Infrastructure/Services/MealDraftService.cs` |
| Review UI | `frontend/components/meals/MealDraftReviewSheet.tsx` edits portions/matches, surfaces warnings and provenance, and commits or discards the shared draft. | `frontend/components/meals/MealDraftReviewSheet.tsx` |
| Legacy routes (app ≤ 1.0.10) | The scan-session persistence model is removed. Installed app 1.0.10 has no over-the-air updates, so `GET`/`DELETE /api/meals/scan/{id}` and `PUT /api/meals/scan/{id}/confirm` remain as a thin adapter over the photo draft, and scan responses also carry `scanSessionId` (the draft id). Confirm maps the old body onto a draft commit: the old grams are kept and its nutrition numbers are ignored. A food the user picked becomes a candidate or replacement choice, and an item the old UI showed without a match is logged without calories. Nothing is auto-selected. Remove the adapter once `AppRequests` shows no traffic to these routes. | `Api/Endpoints/LegacyMealScanEndpoints.cs` |

MCP is live at `/mcp`; it is not being removed. MCP and Coach use the same draft service for meal writes (`Api/Mcp/MealSymptomTools.cs`, `Infrastructure/Services/CoachChatService.cs`).

## 2. Scan flow

1. **Image ingress:** the endpoint rejects missing/oversize uploads and notes longer than 200 characters, then runs `MealPhotoPreprocessor` before calling `MealScanService` (`Api/Endpoints/MealScanEndpoints.cs`).
2. **Stage A — vision:** the `vision` keyed `IChatClient` returns a structured component list with identity, portion low/mid/high estimates, confidence, preparation cues, serving hints and search queries. It does not supply nutrition values. The implementation makes at most two structured-output attempts and semantically validates the result (`Infrastructure/Services/MealScanService.cs`, `Application/Common/Helpers/MealVisionValidator.cs`).
3. **Stage B1 — grounding:** `ComponentGroundingEngine` resolves components against the existing food-search stack and the shared `GroundingPolicy`; searches are bounded in parallel (`Infrastructure/Services/MealScanService.cs`, `ComponentGroundingEngine.cs`, `GroundingPolicy.cs`).
4. **Stage B2 — batched selection:** one structured `MealScanCandidateSelectionStage` request covers every eligible ambiguous component; `MealScan:MaxComponentsPerPhoto` bounds the batch. Model output can only refer to candidate indices already supplied by the server and must meet the confidence floor; the optional compatibility-agreement flag restricts acceptance to the compatibility-top candidate. If enabled, bounded Agent Framework review is attempted only for unresolved eligible items (`Infrastructure/Services/MealScanCandidateSelectionStage.cs`, `MealScanAgentReviewService.cs`, `Application/Common/Helpers/MealScanCandidateSelector.cs`).
5. **Agent review/reanalysis:** agent review can inspect only server-owned candidate snapshots and request at most one reanalysis, using an allowed reasoning effort under `MealScan:AgentMaxReanalysisEffort`. Its selection and reanalysis calls are recorded in the scan's `AiUsageMeter`. Any chosen candidate must pass deterministic confidence, identity-overlap and post-reanalysis improvement gates. Reanalysis may produce a new grounding snapshot, but the returned item is based on the original `GroundedItem`; its original portion estimate is retained, so agent reanalysis does not change logged grams (`Infrastructure/Services/MealScanAgentReviewService.cs`, `MealScanAgentDecisionGate.cs`). Agent review is opt-in under `MealScan:EnableAgentGroundingReview` (default false).
6. **Stage C — nutrition and health:** nutrition is computed from grounded per-100g basis × item grams by `NutritionCalculator`. FODMAP/gut signals are attached only to catalog-grounded items. Optional web results pass nutrition plausibility checks, carry a citation, and do not receive FODMAP/gut ratings. Web-nutrition extraction records to the scan's ambient `AiUsageMeter` scope; lookups outside a scan (such as NLP or Coach) do not contribute to scan usage (`Infrastructure/Services/MealScanService.cs`, `Application/Common/Helpers/NutritionCalculator.cs`, `Infrastructure/Services/MealScanHealthSignals.cs`, `WebNutritionCascade.cs`).
7. **Persist draft:** scan output is saved with origin `photo`, warnings, prompt/deployment metadata and nutrition provenance; the frontend reviews it through the common draft UI. Commit re-resolves/recomputes catalog-linked items server-side, rejects unresolved included items, and claims the pending draft with an ETag-conditional replace before meal creation (`Infrastructure/Services/MealDraftService.cs`, `Application/Common/Interfaces/ITableStore.cs`).

`GroundingPolicy` is the shared auto-select decision across scan, NLP, Coach, MCP, and describe-food: only `Exact`/`Probable` with `MatchConfidence >= 0.85`, calories present, passing `NutritionSanity`, and no compatibility/food-form veto can be auto-selected. Otherwise candidates are returned with `needs_choice`; draft commit requires an explicit candidate key (including the preview candidate), replacement, or log-without-calories choice, or returns 422 (`Infrastructure/Services/GroundingPolicy.cs`, `MealDraftService.cs`).

## 3. Scan cost and work budgets

The scan starts a linked cancellation deadline controlled by `MealScan:DeadlineSeconds` (default 60). It bounds Stage A, grounding, selection, web lookups and enrichment. The scan skips batched selection when less than `MealScan:MinSecondsForSelection` remains (default 8), and skips the web cascade when less than `MealScan:MinSecondsForWeb` remains (default 6). Candidate selection and web lookup use remaining time; grounding concurrency and per-scan candidate/web counts are bounded by configuration (`Infrastructure/Services/MealScanService.cs`, `MealScanCandidateSelectionStage.cs`). Final draft persistence deliberately uses the request cancellation token, not the scan-deadline token, so completed analysis is not discarded at the last step.

`VisionResultCache` deduplicates Stage-A work using a key containing user, image digest, effective prompt version, deployment, reasoning effort and note. It caches the decomposition only; subsequent grounding and review remain part of the scan (`Infrastructure/Services/VisionResultCache.cs`, `MealScanService.cs`).

Vision and selection are separately keyed `IChatClient` workloads. Deployment/reasoning effort resolve from `AzureOpenAI:Workloads:{workload}:Deployment` and `AzureOpenAI:Workloads:{workload}:ReasoningEffort`, with shared deployment fallback. `AiUsageMeter` records every scan model call (Stage A, batched selection, agent review/reanalysis and web-nutrition extraction) in the per-scan summary and `gutai.ai.operation.*` metrics. `WebNutritionCascade` uses `AiUsageMeter.Current` from the AsyncLocal-backed `BeginScope()` opened for the scan; lookups outside a scan record nothing (`Infrastructure/Services/AiWorkloads.cs`, `AiUsageMeter.cs`, `DependencyInjection.cs`).

## 4. Accuracy controls

All the following accuracy flags default to false unless noted. Configuration and behavior are implemented in `MealScanService.cs`, `MealScanCandidateSelectionStage.cs`, `MealScanAgentReviewService.cs`, and `PortionCalibrator.cs`.

### Hidden calories

`Features:HiddenCalories` selects a dedicated `HiddenCaloriesVisionResult` wire type and appends the versioned hidden-calorie instructions to the Stage-A prompt. The ordinary wire schema and prompt version remain unchanged when the flag is off; `EffectiveVisionPromptVersion` adds `+hidden-calories.v1` when enabled, keeping cache/evaluation keys separate.

When enabled, Stage A may infer cooking additions only from visible cues. Inferred lines include identity, portion range, confidence and cue, never nutrition values. `MealVisionValidator` caps inferred components (`MealScan:MaxInferredComponents`, default 3) and their combined midpoint mass (`MealScan:MaxInferredGramsPerMeal`, default 40 g), and requires an explicit cue. Inferred draft rows are not included by default; the review UI explicitly lets the user opt them in (`MealScanService.cs`, `MealVisionValidator.cs`, `MealDraftReviewSheet.tsx`).

### Portion calibration

`Features:PortionCalibration` enables `PortionCalibrator`, but factors are applied only when a non-empty `MealScan:PortionCalibration:Version` is configured. Otherwise calibration is a no-op and the recorded calibration version is null. Class/tier factors come from `MealScan:PortionCalibration` configuration. Calibration is clamped to the validated vision low/high range, marks the method `vision_estimate_calibrated`, and recomputes nutrition from the same basis (`Infrastructure/Services/PortionCalibrator.cs`, `FoodClassClassifier.cs`, `MealScanService.cs`).

### Candidate agreement and multi-query grounding

`MealScan:RequireCompatibilityAgreement` defaults false. When true, B2 accepts only candidate index 0 (the compatibility-top candidate); agent review applies the matching top-candidate condition. `MealScan:MultiQueryAutoSelect` defaults false and is passed to `ComponentGroundingEngine` (`MealScanCandidateSelectionStage.cs`, `MealScanAgentReviewService.cs`, `MealScanService.cs`).

Web nutrition lookup is separately controlled by `Features:WebGrounding` (default false). The cascade is cache-first, then DuckDuckGo/Jina retrieval and structured extraction; it sanity-checks extracted nutrition, retains source URL, and fails soft. Web-grounded lines remain distinct from catalog provenance and are excluded from FODMAP/gut classification (`Infrastructure/Services/WebNutritionCascade.cs`, `MealScanService.cs`).

## 5. Personalization and context

The optional image `note` is trimmed, limited to 200 characters at the endpoint, and presented to the model as user context rather than instructions. It is included in the Stage-A cache key so distinct notes do not share a decomposition (`Api/Endpoints/MealScanEndpoints.cs`, `Infrastructure/Services/MealScanService.cs`, `VisionResultCache.cs`).

The scan loads `User.PreferredFoodRegion` for regional web grounding. It also derives food-product boost IDs from the user's recent 30-day meal history (up to 50 IDs) for catalog resolution. `PreferredFoodRegion` is threaded through natural-language resolution and the Coach/MCP food lookup paths as well (`Infrastructure/Services/MealScanService.cs`, `AgentMealItemResolver.cs`, `Api/Endpoints/MealEndpoints.cs`, `Infrastructure/Services/CoachChatService.cs`, `Api/Mcp/FoodTools.cs`).

## 6. Related meal creation, suggestions and evaluation

Photo scans are one of five shared draft origins: `photo`, `coach`, `mcp`, `nlp`, and `suggestion` (`Application/Common/Interfaces/MealDraftRecord.cs`). NLP logging at `/api/meals/log-natural` creates an `nlp` draft; an ambiguous food retains its preview candidate with a `needs_choice` warning. Commit requires an explicit candidate key (including selecting the preview), replacement, or log-without-calories choice; otherwise the server returns 422. `LogMealSheet` and `MealDraftReviewSheet` block saving until the user chooses (`Api/Endpoints/MealEndpoints.cs`, `Infrastructure/Services/MealDraftService.cs`, frontend components).

When `Features:MealSuggestions` is enabled (default false), `/api/meals/suggestions` builds server-owned food candidates; the model returns only pool indices and grams, and deterministic server checks enforce nutrition budget, FODMAP/exclusion rules and substitution repair before suggestion-origin drafts are stored (`Infrastructure/Services/MealSuggestionService.cs`, `Api/Endpoints/MealSuggestionEndpoints.cs`).

Evaluation and operator tools:

- `backend/tools/GoldenScanHarness` has `stage-a`, `in-process` and `e2e` modes, manifest v2 and GoldenMetrics v2 gates; gates are run manually against the live deployment (`--mode in-process --refresh --gate`).
- `backend/tools/AgentEvalHarness` evaluates Coach, describe-food and label suites with graders in `backend/src/GutAI.Infrastructure/Services/Evaluation`.
- `backend/tools/CorrectionAnalytics` produces a read-only operator report and calibration snippet. `backend/tools/ScanMealRepair` is a dry-run historical scan-meal repair utility.

These tools measure or report the implemented behavior; their thresholds and reports do not replace user review or server-side commit validation.
### Scan configuration inventory

Defaults below are code defaults unless supplied in appsettings. Feature flags default false: `Features:HiddenCalories`, `Features:PortionCalibration`, `Features:MealSuggestions`, and `Features:WebGrounding`. Scan keys: `MealScan:DeadlineSeconds` (60); `MealScan:MaxComponentsPerPhoto` (12); `MealScan:MaxInferredComponents` (3); `MealScan:MaxInferredGramsPerMeal` (40 g); `MealScan:MaxConcurrentGrounding` (4); `MealScan:EnableCandidateDisambiguation` (true); `MealScan:EnableAgentGroundingReview` (false); `MealScan:AgentMaxReanalysisEffort` (high); `MealScan:AgentMinSelectionConfidence` (0.90); `MealScan:AgentReanalysisMinImprovement` (0.05); `MealScan:MinCandidateSelectionConfidence` (0.85); `MealScan:MinSecondsForSelection` (8); `MealScan:MinSecondsForWeb` (6); `MealScan:MaxWebQueriesPerScan` (2); `MealScan:RequireCompatibilityAgreement` (false); `MealScan:MultiQueryAutoSelect` (false); and `MealScan:PortionCalibration:Version` plus class/tier factors. `MealScan:MaxCandidateDisambiguationsPerScan` has been removed; B2 batches all eligible ambiguous components, bounded by `MealScan:MaxComponentsPerPhoto`.
