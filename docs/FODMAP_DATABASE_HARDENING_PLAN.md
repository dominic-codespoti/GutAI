# FODMAP Database Hardening Plan

> Status: **EXECUTED 2026-08-26.** Fixes 1, 2 and 5 are implemented and verified (plural-tolerant whole-food matcher; chemistry-family breadth parsing; `Resolve(key, fallback)` governance with ~85% keyed coverage — 33 contested composite-dish/dual-judgment keys deliberately left list-local per the inclusion policy). Fix 4 remains planned; fixes 3 and 6 stand as policies.
> Scope: closes the six residual gaps from the post-fix audit
> (governance coverage, plural brittleness, dose model, free-label trust,
> category-breadth parsing, literature-judgment severities).

## Summary

| # | Gap | Fix | Effort | Risk |
|---|-----|-----|--------|------|
| 1 | Canonical governance covers only 34%/32% of patterns | Derivation-by-construction: entries resolve severity through the shared map | ½ day | Low |
| 2 | Whole-food matching has no plural tolerance | Reuse the ingredient path's plural-boundary matcher | ½ hr | Low |
| 3 | No dose/portion model | **Deferred with trigger condition** (honest deferral, see §3) | — | — |
| 4 | Free-from claims trusted blindly, even against contradicting ingredients | Scope claims to ingredient text + contradiction downgrade | ½ day | Medium (behavior change) |
| 5 | Multi-class categories parsed as first token only | Proper chemistry-token parsing for the stacking bonus | 2 hrs | Medium (score shifts) |
| 6 | Severities are literature judgment | Accepted-limitation policy + review cadence (cannot be "fixed") | 1 hr | — |

Recommended sequence: **2 → 5 → 1 → 4**, with 3 and 6 closed as policies.
Total engineering effort ≈ **1.5 days** including tests.

---

## 1. Governance by construction (was: 34%/32% keyed)

Today `SharedFodmapSeverities` governs an entry only while a consistency test
happens to compare it. Drift is *detected*, not *impossible*.

**Fix — derivation API on the canonical map:**

```csharp
// SharedFodmapSeverities.cs
public static string Resolve(string key, string fallback)
    => Severities.TryGetValue(key, out var v) ? v : fallback;
```

Every `FodmapData` entry becomes:

```csharp
new("banana", MatchUtils.WordBoundary("bananas?"), new() {
    Name = "Banana (Fructan)",
    Severity = SharedFodmapSeverities.Resolve("banana", "Moderate"), ... })
```

Properties:

- Keyed entries can no longer diverge from canon — drift is impossible, not tested-for.
- Unkeyed entries keep their curated fallback (dishes, brands, composite names do
  **not** belong in a canonical *ingredient* map).
- The existing equality consistency tests remain as belt-and-braces and become trivially green.

**One-time key-completion pass** (script-assisted): extract all unique
single-concept patterns + current severities; add missing keys to the map
matching today's values verbatim (zero behavior change by definition); skip
multi-word dish/brand names. Target metric: **≥ 85% of ingredient patterns
keyed**, reported by a new test assertion so regression is visible.

## 2. Plural-tolerant whole-food matcher

`WholeFoodRegexMatch` gets the treatment `IngredientPatternMatch` already has:

```csharp
var patternSuffix = pattern.EndsWith("s") ? "" : "s?";
regex = new Regex($@"\b{Regex.Escape(pattern)}{patternSuffix}\b", ...);
```

- Word boundaries preserved → the `pita`/`pepitas` false-positive stays dead.
- Explicitly-authored irregulars (`blackberries`, `cherries`) end in `s` and are untouched.
- New parametrized probe test: singular entry must catch plural product name
  (`Pistachios`, `Portobellos`, `Barley grains`) across a sampled entry set.

## 3. Dose model — deferred with an honest trigger condition

A real portion model needs measured thresholds AND serving-size data; our
embedded catalogs carry **0% serving fields** and Monash thresholds are
proprietary. Building it now would fabricate precision — the exact failure mode
this codebase eliminated elsewhere.

**Trigger condition to revisit:** after `scripts/refresh-food-data.sh`
regenerations land serving sizes, OR a licensed measured-threshold source
becomes available. Until then the interface intentionally takes no grams, and
every summary states portion-dependence. *(No code.)*

## 4. Free-label claims scoped + contradiction downgrade

Two deterministic rules, no heuristics:

1. **Scope**: for `Assess(product)`, free-from claims suppress triggers only when
   found in the **ingredient text**, not the product name ("Lactose-Free
   Ice Cream Bar" as a name is marketing; an ingredient list saying
   "lactose-free milk" is evidence). `AssessText` keeps current behavior —
   the description *is* the evidence there.
2. **Contradiction downgrade**: when a claim coexists with its own trigger word
   in ingredients (`"lactose-free"` + `"milk solid"`), keep the trigger but set
   `Confidence = "Low"` and append to the explanation:
   *"Label carries a free-from claim that conflicts with the ingredient list;
   ingredient evidence prevails."*

Negation handling (`not lactose-free`) is unchanged. Tests: name-only claim no
longer suppresses; contradicting pair downgrades confidence and keeps the flag;
clean claim still suppresses.

## 5. Correct chemistry-breadth parsing for the stacking bonus

`CalculateIngredientScreeningScore` counts distinct categories via
`SubCategory.Split('+',' ')[0]`, so `"Excess Fructose + Sorbitol"` counts once
as "Excess". Fix: split on `'+'`, trim, dedupe case-insensitively, and count
each fragment as its own chemistry:

- `"Excess Fructose + Sorbitol"` → {Excess Fructose, Sorbitol} = 2
- `"Fructan + GOS"` → 2
- plain `"Fructan"` → 1

Effect: dual-class foods (apple, watermelon, snow peas) now correctly feed the
≥3-distinct-chemistry multiplier. Score expectations in
`ThreeCategoryStacking_Score27`-style tests shift slightly and are updated
deliberately with worked arithmetic in comments.

## 6. Literature-judgment severities — accepted limitation, managed

Cannot be closed by curation; managed instead:

- `FodmapData.cs` header gains a `Last reviewed vs Monash public retests:` stamp.
- Policy line added to ARCHITECTURE.md: contested entries are re-checked against
  Monash public retest announcements quarterly; changes update the canonical map
  first, lists follow by construction (via §1).
- Contested items (ripe banana, IMO syrup, sorghum-class grains) carry an
  inline `// contested:` comment so future editors know which judgments are soft.

---

## Test & rollout plan

| Step | Tests touched | Behavior change? |
|---|---|---|
| 2 plural matcher | new parametrized probes | additive detections only |
| 5 breadth parsing | 3–4 score expectations updated with arithmetic | yes, intended |
| 1 derivation + keys | consistency suite simplifies; ≥85% keyed assertion | none (values copied verbatim) |
| 4 claim scoping | 3 new facts + 1 updated | yes, intended |

Each step lands separately with the full suite green between steps, ordered
least-behavioral-change first (2 → 5 → 1 → 4).
