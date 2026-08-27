import { Platform } from "react-native";

/* ── Color palettes ── */

export const lightColors = {
  primary: "#16a34a",
  primaryLight: "#22c55e",
  primaryBg: "#f0fdf4",
  primaryBorder: "#bbf7d0",

  secondary: "#0ea5e9",
  secondaryBg: "#f0f9ff",

  accent: "#8b5cf6",
  accentBg: "#f5f3ff",

  warning: "#f59e0b",
  warningBg: "#fffbeb",
  warningBorder: "#fde68a",

  danger: "#ef4444",
  dangerBg: "#fef2f2",
  dangerBorder: "#fecaca",

  bg: "#f8fafc",
  card: "#ffffff",
  cardHover: "#f8fafc",
  tabBar: "#ffffff",

  text: "#0f172a",
  textSecondary: "#475569",
  textMuted: "#94a3b8",
  textLight: "#cbd5e1",

  border: "#e2e8f0",
  borderLight: "#f1f5f9",
  divider: "#f1f5f9",

  textOnPrimary: "#ffffff",
  overlay: "rgba(0,0,0,0.3)",

  protein: "#3b82f6",
  carbs: "#f59e0b",
  fat: "#ef4444",
  fiber: "#8b5cf6",
  sugar: "#f97316",
  sodium: "#06b6d4",

  /* Camera surfaces are theme-independent (always dark) */
  cameraBackdrop: "#000000",
  cameraScrim: "rgba(0,0,0,0.35)",
  cameraOnScrim: "#ffffff",
  cameraOnScrimMuted: "rgba(255,255,255,0.88)",
};


/** Food-source identity chip colors — fixed hues like the macro colors above (not theme-reactive). */
export const sourceChipColors = {
  usda: { bg: "#EDE9FE", border: "#DDD6FE", text: "#6D28D9" },
  off: { bg: "#FFEDD5", border: "#FED7AA", text: "#C2410C" },
  au: { bg: "#DBEAFE", border: "#BFDBFE", text: "#1D4ED8" },
  web: { bg: "#EDE9FE", border: "#DDD6FE", text: "#6D28D9" },
  ai: { bg: "#FCE7F3", border: "#FBCFE8", text: "#BE185D" },
} as const;

/** Toast palettes — keyed by variant, selected by active color scheme (see stores/theme). */
export const toastColors = {
  light: {
    error: { bg: "#fef2f2", border: "#fca5a5", text: "#991b1b" },
    success: { bg: "#f0fdf4", border: "#86efac", text: "#166534" },
    info: { bg: "#eff6ff", border: "#93c5fd", text: "#1e40af" },
  },
  dark: {
    error: { bg: "#1f0a0a", border: "#991b1b", text: "#fca5a5" },
    success: { bg: "#052e16", border: "#166534", text: "#86efac" },
    info: { bg: "#0c1929", border: "#1e40af", text: "#93c5fd" },
  },
} as const;

/**
 * Share-card palette — deliberately fixed (social cards are brand assets,
 * always light regardless of app theme). Mirrors the sourceChipColors precedent.
 */
export const shareCardColors = {
  bg: "#ffffff",
  bgAccent: "#f0fdf4",
  text: "#0f172a",
  textMuted: "#64748b",
  primary: "#16a34a",
  border: "#e2e8f0",
  warning: "#f59e0b",
  danger: "#ef4444",
} as const;

export const darkColors: typeof lightColors = {
  primary: "#22c55e",
  primaryLight: "#4ade80",
  primaryBg: "#052e16",
  primaryBorder: "#166534",

  secondary: "#38bdf8",
  secondaryBg: "#0c1929",

  accent: "#a78bfa",
  accentBg: "#1e1633",

  warning: "#fbbf24",
  warningBg: "#1c1508",
  warningBorder: "#854d0e",

  danger: "#f87171",
  dangerBg: "#1f0a0a",
  dangerBorder: "#991b1b",

  bg: "#0f172a",
  card: "#1e293b",
  cardHover: "#253349",
  tabBar: "#0b1426",

  text: "#f1f5f9",
  textSecondary: "#cbd5e1",
  textMuted: "#94a3b8",
  textLight: "#475569",

  border: "#334155",
  borderLight: "#293548",
  divider: "#253349",

  textOnPrimary: "#ffffff",
  overlay: "rgba(0,0,0,0.5)",

  protein: "#60a5fa",
  carbs: "#fbbf24",
  fat: "#f87171",
  fiber: "#a78bfa",
  sugar: "#fb923c",
  sodium: "#22d3ee",

  /* Camera surfaces are theme-independent (always dark) */
  cameraBackdrop: "#000000",
  cameraScrim: "rgba(0,0,0,0.35)",
  cameraOnScrim: "#ffffff",
  cameraOnScrimMuted: "rgba(255,255,255,0.88)",
};

/** Backward-compatible export — defaults to light. Use `useThemeColors()` for reactive dark mode. */
export const colors = lightColors;

export type ThemeColors = typeof lightColors;

export const spacing = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 20,
  xxl: 24,
  xxxl: 32,
};

export const radius = {
  sm: 8,
  md: 12,
  lg: 16,
  xl: 20,
  full: 999,
};

export const shadow =
  Platform.OS === "web"
    ? ({
        boxShadow: "0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)",
      } as any)
    : {
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 1 },
        shadowOpacity: 0.05,
        shadowRadius: 3,
        elevation: 2,
      };

export const shadowMd =
  Platform.OS === "web"
    ? ({
        boxShadow: "0 4px 6px rgba(0,0,0,0.05), 0 2px 4px rgba(0,0,0,0.04)",
      } as any)
    : {
        shadowColor: "#000",
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.08,
        shadowRadius: 6,
        elevation: 3,
      };

export const fonts = {
  h1: {
    fontSize: 28,
    fontWeight: "800" as const,
    color: colors.text,
    letterSpacing: -0.5,
  },
  h2: { fontSize: 22, fontWeight: "700" as const, color: colors.text },
  h3: { fontSize: 18, fontWeight: "700" as const, color: colors.text },
  h4: { fontSize: 16, fontWeight: "600" as const, color: colors.textSecondary },
  body: { fontSize: 15, color: colors.textSecondary },
  caption: { fontSize: 13, color: colors.textMuted },
  small: { fontSize: 11, color: colors.textMuted },
};

export const mealTypeEmoji: Record<string, string> = {
  Breakfast: "🌅",
  Lunch: "☀️",
  Dinner: "🌙",
  Snack: "🍿",
};
