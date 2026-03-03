import { BottomSheet } from "../BottomSheet";
import { SwapSearchContent } from "./SwapSearchContent";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { toast } from "../../src/stores/toast";
import type { FoodProduct } from "../../src/types";

/**
 * Standalone swap sheet for replacing a single food item in an existing meal
 * (opened via swipe-left → Swap on the meal list).
 */
export function ItemSwapSheet() {
  const mode = useMealSheetStore((s) => s.mode);
  const swapContext = useMealSheetStore((s) => s.swapContext);
  const close = useMealSheetStore((s) => s.close);
  const visible = mode === "swap-search" && !!swapContext;

  const { swapItem } = useMealMutations();

  const handleSelect = (food: FoodProduct) => {
    if (!swapContext) return;
    const { meal, itemIndex } = swapContext;
    const origItem = meal.items[itemIndex];
    const g = origItem?.servingWeightG ?? 100;

    swapItem(meal, itemIndex, {
      foodName: food.name,
      foodProductId:
        food.id !== "00000000-0000-0000-0000-000000000000"
          ? food.id
          : undefined,
      servings: 1,
      servingUnit: `${g}g`,
      servingWeightG: g,
      calories: Math.round(((food.calories100g ?? 0) * g) / 100),
      proteinG: Math.round((((food.protein100g ?? 0) * g) / 100) * 10) / 10,
      carbsG: Math.round((((food.carbs100g ?? 0) * g) / 100) * 10) / 10,
      fatG: Math.round((((food.fat100g ?? 0) * g) / 100) * 10) / 10,
      fiberG: Math.round((((food.fiber100g ?? 0) * g) / 100) * 10) / 10,
      sugarG: Math.round((((food.sugar100g ?? 0) * g) / 100) * 10) / 10,
      sodiumMg: Math.round(((food.sodium100g ?? 0) * g) / 100),
    });
    toast.success(`Swapped to "${food.name}"`);
    close();
  };

  const initialSearch =
    swapContext?.meal.items[swapContext.itemIndex]?.foodName ?? "";

  return (
    <BottomSheet visible={visible} onClose={close} maxHeight="80%">
      <SwapSearchContent
        initialSearch={initialSearch}
        onSelect={handleSelect}
        onBack={close}
      />
    </BottomSheet>
  );
}
