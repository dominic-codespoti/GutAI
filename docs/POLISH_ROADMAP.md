# GutAI Polish & Wow-Factor Roadmap

> Outcome of the 2026-08 polish audit (frontend motion/theme/shell scouts + feature-surface gap analysis).
> Goal: convert GutAI's capability lead (correlation engine, grounded AI scan, coach) into perceived
> wow, delight, and retention — without violating the evidence-honesty architecture.

## Decision Record

| Decision | Choice | Rationale |
| --- | --- | --- |
| Telemetry | **None** | `privacy.tsx` states analytics are not collected; stance stays true. Consequence: all verification in this roadmap is manual/visual/EAS-device based, not metric-based. |
| Chart library | **`victory-native` v42** (the npm package formerly published as "victory-native-xl") + `@shopify/react-native-skia@^2.11.1` | Apple-Health-grade charts fastest path; Skia pinned ABOVE the Expo SDK pin because victory v42 requires ≥2.6 — see ARCHITECTURE.md tech-stack note. Native batch installed 2026-08-26: skia, view-shot, expo-notifications (+ plugin in app.json). One EAS dev rebuild covers Phases 2/3/4A: `eas build --profile development`. |
| Notifications | **Local scheduled only** this cycle (`expo-notifications`) | No backend push infra needed; privacy disclosure stays small and truthful. Server push = explicit follow-up. |
| Offline + OTA | **Descoped** (user decision, 2026-08-26) | Phase 6 reduced to splash-finish only; `expo-updates`, NetInfo banner, query persister, and optimistic logging dropped from this roadmap. Revisit as a future roadmap if needed. |

## Native-dependency batching (important)

Phases 2, 3, and 4A each add native code. To avoid three EAS rebuild cycles, install
**all three** (`@shopify/react-native-skia`, `react-native-view-shot`, `expo-notifications`)
in a single dev-build cycle when Phase 2 starts, then ship phases independently against
that build.

---

## Phase 0 — Hygiene & Trust (no native deps)

Fixes the consistency debt that quietly undermines everything above it.

1. **Trust copy fixes** — `frontend/app/privacy.tsx` and user-facing surfaces keep
   **GutLens** as the product brand; technical `GutAI` identifiers may remain internal.
   Replace `support@workoutquestapp.com` contact, correct "processed locally" claim
   (insights are server-computed).
2. **Theme token leaks** — route hardcoded colors through `useThemeColors()` /
   `src/utils/theme.ts`: `components/Toast.tsx` (duplicate light/dark literals),
   camera overlays in `app/(tabs)/scan.tsx` + `components/meals/LogMealSheet.tsx`
   (`#000/#fff/rgba`), `MealScanReviewSheet.tsx` source-chip literals, PRO badge `#fff`.
   Add an explicit `cameraOverlay` token pair rather than inline hex.
3. **Contrast** — preserve body-readable muted text in dark mode while adapting the
   palette toward the GutLens forest surfaces; audit all body-size color pairs.
4. **Haptics consolidation** — every direct `expo-haptics` import outside
   `src/utils/haptics.ts` moves behind the util; wire the currently-unused
   `success/warning/error` wrappers at meal-save / validation-failure / error-retry sites;
   add missing feedback (date nav, retry buttons, logout, search submit, camera open,
   template open).
5. **Empty states** — shared `<EmptyState icon title body action>` component; dedicated
   zero-meal state in `app/(tabs)/meals.tsx` (currently renders nothing); first-run
   guidance where Profile conditionally omits sections. Simple themed SVG illustrations,
   no Lottie.

**Acceptance:** grep gates pass — no direct `expo-haptics` outside the util, hex literals
only inside theme files; `make ci` green; dark-mode visual pass.

## Phase 1 — Delight Layer (no native deps)

1. **CountUp** text component (Reanimated-driven) for calories, macro numbers, streak —
   dashboard, `DailySummary.tsx`, scan review totals.
2. **Meal-logged celebration** — confetti burst + recap card ("Chicken bowl · 540 kcal")
   with photo thumbnail after scan save *and* manual/natural-language saves; links into
   diary context.
3. **Entrance stagger** on dashboard/meals/insights cards and list rows (Reanimated
   entering/layout transitions).
4. **Scan review sheet reveal** — staged confidence/result presentation instead of
   instant mount.
5. **Reduced motion** — gate celebrations/staggers via Reanimated's built-in
   `useReducedMotion`; honor OS setting everywhere motion was added.

**Acceptance:** visual verification on iOS + Android simulators; reduce-motion OS toggle
suppresses celebratory motion; no regression in sheet/keyboard behavior.

## Phase 2 — Charts & Insight Storytelling (native deps batch starts here)

The correlation engine is the moat; give it a surface worth screenshotting.

