import { create } from "zustand";

export type Celebration = {
  title: string;
  subtitle?: string;
  photoUri?: string;
  kcal?: number | null;
};

interface CelebrationState {
  celebration: Celebration | null;
  celebrate: (c: Celebration) => void;
  clear: () => void;
}

/** Transient meal-logged celebration state — rendered once by CelebrationOverlay. */
export const useCelebrationStore = create<CelebrationState>((set) => ({
  celebration: null,
  celebrate: (celebration) => set({ celebration }),
  clear: () => set({ celebration: null }),
}));
