import { useState } from "react";
import { View, Text, TouchableOpacity, ActivityIndicator } from "react-native";
import * as haptics from "../../src/utils/haptics";
import { BottomSheet } from "../BottomSheet";
import { MealTypePicker } from "../MealTypePicker";
import { ServingSizeSelector } from "../ServingSizeSelector";
import { scaleNutrition, nutritionSummaryText } from "../../src/utils/nutrition";
import { radius, spacing } from "../../src/utils/theme";
import { useThemeColors } from "../../src/stores/theme";
import type { FoodProduct } from "../../src/types";

type AddToMealSheetProps = {
  product: FoodProduct | null;
  visible: boolean;
  onClose: () => void;
  onLog: (product: FoodProduct, servingG: number, mealType: string) => void;
  isPending?: boolean;
  defaultMealType?: string;
  triggerWarning?: string | null;
};

export function AddToMealSheet({
  product,
  visible,
  onClose,
  onLog,
  isPending = false,
  defaultMealType = "Lunch",
  triggerWarning = null,
}: AddToMealSheetProps) {
  const colors = useThemeColors();
  const [mealType, setMealType] = useState(defaultMealType);
  const [servingG, setServingG] = useState(100);
  const [multiplier, setMultiplier] = useState(1);
  const [customText, setCustomText] = useState("");

  const effectiveGrams = servingG * multiplier;

  const handleLog = () => {
    if (!product) return;
    onLog(product, effectiveGrams, mealType);
    haptics.success();
  };

  const handleClose = () => {
    setServingG(100);
    setMultiplier(1);
    setCustomText("");
    setMealType(defaultMealType);
    onClose();
  };

  return (
    <BottomSheet visible={visible} onClose={handleClose}>
      <Text style={{ fontSize: 20, fontWeight: "700", color: colors.text, marginBottom: 4 }}>
        Add to Meal
      </Text>
      {product && (
        <Text style={{ fontSize: 16, color: colors.textSecondary, marginBottom: 20 }}>
          {product.name}
        </Text>
      )}

      {triggerWarning && (
        <View
          style={{
            backgroundColor: colors.dangerBg,
            borderRadius: 8,
            padding: 10,
            marginBottom: 10,
            flexDirection: "row",
            alignItems: "center",
          }}
        >
          <Text style={{ fontSize: 14, marginRight: 6 }}>⚠️</Text>
          <Text style={{ fontSize: 12, color: colors.danger, fontWeight: "600", flex: 1 }}>
            {triggerWarning}
          </Text>
        </View>
      )}

      <View style={{ marginBottom: 20 }}>
        <Text style={{ fontSize: 14, fontWeight: "600", color: colors.textSecondary, marginBottom: 8 }}>
          Meal Type
        </Text>
        <MealTypePicker selected={mealType} onSelect={setMealType} />
      </View>

      {product && (
        <ServingSizeSelector
          servingG={servingG}
          onServingChange={setServingG}
          customText={customText}
          onCustomTextChange={setCustomText}
          multiplier={multiplier}
          onMultiplierChange={setMultiplier}
          product={product}
          summaryText={nutritionSummaryText(
            scaleNutrition(product, effectiveGrams),
            effectiveGrams,
          )}
        />
      )}

      <View style={{ flexDirection: "row", justifyContent: "space-between", gap: 12, marginTop: 24 }}>
        <TouchableOpacity
          onPress={handleClose}
          style={{
            flex: 1,
            paddingVertical: 14,
            borderRadius: 12,
            backgroundColor: colors.borderLight,
            alignItems: "center",
          }}
        >
          <Text style={{ color: colors.textSecondary, fontWeight: "600", fontSize: 16 }}>
            Cancel
          </Text>
        </TouchableOpacity>
        <TouchableOpacity
          onPress={handleLog}
          disabled={isPending || !product}
          style={{
            flex: 1,
            backgroundColor: colors.primary,
            paddingVertical: 14,
            borderRadius: 12,
            alignItems: "center",
          }}
        >
          {isPending ? (
            <ActivityIndicator color={colors.textOnPrimary} size="small" />
          ) : (
            <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 16 }}>
              Log It
            </Text>
          )}
        </TouchableOpacity>
      </View>
    </BottomSheet>
  );
}
