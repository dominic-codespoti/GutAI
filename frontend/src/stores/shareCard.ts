import { create } from "zustand";

export type TriggerFoodCardData = {
  items: { food: string; count: number }[];
  periodLabel: string;
};

export type SafetyReportCardData = {
  name: string;
  brand?: string | null;
  score?: number | null;
  scoreLabel?: string;
  gutRating?: string | null;
  fodmapStatus?: string | null;
  giText?: string | null;
  flaggedAdditives: string[];
};

export type StreakCardData = {
  days: number;
};

export type WeeklySummaryCardData = {
  rangeLabel: string;
  mealsLogged: number;
  avgCalories?: number | null;
  symptomsLogged?: number | null;
  topTrigger?: string | null;
};

export type ShareCardRequest =
  | { template: "triggerFoods"; data: TriggerFoodCardData }
  | { template: "safetyReport"; data: SafetyReportCardData }
  | { template: "streak"; data: StreakCardData }
  | { template: "weeklySummary"; data: WeeklySummaryCardData };

interface ShareCardState {
  request: ShareCardRequest | null;
  showShareCard: (request: ShareCardRequest) => void;
  clearShareCard: () => void;
}

/**
 * Drives the off-screen ShareCardPortal: show → portal renders the
 * template off-screen → captures PNG → opens the share sheet → clears.
 */
export const useShareCardStore = create<ShareCardState>((set) => ({
  request: null,
  showShareCard: (request) => set({ request }),
  clearShareCard: () => set({ request: null }),
}));
