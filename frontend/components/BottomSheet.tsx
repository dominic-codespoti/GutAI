import { useEffect, useRef, useState } from "react";
import {
  View,
  Pressable,
  KeyboardAvoidingView,
  Platform,
  Keyboard,
  Animated,
  Dimensions,
  BackHandler,
  StyleSheet,
} from "react-native";
import { radius, spacing } from "../src/utils/theme";
import { useThemeColors } from "../src/stores/theme";

interface BottomSheetProps {
  visible: boolean;
  onClose: () => void;
  children: React.ReactNode;
  maxHeight?: string;
}

const SCREEN_HEIGHT = Dimensions.get("window").height;

export function BottomSheet({
  visible,
  onClose,
  children,
  maxHeight = "85%",
}: BottomSheetProps) {
  const colors = useThemeColors();
  const translateY = useRef(new Animated.Value(SCREEN_HEIGHT));
  const opacity = useRef(new Animated.Value(0));
  const [rendered, setRendered] = useState(visible);

  useEffect(() => {
    if (visible) {
      setRendered(true);
      // Reset values before animating in
      translateY.current.setValue(SCREEN_HEIGHT);
      opacity.current.setValue(0);
      Animated.parallel([
        Animated.timing(opacity.current, {
          toValue: 1,
          duration: 250,
          useNativeDriver: true,
        }),
        Animated.spring(translateY.current, {
          toValue: 0,
          useNativeDriver: true,
          damping: 28,
          stiffness: 300,
        }),
      ]).start();
    } else if (rendered) {
      Animated.parallel([
        Animated.timing(opacity.current, {
          toValue: 0,
          duration: 200,
          useNativeDriver: true,
        }),
        Animated.timing(translateY.current, {
          toValue: SCREEN_HEIGHT,
          duration: 200,
          useNativeDriver: true,
        }),
      ]).start(() => setRendered(false));
    }
  }, [visible]);

  // Handle Android back button
  useEffect(() => {
    if (!visible || Platform.OS !== "android") return;
    const handler = BackHandler.addEventListener("hardwareBackPress", () => {
      onClose();
      return true;
    });
    return () => handler.remove();
  }, [visible, onClose]);

  if (!visible && !rendered) return null;

  return (
    <View style={StyleSheet.absoluteFill} pointerEvents="box-none">
      <KeyboardAvoidingView
        style={StyleSheet.absoluteFill}
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        keyboardVerticalOffset={0}
        pointerEvents="box-none"
        accessibilityViewIsModal={true}
      >
        {/* Backdrop */}
        <Animated.View
          style={[StyleSheet.absoluteFill, { opacity: opacity.current }]}
        >
          <Pressable
            style={{ flex: 1, backgroundColor: colors.overlay }}
            onPress={() => {
              Keyboard.dismiss();
              onClose();
            }}
            accessibilityRole="button"
            accessibilityLabel="Close sheet"
          />
        </Animated.View>

        {/* Sheet */}
        <Animated.View
          style={{
            position: "absolute",
            bottom: 0,
            left: 0,
            right: 0,
            transform: [{ translateY: translateY.current }],
            backgroundColor: colors.card,
            borderTopLeftRadius: radius.xl,
            borderTopRightRadius: radius.xl,
            padding: spacing.xxl,
            maxHeight: maxHeight as any,
          }}
        >
          <View
            style={{
              width: 36,
              height: 4,
              borderRadius: 2,
              backgroundColor: colors.borderLight,
              alignSelf: "center",
              marginBottom: spacing.lg,
            }}
            accessibilityRole="button"
            accessibilityLabel="Close"
          />
          {children}
        </Animated.View>
      </KeyboardAvoidingView>
    </View>
  );
}
