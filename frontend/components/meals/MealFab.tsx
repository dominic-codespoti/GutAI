import { useRef, useState } from "react";
import {
  Animated,
  Pressable,
  Text,
  TouchableOpacity,
  View,
  type ViewStyle,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { radius, spacing } from "../../src/utils/theme";
import { useThemeColors, useThemeShadow } from "../../src/stores/theme";
import * as haptics from "../../src/utils/haptics";

export type MealFabAction = {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  color: string;
  onPress: () => void;
};

type MealFabProps = {
  actions: MealFabAction[];
  bottom?: number;
  right?: number;
};

const MAIN_FAB_SIZE = 58;
const ACTION_BUTTON_SIZE = 46;
const ACTION_ROW_HEIGHT = 54;
const ACTION_ROW_GAP = 10;
const MENU_BOTTOM_OFFSET = MAIN_FAB_SIZE + 16;
const ACTION_RIGHT_OFFSET = (MAIN_FAB_SIZE - ACTION_BUTTON_SIZE) / 2;

export function MealFab({ actions, bottom = 28, right = 24 }: MealFabProps) {
  const colors = useThemeColors();
  const { shadow, shadowMd } = useThemeShadow();

  const [open, setOpen] = useState(false);
  const [renderMenu, setRenderMenu] = useState(false);

  const anim = useRef(new Animated.Value(0)).current;

  const backdropOpacity = anim.interpolate({
    inputRange: [0, 1],
    outputRange: [0, 0.35],
    extrapolate: "clamp",
  });

  const menuTranslateY = anim.interpolate({
    inputRange: [0, 1],
    outputRange: [12, 0],
    extrapolate: "clamp",
  });

  const menuScale = anim.interpolate({
    inputRange: [0, 1],
    outputRange: [0.96, 1],
    extrapolate: "clamp",
  });

  const iconRotate = anim.interpolate({
    inputRange: [0, 1],
    outputRange: ["0deg", "45deg"],
    extrapolate: "clamp",
  });

  const animateOpen = () => {
    anim.stopAnimation();
    Animated.spring(anim, {
      toValue: 1,
      useNativeDriver: true,
      friction: 7,
      tension: 90,
    }).start();
  };

  const animateClose = (onComplete?: () => void) => {
    anim.stopAnimation();
    Animated.timing(anim, {
      toValue: 0,
      duration: 140,
      useNativeDriver: true,
    }).start(({ finished }) => {
      if (finished) {
        setRenderMenu(false);
        onComplete?.();
      }
    });
  };

  const openMenu = () => {
    setRenderMenu(true);
    setOpen(true);
    animateOpen();
  };

  const closeMenu = (onComplete?: () => void) => {
    setOpen(false);
    animateClose(onComplete);
  };

  const toggle = () => {
    haptics.medium();
    if (open) {
      closeMenu();
    } else {
      openMenu();
    }
  };

  const handleActionPress = (action: MealFabAction) => {
    haptics.medium();
    closeMenu(() => {
      action.onPress();
    });
  };

  return (
    <>
      {renderMenu && (
        <Pressable
          onPress={() => closeMenu()}
          accessibilityRole="button"
          accessibilityLabel="Close menu"
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
          }}
        >
          <Animated.View
            pointerEvents="none"
            style={{
              flex: 1,
              backgroundColor: colors.overlay,
              opacity: backdropOpacity,
            }}
          />
        </Pressable>
      )}

      {renderMenu && (
        <Animated.View
          pointerEvents={open ? "auto" : "none"}
          style={{
            position: "absolute",
            right: right + ACTION_RIGHT_OFFSET,
            bottom: bottom + MENU_BOTTOM_OFFSET,
            opacity: anim,
            transform: [{ translateY: menuTranslateY }, { scale: menuScale }],
          }}
        >
          <View style={{ gap: ACTION_ROW_GAP, alignItems: "flex-end" }}>
            {actions.map((action, index) => (
              <MealFabActionButton
                key={action.label}
                action={action}
                index={index}
                total={actions.length}
                open={open}
                anim={anim}
                shadow={shadow}
                buttonShadow={shadowMd}
                colors={{
                  card: colors.card,
                  border: colors.border,
                  text: colors.text,
                  textOnPrimary: colors.textOnPrimary,
                }}
                onPress={() => handleActionPress(action)}
              />
            ))}
          </View>
        </Animated.View>
      )}

      <TouchableOpacity
        activeOpacity={0.88}
        onPress={toggle}
        accessibilityRole="button"
        accessibilityLabel={open ? "Close menu" : "Add"}
        accessibilityState={{ expanded: open }}
        style={{
          position: "absolute",
          bottom,
          right,
          width: MAIN_FAB_SIZE,
          height: MAIN_FAB_SIZE,
          borderRadius: MAIN_FAB_SIZE / 2,
          backgroundColor: colors.primary,
          alignItems: "center",
          justifyContent: "center",
          ...shadowMd,
          elevation: 8,
        }}
      >
        <Animated.View style={{ transform: [{ rotate: iconRotate }] }}>
          <Ionicons name="add" size={31} color={colors.textOnPrimary} />
        </Animated.View>
      </TouchableOpacity>
    </>
  );
}

