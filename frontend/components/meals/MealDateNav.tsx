import { View, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { radius, spacing } from "../../src/utils/theme";
import { useThemeColors, useThemeShadow } from "../../src/stores/theme";
import {
  shiftDate,
  formatDateLabel,
  toLocalDateStr,
} from "../../src/utils/date";
import { useMealSheetStore } from "../../src/stores/mealSheet";

export function MealDateNav() {
  const colors = useThemeColors();
  const { shadow } = useThemeShadow();
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const setDate = useMealSheetStore((s) => s.setDate);
  const todayStr = toLocalDateStr();
  const isToday = selectedDate === todayStr;

  return (
    <View
      style={{
        flexDirection: "row",
        alignItems: "center",
        justifyContent: "space-between",
        backgroundColor: colors.card,
        borderRadius: radius.md,
        padding: spacing.md,
        marginBottom: spacing.lg,
        ...shadow,
      }}
    >
      <TouchableOpacity
        onPress={() => setDate(shiftDate(selectedDate, -1))}
        style={{ padding: 8 }}
      >
        <Ionicons name="chevron-back" size={22} color={colors.textSecondary} />
      </TouchableOpacity>

      <TouchableOpacity onPress={() => setDate(todayStr)}>
        <Text
          style={{
            fontSize: 16,
            fontWeight: "700",
            color: colors.text,
            textAlign: "center",
          }}
        >
          {formatDateLabel(selectedDate)}
        </Text>
        {!isToday && (
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "center",
              marginTop: 2,
              gap: 4,
            }}
          >
            <Ionicons
              name="return-down-back"
              size={11}
              color={colors.primary}
            />
            <Text
              style={{
                fontSize: 11,
                color: colors.primary,
                fontWeight: "600",
              }}
            >
              Today
            </Text>
          </View>
        )}
      </TouchableOpacity>

      <TouchableOpacity
        onPress={() => !isToday && setDate(shiftDate(selectedDate, 1))}
        style={{ padding: 8, opacity: isToday ? 0.3 : 1 }}
      >
        <Ionicons
          name="chevron-forward"
          size={22}
          color={colors.textSecondary}
        />
      </TouchableOpacity>
    </View>
  );
}
