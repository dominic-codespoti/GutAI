import { View, Text, StyleSheet } from "react-native";
import Animated, {
  FadeInDown,
  useReducedMotion,
} from "react-native-reanimated";
import { CartesianChart } from "victory-native";
import { Circle } from "@shopify/react-native-skia";
import type { SymptomLog } from "../../types";
import { severityColor } from "../../utils/colors";
import { useThemeColors } from "../../stores/theme";
import { spacing } from "../../utils/theme";
import { useChartFonts } from "./useChartFonts";

type SeverityTimelineProps = {
  logs: SymptomLog[];
  height?: number;
};

/**
 * Severity-over-time scatter for symptom history.
 * Y fixed at 0–10; dot size scales with severity; color via severityColor bands.
 */
export function SeverityTimeline({
  logs,
  height = 180,
}: SeverityTimelineProps) {
  const c = useThemeColors();
  const reduced = useReducedMotion();
  const fonts = useChartFonts();

  const points = logs
    .map((l) => ({ t: new Date(l.occurredAt).getTime(), severity: l.severity }))
    .filter((p) => Number.isFinite(p.t))
    .sort((a, b) => a.t - b.t);
  if (points.length === 0) return null;

  const fmtDay = (ms: number) => {
    const d = new Date(ms);
    if (Number.isNaN(d.getTime())) return "";
    return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
  };

  return (
    <Animated.View
      entering={reduced ? undefined : FadeInDown.duration(400)}
      style={{ height }}
    >
      <CartesianChart
        data={points}
        xKey="t"
        yKeys={["severity"]}
        domain={{ y: [0, 10] }}
        axisOptions={{
          font: fonts.regular ?? undefined,
          labelColor: c.textMuted,
          lineWidth: 0,
          tickCount: Math.min(5, points.length),
          formatXLabel: (v: number) => fmtDay(v),
          formatYLabel: (v: number) => (Number.isInteger(v) ? `${v}` : ""),
        }}
      >
        {({ points: pts }) => (
          <>
            {pts.severity.map((pt, i) => {
              const yv = pt.yValue ?? 0;
              return (
                <Circle
                  key={`sev-${i}`}
                  cx={Number(pt.x ?? 0)}
                  cy={Number(pt.y ?? 0)}
                  r={3 + yv * 0.45}
                  color={severityColor(yv)}
                  opacity={0.8}
                />
              );
            })}
            </>
        )}
      </CartesianChart>
      <View style={styles.legendRow}>
        {[
          { label: "Mild (1–3)", n: 2 },
          { label: "Moderate (4–6)", n: 5 },
          { label: "Severe (7–10)", n: 8 },
        ].map(({ label, n }) => (
          <View key={label} style={styles.legendItem}>
            <View style={[styles.dot, { backgroundColor: severityColor(n) }]} />
            <Text style={[styles.legendText, { color: c.textSecondary }]}>
              {label}
            </Text>
          </View>
        ))}
      </View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  legendRow: {
    flexDirection: "row",
    justifyContent: "center",
    gap: spacing.lg,
    marginTop: -spacing.sm,
  },
  legendItem: { flexDirection: "row", alignItems: "center", gap: 6 },
  dot: { width: 9, height: 9, borderRadius: 4.5 },
  legendText: { fontSize: 11 },
});