type MealFabActionButtonProps = {
  action: MealFabAction;
  index: number;
  total: number;
  open: boolean;
  anim: Animated.Value;
  shadow: ViewStyle;
  buttonShadow: ViewStyle;
  colors: {
    card: string;
    border: string;
    text: string;
    textOnPrimary: string;
  };
  onPress: () => void;
};

function MealFabActionButton({
  action,
  index,
  total,
  open,
  anim,
  shadow,
  buttonShadow,
  colors,
  onPress,
}: MealFabActionButtonProps) {
  const translateY = anim.interpolate({
    inputRange: [0, 1],
    outputRange: [8 + (total - index) * 4, 0],
    extrapolate: "clamp",
  });

  const translateX = anim.interpolate({
    inputRange: [0, 1],
    outputRange: [12, 0],
    extrapolate: "clamp",
  });

  const scale = anim.interpolate({
    inputRange: [0, 1],
    outputRange: [0.94, 1],
    extrapolate: "clamp",
  });

  return (
    <Animated.View
      style={{
        opacity: anim,
        transform: [{ translateX }, { translateY }, { scale }],
      }}
    >
      <TouchableOpacity
        onPress={onPress}
        accessibilityRole="button"
        accessibilityLabel={action.label}
        activeOpacity={0.86}
        style={{
          minHeight: ACTION_ROW_HEIGHT,
          flexDirection: "row",
          alignItems: "center",
          justifyContent: "flex-end",
          gap: spacing.sm,
        }}
      >
        <View
          style={{
            minHeight: 40,
            maxWidth: 220,
            paddingVertical: 9,
            paddingHorizontal: 14,
            borderRadius: radius.full,
            backgroundColor: colors.card,
            borderWidth: 1,
            borderColor: colors.border,
            justifyContent: "center",
            ...(open ? shadow : null),
          }}
        >
          <Text
            numberOfLines={1}
            style={{
              fontSize: 13,
              lineHeight: 17,
              fontWeight: "700",
              letterSpacing: 0.1,
              color: colors.text,
            }}
          >
            {action.label}
          </Text>
        </View>

        <View
          style={{
            width: ACTION_BUTTON_SIZE,
            height: ACTION_BUTTON_SIZE,
            borderRadius: ACTION_BUTTON_SIZE / 2,
            backgroundColor: action.color,
            alignItems: "center",
            justifyContent: "center",
            ...(open ? buttonShadow : null),
            elevation: open ? 7 : 0,
          }}
        >
          <Ionicons name={action.icon} size={22} color={colors.textOnPrimary} />
        </View>
      </TouchableOpacity>
    </Animated.View>
  );
}
