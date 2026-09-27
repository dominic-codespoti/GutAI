import assert from "node:assert/strict";
import test from "node:test";
import { computeNutrition, scaleNutrition, sumNutrition } from "../nutrition";
import type { NutritionAmounts, NutritionPer100g } from "../../types";

const fixtures: Array<{ basis: NutritionPer100g; grams: number; expected: NutritionAmounts }> = [
  {
    basis: {
      caloriesKcal: 155.5,
      proteinG: 20.25,
      carbsG: 30.15,
      fatG: 4.05,
      fiberG: 2.25,
      sugarG: 5.05,
      sodiumMg: 123.5,
    },
    grams: 100,
    expected: {
      calories: 156,
      proteinG: 20.3,
      carbsG: 30.2,
      fatG: 4.1,
      fiberG: 2.3,
      sugarG: 5.1,
      sodiumMg: 124,
    },
  },
  {
    basis: {
      caloriesKcal: 0.5,
      proteinG: 2.25,
      carbsG: 1.05,
      fatG: 0.05,
      fiberG: 0.15,
      sugarG: 0.05,
      sodiumMg: 0.5,
    },
    grams: 100,
    expected: {
      calories: 1,
      proteinG: 2.3,
      carbsG: 1.1,
      fatG: 0.1,
      fiberG: 0.2,
      sugarG: 0.1,
      sodiumMg: 1,
    },
  },
  {
    basis: {
      caloriesKcal: 203.4,
      proteinG: 7.36,
      carbsG: 18.24,
      fatG: 9.98,
      fiberG: null,
      sugarG: 3.18,
      sodiumMg: null,
    },
    grams: 37.5,
    expected: {
      calories: 76,
      proteinG: 2.8,
      carbsG: 6.8,
      fatG: 3.7,
      fiberG: null,
      sugarG: 1.2,
      sodiumMg: null,
    },
  },
];

test("nutrition calculator matches the backend fixture table", () => {
  for (const { basis, grams, expected } of fixtures) {
    assert.deepEqual(computeNutrition(basis, grams), expected);
  }
});

test("existing product scaling matches the shared calculator on a fixture row", () => {
  const { basis, grams } = fixtures[0];

  assert.deepEqual(scaleNutrition({
    calories100g: basis.caloriesKcal,
    protein100g: basis.proteinG,
    carbs100g: basis.carbsG,
    fat100g: basis.fatG,
    fiber100g: basis.fiberG ?? null,
    sugar100g: basis.sugarG ?? null,
    sodiumMg100g: basis.sodiumMg ?? null,
  }, grams), computeNutrition(basis, grams));
});

test("optional nutrients remain null and negative grams are rejected", () => {
  const result = computeNutrition({ caloriesKcal: 80, proteinG: 0, carbsG: 0, fatG: 0 }, 50);
  assert.equal(result.fiberG, null);
  assert.equal(result.sugarG, null);
  assert.equal(result.sodiumMg, null);
  assert.throws(() => computeNutrition({ caloriesKcal: 80, proteinG: 0, carbsG: 0, fatG: 0 }, -1), RangeError);
});

test("summing nutrition keeps optional values null only when every part is null", () => {
  assert.deepEqual(sumNutrition([
    { calories: 2, proteinG: 0, carbsG: 0, fatG: 0, fiberG: null, sugarG: null, sodiumMg: null },
    { calories: 3, proteinG: 0, carbsG: 0, fatG: 0, fiberG: null, sugarG: null, sodiumMg: null },
  ]), {
    calories: 5,
    proteinG: 0,
    carbsG: 0,
    fatG: 0,
    fiberG: null,
    sugarG: null,
    sodiumMg: null,
  });

  assert.deepEqual(sumNutrition([
    { calories: 1, proteinG: 0, carbsG: 0, fatG: 0, fiberG: 1, sugarG: null, sodiumMg: 0 },
    { calories: 2, proteinG: 0, carbsG: 0, fatG: 0, fiberG: null, sugarG: 0.5, sodiumMg: null },
  ]), {
    calories: 3,
    proteinG: 0,
    carbsG: 0,
    fatG: 0,
    fiberG: 1,
    sugarG: 0.5,
    sodiumMg: 0,
  });
});
