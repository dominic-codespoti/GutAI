# Meal Photo Scan — Detailed Design & Implementation Plan

*Companion to `ai-meal-scan-upgrade-research.md`. This document is implementation-grade: orchestration-stack decision, DTOs, service contracts, tool definitions, endpoint wiring, data model, and a phased build order.*

---

## 0. Current LLM stack (audit facts the design must respect)

| Concern | Today | Where |
|---|---|---|
| Chat coach | `Microsoft.Extensions.AI` `IChatClient` over Azure OpenAI **Responses** transport, `UseFunctionInvocation` tool loop, streaming SSE events | `CoachChatService.cs`, `DependencyInjection.cs` |
| Tool definitions | 12 typed `AIFunctionFactory` adapters with ambient authenticated user identity | `CoachChatService.cs` |
| ~~MCP server~~ | **Being removed** — external-AI-apps surface (`/mcp`, `Api/Mcp/*.cs`, `ModelContextProtocol.AspNetCore`). Nothing internal consumed it; chat used its own definitions. Re-addable later via `McpServerTool.Create(AIFunction)` wrapping the same shared tool classes if ever needed | `Api/Mcp/*.cs`, `Program.cs` |
| Vision | `IChatClient.GetResponseAsync<T>` with strict structured output over Azure OpenAI **Responses**; developer-role instructions | `MealScanService.cs`, `DependencyInjection.cs` |
| Text→food / Foundry agents | `IChatClient` structured output or `AIProjectClient.OpenAI.GetProjectResponsesClientForAgent(AgentReference)` | `ContentUnderstandingService.cs` |
| Search/ranking | `IFoodSearchService.SearchAsync / ResolveAsync` over USDA·OFF·AU aggregator with canonicalizer + single ranking pass | `Application/Common/Interfaces/IFoodSearchService.cs` |
| Persistence | Azure Table Storage via `ITableStore`; Pro gating via `useSubscriptionStore` paywall; `aiExtraction` rate-limit partition | throughout |
| Packages | `Azure.AI.OpenAI 2.9.0-beta.1`, `Azure.AI.Projects 2.0.0-beta.2`, `Microsoft.Extensions.AI/OpenAI 10.4.0`, `Microsoft.Agents.AI/OpenAI 1.0.0`, Content Understanding, ImageSharp | csproj |

**Notable:** meal photos use a dedicated color-preserving preprocessor (`MealPhotoPreprocessor`: auto-orient, 2000px cap, JPEG q85). The grayscale `NutritionLabelImagePreprocessor` is for label OCR only. All model inference uses the Responses transport. Reasoning-model requests omit sampling parameters such as `temperature`; stable instructions are sent once as a developer message, while dynamic user data remains user input. Stage-A `VisionReasoningEffort` accepts `none`, `low`, `medium`, `high`, `xhigh`, and `max`; the golden harness isolates each effort in its cache key and uses a 10-minute network timeout so high-effort runs are measured rather than prematurely cancelled. OpenAI has deprecated the Assistants API in favor of Responses; Semantic Kernel itself has been superseded by Microsoft Agent Framework (SK is in maintenance).

---

## 1. Orchestration-stack decision (Semantic Kernel question)

### Options assessed

| Option | Assessment |
|---|---|
| **A. Semantic Kernel** | ❌ Don't introduce in 2026. SK is in maintenance mode; Microsoft's own migration guide moves all new work to Agent Framework. Adding it now means adopting a framework already two steps behind its successor. |
| **B. Microsoft Agent Framework** (`Microsoft.Agents.AI`) | The official SK successor, built on `Microsoft.Extensions.AI` types. Good fit *if* we want multi-agent orchestration or workflow graphs. Overkill for v1: our scan pipeline is a fixed 3-stage graph, not an open-ended agent loop. The Coach already uses the Responses-backed `IChatClient` tool loop; revisit Agent Framework only if durable workflow/session features justify it. |
| **C. Microsoft.Extensions.AI** (`IChatClient`) ✅ | The stable .NET 10 base-layer abstraction that both SK and Agent Framework sit on. Gives us: provider-agnostic `IChatClient` (swap Azure OpenAI ↔ Gemini ↔ Ollama via config), built-in middleware (`UseFunctionInvocation` auto tool-loop, `UseLogging`, OpenTelemetry), **native structured output** via `GetResponseAsync<T>(...)`, and `AIFunctionFactory.Create()` for POCO-as-tool registration without JSON-string schemas. No vendor lock-in, no dead framework. |
| **D. Stay on raw Azure SDK** | Not permitted for production inference. Keep raw Azure clients only where an Azure-specific service has no `IChatClient` equivalent; all OpenAI model calls go through the shared Responses-backed adapter. |

