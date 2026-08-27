import { View, Text, StyleSheet } from "react-native";
import type { SymptomLog } from "../../types";
import { severityColor } from "../../utils/colors";
import { useThemeColors } from "../../stores/theme";
import { spacing } from "../../utils/theme";

/** Web fallback for the native Skia severity chart. */
export function SeverityTimeline({
  logs,
  height = 180,
}: {
  logs: SymptomLog[];
  height?: number;
}) {
  const c = useThemeColors();
  const points = logs
    .map((log) => ({
      time: new Date(log.occurredAt).getTime(),
      severity: log.severity,
    }))
    .filter((point) => Number.isFinite(point.time))
    .sort((a, b) => a.time - b.time);
  if (points.length === 0) return null;

  const first = points[0].time;
  const last = points[points.length - 1].time;
  const span = Math.max(last - first, 1);
  const dayLabel = (time: number) =>
    new Date(time).toLocaleDateString(undefined, { month: "short", day: "numeric" });

  return (
    <View style={{ height }} accessibilityLabel="Symptom severity timeline">
      <View style={[styles.plot, { borderBottomColor: c.border }]}>
        {[2, 4, 6, 8].map((level) => (
          <View
            key={level}
            style={[styles.guide, { bottom: `${level * 10}%`, borderColor: c.borderLight }]}
          />
        ))}
        {points.map((point, index) => (
          <View
            key={`${point.time}-${index}`}
            style={{
              position: "absolute",
              left: `${((point.time - first) / span) * 96 + 2}%`,
              bottom: `${point.severity * 10}%`,
              width: 10,
              height: 10,
              marginLeft: -5,
              marginBottom: -5,
              borderRadius: 5,
              backgroundColor: severityColor(point.severity),
              borderWidth: 2,
              borderColor: c.card,
            }}
          />
        ))}
      </View>
      <View style={styles.axisRow}>
        <Text style={[styles.axisText, { color: c.textMuted }]}>{dayLabel(first)}</Text>
        <Text style={[styles.axisText, { color: c.textMuted }]}>{dayLabel(last)}</Text>
      </View>
      <View style={styles.legendRow}>
        {[{ label: "Mild", severity: 2 }, { label: "Moderate", severity: 5 }, { label: "Severe", severity: 8 }].map((item) => (
          <View key={item.label} style={styles.legendItem}>
            <View style={[styles.legendDot, { backgroundColor: severityColor(item.severity) }]} />
            <Text style={[styles.legendText, { color: c.textSecondary }]}>{item.label}</Text>
          </View>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  plot: { flex: 1, position: "relative", borderBottomWidth: 1 },
  guide: { position: "absolute", left: 0, right: 0, borderTopWidth: StyleSheet.hairlineWidth },
  axisRow: { flexDirection: "row", justifyContent: "space-between", paddingTop: 4 },
  axisText: { fontSize: 10 },
  legendRow: { flexDirection: "row", justifyContent: "center", gap: spacing.lg, marginTop: 4 },
  legendItem: { flexDirection: "row", alignItems: "center", gap: 5 },
  legendDot: { width: 8, height: 8, borderRadius: 4 },
  legendText: { fontSize: 11 },
});
