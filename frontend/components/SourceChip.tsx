import React from "react";
import {
  TouchableOpacity,
  Text,
  Linking,
  ViewStyle,
  TextStyle,
} from "react-native";
import { colors, radius, spacing } from "../src/utils/theme";

interface SourceChipProps {
  source: string | null;
  url: string | null;
  style?: ViewStyle;
}

export function SourceChip({ source, url, style }: SourceChipProps) {
  if (!source) return null;

  const handlePress = () => {
    if (url) {
      Linking.openURL(url).catch((err) =>
        console.error("Failed to open source URL", err),
      );
    }
  };

  const isClickable = !!url;

  return (
    <TouchableOpacity
      onPress={handlePress}
      disabled={!isClickable}
      activeOpacity={0.7}
      style={[
        {
          backgroundColor: colors.borderLight,
          borderWidth: 1,
          borderColor: colors.border,
          borderRadius: radius.full,
          paddingHorizontal: spacing.sm,
          paddingVertical: 2,
          alignSelf: "flex-start",
        },
        isClickable && {
          backgroundColor: colors.secondaryBg,
          borderColor: colors.secondary,
        },
        style,
      ]}
    >
      <Text
        style={{
          fontSize: 11,
          fontWeight: "600",
          color: isClickable ? colors.secondary : colors.textMuted,
        }}
      >
        {source}
      </Text>
    </TouchableOpacity>
  );
}
