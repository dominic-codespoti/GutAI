import type { MealItem, CreateMealItemRequest, ParsedFoodItem, MealDraftCommitItem } from "../types";

export type NlpServingConfig = { servingG: number; multiplier: number };

/** Build server-authoritative draft commit items from the NLP review state. */
export function buildNlpCommitItems(
  parsedItems: ParsedFoodItem[],
  configs: Record<number, NlpServingConfig>,
  replacements: Record<number, string>,
  candidateChoices: Record<number, string> = {},
): MealDraftCommitItem[] {
  return parsedItems.flatMap((item, idx) => {
    if (!item.draftItemId) return [];
    const config = configs[idx];
    const grams = config
      ? config.servingG * config.multiplier
      : item.servingWeightG ?? 100;
    return [{
      itemId: item.draftItemId,
      grams: Math.round(grams * 10) / 10,
      ...(replacements[idx] ? { replacementFoodProductId: replacements[idx] } : {}),
      ...(item.needsChoice && !replacements[idx] && candidateChoices[idx]
        ? { selectedCandidateKey: candidateChoices[idx] }
        : {}),
    }];
  });
}

/** Return included ambiguous rows that still need an explicit match choice. */
export function unresolvedNlpChoices(
  parsedItems: ParsedFoodItem[],
  candidateChoices: Record<number, string>,
  replacements: Record<number, string>,
  included: Record<number, boolean> = {},
): number[] {
  return parsedItems.flatMap((item, idx) =>
    included[idx] !== false &&
    item.needsChoice &&
    !candidateChoices[idx] &&
    !replacements[idx]
      ? [idx]
      : [],
  );
}
/** A parsed NLP review is commit-ready only when backed by its server draft. */
export function isNlpDraftUsable(
  parsedItems: ParsedFoodItem[],
  draftId: string | null | undefined,
): boolean {
  return !!draftId && parsedItems.every((item) => !!item.draftItemId);
}




/** Map a MealItem entity to a CreateMealItemRequest (used by copy, edit, delete-item, swap). */
export function mapItemToRequest(it: MealItem): CreateMealItemRequest {
  return {
    foodName: it.foodName,
    foodProductId: it.foodProductId ?? undefined,
    servings: it.servings,
    servingUnit: it.servingUnit,
    servingWeightG: it.servingWeightG,
    servingHintUnit: it.servingHintUnit,
    servingHintUnitPlural: it.servingHintUnitPlural,
    servingHintUnitGrams: it.servingHintUnitGrams,
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
    nutritionProvenance: it.nutritionProvenance ??
      (it.foodProductId ? undefined : "UserEntered"),
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
