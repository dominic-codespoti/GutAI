import { useState } from "react";
import { Text, TouchableOpacity, View, StyleSheet } from "react-native";
import Animated, {
  FadeInDown,
  useAnimatedReaction,
  useReducedMotion,
} from "react-native-reanimated";
import {
  CartesianChart,
  Line,
  Area,
  useChartPressState,
} from "victory-native";
import { Circle } from "@shopify/react-native-skia";
import type { NutritionTrend } from "../../types";
import { useThemeColors } from "../../stores/theme";
import { spacing } from "../../utils/theme";
import { useChartFonts } from "./useChartFonts";

export type TrendMetric =
  | "calories"
  | "protein"
  | "carbs"
  | "fat"
  | "fiber"
  | "sugar";
type TrendChartProps = {
  data: NutritionTrend[];
  metric: TrendMetric;
  color: string;
  height?: number;
  /** Called with the ISO date when the user taps the scrubbed caption. */
  onDayPress?: (date: string) => void;
};

function shortDate(iso: string) {
  const d = new Date(iso.length === 10 ? `${iso}T00:00:00` : iso);
  if (Number.isNaN(d.getTime())) return String(iso);
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

/**
 * Interactive line+area trend chart with press-scrubbing.
 * Tap/drag to inspect a day; the caption reports the selected date + value.
 */
export function TrendChart({
  data,
  metric,
  color,
  height = 200,
  onDayPress,
}: TrendChartProps) {
  const c = useThemeColors();
  const reduced = useReducedMotion();
  const fonts = useChartFonts();
  const { state, isActive } = useChartPressState({
    x: "",
    y: { value: 0 },
  });
  const [pressedDate, setPressedDate] = useState<string | null>(null);

  const points = data.map((d) => ({
    date: d.date,
    value: Number(d[metric]) || 0,
  }));
  if (points.length < 2) return null;

  useAnimatedReaction(
    () => (isActive ? String(state.x.value.value) : ""),
    (current, previous) => {
      if (current !== previous) {
        setPressedDate(current === "" ? null : current);
      }
    },
  );

  const unit = metric === "calories" ? "kcal" : "g";
  const pressedIdx = pressedDate
    ? points.findIndex((p) => p.date === pressedDate)
    : -1;
  const pressed = pressedIdx >= 0 ? points[pressedIdx] : null;

  return (
    <Animated.View
      entering={reduced ? undefined : FadeInDown.duration(400)}
      style={{ height }}
    >
      <CartesianChart
        data={points}
        xKey="date"
        yKeys={["value"]}
        chartPressState={state}
        domain={{
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          y: [0, ((max: number) => Math.max(max * 1.15, 1)) as any],
        }}
        axisOptions={{
          font: fonts.regular ?? undefined,
          labelColor: c.textMuted,
          lineWidth: 0,
          tickCount: Math.min(5, points.length),
          formatXLabel: (v: string) => shortDate(v),
          formatYLabel: (v: number) => `${Math.round(Number(v))}`,
        }}
      >
        {({ points: pts, chartBounds }) => (
          <>
            <Area
              points={pts.value}
              y0={chartBounds.bottom}
              color={`${color}1F`}
              curveType="natural"
            />
            <Line
              points={pts.value}
              color={color}
              strokeWidth={2.5}
              curveType="natural"
            />
            {pressed && pts.value[pressedIdx] ? (
              <Circle
                cx={Number(pts.value[pressedIdx].x ?? 0)}
                cy={Number(pts.value[pressedIdx].y ?? 0)}
                r={5}
                color={color}
              />
            ) : null}
          </>
        )}
      </CartesianChart>
      <View style={[styles.captionRow, { marginTop: -spacing.sm }]}>
        {pressed ? (
          onDayPress ? (
            <TouchableOpacity
              onPress={() => onDayPress(pressed.date)}
              accessibilityRole="button"
              accessibilityLabel={`Open meals for ${shortDate(pressed.date)}`}
              style={styles.drillButton}
            >
              <Text style={[styles.captionDate, { color: c.textMuted }]}>
                {shortDate(pressed.date)}
              </Text>
              <Text style={[styles.captionValue, { color }]}>
                {Math.round(pressed.value).toLocaleString()} {unit} · View →
              </Text>
            </TouchableOpacity>
          ) : (
            <>
              <Text style={[styles.captionDate, { color: c.textMuted }]}>
                {shortDate(pressed.date)}
              </Text>
              <Text style={[styles.captionValue, { color }]}>
                {Math.round(pressed.value).toLocaleString()} {unit}
              </Text>
            </>
          )
        ) : (
          <Text style={[styles.captionHint, { color: c.textMuted }]}>
            Tap the chart to inspect a day
          </Text>
        )}
      </View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  captionRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing.sm,
    minHeight: 20,
  },
  captionDate: { fontSize: 12 },
  captionValue: { fontSize: 13, fontWeight: "700" },
  captionHint: { fontSize: 11 },
  drillButton: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    paddingVertical: 2,
  },
});
