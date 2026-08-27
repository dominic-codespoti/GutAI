import { useState } from "react";
import { View, Text, TouchableOpacity, StyleSheet } from "react-native";
import type { NutritionTrend } from "../../types";
import { useThemeColors } from "../../stores/theme";
import { spacing } from "../../utils/theme";

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
  onDayPress?: (date: string) => void;
};

function shortDate(iso: string) {
  const d = new Date(iso.length === 10 ? `${iso}T00:00:00` : iso);
  if (Number.isNaN(d.getTime())) return String(iso);
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

/** Web fallback for the native Skia chart. Keeps the same contract without CanvasKit. */
export function TrendChart({
  data,
  metric,
  color,
  height = 200,
  onDayPress,
}: TrendChartProps) {
  const c = useThemeColors();
  const visible = data.slice(-14);
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  if (visible.length === 0) return null;

  const max = Math.max(...visible.map((d) => Number(d[metric]) || 0), 1);
  const selected =
    visible.find((d) => d.date === selectedDate) ?? visible[visible.length - 1];
  const unit = metric === "calories" ? "kcal" : "g";

  return (
    <View style={{ height }} accessibilityLabel={`${metric} trend chart`}>
      <View style={styles.chartArea}>
        {visible.map((d) => {
          const value = Number(d[metric]) || 0;
          const isSelected = d.date === selected.date;
          return (
            <TouchableOpacity
              key={d.date}
              onPress={() => {
                setSelectedDate(d.date);
                onDayPress?.(d.date);
              }}
              accessibilityRole="button"
              accessibilityLabel={`${shortDate(d.date)}: ${Math.round(value)} ${unit}`}
              style={styles.column}
            >
              <View
                style={[
                  styles.bar,
                  {
                    height: `${Math.max((value / max) * 88, 5)}%`,
                    backgroundColor: isSelected ? color : `${color}99`,
                  },
                ]}
              />
            </TouchableOpacity>
          );
        })}
      </View>
      <View style={styles.axisRow}>
        <Text style={[styles.axisText, { color: c.textMuted }]}>
          {shortDate(visible[0].date)}
        </Text>
        <Text style={[styles.axisText, { color: c.textMuted }]}>
          {shortDate(visible[visible.length - 1].date)}
        </Text>
      </View>
      <View style={[styles.caption, { borderTopColor: c.borderLight }]}>
        <Text style={[styles.captionDate, { color: c.textMuted }]}>
          {shortDate(selected.date)}
        </Text>
        <Text style={[styles.captionValue, { color }]}>
          {Math.round(Number(selected[metric]) || 0).toLocaleString()} {unit}
        </Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  chartArea: {
    flex: 1,
    flexDirection: "row",
    alignItems: "flex-end",
    gap: 4,
    paddingHorizontal: spacing.sm,
    paddingTop: spacing.sm,
  },
  column: {
    flex: 1,
    height: "100%",
    alignItems: "center",
    justifyContent: "flex-end",
  },
  bar: { width: "72%", minWidth: 4, borderRadius: 5 },
  axisRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    paddingHorizontal: spacing.sm,
  },
  axisText: { fontSize: 10 },
  caption: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing.sm,
    borderTopWidth: StyleSheet.hairlineWidth,
    marginTop: 2,
    paddingTop: 4,
  },
  captionDate: { fontSize: 11 },
  captionValue: { fontSize: 12, fontWeight: "700" },
});
