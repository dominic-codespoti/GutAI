import type { CustomFood, CreateMealItemRequest } from "../types";

export type AiGeneratedFood = CustomFood & { extractionConfidence?: number | null };

export const ROUND = (v: number) => Math.round(v * 10) / 10;

export function normalizeCustomFood(data: AiGeneratedFood): CustomFood {
  return {
    name: data.name ?? "",
    brandName: data.brandName ?? "",
    servingSize: Math.max(1, ROUND(data.servingSize ?? 100)),
    servingSizeUnit: data.servingSizeUnit || "g",
    calories: ROUND(data.calories ?? 0),
    proteinG: ROUND(data.proteinG ?? 0),
    carbG: ROUND(data.carbG ?? 0),
    fatG: ROUND(data.fatG ?? 0),
    fiberG: data.fiberG != null ? ROUND(data.fiberG) : null,
    sugarG: data.sugarG != null ? ROUND(data.sugarG) : null,
    sodiumMg: data.sodiumMg != null ? ROUND(data.sodiumMg) : null,
    ingredients: data.ingredients ?? "",
  };
}

export function customFoodToMealItem(
  food: CustomFood & { id?: string },
): CreateMealItemRequest {
  return {
    foodName: food.name,
    foodProductId: food.id,
    servings: 1,
    servingUnit: `${food.servingSize}${food.servingSizeUnit}`,
    servingWeightG: food.servingSize,
    calories: food.calories,
    proteinG: food.proteinG,
    carbsG: food.carbG,
    fatG: food.fatG,
    fiberG: food.fiberG ?? 0,
    sugarG: food.sugarG ?? 0,
    sodiumMg: food.sodiumMg ?? 0,
  };
}
