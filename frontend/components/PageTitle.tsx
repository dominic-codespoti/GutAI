import { View, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { spacing, radius } from "../src/utils/theme";
import { useThemeColors, useThemeFonts } from "../src/stores/theme";

type PageTitleProps = {
  title: string;
  eyebrow?: string;
  subtitle?: string;
};

export function PageTitle({ title, eyebrow, subtitle }: PageTitleProps) {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const router = useRouter();

  return (
    <View
      style={{
        flexDirection: "row",
        alignItems: "flex-start",
        justifyContent: "space-between",
        marginBottom: spacing.lg,
      }}
    >
      <View style={{ flex: 1, paddingRight: spacing.md }}>
        {eyebrow ? (
          <Text style={{ ...fonts.caption, marginBottom: 2 }}>{eyebrow}</Text>
        ) : null}
        <Text style={fonts.h1} accessibilityRole="header">
          {title}
        </Text>
        {subtitle ? (
          <Text style={{ ...fonts.caption, marginTop: 4 }}>{subtitle}</Text>
        ) : null}
      </View>
      <TouchableOpacity
        onPress={() => router.push("/(tabs)/profile")}
        accessibilityRole="button"
        accessibilityLabel="Profile settings"
        hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
        style={{ padding: spacing.sm, borderRadius: radius.full }}
      >
        <Ionicons name="settings-outline" size={22} color={colors.textMuted} />
      </TouchableOpacity>
    </View>
  );
}