1. Install `@shopify/react-native-skia` + `victory-native-xl`; **one EAS dev build** also
   carrying Phase 3/4A native deps.
2. Themed chart-kit wrappers over victory-native (`src/components/charts/`):
   trend line/area, stacked bar, donut, scatter, calendar-style heatmap — all fed by
   theme tokens, entrance-animated.
3. Wire to **existing endpoints only** (no backend changes):
   - Nutrition trends → `GET /api/insights/nutrition-trends`
   - Macros by meal → `/nutrition-by-meal-type`
   - Symptom severity timeline/heatmap → symptoms history
   - Correlation strength visualization → `/correlations`
   - Additive exposure → `/additive-exposure`
   - Streak calendar → `GET /api/meals/streak` (+ history)
4. Upgrade `DailySummary.tsx` from static text to ring + goal state.
5. Tap-through drilldowns from charts to filtered lists/details.

**Acceptance:** `npx tsc --noEmit` + visual pass with live API data; scrubbing works;
charts respect dark mode and reduced motion.

## Phase 3 — Share Cards (uses Phase 2 build)

1. `react-native-view-shot` capture of chart/card subtrees → share sheet.
2. Templates: top-trigger-food card, food safety report card, streak milestone,
   weekly insights summary.
3. Branded footer + one-line medical disclaimer on every card (health-claim hygiene).

**Acceptance:** sharing produces a rendered image on both platforms; disclaimer present.

## Phase 4 — Retention Backbone

**Tier A (this cycle, local-only):**
1. `expo-notifications` (installed in the Phase-2 batch build); contextual permission
   priming — never a cold ask on launch.
2. Scheduled logging reminder (user-selected time) + streak-protection evening nudge.
3. Settings toggles + `privacy.tsx` disclosure update (local notifications, no tokens
   sent anywhere — keep the existing honesty).
4. Streak milestones: badge levels + streak calendar (Phase 2 heatmap kit).

**Tier B (explicit follow-up, out of this roadmap's scope):** server push via Expo Push
API from .NET, device-token registration endpoint, weekly digest. Requires backend work,
store permission declarations, and a larger privacy-policy change.

**Acceptance:** EAS build delivers local notifications at chosen times; toggles persist;
revoking OS permission degrades gracefully.

## Phase 5 — Coach Chat Richness

1. Extend SSE `tool_call` events with a compact structured result payload
   (tool name + typed summary JSON) — backend `ChatEndpoints.cs` +
   `CoachChatService`.
2. Frontend message model gains typed tool results; render **inline rich cards**
   (meal-logged confirmation, food-safety mini-report, trigger insight) instead of
   markdown blobs; tool-call chips become a persistent timeline, not a transient badge.
3. Contextual suggested prompts computed client-side from time-of-day, remaining
   calories, today's symptoms/recent triggers (replaces the 3 static strings).

**Guardrails hit:** AGENTS.md #2/#3 — new payload fields must land in
`frontend/src/types/index.ts` AND get `GutAI.Api.Tests` contract assertions for the
anonymous/SSE shapes.

**Acceptance:** updated contract tests green; end-to-end streamed tool call renders a
rich card over real transport.

## Phase 6 — Splash Finish (SHIPPED; offline + OTA descoped per decision record)

1. **Splash finish** — `expo-splash-screen` `preventAutoHideAsync()` at module
   scope in `app/_layout.tsx` + `hideAsync()` once auth hydration resolves
   (no white flash on cold start). Joins the pending native-dep EAS rebuild.

~~OTA updates · offline resilience~~ — descoped; see Decision Record.

**Acceptance:** no splash flash on cold start; `make ci` green.

---


## Execution Order (completed 2026-08-26; offline + OTA descoped)

```
Phase 0 ──► Phase 1 ──► Phase 2 ──► Phase 3
   │                      │
   │                      └── native-dep batch build (Skia, view-shot, notifications, splash-screen)
   ├──► Phase 4A
   ├──► Phase 5
   └──► Phase 6 (splash finish only — offline + OTA descoped per decision record)

Sizing as shipped: Phase 0 S-M · Phase 1 M · Phase 2 L · Phase 3 S-M ·
Phase 4A M · Phase 5 M-L · Phase 6 S.
```

## Documentation duties per phase

- Each phase updates the relevant `docs/` file (ARCHITECTURE.md frontend tables for
  chart/notification/offline patterns; new patterns get their own section).
- New guardrail-worthy bug categories discovered during execution get added to
  AGENTS.md Strict Project Guardrails.

## Backlog (explicitly deferred)

App-icon refresh · universal links / quick actions · i18n string extraction ·
home-screen widget · Apple Watch target · server push (Phase 4B) · onboarding illustrations.
