import { create } from "zustand";
import type { MealLog } from "../types";
import { getMealTypeForTime, MEAL_TYPES } from "../utils/constants";
import { toLocalDateStr } from "../utils/date";

export type LogMealTab = "describe" | "search" | "scan" | "manual";

export type MealSheetMode =
  | "idle"
  | "log-meal"
  | "edit-meal"
  | "copy-meal"
  | "swap-search";

export interface LogOptions {
  initialTab?: LogMealTab;
  date?: string;
  mealType?: string;
}

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
  logOptions: LogOptions | null;

  openLog: (options?: LogOptions) => void;
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

const todayStr = () => toLocalDateStr();

export const useMealSheetStore = create<MealSheetState>((set) => ({
  mode: "idle",
  editingMeal: null,
  copyingMeal: null,
  swapContext: null,
  selectedMealType: getMealTypeForTime(new Date().getHours()),
  selectedDate: todayStr(),
  logOptions: null,

  openLog: (options) =>
    set({
      mode: "log-meal",
      editingMeal: null,
      copyingMeal: null,
      swapContext: null,
      logOptions: options ?? null,
      ...(options?.date ? { selectedDate: options.date } : {}),
      ...(options?.mealType ? { selectedMealType: options.mealType as MealType } : {}),
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
      logOptions: null,
    }),

  reset: () =>
    set({
      mode: "idle",
      editingMeal: null,
      copyingMeal: null,
      swapContext: null,
      logOptions: null,
      selectedMealType: getMealTypeForTime(new Date().getHours()),
      selectedDate: todayStr(),
    }),
}));

/** Imperative access for use in callbacks / mutations */
export const mealSheet = {
  openLog: (options?: LogOptions) => useMealSheetStore.getState().openLog(options),
  openEdit: (meal: MealLog) => useMealSheetStore.getState().openEdit(meal),
  openCopy: (meal: MealLog) => useMealSheetStore.getState().openCopy(meal),
  openSwap: (meal: MealLog, itemIndex: number, origin: "edit" | "parsed") =>
    useMealSheetStore.getState().openSwap(meal, itemIndex, origin),
  close: () => useMealSheetStore.getState().close(),
  setMealType: (type: MealType) =>
    useMealSheetStore.getState().setMealType(type),
  setDate: (date: string) => useMealSheetStore.getState().setDate(date),
};
