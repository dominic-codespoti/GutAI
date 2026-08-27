import assert from "node:assert/strict";
import test from "node:test";
import {
  formatMealItemPortion,
  formatServingHint,
  titleCaseFoodName,
} from "../foodDisplay";

test("food names use title case without lower-casing acronyms", () => {
  assert.equal(titleCaseFoodName("scrambled eggs"), "Scrambled Eggs");
  assert.equal(titleCaseFoodName("USDA chicken"), "USDA Chicken");
  assert.equal(titleCaseFoodName("grass-fed beef"), "Grass-Fed Beef");
});

test("logged portions prefer the persisted total weight", () => {
  assert.equal(
    formatMealItemPortion({
      servings: 1,
      servingUnit: "g",
      servingWeightG: 150,
    }),
    "150 g",
  );
  assert.equal(
    formatMealItemPortion({ servings: 2, servingUnit: "serving" }),
    "2 serving",
  );
});

test("model serving hints scale with edited grams", () => {
  const eggs = {
    grams: 100,
    servingHintUnit: "large egg",
    servingHintUnitPlural: "large eggs",
    servingHintUnitGrams: 50,
  };
  assert.equal(formatServingHint(eggs), "≈ 2 large eggs");
  assert.equal(formatServingHint({ ...eggs, grams: 50 }), "≈ 1 large egg");
  assert.equal(
    formatServingHint({
      grams: 100,
      servingHintUnit: "large egg",
      servingHintUnitPlural: "large eggs",
      servingHintUnitGrams: 0,
    }),
    null,
  );
});
