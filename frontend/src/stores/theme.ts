import { create } from "zustand";
import { Appearance, Platform } from "react-native";
import {
  lightColors,
  darkColors,
  spacing,
  radius,
  shadow,
  shadowMd,
  fonts,
  type ThemeColors,
} from "../utils/theme";

type ColorScheme = "light" | "dark" | "system";

interface ThemeState {
  /** User preference — "system" follows the OS */
  preference: ColorScheme;
  /** Resolved scheme used for rendering */
  resolved: "light" | "dark";
  setPreference: (pref: ColorScheme) => void;
}

function resolve(pref: ColorScheme): "light" | "dark" {
  if (pref === "system") {
    return Appearance.getColorScheme() === "dark" ? "dark" : "light";
  }
  return pref;
}

export const useThemeStore = create<ThemeState>((set, get) => ({
  preference: "light",
  resolved: resolve("light"),
  setPreference: (pref) => set({ preference: pref, resolved: resolve(pref) }),
}));

// Listen for OS color scheme changes
Appearance.addChangeListener(({ colorScheme }) => {
  const { preference } = useThemeStore.getState();
  if (preference === "system") {
    useThemeStore.setState({
      resolved: colorScheme === "dark" ? "dark" : "light",
    });
  }
});

/** Hook — returns the active color palette. Use in components for reactive dark mode. */
export function useThemeColors(): ThemeColors {
  const resolved = useThemeStore((s) => s.resolved);
  return resolved === "dark" ? darkColors : lightColors;
}

/** Hook — returns themed font styles (text color adapts to scheme). */
export function useThemeFonts() {
  const c = useThemeColors();
  return {
    h1: {
      fontSize: 28,
      fontWeight: "800" as const,
      color: c.text,
      letterSpacing: -0.5,
    },
    h2: { fontSize: 22, fontWeight: "700" as const, color: c.text },
    h3: { fontSize: 18, fontWeight: "700" as const, color: c.text },
    h4: { fontSize: 16, fontWeight: "600" as const, color: c.textSecondary },
    body: { fontSize: 15, color: c.textSecondary },
    caption: { fontSize: 13, color: c.textMuted },
    small: { fontSize: 11, color: c.textMuted },
  };
}

/** Hook — returns the platform shadow with dark-aware color. */
export function useThemeShadow() {
  const c = useThemeColors();
  const isDark = useThemeStore((s) => s.resolved) === "dark";

  const base =
    Platform.OS === "web"
      ? ({
          boxShadow: isDark
            ? "0 1px 3px rgba(0,0,0,0.3)"
            : "0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)",
        } as any)
      : {
          shadowColor: "#000",
          shadowOffset: { width: 0, height: 1 },
          shadowOpacity: isDark ? 0.3 : 0.05,
          shadowRadius: 3,
          elevation: 2,
        };

  const md =
    Platform.OS === "web"
      ? ({
          boxShadow: isDark
            ? "0 4px 6px rgba(0,0,0,0.4)"
            : "0 4px 6px rgba(0,0,0,0.05), 0 2px 4px rgba(0,0,0,0.04)",
        } as any)
      : {
          shadowColor: "#000",
          shadowOffset: { width: 0, height: 2 },
          shadowOpacity: isDark ? 0.4 : 0.08,
          shadowRadius: 6,
          elevation: 3,
        };

  return { shadow: base, shadowMd: md };
}

/** Non-hook accessor for imperative code. */
export function getThemeColors(): ThemeColors {
  return useThemeStore.getState().resolved === "dark"
    ? darkColors
    : lightColors;
}
