import { useEffect } from "react";
import { View, Text, StyleSheet } from "react-native";
import Animated, {
  Easing,
  useAnimatedStyle,
  useReducedMotion,
  useSharedValue,
  withTiming,
} from "react-native-reanimated";
import type { StyleProp, ViewStyle } from "react-native";
import { useThemeColors } from "../../stores/theme";
import { radius, spacing } from "../../utils/theme";
import { cspiColor } from "../../utils/colors";

export type HBarItem = {
  label: string;
  value: number;
  /** Optional rating string controlling bar color (e.g. CSPI rating) */
  rating?: string;
};

type HBarListProps = {
  items: HBarItem[];
  max?: number;
};

function BarRow({
  label,
  value,
  pct,
  color,
}: {
  label: string;
  value: number;
  pct: number;
  color: string;
}) {
  const c = useThemeColors();
  const reduced = useReducedMotion();
  const width = useSharedValue(0);

  useEffect(() => {
    if (reduced) {
      width.value = pct;
      return;
    }
    width.value = 0;
    width.value = withTiming(pct, {
      duration: 650,
      easing: Easing.out(Easing.cubic),
    });
  }, [pct, reduced, width]);

  const barStyle = useAnimatedStyle(() => ({
    width: `${width.value * 100}%`,
  }));

  return (
    <View>
      <View style={styles.topRow}>
        <Text style={[styles.label, { color: c.text }]} numberOfLines={1}>
          {label}
        </Text>
        <Text style={[styles.value, { color: c.textMuted }]}>
          {value.toLocaleString()}
        </Text>
      </View>
      <View style={[styles.track, { backgroundColor: c.borderLight }]}>
        <Animated.View
          style={[
            styles.bar,
            { backgroundColor: color },
            barStyle as StyleProp<ViewStyle>,
          ]}
        />
      </View>
    </View>
  );
}

/**
 * Ranked horizontal bar list for categorical exposure data.
 * Bars grow on mount and re-animate when values change; color per-bar
 * via `cspiColor` when a rating is given.
 */
export function HBarList({ items, max }: HBarListProps) {
  const c = useThemeColors();
  if (items.length === 0) return null;

  const maxValue = Math.max(max ?? 0, ...items.map((i) => i.value), 1);
  const sorted = [...items].sort((a, b) => b.value - a.value);

  return (
    <View style={styles.container}>
      {sorted.map((item, i) => (
        <View key={`${item.label}-${i}`}>
          <BarRow
            label={item.label}
            value={item.value}
            pct={Math.min(item.value / maxValue, 1)}
            color={item.rating ? cspiColor(item.rating) : c.primary}
          />
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.md },
  topRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: 4,
  },
  label: { fontSize: 13, fontWeight: "600", flex: 1, marginRight: 8 },
  value: { fontSize: 12 },
  track: {
    height: 8,
    borderRadius: 4,
    overflow: "hidden",
  },
  bar: {
    height: 8,
    borderRadius: 4,
  },
});
