import { useEffect, useRef } from "react";
import { View, Text, Animated, TouchableOpacity, Platform } from "react-native";
import { useToastStore, type ToastType } from "../src/stores/toast";
import { useColorScheme, useThemeShadow } from "../src/stores/theme";
import { toastColors } from "../src/utils/theme";

const TOAST_ICONS: Record<ToastType, string> = {
  error: "✕",
  success: "✓",
  info: "ℹ",
};

function ToastItem({
  id,
  message,
  type,
}: {
  id: number;
  message: string;
  type: ToastType;
}) {
  const dismiss = useToastStore((s) => s.dismiss);
  const scheme = useColorScheme();
  const opacity = useRef(new Animated.Value(0)).current;
  const translateY = useRef(new Animated.Value(-20)).current;
  const { shadowMd } = useThemeShadow();
  const colors = toastColors[scheme][type];
  const icon = TOAST_ICONS[type];
  useEffect(() => {
    Animated.parallel([
      Animated.timing(opacity, {
        toValue: 1,
        duration: 200,
        useNativeDriver: Platform.OS !== "web",
      }),
      Animated.timing(translateY, {
        toValue: 0,
        duration: 200,
        useNativeDriver: Platform.OS !== "web",
      }),
    ]).start();

    const timer = setTimeout(() => {
      Animated.timing(opacity, {
        toValue: 0,
        duration: 300,
        useNativeDriver: Platform.OS !== "web",
      }).start();
    }, 3500);
    return () => clearTimeout(timer);
  }, []);

  return (
    <Animated.View
      style={{ opacity, transform: [{ translateY }], marginBottom: 8 }}
      accessibilityRole="alert"
      accessibilityLiveRegion="assertive"
    >
      <TouchableOpacity
        activeOpacity={0.8}
        onPress={() => dismiss(id)}
        accessibilityRole="button"
        accessibilityLabel="Dismiss notification"
        style={{
          flexDirection: "row",
          alignItems: "center",
          backgroundColor: colors.bg,
          borderWidth: 1,
          borderColor: colors.border,
          borderRadius: 12,
          paddingHorizontal: 16,
          paddingVertical: 12,
          ...shadowMd,
        }}
      >
        <Text style={{ fontSize: 16, marginRight: 10 }}>{icon}</Text>
        <Text
          style={{ flex: 1, fontSize: 14, color: colors.text, fontWeight: "500" }}
        >
          {message}
        </Text>
        <Text
          style={{ fontSize: 18, color: colors.text, paddingLeft: 8, opacity: 0.5 }}
        >
          ×
        </Text>
      </TouchableOpacity>
    </Animated.View>
  );
}

export default function ToastContainer() {
  const toasts = useToastStore((s) => s.toasts);

  if (toasts.length === 0) return null;

  return (
    <View
      style={{
        position: "absolute",
        top: Platform.OS === "web" ? 16 : 56,
        left: 16,
        right: 16,
        zIndex: 9999,
        pointerEvents: "box-none",
      }}
    >
      {toasts.map((t) => (
        <ToastItem key={t.id} {...t} />
      ))}
    </View>
  );
}
