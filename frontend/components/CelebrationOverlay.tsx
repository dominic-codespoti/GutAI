import { useEffect, useMemo } from "react";
import { Platform, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Image } from "expo-image";
import { Ionicons } from "@expo/vector-icons";
import Animated, {
  Easing,
  FadeIn,
  useAnimatedStyle,
  useReducedMotion,
  useSharedValue,
  withTiming,
  type SharedValue,
} from "react-native-reanimated";
import { useCelebrationStore } from "../src/stores/celebration";
import { useThemeColors } from "../src/stores/theme";
import { radius, spacing } from "../src/utils/theme";

const AUTO_DISMISS_MS = 2600;
const PARTICLE_COUNT = 26;

/** Deterministic pseudo-random in [0,1) — stable across renders and inside worklets. */
function rand(i: number, salt: number) {
  "worklet";
  const x = Math.sin(i * 127.1 + salt * 311.7) * 43758.5453;
  return x - Math.floor(x);
}

function ConfettiParticle({ index }: { index: number }) {
  const c = useThemeColors();
  const progress = useSharedValue(0);

  const palette = [
    c.primary,
    c.primaryLight,
    c.secondary,
    c.accent,
    c.warning,
    c.protein,
    c.carbs,
  ];
  const color = palette[index % palette.length];

  useEffect(() => {
    progress.value = 0;
    progress.value = withTiming(1, {
      duration: 1100 + Math.round(rand(index, 7) * 400),
      easing: Easing.out(Easing.quad),
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const animatedStyle = useConfettiStyle(index, progress);

  return (
    <Animated.View
      style={[
        styles.particle,
        { backgroundColor: color, pointerEvents: "none" },
        animatedStyle,
      ]}
    />
  );
}


function useConfettiStyle(index: number, progress: SharedValue<number>) {
  return useAnimatedStyle(() => {
    const p = progress.value;
    const spreadX = (rand(index, 1) - 0.5) * 320;
    const wobble = Math.sin(p * Math.PI * (2 + rand(index, 2) * 3)) * 14;
    const rise = -90 - rand(index, 3) * 70;
    const fall = 300 + rand(index, 4) * 160;
    const y = rise * Math.sin(Math.min(p * 2, 1) * (Math.PI / 2)) + fall * p * p;
    const rotate = (rand(index, 5) > 0.5 ? 1 : -1) * (360 + rand(index, 6) * 360) * p;
    const opacity = p < 0.65 ? 1 : Math.max(0, 1 - (p - 0.65) / 0.35);
    return {
      transform: [
        { translateX: spreadX * p + wobble },
        { translateY: y },
        { rotate: `${rotate}deg` },
      ],
      opacity,
    };
  });
}

/**
 * Full-screen meal-logged celebration: confetti burst + recap card.
 * Mounted once at the tab layout level; driven by useCelebrationStore.
 * Honors reduced motion (card fades in, no confetti).
 */
export function CelebrationOverlay() {
  const celebration = useCelebrationStore((s) => s.celebration);
  const clear = useCelebrationStore((s) => s.clear);
  const reduced = useReducedMotion();

  useEffect(() => {
    if (!celebration) return;
    const timer = setTimeout(clear, AUTO_DISMISS_MS);
    return () => clearTimeout(timer);
  }, [celebration, clear]);

  const particles = useMemo(
    () => Array.from({ length: PARTICLE_COUNT }, (_, i) => i),
    [],
  );

  if (!celebration) return null;

  return (
    <Animated.View
      entering={reduced ? undefined : FadeIn.duration(120)}
      style={styles.overlay}
      accessibilityLiveRegion="assertive"
      accessibilityLabel={`Meal logged: ${celebration.title}`}
    >
      <TouchableOpacity
        activeOpacity={1}
        onPress={clear}
        style={StyleSheet.absoluteFill}
        accessibilityRole="button"
        accessibilityLabel="Dismiss celebration"
      />
      {!reduced &&
        particles.map((i) => <ConfettiParticle key={`${celebration.title}-${i}`} index={i} />)}
      <View style={styles.center}>
        <View style={[styles.cardWrapper, { pointerEvents: "box-none" }]}>
          <RecapCard
            title={celebration.title}
            subtitle={celebration.subtitle}
            photoUri={celebration.photoUri}
            kcal={celebration.kcal ?? null}
          />
        </View>
      </View>
    </Animated.View>
  );
}

function RecapCard({
  title,
  subtitle,
  photoUri,
  kcal,
}: {
  title: string;
  subtitle?: string;
  photoUri?: string;
  kcal?: number | null;
}) {
  const c = useThemeColors();
  return (
    <View
      style={[
        styles.card,
        { backgroundColor: c.card },
      ]}
    >
      <View
        style={{
          width: 64,
          height: 64,
          borderRadius: 32,
          overflow: "hidden",
          backgroundColor: c.primaryBg,
          borderWidth: 2,
          borderColor: c.primaryBorder,
          alignItems: "center",
          justifyContent: "center",
          marginBottom: spacing.sm,
        }}
      >
        {photoUri ? (
          <Image source={{ uri: photoUri }} style={{ width: 60, height: 60 }} contentFit="cover" />
        ) : (
          <Ionicons name="checkmark-circle" size={34} color={c.primary} />
        )}
      </View>
      <Text style={{ fontSize: 18, fontWeight: "800", color: c.text }}>{title}</Text>
      {subtitle ? (
        <Text
          style={{
            fontSize: 13,
            color: c.textSecondary,
            marginTop: 2,
            textAlign: "center",
          }}
          numberOfLines={2}
        >
          {subtitle}
        </Text>
      ) : null}
      {kcal != null && Number.isFinite(kcal) ? (
        <View
          style={{
            marginTop: spacing.sm,
            backgroundColor: c.primaryBg,
            borderRadius: radius.full,
            paddingHorizontal: spacing.md,
          }}
        >
          <Text style={{ fontSize: 12, fontWeight: "700", color: c.primary }}>
            {Math.round(kcal)} kcal
          </Text>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  overlay: {
    ...StyleSheet.absoluteFillObject,
    zIndex: 1000,
    ...(Platform.OS === "web" ? {} : { elevation: 1000 }),
  },
  center: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "center",
    justifyContent: "center",
  },
  cardWrapper: {},
  card: {
    alignItems: "center",
    borderRadius: radius.lg,
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.lg,
    maxWidth: 280,
    ...(Platform.OS === "web"
      ? ({ boxShadow: "0 12px 32px rgba(0,0,0,0.18)" } as any)
      : {
          shadowOffset: { width: 0, height: 8 },
          shadowOpacity: 0.18,
          shadowRadius: 24,
          elevation: 12,
        }),
  },
  particle: {
    position: "absolute",
    top: "38%",
    left: "50%",
    width: 9,
    height: 14,
    borderRadius: 3,
    marginLeft: -4,
  },
});
