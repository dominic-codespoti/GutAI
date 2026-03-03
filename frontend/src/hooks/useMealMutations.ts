import { Platform } from "react-native";
import * as Haptics from "expo-haptics";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { mealApi } from "../api";
import { toast } from "../stores/toast";
import { mealSheet } from "../stores/mealSheet";
import { mapItemToRequest } from "../utils/mealMappers";
import { formatDateLabel } from "../utils/date";
import { maybeRequestReview } from "../utils/review";
import type {
  MealLog,
  CreateMealRequest,
  CreateMealItemRequest,
} from "../types";

function hapticSuccess() {
  if (Platform.OS !== "web") {
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
  }
}

function invalidate(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ["meals"] });
  qc.invalidateQueries({ queryKey: ["daily-summary"] });
  qc.invalidateQueries({ queryKey: ["recent-foods"] });
}

export function useMealMutations() {
  const queryClient = useQueryClient();

  const createMeal = useMutation({
    mutationFn: (data: CreateMealRequest) => mealApi.create(data),
    onSuccess: () => {
      invalidate(queryClient);
      mealSheet.close();
      toast.success("Meal logged!");
      hapticSuccess();
      maybeRequestReview();
    },
    onError: () => toast.error("Failed to log meal"),
  });

  const updateMeal = useMutation({
    mutationFn: ({ id, data }: { id: string; data: CreateMealRequest }) =>
      mealApi.update(id, data),
    onSuccess: () => {
      invalidate(queryClient);
      mealSheet.close();
      toast.success("Meal updated");
    },
    onError: () => toast.error("Failed to update meal"),
  });

  const deleteMeal = useMutation({
    mutationFn: (id: string) => mealApi.delete(id),
    onSuccess: () => {
      invalidate(queryClient);
      toast.success("Meal deleted");
    },
    onError: () => toast.error("Failed to delete meal"),
  });

  const copyMeal = useMutation({
    mutationFn: ({ meal, targetDate }: { meal: MealLog; targetDate: string }) =>
      mealApi.create({
        mealType: meal.mealType,
        loggedAt: targetDate + "T" + new Date().toISOString().split("T")[1],
        items: meal.items.map(mapItemToRequest),
      }),
    onSuccess: (_, { targetDate }) => {
      invalidate(queryClient);
      mealSheet.close();
      toast.success(`Meal copied to ${formatDateLabel(targetDate)}`);
      hapticSuccess();
    },
    onError: () => toast.error("Failed to copy meal"),
  });

  /** Remove a single item from a meal (or delete the whole meal if last item). */
  const removeItem = (meal: MealLog, itemIndex: number) => {
    const remaining = meal.items.filter((_, i) => i !== itemIndex);
    if (remaining.length === 0) {
      deleteMeal.mutate(meal.id);
      return;
    }
    updateMeal.mutate({
      id: meal.id,
      data: {
        mealType: meal.mealType,
        loggedAt: meal.loggedAt,
        items: remaining.map(mapItemToRequest),
      },
    });
  };

  /** Swap a single food item in-place via the API. */
  const swapItem = (
    meal: MealLog,
    itemIndex: number,
    newItem: CreateMealItemRequest,
  ) => {
    const items = meal.items.map((it, i) =>
      i === itemIndex ? newItem : mapItemToRequest(it),
    );
    updateMeal.mutate({
      id: meal.id,
      data: { mealType: meal.mealType, loggedAt: meal.loggedAt, items },
    });
  };

  return {
    createMeal,
    updateMeal,
    deleteMeal,
    copyMeal,
    removeItem,
    swapItem,
    isPending:
      createMeal.isPending ||
      updateMeal.isPending ||
      deleteMeal.isPending ||
      copyMeal.isPending,
  };
}
