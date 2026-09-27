import assert from "node:assert/strict";
import test from "node:test";
import type { MealSuggestion } from "../../types";
import {
  defaultSuggestionMealType,
  mealSuggestionCardViewModel,
  siblingSuggestionDraftIds,
} from "../mealSuggestions";

function suggestion(draftId: string, calories = 400): MealSuggestion {
  return {
    title: `Meal ${draftId}`,
    rationale: "Fits the remaining budget.",
    draft: {
      draftId,
      origin: "suggestion",
      status: "PendingReview",
      items: [
        {
          itemId: `${draftId}-1`,
          name: "Roasted chickpeas",
          source: "usda",
          grams: 125,
          matchConfidence: 0.95,
        },
      ],
      warnings: [],
      referenceObjectVisible: false,
      overallConfidence: 0.9,
      totals: {
        calories,
        proteinG: 17.3,
        carbsG: 62.1,
        fatG: 8.4,
        itemsWithoutNutrition: 0,
      },
      createdAt: "2026-09-25T12:00:00Z",
      expiresAt: "2026-09-25T13:00:00Z",
    },
  };
}

test("default meal type changes at local meal-time boundaries", () => {
  assert.equal(defaultSuggestionMealType(0), "Breakfast");
  assert.equal(defaultSuggestionMealType(10), "Breakfast");
  assert.equal(defaultSuggestionMealType(11), "Lunch");
  assert.equal(defaultSuggestionMealType(14), "Lunch");
  assert.equal(defaultSuggestionMealType(15), "Dinner");
  assert.equal(defaultSuggestionMealType(20), "Dinner");
  assert.equal(defaultSuggestionMealType(21), "Snack");
  assert.equal(defaultSuggestionMealType(23), "Snack");
});

test("sibling selection excludes only the committed suggestion draft", () => {
  assert.deepEqual(
    siblingSuggestionDraftIds(
      [suggestion("draft-a"), suggestion("draft-b"), suggestion("draft-c")],
      "draft-b",
    ),
    ["draft-a", "draft-c"],
  );
});

test("card view model uses server totals and item grams without recomputation", () => {
  const view = mealSuggestionCardViewModel(suggestion("server-values", 437));
  assert.deepEqual(view, {
    title: "Meal server-values",
    rationale: "Fits the remaining budget.",
    draftId: "server-values",
    calories: 437,
    proteinG: 17.3,
    carbsG: 62.1,
    fatG: 8.4,
    items: [{ name: "Roasted chickpeas", grams: 125 }],
  });
});
