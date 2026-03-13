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
import { useFavorites } from "../../src/hooks/useFavorites";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { radius, spacing } from "../../src/utils/theme";
import { buildLoggedAt } from "../../src/utils/date";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import type { RecentFood } from "../../src/types";

interface QuickAddItem {
  key: string;
  kind: "favorite" | "recent";
  foodName: string;
  foodProductId?: string;
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  fiberG?: number;
  sugarG?: number;
  sodiumMg?: number;
  servingUnit: string;
  servingWeightG?: number;
}

export function QuickAddRow() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow } = useThemeShadow();
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const selectedMealType = useMealSheetStore((s) => s.selectedMealType);
  const { createMeal } = useMealMutations();
  const { favorites, isLoading: loadingFavs } = useFavorites();

  const { data: recentFoods, isLoading: loadingRecent } = useQuery({
    queryKey: ["recent-foods"],
    queryFn: () => mealApi.recentFoods(20).then((r) => r.data),
    staleTime: 5 * 60 * 1000,
    select: (data) => [...data].sort((a, b) => b.logCount - a.logCount),
  });

  const isLoading = loadingFavs || loadingRecent;

  // Build merged list: favorites first, then recent (deduped)
  const items: QuickAddItem[] = [];
  const seen = new Set<string>();

  for (const fav of favorites) {
    const servingG = fav.servingQuantity ?? 100;
    const factor = servingG / 100;
    const key = fav.foodProductId;
    seen.add(key);
    seen.add(fav.foodName.toLowerCase());
    items.push({
      key: `fav-${key}`,
      kind: "favorite",
      foodName: fav.foodName,
      foodProductId: fav.foodProductId,
      calories: Math.round((fav.calories100g ?? 0) * factor),
      proteinG: Math.round((fav.protein100g ?? 0) * factor * 10) / 10,
      carbsG: Math.round((fav.carbs100g ?? 0) * factor * 10) / 10,
      fatG: Math.round((fav.fat100g ?? 0) * factor * 10) / 10,
      servingUnit: fav.servingSize || "serving",
      servingWeightG: servingG,
    });
  }

  if (recentFoods) {
    for (const food of recentFoods) {
      // skip if already in favorites (by productId or name)
      if (food.foodProductId && seen.has(food.foodProductId)) continue;
      if (seen.has(food.foodName.toLowerCase())) continue;
      seen.add(food.foodName.toLowerCase());
      items.push({
        key: `recent-${food.foodName}`,
        kind: "recent",
        foodName: food.foodName,
        foodProductId: food.foodProductId,
        calories: food.calories,
        proteinG: food.proteinG,
        carbsG: food.carbsG,
        fatG: food.fatG,
        fiberG: food.fiberG,
        sugarG: food.sugarG,
        sodiumMg: food.sodiumMg,
        servingUnit: food.servingUnit || "serving",
        servingWeightG: food.servingWeightG,
      });
    }
  }

  const handleQuickLog = (item: QuickAddItem) => {
    createMeal.mutate({
      mealType: selectedMealType,
      loggedAt: buildLoggedAt(selectedDate),
      items: [
        {
          foodName: item.foodName,
          foodProductId: item.foodProductId,
          servings: 1,
          servingUnit: item.servingUnit,
          servingWeightG: item.servingWeightG,
          calories: item.calories,
          proteinG: item.proteinG,
          carbsG: item.carbsG,
          fatG: item.fatG,
          fiberG: item.fiberG,
          sugarG: item.sugarG,
          sodiumMg: item.sodiumMg,
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
          marginBottom: spacing.lg,
        }}
      >
        <ActivityIndicator size="small" color={colors.primary} />
      </View>
    );
  }

  if (items.length === 0) {
    return (
      <View
        style={{
          alignItems: "center",
          paddingVertical: spacing.md,
          marginBottom: spacing.lg,
        }}
      >
        <Ionicons name="time-outline" size={20} color={colors.textLight} />
        <Text style={{ ...fonts.caption, marginTop: 4, textAlign: "center" }}>
          Your favorites and recent foods will appear here
        </Text>
      </View>
    );
  }

  return (
    <View style={{ marginBottom: spacing.lg }}>
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
        data={items}
        keyExtractor={(item) => item.key}
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ gap: 8 }}
        renderItem={({ item }) => {
          const isFav = item.kind === "favorite";
          return (
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
                marginBottom: 4,
                gap: 6,
                borderWidth: 1,
                borderColor: isFav ? colors.danger + "40" : colors.borderLight,
                ...shadow,
              }}
            >
              <Ionicons
                name={isFav ? "heart" : "add-circle"}
                size={isFav ? 14 : 16}
                color={isFav ? colors.danger : colors.primary}
              />
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
          );
        }}
      />
    </View>
  );
}
