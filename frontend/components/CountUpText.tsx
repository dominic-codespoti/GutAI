import { useEffect, useState } from "react";
import { Text, type TextStyle } from "react-native";
import {
  Easing,
  useAnimatedReaction,
  useReducedMotion,
  useSharedValue,
  withTiming,
} from "react-native-reanimated";
import { scheduleOnRN } from "react-native-worklets";

type CountUpTextProps = {
  value: number;
  style?: TextStyle | TextStyle[];
  duration?: number;
  suffix?: string;
};

/**
 * Animated integer that counts up to `value` on mount and re-targets on change.
 * Honors the OS reduced-motion setting (snaps instantly).
 */
export function CountUpText({
  value,
  style,
  duration = 900,
  suffix = "",
}: CountUpTextProps) {
  const reduced = useReducedMotion();
  const progress = useSharedValue(0);
  const [display, setDisplay] = useState(() =>
    Number.isFinite(value) ? Math.round(value) : 0,
  );

  useEffect(() => {
    if (!Number.isFinite(value)) return;
    if (reduced) {
      setDisplay(Math.round(value));
      return;
    }
    progress.value = 0;
    progress.value = withTiming(1, {
      duration,
      easing: Easing.out(Easing.cubic),
    });
  }, [value, reduced, duration, progress]);

  useAnimatedReaction(
    () => Math.round(progress.value * value),
    (current, previous) => {
      if (current !== previous && Number.isFinite(current)) {
        scheduleOnRN(setDisplay, current);
      }
    },
  );

  return (
    <Text style={style}>
      {display}
      {suffix}
    </Text>
  );
}
