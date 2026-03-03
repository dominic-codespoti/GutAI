import { View, Text, TouchableOpacity } from "react-native";
import {
  colors,
  shadow,
  radius,
  spacing,
  mealTypeEmoji,
} from "../../src/utils/theme";
import { MEAL_TYPES, getMealTypeForTime } from "../../src/utils/constants";
import { useMealSheetStore, type MealType } from "../../src/stores/mealSheet";

/**
 * Merged chip row: selects the active meal type which BOTH
 * filters the meal list AND pre-selects the type for new entries.
 * "All" shows everything and uses time-based type for new entries.
 */
export function MealTypeChips() {
  const selectedMealType = useMealSheetStore((s) => s.selectedMealType);
  const setMealType = useMealSheetStore((s) => s.setMealType);

  // null means "All" – we still expose a MealType for new entries via getMealTypeForTime
  const filterType = useMealSheetStore((s) => s.selectedMealType);

  return (
    <View
      style={{
        flexDirection: "row",
        marginBottom: spacing.lg,
        gap: 6,
      }}
    >
      {MEAL_TYPES.map((type) => {
        const active = selectedMealType === type;
        return (
          <TouchableOpacity
            key={type}
            onPress={() => setMealType(type)}
            style={{
              flex: 1,
              paddingVertical: 10,
              borderRadius: radius.md,
              backgroundColor: active ? colors.primary : colors.card,
              alignItems: "center",
              ...shadow,
              borderWidth: active ? 0 : 1,
              borderColor: colors.borderLight,
            }}
          >
            <Text style={{ fontSize: 18, marginBottom: 2 }}>
              {mealTypeEmoji[type] ?? "🍽️"}
            </Text>
            <Text
              style={{
                fontSize: 11,
                fontWeight: "600",
                color: active ? "#fff" : colors.textSecondary,
              }}
            >
              {type}
            </Text>
          </TouchableOpacity>
        );
      })}
    </View>
  );
}
