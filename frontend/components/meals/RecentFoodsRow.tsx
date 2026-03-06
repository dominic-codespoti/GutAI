import {
  View,
  Text,
  FlatList,
  TouchableOpacity,
  ActivityIndicator,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
import { Ionicons } from "@expo/vector-icons";
import { mealApi } from "../../src/api";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { radius, spacing } from "../../src/utils/theme";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import type { RecentFood } from "../../src/types";

export function RecentFoodsRow() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow } = useThemeShadow();
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const selectedMealType = useMealSheetStore((s) => s.selectedMealType);
  const { createMeal } = useMealMutations();

  const { data: recentFoods, isLoading } = useQuery({
    queryKey: ["recent-foods"],
    queryFn: () => mealApi.recentFoods(20).then((r) => r.data),
    staleTime: 5 * 60 * 1000,
    select: (data) => [...data].sort((a, b) => b.logCount - a.logCount),
  });

  const handleQuickLog = (food: RecentFood) => {
    createMeal.mutate({
      mealType: selectedMealType,
      loggedAt: selectedDate + "T" + new Date().toISOString().split("T")[1],
      items: [
        {
          foodName: food.foodName,
          foodProductId: food.foodProductId,
          servings: 1,
          servingUnit: food.servingUnit || "serving",
          servingWeightG: food.servingWeightG,
          calories: food.calories,
          proteinG: food.proteinG,
          carbsG: food.carbsG,
          fatG: food.fatG,
          fiberG: food.fiberG,
          sugarG: food.sugarG,
          sodiumMg: food.sodiumMg,
        },
      ],
    });
  };

  if (isLoading) {
    return (
      <View
        style={{
          height: 52,
          justifyContent: "center",
          marginBottom: spacing.md,
        }}
      >
        <ActivityIndicator size="small" color={colors.primary} />
      </View>
    );
  }

  if (!recentFoods || recentFoods.length === 0) return null;

  return (
    <View style={{ marginBottom: spacing.md }}>
      <Text
        style={{
          ...fonts.caption,
          fontWeight: "600",
          marginBottom: spacing.xs,
          marginLeft: 2,
        }}
      >
        Quick Add
      </Text>
      <FlatList
        horizontal
        data={recentFoods}
        keyExtractor={(item) => item.foodName}
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ gap: 8 }}
        renderItem={({ item }) => (
          <TouchableOpacity
            onPress={() => handleQuickLog(item)}
            activeOpacity={0.7}
            style={{
              flexDirection: "row",
              alignItems: "center",
              backgroundColor: colors.card,
              borderRadius: radius.full,
              paddingHorizontal: 12,
              paddingVertical: 8,
              gap: 6,
              borderWidth: 1,
              borderColor: colors.borderLight,
              ...shadow,
            }}
          >
            <Ionicons name="add-circle" size={16} color={colors.primary} />
            <Text
              style={{ fontSize: 13, fontWeight: "500", color: colors.text }}
              numberOfLines={1}
            >
              {item.foodName}
            </Text>
            <Text style={{ fontSize: 11, color: colors.textMuted }}>
              {item.calories}cal
            </Text>
          </TouchableOpacity>
        )}
      />
    </View>
  );
}
