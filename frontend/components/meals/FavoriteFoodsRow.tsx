import {
  View,
  Text,
  FlatList,
  TouchableOpacity,
  ActivityIndicator,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
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
import type { FavoriteFood } from "../../src/types";

export function FavoriteFoodsRow() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow } = useThemeShadow();
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const selectedMealType = useMealSheetStore((s) => s.selectedMealType);
  const { createMeal } = useMealMutations();
  const { favorites, isLoading } = useFavorites();

  const handleQuickLog = (food: FavoriteFood) => {
    const servingG = food.servingQuantity ?? 100;
    const factor = servingG / 100;
    createMeal.mutate({
      mealType: selectedMealType,
      loggedAt: buildLoggedAt(selectedDate),
      items: [
        {
          foodName: food.foodName,
          foodProductId: food.foodProductId,
          servings: 1,
          servingUnit: food.servingSize || "serving",
          servingWeightG: servingG,
          calories: Math.round((food.calories100g ?? 0) * factor),
          proteinG: Math.round((food.protein100g ?? 0) * factor * 10) / 10,
          carbsG: Math.round((food.carbs100g ?? 0) * factor * 10) / 10,
          fatG: Math.round((food.fat100g ?? 0) * factor * 10) / 10,
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
          marginBottom: spacing.sm,
        }}
      >
        <ActivityIndicator size="small" color={colors.primary} />
      </View>
    );
  }

  if (!favorites || favorites.length === 0) {
    return null;
  }

  return (
    <View style={{ marginBottom: spacing.sm }}>
      <Text
        style={{
          ...fonts.caption,
          fontWeight: "600",
          marginBottom: spacing.xs,
          marginLeft: 2,
        }}
      >
        ⭐ Favorites
      </Text>
      <FlatList
        horizontal
        data={favorites}
        keyExtractor={(item) => item.foodProductId}
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
              borderColor: colors.danger + "40",
              ...shadow,
            }}
          >
            <Ionicons name="heart" size={14} color={colors.danger} />
            <Text
              style={{ fontSize: 13, fontWeight: "500", color: colors.text }}
              numberOfLines={1}
            >
              {item.foodName}
            </Text>
            {item.calories100g != null && (
              <Text style={{ fontSize: 11, color: colors.textMuted }}>
                {Math.round(item.calories100g)}cal
              </Text>
            )}
          </TouchableOpacity>
        )}
      />
    </View>
  );
}
