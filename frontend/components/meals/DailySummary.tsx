import { View, Text } from "react-native";
import { colors, shadow, radius, spacing, fonts } from "../../src/utils/theme";
import type { DailyNutritionSummary as Summary } from "../../src/types";

interface Props {
  summary: Summary;
}

export function DailySummary({ summary }: Props) {
  return (
    <View
      style={{
        backgroundColor: colors.card,
        borderRadius: radius.md,
        padding: spacing.lg,
        marginBottom: spacing.lg,
        ...shadow,
      }}
    >
      <Text style={{ ...fonts.h4, marginBottom: spacing.md }}>
        Daily Summary
      </Text>
      <View style={{ flexDirection: "row", justifyContent: "space-around" }}>
        <View style={{ alignItems: "center" }}>
          <Text
            style={{ fontSize: 20, fontWeight: "700", color: colors.primary }}
          >
            {Math.round(summary.totalCalories)}
          </Text>
          <Text style={fonts.small}>/ {summary.calorieGoal} cal</Text>
        </View>
        <View style={{ alignItems: "center" }}>
          <Text
            style={{ fontSize: 17, fontWeight: "600", color: colors.protein }}
          >
            {Math.round(summary.totalProteinG)}g
          </Text>
          <Text style={fonts.small}>protein</Text>
        </View>
        <View style={{ alignItems: "center" }}>
          <Text
            style={{ fontSize: 17, fontWeight: "600", color: colors.carbs }}
          >
            {Math.round(summary.totalCarbsG)}g
          </Text>
          <Text style={fonts.small}>carbs</Text>
        </View>
        <View style={{ alignItems: "center" }}>
          <Text style={{ fontSize: 17, fontWeight: "600", color: colors.fat }}>
            {Math.round(summary.totalFatG)}g
          </Text>
          <Text style={fonts.small}>fat</Text>
        </View>
      </View>
    </View>
  );
}
