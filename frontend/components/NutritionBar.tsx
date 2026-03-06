import { View, Text } from "react-native";
import { useThemeColors } from "../src/stores/theme";

interface NutritionBarProps {
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  subtitle?: string;
}

export function NutritionBar({
  calories,
  proteinG,
  carbsG,
  fatG,
  subtitle,
}: NutritionBarProps) {
  const colors = useThemeColors();
  return (
    <View>
      <View
        style={{
          flexDirection: "row",
          justifyContent: "space-around",
          backgroundColor: colors.bg,
          borderRadius: 8,
          paddingVertical: 10,
          marginBottom: subtitle ? 4 : 12,
        }}
      >
        <View style={{ alignItems: "center" }}>
          <Text style={{ fontSize: 16, fontWeight: "700", color: colors.text }}>
            {calories}
          </Text>
          <Text style={{ fontSize: 11, color: colors.textMuted }}>cal</Text>
        </View>
        <View style={{ alignItems: "center" }}>
          <Text
            style={{ fontSize: 16, fontWeight: "700", color: colors.protein }}
          >
            {proteinG}g
          </Text>
          <Text style={{ fontSize: 11, color: colors.textMuted }}>protein</Text>
        </View>
        <View style={{ alignItems: "center" }}>
          <Text
            style={{ fontSize: 16, fontWeight: "700", color: colors.carbs }}
          >
            {carbsG}g
          </Text>
          <Text style={{ fontSize: 11, color: colors.textMuted }}>carbs</Text>
        </View>
        <View style={{ alignItems: "center" }}>
          <Text style={{ fontSize: 16, fontWeight: "700", color: colors.fat }}>
            {fatG}g
          </Text>
          <Text style={{ fontSize: 11, color: colors.textMuted }}>fat</Text>
        </View>
      </View>
      {subtitle && (
        <Text
          style={{
            fontSize: 11,
            color: colors.textMuted,
            textAlign: "center",
            marginBottom: 12,
          }}
        >
          {subtitle}
        </Text>
      )}
    </View>
  );
}
