import React from "react";
import { View, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useThemeColors } from "../src/stores/theme";
import { radius, spacing } from "../src/utils/theme";

type EmptyStateProps = {
  /** Icon shown inside the disc when no illustration is given */
  icon?: keyof typeof Ionicons.glyphMap;
  /** Optional full-color SVG illustration rendered instead of the icon disc */
  illustration?: React.ReactNode;
  title: string;
  body?: string;
  actionLabel?: string;
  onAction?: () => void;
  /** Smaller footprint for inline sections inside scrolling screens */
  compact?: boolean;
};

/**
 * Shared themed empty state: illustrated art (or icon disc) + title/body +
 * optional action. Replaces ad-hoc plain-text empty copies across tabs.
 */
export function EmptyState({
  icon = "leaf-outline",
  illustration,
  title,
  body,
  actionLabel,
  onAction,
  compact = false,
}: EmptyStateProps) {
  const c = useThemeColors();
  const pad = compact ? spacing.lg : spacing.xxl;

  return (
    <View
      style={{
        alignItems: "center",
        paddingVertical: pad,
        paddingHorizontal: spacing.lg,
      }}
      accessibilityRole="text"
      accessibilityLabel={title}
    >
      {illustration ? (
        <View style={{ marginBottom: spacing.md }}>{illustration}</View>
      ) : (
        <View
          style={{
            width: compact ? 56 : 88,
            height: compact ? 56 : 88,
            borderRadius: compact ? 28 : 44,
            backgroundColor: c.primaryBg,
            borderWidth: 1,
            borderColor: c.primaryBorder,
            alignItems: "center",
            justifyContent: "center",
            marginBottom: spacing.md,
          }}
        >
          <View
            style={{
              width: compact ? 36 : 56,
              height: compact ? 36 : 56,
              borderRadius: compact ? 18 : 28,
              backgroundColor: c.card,
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            <Ionicons name={icon} size={compact ? 20 : 30} color={c.primary} />
          </View>
        </View>
      )}
      <Text
        style={{
          fontSize: compact ? 15 : 17,
          fontWeight: "700",
          color: c.text,
          textAlign: "center",
        }}
      >
        {title}
      </Text>
      {body ? (
        <Text
          style={{
            fontSize: 13,
            color: c.textMuted,
            textAlign: "center",
            marginTop: spacing.xs,
            maxWidth: 280,
          }}
        >
          {body}
        </Text>
      ) : null}
      {actionLabel && onAction ? (
        <TouchableOpacity
          onPress={onAction}
          accessibilityRole="button"
          accessibilityLabel={actionLabel}
          style={{
            marginTop: spacing.md,
            backgroundColor: c.primary,
            borderRadius: radius.md,
            paddingHorizontal: spacing.lg,
            paddingVertical: spacing.sm + 2,
          }}
        >
          <Text
            style={{
              color: c.textOnPrimary,
              fontWeight: "700",
              fontSize: 14,
            }}
          >
            {actionLabel}
          </Text>
        </TouchableOpacity>
      ) : null}
    </View>
  );
}