### Recommendation
**Adopt `Microsoft.Extensions.AI` (option C) as the LLM abstraction for all new AI code**, wrapped over the existing `AzureOpenAIClient`:

```
Microsoft.Extensions.AI.Abstractions   (IChatClient, ChatMessage, AIFunction)
Microsoft.Extensions.AI                (UseFunctionInvocation/Logging/OpenTelemetry middleware)
Microsoft.Extensions.AI.OpenAI        (OpenAI SDK → IChatClient adapter, works with Azure credential + endpoint)
```

This is forward-compatible with Agent Framework later (`chatClient.AsAIAgent(...)`).

### North-star stack statement (amended 2026-08-23 after AF guidance review)

> Microsoft.Extensions.AI remains the common inference abstraction. The fixed scan
> pipeline remains ordinary C# for sequencing, validation, grounding, persistence,
> and deterministic nutrition. Stage-B2 is a cascade: direct structured candidate
> choice first, then a bounded Agent Framework `ChatClientAgent` only when direct
> B2 abstains. The agent receives read-only server-owned grounding snapshots and
> may request one capped reanalysis; its proposal must pass the deterministic
> agent acceptance gate. Microsoft Agent Framework also owns the Coach agent
> surface. No autonomous workflow may bypass resolver status, compatibility gates,
> or human review.

Layer map:

| Concern | Technology |
|---|---|
| Bounded model inference | `IChatClient` (M.E.AI) |
| Model transport | Azure OpenAI Responses API |
| Meal-scan orchestration | plain deterministic C# |
| Meal-scan ambiguous review | Agent Framework `ChatClientAgent` + typed read-only tools |
| Nutrition/FODMAP computation | existing domain services |
| Conversational coach | Agent Framework `ChatClientAgent` |
| Coach tools | `AIFunctionFactory` over thin typed tool adapters (ambient user identity — never model-supplied userIds) |
| Scan review tools | server-owned grounding snapshots; one bounded reanalysis call; no mutation |
| Coach state | `AgentSession` (opaque) + app-owned `CoachSessionEntity` |
| Mutating tools | approval-gated via function middleware (structural, not prompt-level) |
| Telemetry | OpenTelemetry on `IChatClient` spans only; sensitive content disabled in prod |

**⚠️ URGENT dependency discovered by this review:** the OpenAI **Assistants API sunsets
2026-08-26**. `AzureOpenAIChatService` + `AssistantFactory` sit directly on it
(`AssistantClient`, threads, `SubmitToolOutputsToRunStreamingAsync`). Coach migration
to `ChatClientAgent` + Responses is therefore **P0b — before any meal-scan work**.

---

## 2. Target architecture

