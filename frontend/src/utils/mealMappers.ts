import type { MealItem, CreateMealItemRequest, ParsedFoodItem } from "../types";

/** Map a MealItem entity to a CreateMealItemRequest (used by copy, edit, delete-item, swap). */
export function mapItemToRequest(it: MealItem): CreateMealItemRequest {
  return {
    foodName: it.foodName,
    foodProductId: it.foodProductId ?? undefined,
    servings: it.servings,
    servingUnit: it.servingUnit,
    servingWeightG: it.servingWeightG,
    calories: it.calories,
    proteinG: it.proteinG,
    carbsG: it.carbsG,
    fatG: it.fatG,
    fiberG: it.fiberG,
    sugarG: it.sugarG,
    sodiumMg: it.sodiumMg,
    cholesterolMg: it.cholesterolMg ?? 0,
    saturatedFatG: it.saturatedFatG ?? 0,
    potassiumMg: it.potassiumMg ?? 0,
  };
}

/** Scale nutrition values by a serving ratio. */
export function scaleNutrition(
  base: {
    calories: number;
    proteinG: number;
    carbsG: number;
    fatG: number;
    fiberG: number;
    sugarG: number;
    sodiumMg: number;
    cholesterolMg?: number;
    saturatedFatG?: number;
    potassiumMg?: number;
  },
  scale: number,
): Pick<
  CreateMealItemRequest,
  | "calories"
  | "proteinG"
  | "carbsG"
  | "fatG"
  | "fiberG"
  | "sugarG"
  | "sodiumMg"
  | "cholesterolMg"
  | "saturatedFatG"
  | "potassiumMg"
> {
  return {
    calories: Math.round(base.calories * scale),
    proteinG: Math.round(base.proteinG * scale * 10) / 10,
    carbsG: Math.round(base.carbsG * scale * 10) / 10,
    fatG: Math.round(base.fatG * scale * 10) / 10,
    fiberG: Math.round(base.fiberG * scale * 10) / 10,
    sugarG: Math.round(base.sugarG * scale * 10) / 10,
    sodiumMg: Math.round(base.sodiumMg * scale),
    cholesterolMg: Math.round((base.cholesterolMg ?? 0) * scale),
    saturatedFatG: Math.round((base.saturatedFatG ?? 0) * scale * 10) / 10,
    potassiumMg: Math.round((base.potassiumMg ?? 0) * scale),
  };
}

/** Build a CreateMealItemRequest from a parsed item + serving config. */
export function mapParsedItemToRequest(
  it: ParsedFoodItem,
  cfg?: { servingG: number; multiplier: number },
): CreateMealItemRequest {
  const totalG = cfg
    ? cfg.servingG * cfg.multiplier
    : (it.servingWeightG ?? 100);
  const scale = totalG / (it.servingWeightG ?? 100);

  return {
    foodName: it.name,
    servings: cfg?.multiplier ?? it.servingQuantity ?? 1,
    servingUnit: cfg ? `${totalG}g` : (it.servingSize ?? "serving"),
    servingWeightG: totalG,
    foodProductId: it.foodProductId ?? undefined,
    ...scaleNutrition(it, scale),
  };
}

/** Build a scaled CreateMealItemRequest from an edit-mode item + config. */
export function mapEditItemToRequest(
  it: MealItem,
  cfg?: { servingG: number; multiplier: number },
): CreateMealItemRequest {
  if (!cfg) return mapItemToRequest(it);
  const totalG = cfg.servingG * cfg.multiplier;
  const scale = totalG / (it.servingWeightG ?? 100);

  return {
    foodName: it.foodName,
    foodProductId: it.foodProductId ?? undefined,
    servings: 1,
    servingUnit: `${totalG}g`,
    servingWeightG: totalG,
    ...scaleNutrition(it, scale),
  };
}
