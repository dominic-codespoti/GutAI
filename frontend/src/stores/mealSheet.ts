import { create } from "zustand";
import type { MealLog } from "../types";
import { getMealTypeForTime, MEAL_TYPES } from "../utils/constants";

export type MealSheetMode =
  | "idle"
  | "add-describe"
  | "add-manual"
  | "edit-meal"
  | "copy-meal"
  | "swap-search";

export type MealType = (typeof MEAL_TYPES)[number];

interface SwapContext {
  meal: MealLog;
  itemIndex: number;
  origin: "edit" | "parsed";
}

interface MealSheetState {
  mode: MealSheetMode;
  editingMeal: MealLog | null;
  copyingMeal: MealLog | null;
  swapContext: SwapContext | null;
  selectedMealType: MealType;
  selectedDate: string;

  openAdd: (tab: "add-describe" | "add-manual") => void;
  openEdit: (meal: MealLog) => void;
  openCopy: (meal: MealLog) => void;
  openSwap: (
    meal: MealLog,
    itemIndex: number,
    origin: "edit" | "parsed",
  ) => void;
  setMealType: (type: MealType) => void;
  setDate: (date: string) => void;
  close: () => void;
  reset: () => void;
}

const todayStr = () => new Date().toISOString().split("T")[0];

export const useMealSheetStore = create<MealSheetState>((set) => ({
  mode: "idle",
  editingMeal: null,
  copyingMeal: null,
  swapContext: null,
  selectedMealType: getMealTypeForTime(new Date().getHours()),
  selectedDate: todayStr(),

  openAdd: (tab) =>
    set({
      mode: tab,
      editingMeal: null,
      copyingMeal: null,
      swapContext: null,
    }),

  openEdit: (meal) =>
    set({
      mode: "edit-meal",
      editingMeal: meal,
      copyingMeal: null,
      swapContext: null,
    }),

  openCopy: (meal) =>
    set({
      mode: "copy-meal",
      copyingMeal: meal,
      editingMeal: null,
      swapContext: null,
    }),

  openSwap: (meal, itemIndex, origin) =>
    set((s) => ({
      mode: "swap-search",
      swapContext: { meal, itemIndex, origin },
      // preserve editingMeal so we can return to it
      editingMeal: s.editingMeal,
    })),

  setMealType: (type) => set({ selectedMealType: type }),
  setDate: (date) => set({ selectedDate: date }),

  close: () =>
    set({
      mode: "idle",
      editingMeal: null,
      copyingMeal: null,
      swapContext: null,
    }),

  reset: () =>
    set({
      mode: "idle",
      editingMeal: null,
      copyingMeal: null,
      swapContext: null,
      selectedMealType: getMealTypeForTime(new Date().getHours()),
      selectedDate: todayStr(),
    }),
}));

/** Imperative access for use in callbacks / mutations */
export const mealSheet = {
  openAdd: (tab: "add-describe" | "add-manual") =>
    useMealSheetStore.getState().openAdd(tab),
  openEdit: (meal: MealLog) => useMealSheetStore.getState().openEdit(meal),
  openCopy: (meal: MealLog) => useMealSheetStore.getState().openCopy(meal),
  openSwap: (meal: MealLog, itemIndex: number, origin: "edit" | "parsed") =>
    useMealSheetStore.getState().openSwap(meal, itemIndex, origin),
  close: () => useMealSheetStore.getState().close(),
  setMealType: (type: MealType) =>
    useMealSheetStore.getState().setMealType(type),
  setDate: (date: string) => useMealSheetStore.getState().setDate(date),
};