```
                        ┌──────────────────────────────────────────────────┐
                        │ POST /api/meals/scan-image (multipart)           │
                        │ [Authorize] · Pro-gated · aiExtraction limiter   │
                        └───────────────┬──────────────────────────────────┘
                                        ▼
                          IMealScanService.ScanMealImageAsync()
                                        │
        ┌───────────────────────────────┼─────────────────────────────────┐
        ▼ STAGE A                       ▼ STAGE B                         ▼ STAGE C
  Vision decomposition         Grounding & resolution            Deterministic compute
  IChatClient.GetResp          per component:                    pure C#, no LLM:
  onseAsync<MealVision>        B1 IFoodSearchService             per-100g × grams → macros;
  (image + strict JSON         .ResolveAsync(name)               FODMAP/gut-risk scoring via
  schema, reference-object     B2 ambiguous → top-3 back         EXISTING services, unchanged
  prompting, gram ranges,      to vision model for choice
  confidence, no-ref flag)     B3 unresolved → grounded web      → MealScanDraftDto
                               search (citations, flagged)       (items + provenance + conf.)
                                        │
                                        ▼
                     Draft persisted (ScanSession table, status=PendingReview)
                                        │
                                        ▼
              Frontend confirmation sheet (per-item provenance chips,
              editable name/grams, ±25% stepper, model-supplied household serving hints,
              High/Med/Low badges — reuses AddToMealSheet patterns)
                                        │ confirm (PUT /api/meals/scan/{id}/confirm)
                                        ▼
                     Confirm normalizes display names to Title Case, persists
                     total grams in ServingWeightG, then logs the reviewed meal
                                        ▼
                     CreateMeal (existing endpoint/logic, zero changes downstream)
```

Design rules carried over from the research doc:
- The LLM never invents nutrition numbers — only **identity**, **grams (range+midpoint)**,
  **confidence**, and optional **household serving-unit hints** (unit singular/plural plus
  grams for one unit). Hint math is display-only and never contributes nutrition calculations.
- Every displayed value traces to a named source (`usda` | `off` | `au` | `web:<url>` | `ai-estimate`).
- Web results never touch FODMAP flags (curated dataset stays authoritative).

---

## 3. Contracts & DTOs

New file: `backend/src/GutAI.Application/Common/DTOs/MealScanDtos.cs`

```csharp
/// Stage A output — what the vision model returns (strict JSON schema).
public sealed class MealVisionResult
{
    public List<ScannedComponent> Components { get; init; } = [];
    public bool ReferenceObjectVisible { get; init; }
    public string? ScaleNotes { get; init; }          // e.g. "fork ≈18cm at plate edge"
    public decimal OverallConfidence { get; init; }   // 0..1
}

public sealed class ScannedComponent
{
    public required string Name { get; init; }            // "grilled chicken breast"
    public required decimal EstimatedGramsMidpoint { get; init; }
    public required decimal EstimatedGramsLow { get; init; }
    public required decimal EstimatedGramsHigh { get; init; }
    public required decimal Confidence { get; init; }     // 0..1
    public string? PreparationNote { get; init; }         // "appears fried", "sauce visible"
    public string? ServingHintUnit { get; init; }         // "large egg"
    public string? ServingHintUnitPlural { get; init; }   // "large eggs"
    public decimal ServingHintUnitGrams { get; init; }    // grams for one unit; 0 = no hint

}

/// One resolved line item in the draft returned to the client.
public sealed class MealScanItemDto
{
    public required Guid ItemId { get; init; }
    public required string Name { get; init; }

    // Grounding provenance — maps onto existing FoodProduct fields
    public Guid? FoodProductId { get; init; }             // set when matched to DB entry
    public required string Source { get; init; }          // "usda"|"off"|"au"|"web"|"ai"
    public string? SourceUrl { get; init; }               // citation when Source == "web"

    public required decimal Grams { get; set; }           // editable midpoint
    public string? ServingHintUnit { get; init; }         // carried through for review/log display
    public string? ServingHintUnitPlural { get; init; }
    public decimal? ServingHintUnitGrams { get; init; }
    // Computed in Stage C from DB per-100g × grams (null when Source == "ai")
    public decimal? Calories { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? CarbsG { get; set; }
    public decimal? FatG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? SugarG { get; set; }
    public decimal? SodiumMg { get; set; }

    public required decimal MatchConfidence { get; init; }// stage-B resolution confidence
    public required decimal VisionConfidence { get; init; }
    public IReadOnlyList<string>? CandidateNames { get; init; } // alternates for quick swap
}
```

