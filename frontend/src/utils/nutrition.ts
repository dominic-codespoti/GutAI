import type { FoodProduct, NutritionAmounts, NutritionPer100g } from "../types";

export interface ScaledNutrition {
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  fiberG: number;
  sugarG: number;
  sodiumMg: number;
}

function roundAwayFromZero(value: number, decimalPlaces: number): number {
  const factor = 10 ** decimalPlaces;
  const magnitude = Math.abs(value) * factor;
  const epsilon = Number.EPSILON * Math.max(1, magnitude);
  return Math.sign(value) * Math.round(magnitude + epsilon) / factor;
}

export function computeNutrition(basis: NutritionPer100g, grams: number): NutritionAmounts {
  if (grams < 0) {
    throw new RangeError("Grams cannot be negative.");
  }
  const factor = grams / 100;
  return {
    calories: roundAwayFromZero(basis.caloriesKcal * factor, 0),
    proteinG: roundAwayFromZero(basis.proteinG * factor, 1),
    carbsG: roundAwayFromZero(basis.carbsG * factor, 1),
    fatG: roundAwayFromZero(basis.fatG * factor, 1),
    fiberG: basis.fiberG == null ? null : roundAwayFromZero(basis.fiberG * factor, 1),
    sugarG: basis.sugarG == null ? null : roundAwayFromZero(basis.sugarG * factor, 1),
    sodiumMg: basis.sodiumMg == null ? null : roundAwayFromZero(basis.sodiumMg * factor, 0),
  };
}

export function sumNutrition(parts: NutritionAmounts[]): NutritionAmounts {
  let calories = 0;
  let proteinG = 0;
  let carbsG = 0;
  let fatG = 0;
  let fiberG: number | null = null;
  let sugarG: number | null = null;
  let sodiumMg: number | null = null;

  for (const part of parts) {
    calories += part.calories;
    proteinG += part.proteinG;
    carbsG += part.carbsG;
    fatG += part.fatG;
    if (part.fiberG != null) fiberG = (fiberG ?? 0) + part.fiberG;
    if (part.sugarG != null) sugarG = (sugarG ?? 0) + part.sugarG;
    if (part.sodiumMg != null) sodiumMg = (sodiumMg ?? 0) + part.sodiumMg;
  }

  return { calories, proteinG, carbsG, fatG, fiberG, sugarG, sodiumMg };
}

export function scaleNutrition(
  product: Pick<
    FoodProduct,
    | "calories100g"
    | "protein100g"
    | "carbs100g"
    | "fat100g"
    | "fiber100g"
    | "sugar100g"
    | "sodiumMg100g"
  >,
  grams: number,
): ScaledNutrition {
  const amounts = computeNutrition(
    {
      caloriesKcal: product.calories100g ?? 0,
      proteinG: product.protein100g ?? 0,
      carbsG: product.carbs100g ?? 0,
      fatG: product.fat100g ?? 0,
      fiberG: product.fiber100g ?? 0,
      sugarG: product.sugar100g ?? 0,
      sodiumMg: product.sodiumMg100g ?? 0,
    },
    grams,
  );

  return {
    calories: amounts.calories,
    proteinG: amounts.proteinG,
    carbsG: amounts.carbsG,
    fatG: amounts.fatG,
    fiberG: amounts.fiberG ?? 0,
    sugarG: amounts.sugarG ?? 0,
    sodiumMg: amounts.sodiumMg ?? 0,
  };
}

export function nutritionSummaryText(n: ScaledNutrition, grams: number) {
  return `${grams}g total · ${n.calories} cal · ${n.proteinG}g P · ${n.carbsG}g C · ${n.fatG}g F`;
}

export interface ParsedServing {
  unit: string;
  grams: number;
}

export function buildServingPresets(
  product?: Pick<FoodProduct, "servingQuantity" | "servingSize"> | null,
  parsedServing?: ParsedServing | null,
): { label: string; grams: number }[] {
  const presets: { label: string; grams: number }[] = [];

  // If NLP parsed a recognizable serving unit, show it as the first chip
  if (parsedServing?.unit && parsedServing.grams > 0) {
    const g = Math.round(parsedServing.grams);
    presets.push({ label: `1 ${parsedServing.unit} (${g}g)`, grams: g });
  }

  if (product?.servingQuantity && product.servingSize) {
    const g = Math.round(product.servingQuantity);
    // Skip if the parsed serving already covers this gram weight
    if (!presets.some((p) => p.grams === g)) {
      presets.push({
        label: `1 serving (${product.servingSize})`,
        grams: g,
      });
    }
  }
  [50, 100, 150, 200, 250].forEach((g) =>
    presets.push({ label: `${g}g`, grams: g }),
  );
  return presets;
}
