import type {
  GroundingCandidate,
  MealDraft,
  MealDraftCommitRequest,
  MealDraftItem,
  MealDraftTotals,
  NutritionAmounts,
  NutritionPer100g,
} from "../types";
import { computeNutrition } from "./nutrition";

export type DraftRowState = {
  item: MealDraftItem;
  grams: number;
  included: boolean;
  selectedCandidateKey?: string | null;
  replacement?: {
    foodProductId: string;
    name: string;
    basis: NutritionPer100g;
  } | null;
  logWithoutCalories?: boolean;
};

export function initialRows(draft: MealDraft): DraftRowState[] {
  return draft.items.map((item) => ({
    item,
    grams: item.grams,
    included: item.includedByDefault !== false,
  }));
}

export function candidateBasis(
  candidate: GroundingCandidate,
): NutritionPer100g | null {
  if (candidate.calories_100g == null) return null;
  return {
    caloriesKcal: candidate.calories_100g,
    proteinG: candidate.protein_100g ?? 0,
    carbsG: candidate.carbs_100g ?? 0,
    fatG: candidate.fat_100g ?? 0,
    fiberG: candidate.fiber_100g,
    sugarG: candidate.sugar_100g,
    sodiumMg: candidate.sodium_mg_100g,
  };
}

export function effectiveBasis(row: DraftRowState): NutritionPer100g | null {
  if (row.replacement) return row.replacement.basis;
  if (row.selectedCandidateKey) {
    const candidate = row.item.grounding?.candidates.find(
      (item) => item.candidate_key === row.selectedCandidateKey,
    );
    if (candidate) return candidateBasis(candidate);
  }
  return row.item.per100g ?? null;
}

export function rowNutrition(row: DraftRowState): NutritionAmounts | null {
  const basis = effectiveBasis(row);
  return basis ? computeNutrition(basis, row.grams) : null;
}

export function draftTotals(rows: DraftRowState[]): MealDraftTotals {
  const totals: MealDraftTotals = {
    calories: 0,
    proteinG: 0,
    carbsG: 0,
    fatG: 0,
    itemsWithoutNutrition: 0,
  };
  for (const row of rows) {
    if (!row.included) continue;
    const nutrition = rowNutrition(row);
    if (!nutrition) {
      totals.itemsWithoutNutrition += 1;
      continue;
    }
    totals.calories += nutrition.calories;
    totals.proteinG += nutrition.proteinG;
    totals.carbsG += nutrition.carbsG;
    totals.fatG += nutrition.fatG;
  }
  return totals;
}

export function rowNeedsChoice(row: DraftRowState): boolean {
  return (
    row.included &&
    row.item.needsChoice === true &&
    !row.selectedCandidateKey &&
    !row.replacement &&
    row.logWithoutCalories !== true
  );
}
export function canSave(rows: DraftRowState[]): boolean {
  const includedRows = rows.filter((row) => row.included);
  return (
    includedRows.length > 0 &&
    !rows.some(rowNeedsChoice) &&
    includedRows.every(
      (row) => effectiveBasis(row) !== null || row.logWithoutCalories === true,
    )
  );
}


export function buildCommitRequest(
  rows: DraftRowState[],
  mealType: string,
  loggedAt?: string | null,
): MealDraftCommitRequest {
  return {
    mealType,
    ...(loggedAt ? { loggedAt } : {}),
    items: rows
      .filter((row) => row.included)
      .map((row) => ({
        itemId: row.item.itemId,
        grams: Math.round(row.grams * 10) / 10,
        ...(row.selectedCandidateKey
          ? { selectedCandidateKey: row.selectedCandidateKey }
          : {}),
        ...(row.replacement
          ? { replacementFoodProductId: row.replacement.foodProductId }
          : {}),
        ...(row.logWithoutCalories ? { logWithoutCalories: true } : {}),
      })),
  };
}
