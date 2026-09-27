import assert from "node:assert/strict";
import test from "node:test";
import {
  buildNlpCommitItems,
  isNlpDraftUsable,
  mapItemToRequest,
  unresolvedNlpChoices,
} from "../mealMappers";
import { customFoodToMealItem, normalizeCustomFood } from "../customFood";
import type { MealItem, ParsedFoodItem } from "../../types";


test("custom-food logging preserves per-serving nutrition", () => {
  const request = customFoodToMealItem({
    id: "custom-food",
    name: "Recipe",
    servingSize: 250,
    servingSizeUnit: "g",
    calories: 500,
    proteinG: 25,
    carbG: 40,
    fatG: 20,
    fiberG: 5,
    sugarG: 8,
    sodiumMg: 600,
  });

  assert.equal(request.servingWeightG, 250);
  assert.equal(request.calories, 500);
  assert.equal(request.sodiumMg, 600);
});

test("customFoodToMealItem carries AI extraction confidence through as matchConfidence", () => {
  // Regression: an AI-extracted (photo label or description) custom food's confidence was
  // shown to the user in the review UI, then silently dropped when logging it to a meal --
  // meaning the symptom-association engine could never tell an uncertain AI reading apart
  // from a fully deterministic manual entry.
  const base = {
    id: "custom-food",
    name: "Scanned Label Food",
    servingSize: 100,
    servingSizeUnit: "g",
    calories: 200,
    proteinG: 10,
    carbG: 20,
    fatG: 5,
  };

  const aiSourced = customFoodToMealItem(base, 0.42);
  assert.equal(aiSourced.matchConfidence, 0.42);
  assert.equal(aiSourced.nutritionProvenance, "ModelEstimated");

  const manual = customFoodToMealItem(base);
  assert.equal(manual.matchConfidence, undefined);
  assert.equal(manual.nutritionProvenance, "UserEntered");

  assert.equal(
    customFoodToMealItem(base, undefined, true).nutritionProvenance,
    "ModelEstimated",
  );
});
test("custom-food mapping preserves server provenance and only defaults absent provenance", () => {
  const base = {
    name: "Grounded soup",
    servingSize: 100,
    servingSizeUnit: "g",
    calories: 80,
    proteinG: 4,
    carbG: 10,
    fatG: 2,
  };

  assert.equal(
    customFoodToMealItem({ ...base, nutritionProvenance: "Sourced" }).nutritionProvenance,
    "Sourced",
  );
  assert.equal(
    customFoodToMealItem({ ...base, nutritionProvenance: "ModelEstimated" }).nutritionProvenance,
    "ModelEstimated",
  );
  const serverResult = normalizeCustomFood({
    ...base,
    nutritionProvenance: "Sourced",
    extractionConfidence: 0.76,
  });
  assert.equal(serverResult.nutritionProvenance, "Sourced");
  assert.equal(serverResult.extractionConfidence, 0.76);
  assert.equal(normalizeCustomFood(base).nutritionProvenance, "ModelEstimated");
});

test("legacy non-catalog meal items default missing provenance to UserEntered", () => {
  const legacy = {
    id: "legacy-meal-item",
    foodName: "Homemade soup",
    barcode: null,
    servings: 1,
    servingUnit: "bowl",
    servingWeightG: 300,
    calories: 240,
    proteinG: 12,
    carbsG: 30,
    fatG: 8,
    fiberG: 4,
    sugarG: 5,
    sodiumMg: 500,
  } as MealItem;
  assert.equal(mapItemToRequest(legacy).nutritionProvenance, "UserEntered");
});

test("NLP draft commits carry explicit candidate choices only for needs-choice items", () => {
  const items = [
    { draftItemId: "kept", needsChoice: true, servingWeightG: 100 },
    { draftItemId: "alternative", needsChoice: true, servingWeightG: 100 },
    { draftItemId: "unchosen", needsChoice: true, servingWeightG: 100 },
    { draftItemId: "ordinary", needsChoice: false, servingWeightG: 100 },
    { draftItemId: "replaced", needsChoice: true, servingWeightG: 100 },
  ] as ParsedFoodItem[];
  const commits = buildNlpCommitItems(
    items,
    {},
    { 4: "replacement-product" },
    { 0: "preview-key", 1: "alternative-key", 3: "irrelevant-key" },
  );

  assert.deepEqual(commits.map((item) => item.selectedCandidateKey), [
    "preview-key",
    "alternative-key",
    undefined,
    undefined,
    undefined,
  ]);
  assert.deepEqual(commits[4], {
    itemId: "replaced",
    grams: 100,
    replacementFoodProductId: "replacement-product",
  });
  assert.equal("calories" in commits[0], false);
});

test("unresolved NLP choice gating ignores excluded, replaced and chosen rows", () => {
  const items = [
    { needsChoice: true },
    { needsChoice: true },
    { needsChoice: true },
    { needsChoice: true },
    { needsChoice: false },
  ] as ParsedFoodItem[];

  assert.deepEqual(
    unresolvedNlpChoices(
      items,
      { 3: "alternative-key" },
      { 2: "replacement-product" },
      { 1: false },
    ),
    [0],
  );
});
test("NLP draft is usable only when every included item has a draft id", () => {
  const items = [
    { draftItemId: "draft-item" },
    { draftItemId: "second-item" },
  ] as ParsedFoodItem[];
  assert.equal(isNlpDraftUsable(items, "draft-id"), true);
  assert.equal(isNlpDraftUsable(items, null), false);
  assert.equal(
    isNlpDraftUsable([{ draftItemId: "draft-item" }, {}] as ParsedFoodItem[], "draft-id"),
    false,
  );
  assert.equal(isNlpDraftUsable([], "draft-id"), true);
});

