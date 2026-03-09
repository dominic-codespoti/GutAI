import { getThemeColors } from "../stores/theme";

export const severityColor = (n: number) => {
  const c = getThemeColors();
  return n <= 3 ? c.primary : n <= 6 ? c.warning : c.danger;
};

export const ratingColor = (rating: string | null | undefined) => {
  const c = getThemeColors();
  switch (rating?.toLowerCase()) {
    case "safe":
    case "a":
      return c.primary;
    case "low concern":
    case "b":
      return c.primaryLight;
    case "caution":
    case "moderate concern":
    case "c":
      return c.warning;
    case "warning":
    case "high concern":
    case "d":
      return c.sugar;
    case "avoid":
    case "e":
      return c.danger;
    default:
      return c.textMuted;
  }
};

export const cspiColor = (rating: string) => {
  const c = getThemeColors();
  switch (rating) {
    case "Avoid":
      return c.danger;
    case "CutBack":
      return c.warning;
    case "Caution":
      return c.sugar;
    default:
      return c.primary;
  }
};

export const confidenceColor = (conf: string) => {
  const c = getThemeColors();
  switch (conf) {
    case "High":
      return c.danger;
    case "Medium":
      return c.warning;
    default:
      return c.primary;
  }
};

export const confidenceIcon = (c: string) => {
  switch (c) {
    case "High":
      return "alert-circle";
    case "Medium":
      return "warning";
    default:
      return "information-circle";
  }
};
