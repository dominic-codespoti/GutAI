import { View, Text, Alert, Platform } from "react-native";
import { TouchableOpacity } from "react-native";
import * as Haptics from "expo-haptics";
import { Ionicons } from "@expo/vector-icons";
import { radius, spacing, mealTypeEmoji } from "../../src/utils/theme";
import { useThemeColors, useThemeShadow } from "../../src/stores/theme";
import { confirm } from "../../src/utils/confirm";
import { MealItemRow } from "./MealItemRow";
import type { MealLog } from "../../src/types";

interface Props {
  type: string;
  meals: MealLog[];
  totalCalories: number;
  onEdit: (meal: MealLog) => void;
  onCopy: (meal: MealLog) => void;
  onDelete: (mealId: string) => void;
  onSwapItem: (meal: MealLog, itemIndex: number) => void;
  onDeleteItem: (meal: MealLog, itemIndex: number) => void;
}

export function MealGroup({
  type,
  meals,
  totalCalories,
  onEdit,
  onCopy,
  onDelete,
  onSwapItem,
  onDeleteItem,
}: Props) {
  const colors = useThemeColors();
  const { shadow } = useThemeShadow();
  const handleLongPressMeal = (meal: MealLog) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    if (Platform.OS === "web") {
      onEdit(meal);
      return;
    }
    Alert.alert(
      `${mealTypeEmoji[meal.mealType] ?? "🍽️"} ${meal.mealType}`,
      `${meal.items.length} items · ${meal.totalCalories} cal`,
      [
        { text: "Edit Meal", onPress: () => onEdit(meal) },
        { text: "Copy Meal", onPress: () => onCopy(meal) },
        {
          text: "Delete Meal",
          style: "destructive",
          onPress: () =>
            confirm("Delete Meal", "Are you sure?", () => onDelete(meal.id)),
        },
        { text: "Cancel", style: "cancel" },
      ],
    );
  };

  return (
    <View
      style={{
        backgroundColor: colors.card,
        borderRadius: radius.md,
        padding: spacing.lg,
        marginBottom: spacing.sm,
        ...shadow,
      }}
    >
      {/* Group header */}
      <View
        style={{
          flexDirection: "row",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: 8,
        }}
      >
        <View style={{ flexDirection: "row", alignItems: "center" }}>
          <View
            style={{
              width: 38,
              height: 38,
              borderRadius: radius.sm,
              backgroundColor: colors.primaryBg,
              alignItems: "center",
              justifyContent: "center",
              marginRight: spacing.md,
            }}
          >
            <Text style={{ fontSize: 18 }}>{mealTypeEmoji[type] ?? "🍽️"}</Text>
          </View>
          <View>
            <Text
              style={{ fontSize: 15, fontWeight: "600", color: colors.text }}
            >
              {type}
            </Text>
            <Text
              style={{ fontSize: 12, color: colors.textMuted, marginTop: 1 }}
            >
              {meals.reduce((s, m) => s + m.items.length, 0)} items
            </Text>
          </View>
        </View>
        <Text style={{ fontSize: 16, fontWeight: "700", color: colors.text }}>
          {totalCalories}{" "}
          <Text style={{ fontSize: 12, color: colors.textMuted }}>cal</Text>
        </Text>
      </View>

      {/* Meals in this group */}
      {meals.map((meal) => (
        <View key={meal.id}>
          <TouchableOpacity
            onPress={() => onEdit(meal)}
            onLongPress={() => handleLongPressMeal(meal)}
            delayLongPress={400}
            activeOpacity={0.7}
            style={{
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "space-between",
              paddingTop: 6,
              paddingBottom: 4,
              paddingHorizontal: 8,
              marginTop: 4,
              backgroundColor: colors.borderLight,
              borderRadius: radius.sm,
            }}
          >
            <Text style={{ fontSize: 11, color: colors.textMuted }}>
              {new Date(meal.loggedAt).toLocaleTimeString([], {
                hour: "2-digit",
                minute: "2-digit",
              })}
              {" · "}
              {meal.totalCalories} cal
            </Text>
            <View
              style={{ flexDirection: "row", alignItems: "center", gap: 4 }}
            >
              <Text style={{ fontSize: 11, color: colors.textMuted }}>
                Edit
              </Text>
              <Ionicons
                name="ellipsis-horizontal"
                size={14}
                color={colors.textMuted}
              />
            </View>
          </TouchableOpacity>

          {meal.items.map((item, idx) => (
            <MealItemRow
              key={item.id}
              item={item}
              meal={meal}
              itemIndex={idx}
              onSwap={onSwapItem}
              onDelete={onDeleteItem}
            />
          ))}
        </View>
      ))}
    </View>
  );
}
