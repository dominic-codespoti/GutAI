import assert from "node:assert/strict";
import test from "node:test";
import type { GroundingCandidate, MealDraft, MealDraftItem } from "../../types";
import {
  buildCommitRequest,
  canSave,
  candidateBasis,
  draftTotals,
  effectiveBasis,
  initialRows,
  rowNeedsChoice,
  type DraftRowState,
} from "../mealDraft";

const basis = {
  caloriesKcal: 200,
  proteinG: 10,
  carbsG: 20,
  fatG: 5,
  fiberG: 3,
  sugarG: 4,
  sodiumMg: 100,
};

function item(itemId: string, changes: Partial<MealDraftItem> = {}): MealDraftItem {
  return {
    itemId,
    name: itemId,
    source: "db",
    grams: 100,
    matchConfidence: 1,
    ...changes,
  };
}

function draft(items: MealDraftItem[]): MealDraft {
  return {
    draftId: "draft",
    origin: "photo",
    status: "PendingReview",
    items,
    warnings: [],
    referenceObjectVisible: false,
    overallConfidence: 1,
    totals: {
      calories: 0,
      proteinG: 0,
      carbsG: 0,
      fatG: 0,
      itemsWithoutNutrition: 0,
    },
    createdAt: "2026-01-01T00:00:00Z",
    expiresAt: "2026-01-02T00:00:00Z",
  };
}

function candidate(changes: Partial<GroundingCandidate> = {}): GroundingCandidate {
  return {
    name: "candidate",
    source: "usda",
    match_confidence: 0.9,
    candidate_key: "candidate-key",
    calories_100g: 150,
    protein_100g: 8,
    carbs_100g: 12,
    fat_100g: 4,
    ...changes,
  };
}

test("draft totals exclude removed rows and count included rows without a basis", () => {
  const rows: DraftRowState[] = initialRows(
    draft([
      item("known", { per100g: basis }),
      item("unknown"),
      item("excluded", { includedByDefault: false, per100g: basis }),
    ]),
  );
  const totals = draftTotals(rows);
  assert.deepEqual(totals, {
    calories: 200,
    proteinG: 10,
    carbsG: 20,
    fatG: 5,
    itemsWithoutNutrition: 1,
  });
});

test("canSave requires an included row and explicit permission for unknown nutrition", () => {
  assert.equal(canSave([]), false);
  assert.equal(canSave([{ item: item("off"), grams: 50, included: false }]), false);
  assert.equal(canSave([{ item: item("unknown"), grams: 50, included: true }]), false);
  assert.equal(
    canSave([
      {
        item: item("unknown"),
        grams: 50,
        included: true,
        logWithoutCalories: true,
      },
    ]),
    true,
  );
  assert.equal(
    canSave([{ item: item("known", { per100g: basis }), grams: 50, included: true }]),
    true,
  );
});
test("canSave requires an explicit choice for needs-choice rows", () => {
  const preview = candidate({ candidate_key: "preview-key" });
  const other = candidate({ candidate_key: "other-key", name: "other" });
  const needsChoice = item("ambiguous", {
    needsChoice: true,
    per100g: basis,
    grounding: {
      query: "food",
      resolution_status: "ambiguous",
      auto_selected: false,
      candidates: [preview, other],
      match_confidence: 0,
      method: "test",
    },
  });
  const unresolved: DraftRowState = { item: needsChoice, grams: 100, included: true };
  assert.equal(rowNeedsChoice(unresolved), true);
  assert.equal(canSave([unresolved]), false);
  assert.equal(canSave([{ ...unresolved, selectedCandidateKey: "preview-key" }]), true);
  assert.equal(canSave([{ ...unresolved, selectedCandidateKey: "other-key" }]), true);
  assert.equal(
    canSave([
      {
        ...unresolved,
        replacement: { foodProductId: "replacement-id", name: "Replacement", basis },
      },
    ]),
    true,
  );
  assert.equal(
    canSave([
      {
        ...unresolved,
        item: item("ambiguous-no-basis", { needsChoice: true }),
        logWithoutCalories: true,
      },
    ]),
    true,
  );
  assert.equal(
    canSave([
      { ...unresolved, included: false },
      { item: item("other-included", { per100g: basis }), grams: 100, included: true },
    ]),
    true,
  );
});


test("commit request carries choices without nutrition and omits excluded rows", () => {
  const rows: DraftRowState[] = [
    {
      item: item("selected", {
        needsChoice: true,
        per100g: basis,
        grounding: { query: "x", resolution_status: "ambiguous", auto_selected: false, candidates: [candidate()], match_confidence: 0, method: "test" },
      }),
      grams: 123.46,
      included: true,
      selectedCandidateKey: "candidate-key",
    },
    {
      item: item("replacement"),
      grams: 80,
      included: true,
      replacement: { foodProductId: "food-id", name: "Replacement", basis },
    },
    {
      item: item("no-calories"),
      grams: 55.55,
      included: true,
      logWithoutCalories: true,
    },
    { item: item("removed"), grams: 50, included: false },
    { item: item("untoggled", { isInferred: true }), grams: 50, included: false },
  ];
  const request = buildCommitRequest(rows, "Dinner", "2026-09-24T18:30:00Z");
  assert.deepEqual(request, {
    mealType: "Dinner",
    loggedAt: "2026-09-24T18:30:00Z",
    items: [
      { itemId: "selected", grams: 123.5, selectedCandidateKey: "candidate-key" },
      { itemId: "replacement", grams: 80, replacementFoodProductId: "food-id" },
      { itemId: "no-calories", grams: 55.6, logWithoutCalories: true },
    ],
  });
  for (const row of request.items ?? []) {
    assert.equal("calories" in row, false);
    assert.equal("per100g" in row, false);
    assert.equal("proteinG" in row, false);
  }
});

test("basis precedence is replacement, selected candidate, then item basis", () => {
  const original = item("x", {
    per100g: basis,
    grounding: {
      query: "x",
      resolution_status: "ambiguous",
      auto_selected: false,
      candidates: [candidate()],
      match_confidence: 0,
      method: "test",
    },
  });
  const row: DraftRowState = {
    item: original,
    grams: 100,
    included: true,
    selectedCandidateKey: "candidate-key",
  };
  assert.deepEqual(effectiveBasis(row), {
    caloriesKcal: 150,
    proteinG: 8,
    carbsG: 12,
    fatG: 4,
    fiberG: undefined,
    sugarG: undefined,
    sodiumMg: undefined,
  });
  row.replacement = { foodProductId: "replacement", name: "replacement", basis: { ...basis, caloriesKcal: 300 } };
  assert.equal(effectiveBasis(row)?.caloriesKcal, 300);
  delete row.replacement;
  delete row.selectedCandidateKey;
  assert.equal(effectiveBasis(row), basis);
});

test("candidateBasis requires candidate calories", () => {
  assert.equal(candidateBasis(candidate({ calories_100g: null })), null);
  assert.deepEqual(candidateBasis(candidate()), {
    caloriesKcal: 150,
    proteinG: 8,
    carbsG: 12,
    fatG: 4,
    fiberG: undefined,
    sugarG: undefined,
    sodiumMg: undefined,
  });
});
