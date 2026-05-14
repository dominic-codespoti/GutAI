import React, { useEffect, useRef } from "react";
import { Animated, View, StyleSheet } from "react-native";
import { useThemeColors } from "../stores/theme";

const DOT_SIZE = 6;
const DOT_SPACING = 3;
const ANIMATION_DURATION = 600;

function Dot({ delay, color }: { delay: number; color: string }) {
  const anim = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    const bounce = Animated.loop(
      Animated.sequence([
        Animated.delay(delay),
        Animated.timing(anim, {
          toValue: 1,
          duration: ANIMATION_DURATION,
          useNativeDriver: true,
        }),
        Animated.timing(anim, {
          toValue: 0,
          duration: ANIMATION_DURATION,
          useNativeDriver: true,
        }),
      ]),
    );
    bounce.start();
    return () => bounce.stop();
  }, [delay, anim]);

  const translateY = anim.interpolate({
    inputRange: [0, 0.5, 1],
    outputRange: [0, -6, 0],
  });

  return (
    <Animated.View
      style={[
        styles.dot,
        {
          backgroundColor: color,
          transform: [{ translateY }],
          opacity: anim.interpolate({
            inputRange: [0, 0.5, 1],
            outputRange: [0.4, 1, 0.4],
          }),
        },
      ]}
    />
  );
}

interface TypingIndicatorProps {
  visible: boolean;
}

export default function TypingIndicator({ visible }: TypingIndicatorProps) {
  const colors = useThemeColors();

  if (!visible) return null;

  return (
    <View style={styles.container}>
      <Dot delay={0} color={colors.primary} />
      <Dot delay={200} color={colors.primary} />
      <Dot delay={400} color={colors.primary} />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: DOT_SPACING,
  },
  dot: {
    width: DOT_SIZE,
    height: DOT_SIZE,
    borderRadius: DOT_SIZE / 2,
  },
});