Endpoint request/response live in `Endpoints/MealScanEndpoints.cs` (new):

```csharp
POST   /api/meals/scan-image          → 202 { scanSessionId, items: MealScanItemDto[], warnings[] }
PUT    /api/meals/scan/{id}/confirm   → logs via existing CreateMeal path, marks session Confirmed
DELETE /api/meals/scan/{id}           → discards session
GET    /api/meals/scan/{id}           → re-fetch draft (app restart / retry)
```

---

## 4. Service implementation sketch

New: `backend/src/GutAI.Infrastructure/Services/MealScanService.cs`
Interface in `Application/Common/Interfaces/IMealScanService.cs`.

### 4.1 DI registration (Infrastructure/DependencyInjection.cs)

```csharp
// M.E.AI pipeline over the existing AzureOpenAI resource — RESPONSES transport.
// The adapter is registered once in DI; every inference workload uses this client.
services.AddSingleton<IChatClient>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var azure = sp.GetRequiredService<AzureOpenAIClient>();
    var deployment = cfg["AzureOpenAI:DeploymentName"]!;
    return new ChatClientBuilder(
            azure.GetResponsesClient().AsIChatClient(deployment))
        .UseFunctionInvocation(sp.GetRequiredService<ILoggerFactory>())
        .UseLogging(sp.GetRequiredService<ILoggerFactory>())
        .Build();
});

services.AddSingleton<IMealScanService, MealScanService>();
```

`AsIChatClient(deployment)` comes from `Microsoft.Extensions.AI.OpenAI` and accepts the same credential/endpoint already constructed — no new secrets.

### 4.2 Stage A — vision decomposition (structured output)

```csharp
private static readonly ChatOptions VisionOptions = new()
{
    // Do not set Temperature for reasoning deployments.
    MaxOutputTokens = 2000,
};

private static readonly ChatRole DeveloperRole = new("developer");

private const string VisionDeveloperInstructions = """
    You are a food identification assistant. Analyze the meal photo and list every
    distinct food component visible.

    Rules:
    - Estimate grams per component using visual references (plate ≈26cm, cutlery,
      hands) when present; say so in scaleNotes. Without references, widen the
      low/high range and lower confidence.
    - Account for cooking method in preparationNote (oil absorbed, breading, sauces).
    - estimatedGramsMidpoint must lie within [low, high].
    - Return only the structured output schema.
    """;

async Task<MealVisionResult> DecomposeAsync(byte[] image, string contentType, CancellationToken ct)
{
    var message = new ChatMessage(ChatRole.User,
    [
        new TextContent("Identify all food components in this meal photo."),
        new DataContent(image, contentType),
    ]);
    var result = await _chat.GetResponseAsync<MealVisionResult>(
        [
            new ChatMessage(DeveloperRole, VisionDeveloperInstructions),
            message,
        ], VisionOptions, ct);
    return result.Result ?? throw new MealScanException("Vision stage returned no parseable result.");
}
```


### 4.3 Stage B — grounding through the existing search stack

Per component, in parallel (`Task.WhenAll`, bounded at 4 concurrent):

```csharp
// B1: deterministic resolution — the same call NLP meal parsing uses today.
var resolution = await _foodSearch.ResolveAsync(component.Name, boostIds: recentFoodIds, ct);

if (resolution.Status is FoodResolutionStatus.Resolved)
    return FromResolution(resolution);            // FoodProductId + per-100g values attached

// B2: ambiguous → ask the vision model to pick among top candidates (selection, not recall).
var candidates = resolution.Alternatives.Take(3).ToList();
var choice = await _chat.GetResponseAsync<ChoicePick>([
    new ChatMessage(ChatRole.User,
    [
        new TextContent($"Which database entry best matches '{component.Name}' " +
                        $"as seen in the photo? Candidates:\n{FormatCandidates(candidates)}"),
        new DataContent(image, contentType),      // image again so choice uses vision context
    ]),
], ChoiceOptions, ct);
// accept only if choice.Index < candidates.Count and candidate isn't implausible

// B3: still unresolved → optional grounded web search (behind Features:WebGrounding flag).
return await WebGroundFallbackAsync(component, ct);
```

