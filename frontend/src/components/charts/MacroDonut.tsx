import { View, Text, StyleSheet } from "react-native";
import Animated, {
  FadeInDown,
  useReducedMotion,
} from "react-native-reanimated";
import { Pie, PolarChart } from "victory-native";
import type { MealTypeNutrition } from "../../types";
import { useThemeColors } from "../../stores/theme";
import { radius, spacing } from "../../utils/theme";

const MEAL_TYPE_COLOR_KEYS = ["primary", "secondary", "accent", "warning"] as const;

/**
 * Donut chart of calories by meal type with a legend.
 * Colors derive from theme tokens; the center shows the day's total.
 */
export function MacroDonut({
  data,
  size = 150,
}: {
  data: MealTypeNutrition[];
  size?: number;
}) {
  const c = useThemeColors();
  const reduced = useReducedMotion();

  const slices = data
    .filter((d) => d.totalCalories > 0)
    .map((d, i) => ({
      label: d.mealType,
      value: d.totalCalories,
      color: c[MEAL_TYPE_COLOR_KEYS[i % MEAL_TYPE_COLOR_KEYS.length]] as string,
    }));
  if (slices.length === 0) return null;

  const total = slices.reduce((s, d) => s + d.value, 0);

  return (
    <Animated.View
      entering={reduced ? undefined : FadeInDown.duration(400)}
      style={styles.row}
    >
      <View style={{ width: size, height: size }}>
        <PolarChart
          data={slices}
          labelKey="label"
          valueKey="value"
          colorKey="color"
        >
          <Pie.Chart innerRadius={size * 0.32}>
            {({ slice }) => (
              <Pie.Slice key={slice.label}>
                <Pie.SliceAngularInset
                  angularInset={{
                    angularStrokeWidth: 2,
                    angularStrokeColor: c.card,
                  }}
                />
              </Pie.Slice>
            )}
          </Pie.Chart>
        </PolarChart>
        <View style={[styles.centerLabel, { pointerEvents: "none" }]}>
          <Text style={[styles.centerValue, { color: c.text }]}>
            {Math.round(total).toLocaleString()}
          </Text>
          <Text style={[styles.centerUnit, { color: c.textMuted }]}>kcal</Text>
        </View>
      </View>
      <View style={styles.legend}>
        {slices.map((s) => (
          <View key={s.label} style={styles.legendRow}>
            <View style={[styles.dot, { backgroundColor: s.color }]} />
            <Text style={[styles.legendLabel, { color: c.textSecondary }]}>
              {s.label}
            </Text>
            <Text style={[styles.legendValue, { color: c.text }]}>
              {Math.round((s.value / total) * 100)}%
            </Text>
          </View>
        ))}
      </View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing.xl,
    paddingVertical: spacing.sm,
  },
  centerLabel: {
    position: "absolute",
    left: 0,
    right: 0,
    top: 0,
    bottom: 0,
    alignItems: "center",
    justifyContent: "center",
  },
  centerValue: { fontSize: 15, fontWeight: "800" },
  centerUnit: { fontSize: 10 },
  legend: { flex: 1, gap: spacing.sm },
  legendRow: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  dot: { width: 10, height: 10, borderRadius: 5 },
  legendLabel: { fontSize: 13, flex: 1 },
  legendValue: { fontSize: 13, fontWeight: "700" },
});
