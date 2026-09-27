import type { MealSuggestion } from "../types";

export type SuggestionMealType = "Breakfast" | "Lunch" | "Dinner" | "Snack";

export interface MealSuggestionCardViewModel {
  title: string;
  rationale: string;
  draftId: string;
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  items: { name: string; grams: number }[];
}

export function defaultSuggestionMealType(hour: number): SuggestionMealType {
  if (hour < 11) return "Breakfast";
  if (hour < 15) return "Lunch";
  if (hour < 21) return "Dinner";
  return "Snack";
}

export function siblingSuggestionDraftIds(
  suggestions: MealSuggestion[],
  committedDraftId: string,
): string[] {
  return suggestions
    .map(({ draft }) => draft.draftId)
    .filter((draftId) => draftId !== committedDraftId);
}

export function mealSuggestionCardViewModel(
  suggestion: MealSuggestion,
): MealSuggestionCardViewModel {
  const { draft } = suggestion;
  return {
    title: suggestion.title,
    rationale: suggestion.rationale,
    draftId: draft.draftId,
    calories: draft.totals.calories,
    proteinG: draft.totals.proteinG,
    carbsG: draft.totals.carbsG,
    fatG: draft.totals.fatG,
    items: draft.items.map(({ name, grams }) => ({ name, grams })),
  };
}