### 4.4 Stage B3 — web-results incorporation (zero-recurring-cost cascade)

No paid grounding APIs (Bing $14/1k, OpenAI web_search ~$25–40/1k — rejected). The lookup
domain is narrow and repetitive, so a cache-first cascade makes most requests free:

```
IWebNutritionLookup (priority chain):
  0. Cache hit — Azure Table `WebNutritionCache`, key = normalized(foodName)+region
     (food lookups repeat heavily; steady state is pure cache hits, faster over time)
  1. Existing aggregator (USDA FDC + OFF) — free APIs, already integrated
  2. Free search with site: targeting — DuckDuckGo HTML endpoint (no API key):
        GET html.duckduckgo.com/html/?q="{name} nutrition per 100g
            site:fdc.nal.usda.gov OR site:openfoodfacts.org"
     polite scraping: 1 req/s max, real User-Agent, timeout 8s
  3. Fetch top 1–2 hits via Jina Reader (r.jina.ai/{url}) → clean markdown,
     keyless (~20 req/min IP limit — fine at this volume; SearXNG on homelab
     later as drop-in replacement if ever flaky)
  4. Extraction: cheap text call to the EXISTING vision deployment
     "extract per-100g values + source name + source URL from this page;
      prefer USDA when sources disagree; {found:false} if nothing credible"
     — reuse the tolerant number parser from TryParseFallbackResponse
  5. Plausibility validation (Stage-C sanity ranges) → write cache entry
     {values, sourceUrl, fetchedAt} → never fetched again
```

Marginal cost per uncached lookup: 1 DDG request + 1 Jina fetch + ~$0.001 tokens.
Restaurant chains: nutrition pages barely change — scrape-once-cache-forever.

Guardrails: values land as `Source="web"` with `SourceUrl` set; frontend renders a
distinct chip; item excluded from FODMAP/gut-risk scoring inputs beyond generic
additive checks; hard timeout 8s per stage; failure ⇒ fall back to `Source="ai"`
with vision-only estimate and a warning string. NEVER scrape anything Monash-related
(licensed data; third-party FODMAP lists are notoriously wrong).

### 4.5 Stage C — deterministic computation (no LLM)

```csharp
static MealScanItemDto ComputeMacros(FoodProductDto p, decimal grams) => new()
{
    // p carries calories100g/protein100g/... exactly like FoodEndpoints mapping today
    Calories   = Round(p.Calories100g * grams / 100m),
    ProteinG   = Round(p.Protein100g * grams / 100m),
    /* ... */
};
```

Then attach gut-health signals by calling the existing `FodmapService` / `GutRiskService` with the matched `FoodProductId` — identical to how `/food/{id}/fodmap` works, so the confirmation sheet can show the familiar rating chips per scanned item.

### 4.6 Session persistence

New table entity `ScanSessionEntity : ITableEntity` (`ITableStore`, partition = userId):

| Field | Purpose |
|---|---|
| `RawVisionJson` | full Stage-A output (audit + future few-shot corpus) |
| `DraftItemsJson` | the `MealScanItemDto[]` sent to client |
| `Status` | `PendingReview` → `Confirmed` / `Discarded` / `Expired` |
| `CorrectionDeltaJson` | user edits vs draft (name changes, gram deltas) — feedback signal |
| `ModelDeployment`, `CreatedMs` | provenance |

TTL/expiry: sweep or lazy-expire after 24h.

---

## 5. Tool integration across the three LLM surfaces

The repo has three places LLMs meet domain logic. The scan capability appears in each differently:

