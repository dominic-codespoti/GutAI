import * as haptics from "../utils/haptics";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { mealApi } from "../api";
import { toast } from "../stores/toast";
import { mealSheet } from "../stores/mealSheet";
import { useCelebrationStore } from "../stores/celebration";
import { mapItemToRequest } from "../utils/mealMappers";
import { titleCaseFoodName } from "../utils/foodDisplay";
import { formatDateLabel, buildLoggedAt } from "../utils/date";
import {
  loadReminderPrefs,
  syncStreakNudge,
} from "../utils/notifications";
import { maybeRequestReview } from "../utils/review";
import {
  maybeWriteMealToPlatform,
  maybeDeleteMealFromPlatform,
  mealWriteFromResponse,
} from "../services/health";
import type {
  MealLog,
  CreateMealRequest,
  CreateMealItemRequest,
} from "../types";


function invalidate(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ["meals"] });
  qc.invalidateQueries({ queryKey: ["daily-summary"] });
  qc.invalidateQueries({ queryKey: ["recent-foods"] });
  qc.invalidateQueries({ queryKey: ["streak"] });
  qc.invalidateQueries({ queryKey: ["custom-foods"] });
  qc.invalidateQueries({ queryKey: ["trigger-foods-dashboard"] });
  qc.invalidateQueries({ queryKey: ["trigger-foods"] });
  qc.invalidateQueries({ queryKey: ["food-diary-analysis"] });
  qc.invalidateQueries({ queryKey: ["additive-exposure"] });
  qc.invalidateQueries({ queryKey: ["nutrition-trends"] });
}

export function useMealMutations() {
  const queryClient = useQueryClient();
  const celebrate = useCelebrationStore((s) => s.celebrate);

  const createMeal = useMutation({
    mutationFn: (data: CreateMealRequest) => mealApi.create(data),
    onSuccess: (res, variables) => {
      invalidate(queryClient);
      mealSheet.close();
      toast.success("Meal logged!");
      haptics.success();
      void loadReminderPrefs().then((p) => syncStreakNudge(p, true));
      const first = variables.items?.[0]?.foodName;
      celebrate({
        title: "Meal logged!",
        subtitle: first
          ? `${titleCaseFoodName(first)}${(variables.items?.length ?? 0) > 1 ? ` +${(variables.items?.length ?? 1) - 1} more` : ""}`
          : undefined,
      });
      if (res?.data) {
        maybeWriteMealToPlatform(mealWriteFromResponse(res.data));
      }
    },
    onError: () => {
      toast.error("Failed to log meal");
      haptics.error();
    },
  });

  const updateMeal = useMutation({
    mutationFn: ({ id, data }: { id: string; data: CreateMealRequest }) =>
      mealApi.update(id, data),
    onSuccess: (res) => {
      invalidate(queryClient);
      mealSheet.close();
      toast.success("Meal updated");
      haptics.success();
      if (res?.data) {
        maybeWriteMealToPlatform(mealWriteFromResponse(res.data));
      }
    },
    onError: () => {
      toast.error("Failed to update meal");
      haptics.error();
    },
  });

  const deleteMeal = useMutation({
    mutationFn: (id: string) => mealApi.delete(id),
    onSuccess: (_, id) => {
      invalidate(queryClient);
      toast.success("Meal deleted");
      haptics.heavy();
      maybeDeleteMealFromPlatform(id);
    },
    onError: () => {
      toast.error("Failed to delete meal");
      haptics.error();
    },
  });


  const copyMeal = useMutation({
    mutationFn: ({ meal, targetDate }: { meal: MealLog; targetDate: string }) =>
      mealApi.create({
        mealType: meal.mealType,
        loggedAt: buildLoggedAt(targetDate),
        notes: meal.notes ?? undefined,
        items: meal.items.map(mapItemToRequest),
      }),
    onSuccess: (res, { targetDate }) => {
      invalidate(queryClient);
      mealSheet.close();
      toast.success(`Meal copied to ${formatDateLabel(targetDate)}`);
      haptics.success();
      if (res?.data) {
        maybeWriteMealToPlatform(mealWriteFromResponse(res.data));
      }
    },
    onError: () => {
      toast.error("Failed to copy meal");
      haptics.error();
    },
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
