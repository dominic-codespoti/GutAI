import { useState } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ActivityIndicator,
  ScrollView,
  Keyboard,
} from "react-native";
import { useMutation } from "@tanstack/react-query";
import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { BottomSheet } from "../BottomSheet";
import { ServingSizeSelector } from "../ServingSizeSelector";
import { SwapSearchContent } from "./SwapSearchContent";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { mealApi } from "../../src/api";
import { toast } from "../../src/stores/toast";
import { mapParsedItemToRequest } from "../../src/utils/mealMappers";
import { radius, spacing } from "../../src/utils/theme";
import { useThemeColors } from "../../src/stores/theme";
import type { ParsedFoodItem, FoodProduct } from "../../src/types";

type Tab = "describe" | "manual";

export function AddMealSheet() {
  const colors = useThemeColors();

  const editFieldStyle = {
    borderWidth: 1 as const,
    borderColor: colors.border,
    borderRadius: radius.sm,
    padding: 6,
    fontSize: 13,
    color: colors.text,
    textAlign: "center" as const,
    backgroundColor: colors.card,
  };

  const mode = useMealSheetStore((s) => s.mode);
  const selectedMealType = useMealSheetStore((s) => s.selectedMealType);
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const close = useMealSheetStore((s) => s.close);
  const router = useRouter();

  const visible = mode === "add-describe" || mode === "add-manual";
  const initialTab: Tab = mode === "add-manual" ? "manual" : "describe";

  const [activeTab, setActiveTab] = useState<Tab>(initialTab);
  const [subView, setSubView] = useState<"form" | "swap">("form");
  const [swapIndex, setSwapIndex] = useState<number | null>(null);

  // ── Describe state ──
  const [naturalText, setNaturalText] = useState("");
  const [parsedItems, setParsedItems] = useState<ParsedFoodItem[]>([]);
  const [parsedConfigs, setParsedConfigs] = useState<
    Record<number, { servingG: number; multiplier: number; customText: string }>
  >({});
  const [showReview, setShowReview] = useState(false);

  // ── Manual state ──
  const [manualName, setManualName] = useState("");
  const [manualCalories, setManualCalories] = useState("");
  const [manualProtein, setManualProtein] = useState("0");
  const [manualCarbs, setManualCarbs] = useState("0");
  const [manualFat, setManualFat] = useState("0");
  const [manualFiber, setManualFiber] = useState("0");
  const [manualSugar, setManualSugar] = useState("0");
  const [manualSodium, setManualSodium] = useState("0");
  const [manualServings, setManualServings] = useState("1");
  const [manualFoodProductId, setManualFoodProductId] = useState<string>();

  const { createMeal } = useMealMutations();

  const parseMutation = useMutation({
    mutationFn: (text: string) =>
      mealApi
        .parseNatural({ text, mealType: selectedMealType })
        .then((r) => r.data),
    onSuccess: (response) => {
      const parsed = response.parsedItems;
      if (parsed.length === 0) {
        toast.error("No foods recognized. Try being more specific.");
        return;
      }
      const newConfigs: typeof parsedConfigs = {};
      parsed.forEach((it, idx) => {
        newConfigs[idx] = {
          servingG: it.servingWeightG ?? 100,
          multiplier: it.servingQuantity ?? 1,
          customText: "",
        };
      });
      setParsedConfigs(newConfigs);

      if (parsed.length === 1) {
        // Single item → fill manual form
        const item = parsed[0];
        setActiveTab("manual");
        setManualName(item.name);
        setManualCalories(String(Math.round(item.calories)));
        setManualProtein(String(Math.round(item.proteinG)));
        setManualCarbs(String(Math.round(item.carbsG)));
        setManualFat(String(Math.round(item.fatG)));
        setManualFiber(String(Math.round(item.fiberG)));
        setManualSugar(String(Math.round(item.sugarG)));
        setManualSodium(String(Math.round(item.sodiumMg)));
        setManualServings(String(item.servingQuantity ?? 1));
        setManualFoodProductId(item.foodProductId ?? undefined);
        setNaturalText("");
        toast.success(`Filled from "${item.name}"`);
      } else {
        setParsedItems(parsed);
        setNaturalText("");
        setShowReview(true);
        toast.success(`Found ${parsed.length} food items`);
      }
    },
    onError: () =>
      toast.error("Could not parse meal. Try being more specific."),
  });

  const handleLogManual = () => {
    if (!manualName.trim()) return;
    createMeal.mutate({
      mealType: selectedMealType,
      loggedAt: selectedDate + "T" + new Date().toISOString().split("T")[1],
      items: [
        {
          foodName: manualName.trim(),
          servings: Number(manualServings) || 1,
          servingUnit: "serving",
          calories: Number(manualCalories) || 0,
          proteinG: Number(manualProtein) || 0,
          carbsG: Number(manualCarbs) || 0,
          fatG: Number(manualFat) || 0,
          fiberG: Number(manualFiber) || 0,
          sugarG: Number(manualSugar) || 0,
          sodiumMg: Number(manualSodium) || 0,
          foodProductId: manualFoodProductId,
        },
      ],
    });
    resetForm();
  };

  const handleLogParsed = () => {
    if (parsedItems.length === 0) return;
    createMeal.mutate({
      mealType: selectedMealType,
      loggedAt: selectedDate + "T" + new Date().toISOString().split("T")[1],
      items: parsedItems.map((it, idx) =>
        mapParsedItemToRequest(it, parsedConfigs[idx]),
      ),
    });
    resetForm();
  };

  const removeParsedItem = (idx: number) => {
    setParsedItems((prev) => {
      const next = prev.filter((_, i) => i !== idx);
      if (next.length === 0) setShowReview(false);
      return next;
    });
    setParsedConfigs((prev) => {
      const next: typeof prev = {};
      let i = 0;
      parsedItems.forEach((_, oldIdx) => {
        if (oldIdx !== idx) {
          next[i] = prev[oldIdx];
          i++;
        }
      });
      return next;
    });
  };

  const handleSwapSelect = (food: FoodProduct) => {
    if (swapIndex === null) return;
    const cfg = parsedConfigs[swapIndex];
    const servingG = food.servingQuantity ?? cfg?.servingG ?? 100;

    setParsedItems((prev) =>
      prev.map((it, i) =>
        i === swapIndex
          ? {
              ...it,
              name: food.name,
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
    setParsedConfigs((prev) => ({
      ...prev,
      [swapIndex]: { ...prev[swapIndex], servingG, customText: "" },
    }));
    setSwapIndex(null);
    setSubView("form");
    toast.success(`Swapped to "${food.name}"`);
  };

  const resetForm = () => {
    setNaturalText("");
    setParsedItems([]);
    setParsedConfigs({});
    setShowReview(false);
    setManualName("");
    setManualCalories("");
    setManualProtein("0");
    setManualCarbs("0");
    setManualFat("0");
    setManualFiber("0");
    setManualSugar("0");
    setManualSodium("0");
    setManualServings("1");
    setManualFoodProductId(undefined);
    setSubView("form");
    setSwapIndex(null);
  };

  const handleClose = () => {
    resetForm();
    close();
  };

  // Sync tab with mode changes
  if (
    visible &&
    mode === "add-describe" &&
    activeTab !== "describe" &&
    !showReview
  ) {
    setActiveTab("describe");
  }
  if (
    visible &&
    mode === "add-manual" &&
    activeTab !== "manual" &&
    !showReview
  ) {
    setActiveTab("manual");
  }

  return (
    <BottomSheet visible={visible} onClose={handleClose} maxHeight="90%">
      {subView === "swap" ? (
        <SwapSearchContent
          initialSearch={
            swapIndex !== null ? (parsedItems[swapIndex]?.name ?? "") : ""
          }
          onSelect={handleSwapSelect}
          onBack={() => setSubView("form")}
        />
      ) : (
        <View>
          {/* Tab bar */}
          <View
            style={{
              flexDirection: "row",
              marginBottom: spacing.lg,
              backgroundColor: colors.bg,
              borderRadius: radius.sm,
              padding: 3,
            }}
          >
            {(
              [
                {
                  key: "describe" as const,
                  label: "Describe",
                  icon: "chatbubble-outline",
                },
                {
                  key: "manual" as const,
                  label: "Manual",
                  icon: "create-outline",
                },
              ] as const
            ).map(({ key, label, icon }) => (
              <TouchableOpacity
                key={key}
                onPress={() => {
                  setActiveTab(key);
                  setShowReview(false);
                }}
                style={{
                  flex: 1,
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: 6,
                  paddingVertical: 10,
                  borderRadius: radius.sm - 2,
                  backgroundColor:
                    activeTab === key ? colors.card : "transparent",
                }}
              >
                <Ionicons
                  name={icon as any}
                  size={16}
                  color={activeTab === key ? colors.primary : colors.textMuted}
                />
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "600",
                    color: activeTab === key ? colors.text : colors.textMuted,
                  }}
                >
                  {label}
                </Text>
              </TouchableOpacity>
            ))}
          </View>

          {/* ── Describe tab ── */}
          {activeTab === "describe" && !showReview && (
            <View>
              <TextInput
                placeholder='e.g. "2 eggs, toast with butter, orange juice"'
                placeholderTextColor={colors.textLight}
                value={naturalText}
                onChangeText={setNaturalText}
                multiline
                style={{
                  fontSize: 15,
                  minHeight: 80,
                  color: colors.text,
                  backgroundColor: colors.bg,
                  borderRadius: radius.sm,
                  padding: spacing.md,
                  borderWidth: 1,
                  borderColor: colors.border,
                }}
              />
              <View
                style={{
                  flexDirection: "row",
                  justifyContent: "flex-end",
                  marginTop: spacing.md,
                  gap: 8,
                }}
              >
                <TouchableOpacity
                  onPress={handleClose}
                  style={{ paddingHorizontal: 14, paddingVertical: 8 }}
                >
                  <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
                    Cancel
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={() =>
                    naturalText && parseMutation.mutate(naturalText)
                  }
                  disabled={parseMutation.isPending || !naturalText.trim()}
                  style={{
                    backgroundColor: colors.primary,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                    opacity: naturalText.trim() ? 1 : 0.5,
                  }}
                >
                  {parseMutation.isPending ? (
                    <ActivityIndicator color="#fff" size="small" />
                  ) : (
                    <Text
                      style={{ color: "#fff", fontWeight: "600", fontSize: 14 }}
                    >
                      Parse & Log
                    </Text>
                  )}
                </TouchableOpacity>
              </View>
            </View>
          )}

          {/* ── Parsed review ── */}
          {activeTab === "describe" && showReview && (
            <ScrollView
              style={{ maxHeight: 420 }}
              keyboardShouldPersistTaps="handled"
            >
              <Text
                style={{
                  fontWeight: "700",
                  fontSize: 15,
                  color: colors.text,
                  marginBottom: spacing.md,
                }}
              >
                {parsedItems.length} items found — review & edit
              </Text>
              {parsedItems.map((item, idx) => {
                const cfg = parsedConfigs[idx] ?? {
                  servingG: item.servingWeightG || 100,
                  multiplier: 1,
                  customText: "1",
                };
                const totalG = cfg.servingG * cfg.multiplier;
                const scale = totalG / (item.servingWeightG || 100);

                return (
                  <View
                    key={idx}
                    style={{
                      borderWidth: 1,
                      borderColor: colors.borderLight,
                      borderRadius: radius.sm,
                      padding: spacing.md,
                      marginBottom: spacing.sm,
                    }}
                  >
                    <View
                      style={{
                        flexDirection: "row",
                        justifyContent: "space-between",
                        alignItems: "center",
                        marginBottom: 6,
                      }}
                    >
                      <Text
                        style={{
                          fontWeight: "600",
                          fontSize: 14,
                          color: colors.text,
                          flex: 1,
                        }}
                        numberOfLines={1}
                      >
                        {item.name}
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
                        onPress={() => removeParsedItem(idx)}
                        style={{ marginLeft: 8 }}
                      >
                        <Ionicons
                          name="close-circle"
                          size={22}
                          color={colors.danger}
                        />
                      </TouchableOpacity>
                    </View>

                    <ServingSizeSelector
                      servingG={cfg.servingG}
                      onServingChange={(g) =>
                        setParsedConfigs((prev) => ({
                          ...prev,
                          [idx]: { ...prev[idx], servingG: g },
                        }))
                      }
                      customText={cfg.customText}
                      onCustomTextChange={(t) =>
                        setParsedConfigs((prev) => ({
                          ...prev,
                          [idx]: { ...prev[idx], customText: t },
                        }))
                      }
                      multiplier={cfg.multiplier}
                      onMultiplierChange={(m) =>
                        setParsedConfigs((prev) => ({
                          ...prev,
                          [idx]: { ...prev[idx], multiplier: m },
                        }))
                      }
                      product={{
                        servingQuantity:
                          item.servingWeightG || item.servingQuantity || 1,
                        servingSize: item.servingSize || "serving",
                      }}
                      summaryText={`${totalG}g total · ${Math.round(
                        item.calories * scale,
                      )} cal · ${
                        Math.round(item.proteinG * scale * 10) / 10
                      }g P · ${
                        Math.round(item.carbsG * scale * 10) / 10
                      }g C · ${Math.round(item.fatG * scale * 10) / 10}g F`}
                    />
                  </View>
                );
              })}

              {/* Footer */}
              <View
                style={{
                  flexDirection: "row",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginTop: spacing.sm,
                }}
              >
                <Text style={{ fontSize: 12, color: colors.textMuted }}>
                  Total:{" "}
                  {Math.round(
                    parsedItems.reduce((acc, it, idx) => {
                      const cfg = parsedConfigs[idx];
                      if (!cfg) return acc;
                      const scale =
                        (cfg.servingG * cfg.multiplier) /
                        (it.servingWeightG ?? 100);
                      return acc + it.calories * scale;
                    }, 0),
                  )}{" "}
                  cal
                </Text>
                <View style={{ flexDirection: "row", gap: 8 }}>
                  <TouchableOpacity
                    onPress={() => {
                      setParsedItems([]);
                      setShowReview(false);
                    }}
                    style={{ paddingHorizontal: 14, paddingVertical: 8 }}
                  >
                    <Text
                      style={{ color: colors.textMuted, fontWeight: "600" }}
                    >
                      Cancel
                    </Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={handleLogParsed}
                    disabled={createMeal.isPending || parsedItems.length === 0}
                    style={{
                      backgroundColor: colors.primary,
                      paddingHorizontal: 16,
                      paddingVertical: 8,
                      borderRadius: radius.sm,
                    }}
                  >
                    {createMeal.isPending ? (
                      <ActivityIndicator color="#fff" size="small" />
                    ) : (
                      <Text
                        style={{
                          color: "#fff",
                          fontWeight: "600",
                          fontSize: 14,
                        }}
                      >
                        Log Meal ({parsedItems.length} items)
                      </Text>
                    )}
                  </TouchableOpacity>
                </View>
              </View>
            </ScrollView>
          )}

          {/* ── Manual tab ── */}
          {activeTab === "manual" && (
            <View>
              <TextInput
                placeholder="Food name (e.g. Chicken breast)"
                placeholderTextColor={colors.textLight}
                value={manualName}
                onChangeText={setManualName}
                style={{
                  fontWeight: "600",
                  fontSize: 15,
                  color: colors.text,
                  borderBottomWidth: 1,
                  borderBottomColor: colors.borderLight,
                  paddingBottom: 8,
                  marginBottom: spacing.md,
                }}
              />
              <View style={{ flexDirection: "row", gap: 6, marginBottom: 6 }}>
                {[
                  {
                    label: "Calories",
                    value: manualCalories,
                    set: setManualCalories,
                  },
                  {
                    label: "Protein",
                    value: manualProtein,
                    set: setManualProtein,
                  },
                  { label: "Carbs", value: manualCarbs, set: setManualCarbs },
                  { label: "Fat", value: manualFat, set: setManualFat },
                ].map(({ label, value, set }) => (
                  <View key={label} style={{ flex: 1 }}>
                    <Text
                      style={{
                        fontSize: 10,
                        color: colors.textMuted,
                        marginBottom: 2,
                      }}
                    >
                      {label}
                    </Text>
                    <TextInput
                      placeholder="0"
                      value={value}
                      onChangeText={set}
                      keyboardType="numeric"
                      style={editFieldStyle}
                    />
                  </View>
                ))}
              </View>
              <View style={{ flexDirection: "row", gap: 6, marginBottom: 6 }}>
                {[
                  { label: "Fiber", value: manualFiber, set: setManualFiber },
                  { label: "Sugar", value: manualSugar, set: setManualSugar },
                  {
                    label: "Na (mg)",
                    value: manualSodium,
                    set: setManualSodium,
                  },
                  {
                    label: "Servings",
                    value: manualServings,
                    set: setManualServings,
                  },
                ].map(({ label, value, set }) => (
                  <View key={label} style={{ flex: 1 }}>
                    <Text
                      style={{
                        fontSize: 10,
                        color: colors.textMuted,
                        marginBottom: 2,
                      }}
                    >
                      {label}
                    </Text>
                    <TextInput
                      placeholder="0"
                      value={value}
                      onChangeText={set}
                      keyboardType="numeric"
                      style={editFieldStyle}
                    />
                  </View>
                ))}
              </View>
              <View
                style={{
                  flexDirection: "row",
                  justifyContent: "flex-end",
                  marginTop: spacing.md,
                  gap: 8,
                }}
              >
                <TouchableOpacity
                  onPress={handleClose}
                  style={{ paddingHorizontal: 14, paddingVertical: 8 }}
                >
                  <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
                    Cancel
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={handleLogManual}
                  disabled={createMeal.isPending || !manualName.trim()}
                  style={{
                    backgroundColor: colors.primary,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                    opacity: manualName.trim() ? 1 : 0.5,
                  }}
                >
                  {createMeal.isPending ? (
                    <ActivityIndicator color="#fff" size="small" />
                  ) : (
                    <Text
                      style={{ color: "#fff", fontWeight: "600", fontSize: 14 }}
                    >
                      Log Meal
                    </Text>
                  )}
                </TouchableOpacity>
              </View>
            </View>
          )}
        </View>
      )}
    </BottomSheet>
  );
}
