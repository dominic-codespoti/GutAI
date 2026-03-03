import { Text, View, Pressable } from "react-native";
import * as Haptics from "expo-haptics";
import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { SwipeableItemRow } from "../SwipeableItemRow";
import { colors, spacing } from "../../src/utils/theme";
import { foodApi } from "../../src/api";
import { toast } from "../../src/stores/toast";
import type { MealItem, MealLog } from "../../src/types";

interface Props {
  item: MealItem;
  meal: MealLog;
  itemIndex: number;
  onSwap: (meal: MealLog, index: number) => void;
  onDelete: (meal: MealLog, index: number) => void;
}

export function MealItemRow({
  item,
  meal,
  itemIndex,
  onSwap,
  onDelete,
}: Props) {
  const router = useRouter();

  const handleTap = async () => {
    if (item.foodProductId) {
      router.push(`/food/${item.foodProductId}`);
      return;
    }
    // Try to find the product by name
    try {
      const results = await foodApi.search(item.foodName).then((r) => r.data);
      if (
        results.length > 0 &&
        results[0].id &&
        results[0].id !== "00000000-0000-0000-0000-000000000000"
      ) {
        router.push(`/food/${results[0].id}`);
        return;
      }
    } catch {
      // ignore
    }
    toast.info("No detailed info available for this food");
  };

  return (
    <SwipeableItemRow
      onSwap={() => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
        onSwap(meal, itemIndex);
      }}
      onDelete={() => onDelete(meal, itemIndex)}
    >
      <Pressable
        onPress={handleTap}
        style={({ pressed }) => ({
          flexDirection: "row" as const,
          justifyContent: "space-between" as const,
          alignItems: "center" as const,
          paddingTop: 6,
          paddingBottom: 4,
          paddingHorizontal: 4,
          borderTopWidth: 1,
          borderTopColor: colors.divider,
          backgroundColor: pressed ? colors.borderLight : colors.card,
        })}
      >
        <Text
          style={{ color: colors.textSecondary, flex: 1 }}
          numberOfLines={1}
        >
          {item.foodName}
          <Text style={{ color: colors.textMuted }}>
            {" · "}
            {item.servings} {item.servingUnit}
          </Text>
        </Text>
        <View style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
          <Text style={{ color: colors.textSecondary, fontWeight: "500" }}>
            {item.calories} cal
          </Text>
          <Ionicons name="chevron-forward" size={14} color={colors.textLight} />
        </View>
      </Pressable>
    </SwipeableItemRow>
  );
}
