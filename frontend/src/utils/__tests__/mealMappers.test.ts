import assert from "node:assert/strict";
import test from "node:test";
import { mapParsedItemToRequest } from "../mealMappers";
import { customFoodToMealItem } from "../customFood";

test("natural-language quantities are applied once", () => {
  const request = mapParsedItemToRequest(
    {
      name: "Egg",
      servingWeightG: 100,
      servingSize: "2",
      servingQuantity: 2,
      calories: 155,
      proteinG: 26,
      carbsG: 2.2,
      fatG: 22,
      fiberG: 0,
      sugarG: 0,
      sodiumMg: 124,
      cholesterolMg: 372,
      saturatedFatG: 7.2,
      potassiumMg: 252,
      matchConfidence: 1,
      portionConfidence: 1,
      nutritionProvenance: "Sourced",
      resolutionStatus: "Exact",
    },
    { servingG: 50, multiplier: 2 },
  );

  assert.equal(request.servingWeightG, 100);
  assert.equal(request.calories, 155);
  assert.equal(request.sodiumMg, 124);
});

test("matchConfidence and nutritionProvenance survive the commit mapping", () => {
  // Regression: these fields were computed by the parser, shown to the user in the
  // review sheet, then silently dropped when building the save request — meaning
  // every meal logged through the app UI (not chat) lost its identity/nutrition
  // provenance signal before it ever reached the backend.
  const request = mapParsedItemToRequest({
    name: "Billion Bay Oatmeal",
    servingWeightG: 100,
    servingSize: "1 bowl",
    servingQuantity: 1,
    calories: 1580,
    proteinG: 20,
    carbsG: 66.7,
    fatG: 6.67,
    fiberG: 0,
    sugarG: 0,
    sodiumMg: 0,
    cholesterolMg: 0,
    saturatedFatG: 0,
    potassiumMg: 0,
    matchConfidence: 0.53,
    portionConfidence: 0.75,
    nutritionProvenance: "Sourced",
    resolutionStatus: "Probable",
  });

  assert.equal(request.matchConfidence, 0.53);
  assert.equal(request.nutritionProvenance, "Sourced");
});

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
  assert.equal(aiSourced.nutritionProvenance, "Estimated");

  const manual = customFoodToMealItem(base);
  assert.equal(manual.matchConfidence, undefined);
  assert.equal(manual.nutritionProvenance, undefined);
});
