import React, {
  PropsWithChildren,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import {
  Animated,
  BackHandler,
  Easing,
  Keyboard,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  StyleProp,
  StyleSheet,
  useWindowDimensions,
  View,
  ViewStyle,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { radius, spacing } from "../src/utils/theme";
import { useThemeColors } from "../src/stores/theme";

type PercentageString = `${number}%`;

interface BottomSheetProps extends PropsWithChildren {
  visible: boolean;
  onClose: () => void;

  maxHeight?: PercentageString | number;

  paddingTop?: number;
  paddingHorizontal?: number;
  paddingBottom?: number;

  fillHeight?: boolean;

  dismissOnBackdropPress?: boolean;
  dismissOnHardwareBackPress?: boolean;

  respectTopInset?: boolean;
  respectBottomInset?: boolean;

  sheetStyle?: StyleProp<ViewStyle>;
  contentStyle?: StyleProp<ViewStyle>;

  testID?: string;
}

const OPEN_DURATION_MS = 220;
const CLOSE_DURATION_MS = 180;

export function BottomSheet({
  visible,
  onClose,
  children,
  maxHeight = "85%",
  paddingTop = spacing.xxl,
  paddingHorizontal = spacing.xxl,
  paddingBottom = spacing.lg,
  fillHeight = false,
  dismissOnBackdropPress = true,
  dismissOnHardwareBackPress = true,
  respectTopInset = true,
  respectBottomInset = true,
  sheetStyle,
  contentStyle,
  testID = "bottom-sheet",
}: BottomSheetProps) {
  const colors = useThemeColors();
  const insets = useSafeAreaInsets();
  const { height: windowHeight } = useWindowDimensions();

  const translateY = useRef(new Animated.Value(windowHeight)).current;
  const backdropOpacity = useRef(new Animated.Value(0)).current;

  const [isMounted, setIsMounted] = useState(visible);

  const bottomInset = respectBottomInset ? insets.bottom : 0;
  const topInset = respectTopInset ? insets.top : 0;

  const closeSheet = useCallback(() => {
    Keyboard.dismiss();
    onClose();
  }, [onClose]);

  const availableHeight = useMemo(() => {
    return Math.max(windowHeight - topInset, 0);
  }, [windowHeight, topInset]);

  const resolvedMaxHeight = useMemo(() => {
    if (fillHeight) {
      return undefined;
    }

    if (typeof maxHeight === "number") {
      return Math.min(maxHeight, availableHeight);
    }

    const percentage = Number(maxHeight.replace("%", ""));

    if (Number.isNaN(percentage)) {
      return availableHeight * 0.85;
    }

    const clampedPercentage = Math.min(Math.max(percentage, 0), 100);

    return (availableHeight * clampedPercentage) / 100;
  }, [availableHeight, fillHeight, maxHeight]);

  const sheetContainerStyle = useMemo<StyleProp<ViewStyle>>(
    () => [
      styles.sheet,
      {
        backgroundColor: colors.card,
        borderTopLeftRadius: radius.xl,
        borderTopRightRadius: radius.xl,

        paddingTop,
        paddingHorizontal,

        /**
         * Critical bit:
         * Keep the caller's intended padding, then add the safe-area bottom.
         */
        paddingBottom: paddingBottom + bottomInset,

        maxHeight: resolvedMaxHeight,
      },
      fillHeight && {
        height: availableHeight,
      },
      sheetStyle,
    ],
    [
      availableHeight,
      bottomInset,
      colors.card,
      fillHeight,
      paddingBottom,
      paddingHorizontal,
      paddingTop,
      resolvedMaxHeight,
      sheetStyle,
    ],
  );

  const animatedSheetStyle = useMemo<StyleProp<ViewStyle>>(
    () => [
      styles.animatedSheet,
      {
        transform: [{ translateY }],
      },
    ],
    [translateY],
  );

  const animatedBackdropStyle = useMemo<StyleProp<ViewStyle>>(
    () => [
      StyleSheet.absoluteFill,
      {
        opacity: backdropOpacity,
      },
    ],
    [backdropOpacity],
  );

  useEffect(() => {
    let cancelled = false;

    const stopAnimations = () => {
      translateY.stopAnimation();
      backdropOpacity.stopAnimation();
    };

    if (visible) {
      setIsMounted(true);

      stopAnimations();

      translateY.setValue(windowHeight);
      backdropOpacity.setValue(0);

      Animated.parallel([
        Animated.timing(backdropOpacity, {
          toValue: 1,
          duration: OPEN_DURATION_MS,
          easing: Easing.out(Easing.cubic),
          useNativeDriver: true,
        }),
        Animated.spring(translateY, {
          toValue: 0,
          damping: 30,
          stiffness: 320,
          mass: 0.9,
          overshootClamping: true,
          useNativeDriver: true,
        }),
      ]).start();
    } else if (isMounted) {
      stopAnimations();

      Animated.parallel([
        Animated.timing(backdropOpacity, {
          toValue: 0,
          duration: CLOSE_DURATION_MS,
          easing: Easing.out(Easing.cubic),
          useNativeDriver: true,
        }),
        Animated.timing(translateY, {
          toValue: windowHeight,
          duration: CLOSE_DURATION_MS,
          easing: Easing.in(Easing.cubic),
          useNativeDriver: true,
        }),
      ]).start(({ finished }) => {
        if (finished && !cancelled) {
          setIsMounted(false);
        }
      });
    }

    return () => {
      cancelled = true;
      stopAnimations();
    };
  }, [
    visible,
    isMounted,
    windowHeight,
    translateY,
    backdropOpacity,
  ]);

  useEffect(() => {
    if (
      !visible ||
      Platform.OS !== "android" ||
      !dismissOnHardwareBackPress
    ) {
      return;
    }

    const subscription = BackHandler.addEventListener(
      "hardwareBackPress",
      () => {
        closeSheet();
        return true;
      },
    );

    return () => {
      subscription.remove();
    };
  }, [visible, dismissOnHardwareBackPress, closeSheet]);

  if (!isMounted) {
    return null;
  }

  const content = (
    <View
      style={[
        styles.root,
        /**
         * Critical for fill-height sheets:
         * prevent the sheet from rendering under the status bar / dynamic island.
         */
        {
          paddingTop: topInset,
        },
      ]}
      pointerEvents="box-none"
      accessibilityViewIsModal
      testID={testID}
    >
      <Animated.View style={animatedBackdropStyle}>
        <Pressable
          style={[
            styles.backdrop,
            {
              backgroundColor: colors.overlay,
            },
          ]}
          disabled={!dismissOnBackdropPress}
          onPress={dismissOnBackdropPress ? closeSheet : undefined}
          accessibilityRole="button"
          accessibilityLabel="Close bottom sheet"
          testID={`${testID}-backdrop`}
        />
      </Animated.View>

      <Animated.View style={animatedSheetStyle}>
        <View style={sheetContainerStyle}>
          <View
            style={[
              styles.handle,
              {
                backgroundColor: colors.borderLight,
              },
            ]}
            accessibilityElementsHidden
            importantForAccessibility="no"
          />

          <View style={contentStyle}>{children}</View>
        </View>
      </Animated.View>
    </View>
  );

  return (
    <Modal
      visible={isMounted}
      transparent
      animationType="none"
      statusBarTranslucent
      presentationStyle="overFullScreen"
      onRequestClose={closeSheet}
    >
      {Platform.OS === "web" ? (
        content
      ) : (
        <KeyboardAvoidingView
          style={styles.keyboardAvoidingView}
          behavior={Platform.OS === "ios" ? "padding" : undefined}
          pointerEvents="box-none"
        >
          {content}
        </KeyboardAvoidingView>
      )}
    </Modal>
  );
}

const styles = StyleSheet.create({
  keyboardAvoidingView: {
    flex: 1,
  },

  root: {
    ...StyleSheet.absoluteFillObject,
    justifyContent: "flex-end",
  },

  backdrop: {
    flex: 1,
  },

  animatedSheet: {
    width: "100%",
  },

  sheet: {
    width: "100%",
  },

  handle: {
    width: 36,
    height: 4,
    borderRadius: 2,
    alignSelf: "center",
    marginBottom: spacing.lg,
  },
});