| Surface | Integration | Mechanism |
|---|---|---|
| **Scan pipeline (new)** | Direct `IChatClient` calls; Agent Framework escalation is opt-in and disabled by default | Stage A and direct Stage-B2 remain ordinary C#. If `EnableAgentGroundingReview` is explicitly enabled after direct B2 abstains, a `ChatClientAgent` inspects server-owned grounding snapshots and may request one capped reanalysis. |
| **Coach chat** | Agent Framework `ChatClientAgent` with domain tools | `AIFunctionFactory.Create(...)` adapters; ambient authenticated identity; approval rules for mutating tools. |

The scan agent cannot search arbitrary providers or mutate products. Its proposed
candidate must pass:

- candidate membership in the inspected snapshot;
- `AgentMinSelectionConfidence` (default 0.90);
- identity-token overlap with the observed component/search queries;
- post-reanalysis confidence improvement (`AgentReanalysisMinImprovement`, default 0.05)
  when inspection ID > 0;
- existing compatibility/validation gates.

Every agent verdict—accepted, abstained, rejected, or missing—is logged with its
reason and tool-call counts.

### Consolidated tool architecture (MCP dropped)

```
Domain services (IFoodSearchService, IMealScanService, FodmapService, ...)
        │
Shared tool classes — attributed methods, typed parameters, DI-injected
        │                      (replaces ChatTools JSON strings + ExecuteToolAsync switch)
AIFunctionFactory.Create(...)  →  ChatClientAgent tools      ← the ONLY consumer
```

Adding a tool becomes: write one attributed method. No schema strings, no switch case,
no hand-rolled result serialization. If an external MCP surface is ever wanted again,
the same `AIFunction`s can be wrapped server-side without touching the tool classes.

---

## 6. Frontend changes

1. **`(tabs)/scan.tsx`**: add a third mode alongside search/browse — **"Photo"** tab: camera capture (reuse `expo-image-picker` patterns from `food/create.tsx` incl. permission handling, quality 0.8), upload multipart to `scan-image`, navigate to review sheet on 202.
2. **New `components/meals/MealScanReviewSheet.tsx`**: per-item rows (name, grams stepper ±25%, computed kcal), provenance chip (`USDA`/`OFF`/`AU`/`WEB ↗` link/`AI`), confidence badge using the existing `confidenceLevel()` thresholds (≥0.85 High / ≥0.6 Med / Low), candidate-swap dropdown populated from `candidateNames`, overall warnings banner ("No reference object visible — portions are rough estimates").
3. Confirm → `PUT scan/{id}/confirm` then invalidate `meals` / `daily-summary` queries (same pattern as `saveAndLogFood`).
4. Pro paywall + haptics + review-request hooks copied from the label flow.

---

## 7. Configuration & rate limiting

```jsonc
// appsettings.json additions
"AzureOpenAI": { "VisionDeployment": "gpt-4.1-mini" },
"Features": {
  "MealScan": true,
  "WebGrounding": false,          // flip on after citation/QA pass
  // no keys needed: DDG HTML scrape + Jina Reader are keyless; cache in Tables
"MealScan": { "MaxComponentsPerPhoto": 12, "MaxImageBytes": 8388608,
              "MaxWebQueriesPerScan": 2, "SessionTtlHours": 24 }
```

- Rate limit: reuse the `aiExtraction` partition (already per-user, per-subnet keyed in `Program.cs`). Consider a stricter `mealScan` policy (e.g. 20/day Pro users) since vision calls cost more than text.
- Validate/re-encode uploads through **ImageSharp** (already referenced): strip EXIF, cap longest edge ~2048px, transcode JPEG q85 before sending to the model — cuts tokens/cost ~40% and kills GPS-metadata privacy leaks.
- Secrets: none new for option (a); Gemini key only if option (b) — user-secrets locally, Key Vault/container env in deploy per `DEPLOYMENT.md`.

---

## 8. Testing & validation

