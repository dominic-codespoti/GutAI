import { create } from "zustand";
import { Appearance, Platform, type ViewStyle } from "react-native";
import { getItem, setItem } from "../utils/storage";
import {
  lightColors,
  darkColors,
  spacing,
  radius,
  fontFamilies,
  type ThemeColors,
} from "../utils/theme";

type ColorScheme = "light" | "dark" | "system";

interface ThemeState {
  /** User preference — "system" follows the OS */
  preference: ColorScheme;
  /** Resolved scheme used for rendering */
  resolved: "light" | "dark";
  /** Whether the persisted preference has been loaded */
  _hydrated: boolean;
  setPreference: (pref: ColorScheme) => void;
}

const THEME_STORAGE_KEY = "gutlens_theme_preference";

function resolve(pref: ColorScheme): "light" | "dark" {
  if (pref === "system") {
    return Appearance.getColorScheme() === "dark" ? "dark" : "light";
  }
  return pref;
}

export const useThemeStore = create<ThemeState>((set, get) => ({
  preference: "system",
  resolved: resolve("system"),
  _hydrated: false,
  setPreference: (pref) => {
    set({ preference: pref, resolved: resolve(pref) });
    // Persist to storage (fire-and-forget)
    setItem(THEME_STORAGE_KEY, pref).catch(() => {});
  },
}));

// Hydrate persisted preference on app start
(async () => {
  try {
    const stored = await getItem(THEME_STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "system") {
      useThemeStore.setState({
        preference: stored,
        resolved: resolve(stored),
        _hydrated: true,
      });
    } else {
      useThemeStore.setState({ _hydrated: true });
    }
  } catch {
    useThemeStore.setState({ _hydrated: true });
  }
})();

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

/** Hook — returns the resolved active color scheme ('light' | 'dark'). */
export function useColorScheme(): "light" | "dark" {
  return useThemeStore((s) => s.resolved);
}

/** Hook — returns themed font styles (text color adapts to scheme). */
export function useThemeFonts() {
  const c = useThemeColors();
  return {
    h1: {
      fontFamily: fontFamilies.display,
      fontSize: 30,
      lineHeight: 35,
      color: c.text,
      letterSpacing: -0.3,
    },
    h2: {
      fontFamily: fontFamilies.display,
      fontSize: 24,
      lineHeight: 29,
      color: c.text,
      letterSpacing: -0.2,
    },
    h3: {
      fontFamily: fontFamilies.bodyBold,
      fontSize: 18,
      color: c.text,
    },
    h4: {
      fontFamily: fontFamilies.bodySemiBold,
      fontSize: 16,
      color: c.textSecondary,
    },
    body: {
      fontFamily: fontFamilies.body,
      fontSize: 15,
      color: c.textSecondary,
    },
    caption: {
      fontFamily: fontFamilies.body,
      fontSize: 13,
      color: c.textMuted,
    },
    small: {
      fontFamily: fontFamilies.body,
      fontSize: 11,
      color: c.textMuted,
    },
  };
}

/** Hook — returns the platform shadow with dark-aware color. */
export function useThemeShadow() {
  const c = useThemeColors();
  const isDark = useThemeStore((s) => s.resolved) === "dark";
  const webShadowStyle = (boxShadow: string): ViewStyle =>
    // React Native versions before CSS box-shadow support need this narrow boundary cast.
    ({ boxShadow } as unknown as ViewStyle);

  const base =
    Platform.OS === "web"
      ? webShadowStyle(
          isDark
            ? "0 1px 3px rgba(0,0,0,0.3)"
            : "0 4px 16px rgba(15,50,25,0.06)",
        )
      : {
          shadowColor: isDark ? "#000" : "#0f3219",
          shadowOffset: { width: 0, height: isDark ? 1 : 2 },
          shadowOpacity: isDark ? 0.3 : 0.06,
          shadowRadius: isDark ? 3 : 8,
          elevation: isDark ? 2 : 3,
        };

  const md =
    Platform.OS === "web"
      ? webShadowStyle(
          isDark
            ? "0 4px 6px rgba(0,0,0,0.4)"
            : "0 12px 36px rgba(15,50,25,0.09)",
        )
      : {
          shadowColor: isDark ? "#000" : "#0f3219",
          shadowOffset: { width: 0, height: isDark ? 2 : 4 },
          shadowOpacity: isDark ? 0.4 : 0.09,
          shadowRadius: isDark ? 6 : 10,
          elevation: isDark ? 3 : 4,
        };

  return { shadow: base, shadowMd: md };
}

/** Non-hook accessor for imperative code. */
export function getThemeColors(): ThemeColors {
  return useThemeStore.getState().resolved === "dark"
    ? darkColors
    : lightColors;
}
