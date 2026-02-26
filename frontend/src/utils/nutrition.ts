import type { FoodProduct } from "../types";

export interface ScaledNutrition {
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  fiberG: number;
  sugarG: number;
  sodiumMg: number;
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
    | "sodium100g"
  >,
  grams: number,
): ScaledNutrition {
  const s = grams / 100;
  return {
    calories: Math.round((product.calories100g ?? 0) * s),
    proteinG: Math.round((product.protein100g ?? 0) * s),
    carbsG: Math.round((product.carbs100g ?? 0) * s),
    fatG: Math.round((product.fat100g ?? 0) * s),
    fiberG: Math.round((product.fiber100g ?? 0) * s),
    sugarG: Math.round((product.sugar100g ?? 0) * s),
    sodiumMg: Math.round((product.sodium100g ?? 0) * s),
  };
}

export function nutritionSummaryText(n: ScaledNutrition, grams: number) {
  return `${grams}g total · ${n.calories} cal · ${n.proteinG}g P · ${n.carbsG}g C · ${n.fatG}g F`;
}

export function buildServingPresets(
  product?: Pick<FoodProduct, "servingQuantity" | "servingSize"> | null,
): { label: string; grams: number }[] {
  const presets: { label: string; grams: number }[] = [];
  if (product?.servingQuantity && product.servingSize) {
    presets.push({
      label: `1 serving (${product.servingSize})`,
      grams: Math.round(product.servingQuantity),
    });
  }
  [50, 100, 150, 200, 250].forEach((g) =>
    presets.push({ label: `${g}g`, grams: g }),
  );
  return presets;
}