1. **Unit**: Stage-C math (property test: macros monotone in grams, per-100g round-trips); schema-tolerant parsing of vision output (malformed ranges, missing fields — extend the `TryParseFallbackResponse` test family); resolver stubbing for B1/B2/B3 branches.
2. **Golden-image set**: 25–50 real meal photos (own phone) with hand-entered ground truth; CI-nightly job reports mean % error per macro + component-recall. This is your regression net for prompt/model changes — treat any deployment bump as an experiment gated on this suite.
3. **Contract tests**: `MealScanEndpoints` auth (401/403 non-Pro), rate-limiter behavior, ImageSharp rejection paths (oversize/non-image).
4. **E2E (Playwright/Expo)**: mock backend route → happy-path scan → edit grams → confirm → meal appears in diary.
5. **Cost telemetry**: log per-scan token usage + web-query count; alert if p95 exceeds ~$0.05.

---

## 9. Build order & estimates

| Phase | Scope | Files | Est. |
|---|---|---|---|
| **P0a — docs** | Commit both design docs | docs/ | ½ hr |
| **P0b — ⚠️ EMERGENCY: coach off Assistants API** (sunsets 2026-08-26) | Replace `AssistantFactory` + `AssistantClient` usage with code-owned `ChatClientAgent` over Responses transport; port the 12 tools to typed adapters w/ ambient identity; remap streaming events; `CoachSessionEntity` replaces `AgentThreadId`; delete the hand-rolled tool loop | DependencyInjection.cs, AzureOpenAIChatService.cs, AssistantFactory.cs ❌, new Coach/* , TableStore | **2–4 d — FIRST** |
| **P1 — skeleton** | M.E.AI packages + central version pinning (`Directory.Packages.props`: M.E.AI 10.9.0, Agents.AI 1.19.0); `IChatClient` DI (Responses transport, OTel spans, prod `ManagedIdentityCredential` branch); DTOs + semantic validator; `MealScanEndpoints` (auth+limits+paywall); session table | DependencyInjection.cs, MealScanDtos.cs, MealScanEndpoints.cs, Directory.Packages.props, TableStore | 1–2 days |
| **P2 — Stage A** | Vision decomposition w/ structured output + golden-image harness scaffold | MealScanService.cs, tests | 1–2 days |
| **P3 — Stage B** | ResolveAsync wiring, top-3 disambiguation, provenance mapping | MealScanService.cs | 1–2 days |
| **P4 — Stage C + FODMAP attach** | Macro compute, gut-health signals, warnings | MealScanService.cs | 1 day |
| **P5 — Frontend** | Scan tab mode, review sheet, confirm flow | scan.tsx, MealScanReviewSheet.tsx, api/index.ts | 2–3 days |
| **P6 — web grounding** | `IWebNutritionLookup` (openai-responses first), feature flag, citations UI | WebNutritionLookup.cs, sheet chip | 1–2 days |
| **P7 — chat surface + tool-architecture cleanup (v2)** | Shared tool-adapter classes fully replace `ChatTools.cs` strings + switch in the migrated coach; `analyze_meal_photo` tool (draft-only, approval-gated commit) | Coach tool classes, ChatTools.cs ❌ | 1–2 days |
| **P8 — MCP removal** | Delete `Api/Mcp/*`, `ModelContextProtocol.AspNetCore` package, `Program.cs` wiring (independent cleanup; do anytime) | 3 files touched | <½ day |

Sequencing note: P1–P4 are fully testable without the app (curl + golden images); P5 unlocks dogfooding; ship behind `Features:MealScan` flag to internal testers before enabling for all Pro users.

---

## 10. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Vision model drift on deployment upgrades | Golden-image gate (§8.2) blocks promotion |
| Systematic underestimation (documented industry-wide) | Gram ranges default midpoint-high; prominent ±25% stepper; warnings copy sets expectations |
| Web garbage values entering diary | Citations mandatory, distinct UI treatment, capped queries, never touches FODMAP flags |
| Cost blowout | Rate-limit partition, image downscale, MaxComponents/web-query caps, telemetry alert |
| Assistants-API deprecation (upstream) | Out of scope for v1 but M.E.AI adoption here makes the eventual chat migration mechanical (`AsAIAgent`) |
