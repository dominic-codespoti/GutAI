import { Platform } from "react-native";

export const colors = {
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

  text: "#0f172a",
  textSecondary: "#475569",
  textMuted: "#94a3b8",
  textLight: "#cbd5e1",

  border: "#e2e8f0",
  borderLight: "#f1f5f9",
  divider: "#f1f5f9",

  protein: "#3b82f6",
  carbs: "#f59e0b",
  fat: "#ef4444",
  fiber: "#8b5cf6",
  sugar: "#f97316",
  sodium: "#06b6d4",
};

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
