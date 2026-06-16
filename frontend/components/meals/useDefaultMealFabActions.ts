import { useRouter } from "expo-router";
import { useThemeColors } from "../../src/stores/theme";
import { mealSheet } from "../../src/stores/mealSheet";
import type { MealFabAction } from "./MealFab";

export function useDefaultMealFabActions(): MealFabAction[] {
  const colors = useThemeColors();
  const router = useRouter();

  return [
    {
      icon: "restaurant-outline",
      label: "Log Food",
      color: colors.primary,
      onPress: () => mealSheet.openLog(),
    },
    {
      icon: "sparkles-outline",
      label: "Create Custom Food",
      color: colors.accent,
      onPress: () => router.push("/food/create"),
    },
  ];
}
