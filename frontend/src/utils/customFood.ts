import type { CustomFood, CreateMealItemRequest } from "../types";

export type AiGeneratedFood = CustomFood;

export const ROUND = (v: number) => Math.round(v * 10) / 10;

export function normalizeCustomFood(data: AiGeneratedFood): CustomFood {
  return {
    id: data.id,
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
    extractionConfidence: data.extractionConfidence,
    nutritionProvenance: data.nutritionProvenance ?? "ModelEstimated",
    describedComponents: data.describedComponents ?? null,
  };
}

export function customFoodToMealItem(
  food: CustomFood & { id?: string },
  aiExtractionConfidence?: number | null,
  isAiDescribed = aiExtractionConfidence != null,
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
    // Carries the AI extraction confidence through so a meal logged straight from an
    // uncertain photo/description generation doesn't look identical to a manually
    // entered, fully-deterministic item to the symptom-association engine. Undefined
    // (not 0) for manual entry — a real "no confidence signal" case, not "zero confidence".
    matchConfidence: aiExtractionConfidence ?? undefined,
    nutritionProvenance:
      food.nutritionProvenance ?? (isAiDescribed ? "ModelEstimated" : "UserEntered"),
  };
}
