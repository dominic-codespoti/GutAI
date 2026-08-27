import { View, Text, StyleSheet } from "react-native";
import type { MealTypeNutrition } from "../../types";
import { useThemeColors } from "../../stores/theme";
import { spacing } from "../../utils/theme";

const MEAL_TYPE_COLOR_KEYS = ["primary", "secondary", "accent", "warning"] as const;

/** Web fallback for the native Skia donut chart. */
export function MacroDonut({
  data,
}: {
  data: MealTypeNutrition[];
  size?: number;
}) {
  const c = useThemeColors();
  const slices = data
    .filter((d) => d.totalCalories > 0)
    .map((d, i) => ({
      label: d.mealType,
      value: d.totalCalories,
      color: c[MEAL_TYPE_COLOR_KEYS[i % MEAL_TYPE_COLOR_KEYS.length]] as string,
    }));
  if (slices.length === 0) return null;
  const total = slices.reduce((sum, slice) => sum + slice.value, 0);

  return (
    <View accessibilityLabel="Calories by meal type chart">
      <View style={[styles.totalRow, { borderBottomColor: c.borderLight }]}>
        <Text style={[styles.totalValue, { color: c.text }]}>
          {Math.round(total).toLocaleString()} kcal
        </Text>
        <Text style={[styles.totalLabel, { color: c.textMuted }]}>by meal type</Text>
      </View>
      <View style={[styles.track, { backgroundColor: c.borderLight }]}>
        {slices.map((slice) => (
          <View
            key={slice.label}
            style={{
              flex: slice.value / total,
              backgroundColor: slice.color,
              height: 12,
            }}
          />
        ))}
      </View>
      <View style={styles.legend}>
        {slices.map((slice) => (
          <View key={slice.label} style={styles.legendItem}>
            <View style={[styles.dot, { backgroundColor: slice.color }]} />
            <Text style={[styles.legendLabel, { color: c.textSecondary }]}>
              {slice.label}
            </Text>
            <Text style={[styles.legendValue, { color: c.text }]}>
              {Math.round((slice.value / total) * 100)}%
            </Text>
          </View>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  totalRow: {
    flexDirection: "row",
    alignItems: "baseline",
    gap: spacing.sm,
    paddingBottom: spacing.sm,
    marginBottom: spacing.md,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  totalValue: { fontSize: 20, fontWeight: "800" },
  totalLabel: { fontSize: 12 },
  track: {
    flexDirection: "row",
    height: 12,
    borderRadius: 6,
    overflow: "hidden",
  },
  legend: { marginTop: spacing.md, gap: spacing.sm },
  legendItem: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  dot: { width: 10, height: 10, borderRadius: 5 },
  legendLabel: { flex: 1, fontSize: 13 },
  legendValue: { fontSize: 13, fontWeight: "700" },
});
