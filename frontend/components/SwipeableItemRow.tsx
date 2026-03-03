import { useRef, useCallback } from "react";
import {
  View,
  Text,
  Animated,
  TouchableOpacity,
  I18nManager,
} from "react-native";
import { Swipeable } from "react-native-gesture-handler";
import { Ionicons } from "@expo/vector-icons";
import { colors, radius } from "../src/utils/theme";

interface Props {
  children: React.ReactNode;
  onSwap: () => void;
  onDelete: () => void;
}

export function SwipeableItemRow({ children, onSwap, onDelete }: Props) {
  const swipeableRef = useRef<Swipeable>(null);

  const close = useCallback(() => {
    swipeableRef.current?.close();
  }, []);

  const renderLeftActions = (
    _progress: Animated.AnimatedInterpolation<number>,
    dragX: Animated.AnimatedInterpolation<number>,
  ) => {
    const scale = dragX.interpolate({
      inputRange: [0, 60],
      outputRange: [0.5, 1],
      extrapolate: "clamp",
    });
    return (
      <TouchableOpacity
        onPress={() => {
          close();
          onSwap();
        }}
        activeOpacity={0.8}
        style={{
          backgroundColor: colors.primary,
          justifyContent: "center",
          alignItems: "center",
          width: 60,
          borderTopLeftRadius: radius.sm,
          borderBottomLeftRadius: radius.sm,
        }}
      >
        <Animated.View
          style={{
            alignItems: "center",
            transform: [{ scale }],
          }}
        >
          <Ionicons name="swap-horizontal" size={22} color="#fff" />
        </Animated.View>
      </TouchableOpacity>
    );
  };

  const renderRightActions = (
    _progress: Animated.AnimatedInterpolation<number>,
    dragX: Animated.AnimatedInterpolation<number>,
  ) => {
    const scale = dragX.interpolate({
      inputRange: [-60, 0],
      outputRange: [1, 0.5],
      extrapolate: "clamp",
    });
    return (
      <TouchableOpacity
        onPress={() => {
          close();
          onDelete();
        }}
        activeOpacity={0.8}
        style={{
          backgroundColor: colors.danger,
          justifyContent: "center",
          alignItems: "center",
          width: 60,
          borderTopRightRadius: radius.sm,
          borderBottomRightRadius: radius.sm,
        }}
      >
        <Animated.View
          style={{
            alignItems: "center",
            transform: [{ scale }],
          }}
        >
          <Ionicons name="trash-outline" size={22} color="#fff" />
        </Animated.View>
      </TouchableOpacity>
    );
  };

  return (
    <Swipeable
      ref={swipeableRef}
      renderLeftActions={renderLeftActions}
      renderRightActions={renderRightActions}
      leftThreshold={40}
      rightThreshold={40}
      friction={2}
      overshootLeft={false}
      overshootRight={false}
    >
      {children}
    </Swipeable>
  );
}
