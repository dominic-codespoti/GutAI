import { useState, useEffect } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { BottomSheet } from "../BottomSheet";
import { ServingSizeSelector } from "../ServingSizeSelector";
import { SwapSearchContent } from "./SwapSearchContent";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { mapEditItemToRequest } from "../../src/utils/mealMappers";
import {
  shiftDate,
  formatDateLabel,
  redateLoggedAt,
} from "../../src/utils/date";
import { MEAL_TYPES } from "../../src/utils/constants";
import { toast } from "../../src/stores/toast";
import { radius, spacing, mealTypeEmoji } from "../../src/utils/theme";
import { useThemeColors, useThemeFonts } from "../../src/stores/theme";
import type { MealItem, FoodProduct } from "../../src/types";

export function EditMealSheet() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const mode = useMealSheetStore((s) => s.mode);
  const editingMeal = useMealSheetStore((s) => s.editingMeal);
  const close = useMealSheetStore((s) => s.close);
  const visible = mode === "edit-meal" && !!editingMeal;

  const [subView, setSubView] = useState<"form" | "swap">("form");
  const [swapIndex, setSwapIndex] = useState<number | null>(null);
  const [editMealType, setEditMealType] = useState("Lunch");
  const [editMealDate, setEditMealDate] = useState("");
  const [editItems, setEditItems] = useState<MealItem[]>([]);
  const [editConfigs, setEditConfigs] = useState<
    Record<number, { servingG: number; multiplier: number; customText: string }>
  >({});

  const { updateMeal } = useMealMutations();

  // Initialise form when editingMeal changes
  useEffect(() => {
    if (editingMeal) {
      setEditMealType(editingMeal.mealType);
      setEditMealDate(editingMeal.loggedAt.split("T")[0]);
      setEditItems(editingMeal.items.map((it) => ({ ...it })));
      const configs: typeof editConfigs = {};
      editingMeal.items.forEach((it, idx) => {
        configs[idx] = {
          servingG: it.servingWeightG ?? 100,
          multiplier: 1,
          customText: "",
        };
      });
      setEditConfigs(configs);
      setSubView("form");
      setSwapIndex(null);
    }
  }, [editingMeal]);

  const removeItem = (idx: number) => {
    setEditItems((prev) => prev.filter((_, i) => i !== idx));
  };

  const handleSwapSelect = (food: FoodProduct) => {
    if (swapIndex === null) return;
    const cfg = editConfigs[swapIndex];
    const servingG = food.servingQuantity ?? cfg?.servingG ?? 100;

    setEditItems((prev) =>
      prev.map((it, i) =>
        i === swapIndex
          ? {
              ...it,
              foodName: food.name,
              foodProductId:
                food.id !== "00000000-0000-0000-0000-000000000000"
                  ? food.id
                  : undefined,
              calories: food.calories100g ?? 0,
              proteinG: food.protein100g ?? 0,
              carbsG: food.carbs100g ?? 0,
              fatG: food.fat100g ?? 0,
              fiberG: food.fiber100g ?? 0,
              sugarG: food.sugar100g ?? 0,
              sodiumMg: food.sodium100g ?? 0,
              servingWeightG: 100,
            }
          : it,
      ),
    );
    setEditConfigs((prev) => ({
      ...prev,
      [swapIndex]: { ...prev[swapIndex], servingG, customText: "" },
    }));
    setSwapIndex(null);
    setSubView("form");
    toast.success(`Swapped to "${food.name}"`);
  };

  const handleSave = () => {
    if (!editingMeal || editItems.length === 0) return;
    updateMeal.mutate({
      id: editingMeal.id,
      data: {
        mealType: editMealType,
        loggedAt: redateLoggedAt(editingMeal.loggedAt, editMealDate),
        items: editItems.map((it, idx) =>
          mapEditItemToRequest(it, editConfigs[idx]),
        ),
      },
    });
  };

  return (
    <BottomSheet visible={visible} onClose={close}>
      {subView === "swap" ? (
        <SwapSearchContent
          initialSearch={
            swapIndex !== null ? (editItems[swapIndex]?.foodName ?? "") : ""
          }
          onSelect={handleSwapSelect}
          onBack={() => setSubView("form")}
        />
      ) : (
        <View>
          <Text style={{ ...fonts.h3, marginBottom: spacing.md }}>
            Edit Meal
          </Text>

          {/* Meal type selector */}
          <View
            style={{
              flexDirection: "row",
              marginBottom: spacing.lg,
              gap: 6,
            }}
          >
            {MEAL_TYPES.map((t) => {
              const active = editMealType === t;
              return (
                <TouchableOpacity
                  key={t}
                  onPress={() => setEditMealType(t)}
                  style={{
                    flex: 1,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                    backgroundColor: active
                      ? colors.primary
                      : colors.borderLight,
                    alignItems: "center",
                    borderWidth: active ? 0 : 1,
                    borderColor: colors.border,
                  }}
                >
                  <Text
                    style={{
                      fontSize: 12,
                      fontWeight: "600",
                      color: active ? colors.textOnPrimary : colors.textMuted,
                    }}
                  >
                    {mealTypeEmoji[t]} {t}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Date picker */}
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "center",
              marginBottom: spacing.lg,
              gap: 12,
              backgroundColor: colors.bg,
              borderRadius: radius.sm,
              paddingVertical: 8,
              paddingHorizontal: 12,
            }}
          >
            <TouchableOpacity
              onPress={() => setEditMealDate((d) => shiftDate(d, -1))}
              hitSlop={8}
            >
              <Ionicons
                name="chevron-back"
                size={20}
                color={colors.textMuted}
              />
            </TouchableOpacity>
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                gap: 6,
              }}
            >
              <Ionicons
                name="calendar-outline"
                size={16}
                color={colors.primary}
              />
              <Text
                style={{
                  fontSize: 14,
                  fontWeight: "600",
                  color: colors.text,
                }}
              >
                {formatDateLabel(editMealDate)}
              </Text>
            </View>
            <TouchableOpacity
              onPress={() => setEditMealDate((d) => shiftDate(d, 1))}
              hitSlop={8}
            >
              <Ionicons
                name="chevron-forward"
                size={20}
                color={colors.textMuted}
              />
            </TouchableOpacity>
          </View>

          {/* Item list */}
          <ScrollView style={{ maxHeight: 320 }}>
            {editItems.map((item, idx) => {
              const cfg = editConfigs[idx] ?? {
                servingG: item.servingWeightG ?? 100,
                multiplier: 1,
                customText: "",
              };
              const totalG = cfg.servingG * cfg.multiplier;
              const scale = totalG / (item.servingWeightG ?? 100);

              return (
                <View
                  key={item.id || idx}
                  style={{
                    backgroundColor: colors.bg,
                    borderRadius: radius.sm,
                    padding: spacing.md,
                    marginBottom: 8,
                  }}
                >
                  <View
                    style={{
                      flexDirection: "row",
                      justifyContent: "space-between",
                      alignItems: "center",
                      marginBottom: 8,
                    }}
                  >
                    <Text
                      style={{
                        flex: 1,
                        fontWeight: "600",
                        fontSize: 15,
                        color: colors.text,
                      }}
                      numberOfLines={1}
                    >
                      {item.foodName}
                    </Text>
                    <TouchableOpacity
                      onPress={() => {
                        setSwapIndex(idx);
                        setSubView("swap");
                      }}
                      style={{ marginLeft: 8 }}
                    >
                      <Ionicons
                        name="swap-horizontal"
                        size={20}
                        color={colors.primary}
                      />
                    </TouchableOpacity>
                    <TouchableOpacity
                      onPress={() => removeItem(idx)}
                      style={{ marginLeft: 8 }}
                    >
                      <Ionicons
                        name="close-circle"
                        size={20}
                        color={colors.danger}
                      />
                    </TouchableOpacity>
                  </View>

                  <ServingSizeSelector
                    servingG={cfg.servingG}
                    onServingChange={(g) =>
                      setEditConfigs((prev) => ({
                        ...prev,
                        [idx]: { ...prev[idx], servingG: g },
                      }))
                    }
                    customText={cfg.customText}
                    onCustomTextChange={(t) =>
                      setEditConfigs((prev) => ({
                        ...prev,
                        [idx]: { ...prev[idx], customText: t },
                      }))
                    }
                    multiplier={cfg.multiplier}
                    onMultiplierChange={(m) =>
                      setEditConfigs((prev) => ({
                        ...prev,
                        [idx]: { ...prev[idx], multiplier: m },
                      }))
                    }
                    product={{
                      servingQuantity: item.servingWeightG ?? 100,
                      servingSize: item.servingUnit || "serving",
                    }}
                    summaryText={`${totalG}g total · ${Math.round(
                      item.calories * scale,
                    )} cal · ${Math.round(item.proteinG * scale)}g P · ${Math.round(
                      item.carbsG * scale,
                    )}g C · ${Math.round(item.fatG * scale)}g F`}
                  />
                </View>
              );
            })}
          </ScrollView>

          {/* Actions */}
          <View
            style={{
              flexDirection: "row",
              justifyContent: "flex-end",
              marginTop: spacing.lg,
              gap: 12,
            }}
          >
            <TouchableOpacity
              onPress={close}
              style={{ paddingHorizontal: 20, paddingVertical: 10 }}
            >
              <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
                Cancel
              </Text>
            </TouchableOpacity>
            <TouchableOpacity
              onPress={handleSave}
              disabled={updateMeal.isPending}
              style={{
                backgroundColor: colors.primary,
                paddingHorizontal: 20,
                paddingVertical: 10,
                borderRadius: radius.sm,
              }}
            >
              {updateMeal.isPending ? (
                <ActivityIndicator color={colors.textOnPrimary} size="small" />
              ) : (
                <Text
                  style={{ color: colors.textOnPrimary, fontWeight: "600" }}
                >
                  Save
                </Text>
              )}
            </TouchableOpacity>
          </View>
        </View>
      )}
    </BottomSheet>
  );
}
