import { Platform, type ViewStyle } from "react-native";

/* ── Color palettes ── */

export const lightColors = {
  primary: "#16a34a",
  primaryLight: "#22c55e",
  primaryHover: "#15803d",
  primaryBg: "#f1faf3",
  primaryBorder: "#bbf7d0",
  brandInk: "#17351f",
  brandDark: "#0f2417",
  mintSoft: "#f1faf3",
  mintWash: "#dcfce7",

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
  coral: "#ea7a5a",

  bg: "#ffffff",
  card: "#ffffff",
  cardHover: "#f1faf3",
  tabBar: "#ffffff",

  text: "#17351f",
  textSecondary: "#66736a",
  textMuted: "#8c978f",
  textLight: "#8c978f",

  border: "#e4eae5",
  borderLight: "#f1f5f2",
  divider: "#f1f5f2",

  textOnPrimary: "#ffffff",
  overlay: "rgba(15,36,23,0.3)",

  protein: "#3b82f6",
  carbs: "#f59e0b",
  fat: "#ea7a5a",
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
  bgAccent: "#f1faf3",
  text: "#17351f",
  textMuted: "#66736a",
  primary: "#16a34a",
  border: "#e4eae5",
  warning: "#f59e0b",
  danger: "#ef4444",
} as const;

export const darkColors: typeof lightColors = {
  primary: "#22c55e",
  primaryLight: "#4ade80",
  primaryHover: "#86efac",
  primaryBg: "#0d2a18",
  primaryBorder: "#2f7444",
  brandInk: "#e7f5ea",
  brandDark: "#0b1d12",
  mintSoft: "#0f2a18",
  mintWash: "#164a26",

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
  coral: "#fb8b70",

  bg: "#0f2417",
  card: "#172b1e",
  cardHover: "#203a29",
  tabBar: "#0b1d12",

  text: "#f1f8f3",
  textSecondary: "#c6d5ca",
  textMuted: "#91a499",
  textLight: "#8fae9a",

  border: "#304b38",
  borderLight: "#253c2d",
  divider: "#253c2d",

  textOnPrimary: "#ffffff",
  overlay: "rgba(0,0,0,0.5)",

  protein: "#60a5fa",
  carbs: "#fbbf24",
  fat: "#fb8b70",
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
  xl: 24,
  full: 999,
};

const webShadow = (boxShadow: string): ViewStyle =>
  // React Native versions before CSS box-shadow support need this narrow boundary cast.
  ({ boxShadow } as unknown as ViewStyle);

export const shadow =
  Platform.OS === "web"
    ? webShadow("0 4px 16px rgba(15,50,25,0.06)")
    : {
        shadowColor: "#0f3219",
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.06,
        shadowRadius: 8,
        elevation: 3,
      };

export const shadowMd =
  Platform.OS === "web"
    ? webShadow("0 12px 36px rgba(15,50,25,0.09)")
    : {
        shadowColor: "#0f3219",
        shadowOffset: { width: 0, height: 4 },
        shadowOpacity: 0.09,
        shadowRadius: 10,
        elevation: 4,
      };

export const fontFamilies = {
  body: "DMSans_400Regular",
  bodyMedium: "DMSans_500Medium",
  bodySemiBold: "DMSans_600SemiBold",
  bodyBold: "DMSans_700Bold",
  bodyExtraBold: "DMSans_800ExtraBold",
  display: "Fraunces_600SemiBold",
  displayBold: "Fraunces_700Bold",
  displayItalic: "Fraunces_700Bold_Italic",
} as const;

export const fonts = {
  h1: {
    fontFamily: fontFamilies.display,
    fontSize: 30,
    lineHeight: 35,
    color: colors.text,
    letterSpacing: -0.3,
  },
  h2: {
    fontFamily: fontFamilies.display,
    fontSize: 24,
    lineHeight: 29,
    color: colors.text,
    letterSpacing: -0.2,
  },
  h3: {
    fontFamily: fontFamilies.bodyBold,
    fontSize: 18,
    color: colors.text,
  },
  h4: {
    fontFamily: fontFamilies.bodySemiBold,
    fontSize: 16,
    color: colors.textSecondary,
  },
  body: {
    fontFamily: fontFamilies.body,
    fontSize: 15,
    color: colors.textSecondary,
  },
  caption: {
    fontFamily: fontFamilies.body,
    fontSize: 13,
    color: colors.textMuted,
  },
  small: {
    fontFamily: fontFamilies.body,
    fontSize: 11,
    color: colors.textMuted,
  },
};

export const mealTypeEmoji: Record<string, string> = {
  Breakfast: "🌅",
  Lunch: "☀️",
  Dinner: "🌙",
  Snack: "🍿",
};
