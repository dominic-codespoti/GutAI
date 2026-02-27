import { useState, useCallback, useEffect } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  TextInput,
  ActivityIndicator,
  RefreshControl,
  Pressable,
  BackHandler,
  Platform,
} from "react-native";
import * as Haptics from "expo-haptics";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { mealApi, foodApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { toast } from "../../src/stores/toast";
import { confirm } from "../../src/utils/confirm";
import type {
  MealLog,
  MealItem,
  CreateMealRequest,
  SafetyReport,
  FoodProduct,
  ParsedFoodItem,
} from "../../src/types";
import { MealCardSkeleton } from "../../components/SkeletonLoader";
import { BottomSheet } from "../../components/BottomSheet";
import { MEAL_TYPES } from "../../src/utils/constants";
import { shiftDate, formatDateLabel } from "../../src/utils/date";
import { ratingColor } from "../../src/utils/colors";
import {
  colors,
  shadow,
  shadowMd,
  radius,
  spacing,
  fonts,
  mealTypeEmoji,
} from "../../src/utils/theme";
import { useRouter } from "expo-router";
import { maybeRequestReview } from "../../src/utils/review";
import { SafeScreen } from "../../components/SafeScreen";

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

export default function MealsScreen() {
  const router = useRouter();
  const [selectedDate, setSelectedDate] = useState(
    new Date().toISOString().split("T")[0],
  );
  const [showNaturalInput, setShowNaturalInput] = useState(false);
  const [showManualInput, setShowManualInput] = useState(false);
  const [showParsedReview, setShowParsedReview] = useState(false);
  const [naturalText, setNaturalText] = useState("");
  const [selectedMealType, setSelectedMealType] = useState<string>("Lunch");
  const [editingMeal, setEditingMeal] = useState<MealLog | null>(null);
  const [editItems, setEditItems] = useState<MealItem[]>([]);
  const [editMealType, setEditMealType] = useState("Lunch");
  const [editItemConfigs, setEditItemConfigs] = useState<
    Record<number, { servingG: number; multiplier: number; customText: string }>
  >({});
  const [manualFoodName, setManualFoodName] = useState("");
  const [manualCalories, setManualCalories] = useState("");
  const [manualProtein, setManualProtein] = useState("0");
  const [manualCarbs, setManualCarbs] = useState("0");
  const [manualFat, setManualFat] = useState("0");
  const [manualFiber, setManualFiber] = useState("0");
  const [manualSugar, setManualSugar] = useState("0");
  const [manualSodium, setManualSodium] = useState("0");
  const [manualServings, setManualServings] = useState("1");
  const [manualFoodProductId, setManualFoodProductId] = useState<
    string | undefined
  >(undefined);
  const queryClient = useQueryClient();
  const isToday = selectedDate === new Date().toISOString().split("T")[0];
  const [filterMealType, setFilterMealType] = useState<string | null>(null);
  const [parsedItems, setParsedItems] = useState<ParsedFoodItem[]>([]);

  const [sheetItem, setSheetItem] = useState<MealItem | null>(null);
  const [sheetReport, setSheetReport] = useState<SafetyReport | null>(null);
  const [sheetProduct, setSheetProduct] = useState<FoodProduct | null>(null);
  const [sheetLoading, setSheetLoading] = useState(false);

  useEffect(() => {
    if (Platform.OS === "android") {
      const handler = BackHandler.addEventListener("hardwareBackPress", () => {
        if (sheetItem) {
          setSheetItem(null);
          return true;
        }
        if (editingMeal) {
          setEditingMeal(null);
          return true;
        }
        if (showParsedReview) {
          setShowParsedReview(false);
          return true;
        }
        if (showManualInput) {
          setShowManualInput(false);
          return true;
        }
        if (showNaturalInput) {
          setShowNaturalInput(false);
          return true;
        }
        return false;
      });
      return () => handler.remove();
    }
  }, [
    sheetItem,
    editingMeal,
    showParsedReview,
    showManualInput,
    showNaturalInput,
  ]);

  const openItemSheet = async (item: MealItem) => {
    setSheetItem(item);
    setSheetReport(null);
    setSheetProduct(null);
    setSheetLoading(true);
    try {
      if (item.foodProductId) {
        const [prod, report] = await Promise.all([
          foodApi.get(item.foodProductId).then((r) => r.data),
          foodApi.safetyReport(item.foodProductId).then((r) => r.data),
        ]);
        setSheetProduct(prod);
        setSheetReport(report);
      } else {
        const results = await foodApi.search(item.foodName).then((r) => r.data);
        if (results.length > 0) {
          const match = results[0];
          setSheetProduct(match);
          const report = await foodApi
            .safetyReport(match.id)
            .then((r) => r.data);
          setSheetReport(report);
        }
      }
    } catch {
      // sheet will show with limited data
    } finally {
      setSheetLoading(false);
    }
  };

  const handleItemTap = (item: MealItem) => {
    if (item.foodProductId) {
      router.push(`/food/${item.foodProductId}`);
    } else {
      openItemSheet(item);
    }
  };

  const {
    data: meals,
    isLoading,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["meals", selectedDate],
    queryFn: () => mealApi.list(selectedDate).then((r) => r.data),
  });

  const { data: dailySummary } = useQuery({
    queryKey: ["daily-summary", selectedDate],
    queryFn: () => mealApi.dailySummary(selectedDate).then((r) => r.data),
  });

  const filteredMeals = filterMealType
    ? (meals ?? []).filter((m) => m.mealType === filterMealType)
    : meals;

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await refetch();
    setRefreshing(false);
  }, [refetch]);

  const manualMutation = useMutation({
    mutationFn: () =>
      mealApi.create({
        mealType: selectedMealType,
        loggedAt: new Date().toISOString(),
        items: [
          {
            foodName: manualFoodName.trim(),
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
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      setManualFoodName("");
      setManualCalories("");
      setManualProtein("0");
      setManualCarbs("0");
      setManualFat("0");
      setManualFiber("0");
      setManualSugar("0");
      setManualSodium("0");
      setManualServings("1");
      setManualFoodProductId(undefined);
      setShowManualInput(false);
      toast.success("Meal logged!");
      if (Platform.OS !== "web") {
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }
      maybeRequestReview();
    },
    onError: () => toast.error("Failed to log meal"),
  });

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
      if (parsed.length === 1) {
        const item = parsed[0];
        setManualFoodName(item.name);
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
        setShowNaturalInput(false);
        setShowManualInput(true);
        toast.success(`Filled from "${item.name}"`);
      } else {
        setParsedItems(parsed);
        setNaturalText("");
        setShowNaturalInput(false);
        setShowParsedReview(true);
        toast.success(`Found ${parsed.length} food items`);
      }
    },
    onError: () =>
      toast.error("Could not parse meal. Try being more specific."),
  });

  const updateParsedItem = (idx: number, field: string, value: string) => {
    setParsedItems((prev) =>
      prev.map((it, i) =>
        i === idx
          ? {
              ...it,
              [field]: field === "name" ? value : Number(value) || 0,
            }
          : it,
      ),
    );
  };

  const removeParsedItem = (idx: number) => {
    setParsedItems((prev) => {
      const next = prev.filter((_, i) => i !== idx);
      if (next.length === 0) setShowParsedReview(false);
      return next;
    });
  };

  const parsedMealMutation = useMutation({
    mutationFn: () =>
      mealApi.create({
        mealType: selectedMealType,
        loggedAt: new Date().toISOString(),
        items: parsedItems.map((it) => ({
          foodName: it.name,
          servings: it.servingQuantity ?? 1,
          servingUnit: it.servingSize ?? "serving",
          servingWeightG: it.servingWeightG,
          calories: it.calories,
          proteinG: it.proteinG,
          carbsG: it.carbsG,
          fatG: it.fatG,
          fiberG: it.fiberG,
          sugarG: it.sugarG,
          sodiumMg: it.sodiumMg,
          cholesterolMg: it.cholesterolMg,
          saturatedFatG: it.saturatedFatG,
          potassiumMg: it.potassiumMg,
          foodProductId: it.foodProductId ?? undefined,
        })),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      setParsedItems([]);
      setShowParsedReview(false);
      toast.success("Meal logged!");
      if (Platform.OS !== "web") {
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }
      maybeRequestReview();
    },
    onError: () => toast.error("Failed to log meal"),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => mealApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      toast.success("Meal deleted");
    },
    onError: () => toast.error("Failed to delete meal"),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: CreateMealRequest }) =>
      mealApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      setEditingMeal(null);
      toast.success("Meal updated");
    },
    onError: () => toast.error("Failed to update meal"),
  });

  const handleDelete = (id: string) => {
    confirm("Delete Meal", "Are you sure?", () => deleteMutation.mutate(id));
  };

  const openEdit = (meal: MealLog) => {
    setEditingMeal(meal);
    setEditMealType(meal.mealType);
    setEditItems(meal.items.map((it) => ({ ...it })));
    const configs: Record<
      number,
      { servingG: number; multiplier: number; customText: string }
    > = {};
    meal.items.forEach((it, idx) => {
      configs[idx] = {
        servingG: it.servingWeightG ?? 100,
        multiplier: 1,
        customText: "",
      };
    });
    setEditItemConfigs(configs);
  };

  const updateEditItem = (idx: number, field: string, value: string) => {
    setEditItems((prev) =>
      prev.map((it, i) =>
        i === idx
          ? {
              ...it,
              [field]: field === "foodName" ? value : Number(value) || 0,
            }
          : it,
      ),
    );
  };

  const removeEditItem = (idx: number) => {
    setEditItems((prev) => prev.filter((_, i) => i !== idx));
  };

  const saveEdit = () => {
    if (!editingMeal || editItems.length === 0) return;
    updateMutation.mutate({
      id: editingMeal.id,
      data: {
        mealType: editMealType,
        loggedAt: editingMeal.loggedAt,
        items: editItems.map((it, idx) => {
          const cfg = editItemConfigs[idx];
          if (!cfg) {
            return {
              foodName: it.foodName,
              servings: it.servings,
              servingUnit: it.servingUnit,
              servingWeightG: it.servingWeightG,
              calories: it.calories,
              proteinG: it.proteinG,
              carbsG: it.carbsG,
              fatG: it.fatG,
              fiberG: it.fiberG,
              sugarG: it.sugarG,
              sodiumMg: it.sodiumMg,
            };
          }
          const totalG = cfg.servingG * cfg.multiplier;
          const origG = it.servingWeightG ?? 100;
          const scale = totalG / origG;
          const origServing = it.servingWeightG ?? 100;
          const presets: { label: string; grams: number }[] = [
            { label: `${origServing}g`, grams: origServing },
            { label: "50g", grams: 50 },
            { label: "100g", grams: 100 },
            { label: "150g", grams: 150 },
            { label: "200g", grams: 200 },
          ];
          const seen = new Set<number>();
          const uniquePresets = presets.filter((p) => {
            if (seen.has(p.grams)) return false;
            seen.add(p.grams);
            return true;
          });
          return {
            foodName: it.foodName,
            servings: 1,
            servingUnit: `${totalG}g`,
            servingWeightG: totalG,
            calories: Math.round(it.calories * scale),
            proteinG: Math.round(it.proteinG * scale * 10) / 10,
            carbsG: Math.round(it.carbsG * scale * 10) / 10,
            fatG: Math.round(it.fatG * scale * 10) / 10,
            fiberG: Math.round(it.fiberG * scale * 10) / 10,
            sugarG: Math.round(it.sugarG * scale * 10) / 10,
            sodiumMg: Math.round(it.sodiumMg * scale),
          };
        }),
      },
    });
  };

  const [swapIndex, setSwapIndex] = useState<number | null>(null);
  const [swapSearch, setSwapSearch] = useState("");
  const [debouncedSwapSearch, setDebouncedSwapSearch] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSwapSearch(swapSearch), 300);
    return () => clearTimeout(timer);
  }, [swapSearch]);

  const swapSearchResults = useQuery({
    queryKey: ["swap-food-search", debouncedSwapSearch],
    queryFn: () => foodApi.search(debouncedSwapSearch).then((r) => r.data),
    enabled: debouncedSwapSearch.length >= 2 && swapIndex !== null,
    staleTime: 5 * 60 * 1000,
    placeholderData: (prev) => prev,
  });

  const openSwapSearch = (idx: number) => {
    setSwapIndex(idx);
    setSwapSearch(parsedItems[idx]?.name ?? "");
  };

  const selectSwapFood = (food: FoodProduct) => {
    if (swapIndex === null) return;
    const qty = parsedItems[swapIndex]?.servingQuantity ?? 1;
    const servingG = food.servingQuantity
      ? food.servingQuantity * qty
      : (parsedItems[swapIndex]?.servingWeightG ?? 100);
    const scale = servingG / 100;

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
              calories: Math.round((food.calories100g ?? 0) * scale),
              proteinG: Math.round((food.protein100g ?? 0) * scale),
              carbsG: Math.round((food.carbs100g ?? 0) * scale),
              fatG: Math.round((food.fat100g ?? 0) * scale),
              fiberG: Math.round((food.fiber100g ?? 0) * scale),
              sugarG: Math.round((food.sugar100g ?? 0) * scale),
              sodiumMg: Math.round((food.sodium100g ?? 0) * scale),
              servingWeightG: servingG,
            }
          : it,
      ),
    );
    setSwapIndex(null);
    setSwapSearch("");
    toast.success(`Swapped to "${food.name}"`);
  };

  return (
    <SafeScreen edges={["top"]}>
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={colors.primary}
          />
        }
        showsVerticalScrollIndicator={false}
      >
        <View style={{ padding: spacing.xl }}>
          {/* Date Navigation */}
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "space-between",
              backgroundColor: colors.card,
              borderRadius: radius.md,
              padding: spacing.md,
              marginBottom: spacing.lg,
              ...shadow,
            }}
          >
            <TouchableOpacity
              onPress={() => setSelectedDate(shiftDate(selectedDate, -1))}
              style={{ padding: 8 }}
            >
              <Ionicons
                name="chevron-back"
                size={22}
                color={colors.textSecondary}
              />
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() =>
                setSelectedDate(new Date().toISOString().split("T")[0])
              }
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "700",
                  color: colors.text,
                  textAlign: "center",
                }}
              >
                {formatDateLabel(selectedDate)}
              </Text>
              {!isToday && (
                <Text
                  style={{
                    fontSize: 11,
                    color: colors.textMuted,
                    textAlign: "center",
                  }}
                >
                  {selectedDate}
                </Text>
              )}
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() =>
                !isToday && setSelectedDate(shiftDate(selectedDate, 1))
              }
              style={{ padding: 8, opacity: isToday ? 0.3 : 1 }}
            >
              <Ionicons
                name="chevron-forward"
                size={22}
                color={colors.textSecondary}
              />
            </TouchableOpacity>
          </View>

          {/* Meal Type Selector with Emojis */}
          <View
            style={{ flexDirection: "row", marginBottom: spacing.lg, gap: 6 }}
          >
            {MEAL_TYPES.map((type) => {
              const active = selectedMealType === type;
              return (
                <TouchableOpacity
                  key={type}
                  onPress={() => setSelectedMealType(type)}
                  style={{
                    flex: 1,
                    paddingVertical: 10,
                    borderRadius: radius.md,
                    backgroundColor: active ? colors.primary : colors.card,
                    alignItems: "center",
                    ...shadow,
                    borderWidth: active ? 0 : 1,
                    borderColor: colors.borderLight,
                  }}
                >
                  <Text style={{ fontSize: 18, marginBottom: 2 }}>
                    {mealTypeEmoji[type] ?? "🍽️"}
                  </Text>
                  <Text
                    style={{
                      fontSize: 11,
                      fontWeight: "600",
                      color: active ? "#fff" : colors.textSecondary,
                    }}
                  >
                    {type}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Input Buttons Row */}
          <View
            style={{ flexDirection: "row", gap: 8, marginBottom: spacing.lg }}
          >
            {showNaturalInput ? (
              <View
                style={{
                  flex: 1,
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  padding: spacing.lg,
                  ...shadowMd,
                }}
              >
                <TextInput
                  placeholder='e.g. "2 eggs, toast with butter, orange juice"'
                  placeholderTextColor={colors.textLight}
                  value={naturalText}
                  onChangeText={setNaturalText}
                  multiline
                  style={{ fontSize: 15, minHeight: 60, color: colors.text }}
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
                    onPress={() => setShowNaturalInput(false)}
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
                    disabled={parseMutation.isPending}
                    style={{
                      backgroundColor: colors.primary,
                      paddingHorizontal: 16,
                      paddingVertical: 8,
                      borderRadius: radius.sm,
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
            ) : showManualInput ? (
              <View
                style={{
                  flex: 1,
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  padding: spacing.lg,
                  ...shadowMd,
                }}
              >
                <TextInput
                  placeholder="Food name (e.g. Chicken breast)"
                  placeholderTextColor={colors.textLight}
                  value={manualFoodName}
                  onChangeText={setManualFoodName}
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
                    onPress={() => setShowManualInput(false)}
                    style={{ paddingHorizontal: 14, paddingVertical: 8 }}
                  >
                    <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
                      Cancel
                    </Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={() =>
                      manualFoodName.trim() && manualMutation.mutate()
                    }
                    disabled={manualMutation.isPending || !manualFoodName.trim()}
                    style={{
                      backgroundColor: colors.primary,
                      paddingHorizontal: 16,
                      paddingVertical: 8,
                      borderRadius: radius.sm,
                      opacity: manualFoodName.trim() ? 1 : 0.5,
                    }}
                  >
                    {manualMutation.isPending ? (
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
            ) : showParsedReview ? (
              <View
                style={{
                  flex: 1,
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  padding: spacing.lg,
                  ...shadowMd,
                }}
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
                {parsedItems.map((item, idx) => (
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
                      <TextInput
                        value={item.name}
                        onChangeText={(v) => updateParsedItem(idx, "name", v)}
                        style={{
                          fontWeight: "600",
                          fontSize: 14,
                          color: colors.text,
                          flex: 1,
                          borderBottomWidth: 1,
                          borderBottomColor: colors.borderLight,
                          paddingBottom: 4,
                        }}
                      />
                      <TouchableOpacity
                        onPress={() => openSwapSearch(idx)}
                        style={{ marginLeft: 6, padding: 4 }}
                      >
                        <Ionicons
                          name="swap-horizontal"
                          size={18}
                          color={colors.primary}
                        />
                      </TouchableOpacity>
                      <TouchableOpacity
                        onPress={() => removeParsedItem(idx)}
                        style={{ marginLeft: 4, padding: 4 }}
                      >
                        <Ionicons
                          name="close-circle"
                          size={20}
                          color={colors.danger}
                        />
                      </TouchableOpacity>
                    </View>
                    <View
                      style={{ flexDirection: "row", gap: 6, marginBottom: 4 }}
                    >
                      {[
                        { label: "Cal", field: "calories", value: item.calories },
                        {
                          label: "Prot",
                          field: "proteinG",
                          value: item.proteinG,
                        },
                        { label: "Carbs", field: "carbsG", value: item.carbsG },
                        { label: "Fat", field: "fatG", value: item.fatG },
                      ].map(({ label, field, value }) => (
                        <View key={field} style={{ flex: 1 }}>
                          <Text
                            style={{
                              fontSize: 9,
                              color: colors.textMuted,
                              marginBottom: 1,
                            }}
                          >
                            {label}
                          </Text>
                          <TextInput
                            value={String(Math.round(value))}
                            onChangeText={(v) => updateParsedItem(idx, field, v)}
                            keyboardType="numeric"
                            style={editFieldStyle}
                          />
                        </View>
                      ))}
                    </View>
                    <View style={{ flexDirection: "row", gap: 6 }}>
                      {[
                        { label: "Fiber", field: "fiberG", value: item.fiberG },
                        { label: "Sugar", field: "sugarG", value: item.sugarG },
                        { label: "Na", field: "sodiumMg", value: item.sodiumMg },
                        {
                          label: "Qty",
                          field: "servingQuantity",
                          value: item.servingQuantity,
                        },
                      ].map(({ label, field, value }) => (
                        <View key={field} style={{ flex: 1 }}>
                          <Text
                            style={{
                              fontSize: 9,
                              color: colors.textMuted,
                              marginBottom: 1,
                            }}
                          >
                            {label}
                          </Text>
                          <TextInput
                            value={String(Math.round(value))}
                            onChangeText={(v) => updateParsedItem(idx, field, v)}
                            keyboardType="numeric"
                            style={editFieldStyle}
                          />
                        </View>
                      ))}
                    </View>
                  </View>
                ))}
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
                    {Math.round(parsedItems.reduce((s, i) => s + i.calories, 0))}{" "}
                    cal
                  </Text>
                  <View style={{ flexDirection: "row", gap: 8 }}>
                    <TouchableOpacity
                      onPress={() => {
                        setParsedItems([]);
                        setShowParsedReview(false);
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
                      onPress={() =>
                        parsedItems.length > 0 && parsedMealMutation.mutate()
                      }
                      disabled={
                        parsedMealMutation.isPending || parsedItems.length === 0
                      }
                      style={{
                        backgroundColor: colors.primary,
                        paddingHorizontal: 16,
                        paddingVertical: 8,
                        borderRadius: radius.sm,
                      }}
                    >
                      {parsedMealMutation.isPending ? (
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
              </View>
            ) : (
              <>
                <TouchableOpacity
                  onPress={() => setShowNaturalInput(true)}
                  style={{
                    flex: 1,
                    backgroundColor: colors.primary,
                    borderRadius: radius.md,
                    padding: 14,
                    flexDirection: "row",
                    alignItems: "center",
                    justifyContent: "center",
                    ...shadowMd,
                  }}
                >
                  <Ionicons name="chatbubble-outline" size={18} color="#fff" />
                  <Text
                    style={{
                      color: "#fff",
                      fontWeight: "600",
                      marginLeft: 6,
                      fontSize: 14,
                    }}
                  >
                    Describe
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={() => setShowManualInput(true)}
                  style={{
                    flex: 1,
                    backgroundColor: colors.secondaryBg,
                    borderRadius: radius.md,
                    padding: 14,
                    alignItems: "center",
                    justifyContent: "center",
                    borderWidth: 1,
                    borderColor: colors.secondary,
                    ...shadow,
                  }}
                >
                  <Ionicons
                    name="create-outline"
                    size={18}
                    color={colors.secondary}
                  />
                  <Text
                    style={{
                      color: colors.secondary,
                      fontWeight: "600",
                      marginLeft: 6,
                      fontSize: 14,
                    }}
                  >
                    Manual
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={() => router.push("/(tabs)/scan")}
                  style={{
                    flex: 1,
                    backgroundColor: colors.accentBg,
                    borderRadius: radius.md,
                    padding: 14,
                    alignItems: "center",
                    justifyContent: "center",
                    borderWidth: 1,
                    borderColor: colors.accent,
                    ...shadow,
                  }}
                >
                  <Ionicons
                    name="search-outline"
                    size={20}
                    color={colors.accent}
                  />
                  <Text
                    style={{
                      color: colors.accent,
                      fontWeight: "600",
                      marginTop: 4,
                      fontSize: 12,
                    }}
                  >
                    Search
                  </Text>
                </TouchableOpacity>
              </>
            )}
        </View>

        {/* Daily Summary */}
        {dailySummary && (
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
            <View
              style={{ flexDirection: "row", justifyContent: "space-around" }}
            >
              <View style={{ alignItems: "center" }}>
                <Text
                  style={{
                    fontSize: 20,
                    fontWeight: "700",
                    color: colors.primary,
                  }}
                >
                  {Math.round(dailySummary.totalCalories)}
                </Text>
                <Text style={fonts.small}>
                  / {dailySummary.calorieGoal} cal
                </Text>
              </View>
              <View style={{ alignItems: "center" }}>
                <Text
                  style={{
                    fontSize: 17,
                    fontWeight: "600",
                    color: colors.protein,
                  }}
                >
                  {Math.round(dailySummary.totalProteinG)}g
                </Text>
                <Text style={fonts.small}>protein</Text>
              </View>
              <View style={{ alignItems: "center" }}>
                <Text
                  style={{
                    fontSize: 17,
                    fontWeight: "600",
                    color: colors.carbs,
                  }}
                >
                  {Math.round(dailySummary.totalCarbsG)}g
                </Text>
                <Text style={fonts.small}>carbs</Text>
              </View>
              <View style={{ alignItems: "center" }}>
                <Text
                  style={{ fontSize: 17, fontWeight: "600", color: colors.fat }}
                >
                  {Math.round(dailySummary.totalFatG)}g
                </Text>
                <Text style={fonts.small}>fat</Text>
              </View>
            </View>
          </View>
        )}

        {/* Filter Chips */}
        <View
          style={{ flexDirection: "row", marginBottom: spacing.md, gap: 6 }}
        >
          <TouchableOpacity
            onPress={() => setFilterMealType(null)}
            style={{
              paddingVertical: 6,
              paddingHorizontal: 14,
              borderRadius: radius.full,
              backgroundColor:
                filterMealType === null ? colors.text : colors.borderLight,
            }}
          >
            <Text
              style={{
                fontSize: 12,
                fontWeight: "600",
                color: filterMealType === null ? "#fff" : colors.textMuted,
              }}
            >
              All
            </Text>
          </TouchableOpacity>
          {MEAL_TYPES.map((type) => (
            <TouchableOpacity
              key={type}
              onPress={() =>
                setFilterMealType(filterMealType === type ? null : type)
              }
              style={{
                paddingVertical: 6,
                paddingHorizontal: 14,
                borderRadius: radius.full,
                backgroundColor:
                  filterMealType === type ? colors.text : colors.borderLight,
              }}
            >
              <Text
                style={{
                  fontSize: 12,
                  fontWeight: "600",
                  color: filterMealType === type ? "#fff" : colors.textMuted,
                }}
              >
                {mealTypeEmoji[type]} {type}
              </Text>
            </TouchableOpacity>
          ))}
        </View>

        {/* Meal List */}
        {isLoading ? (
          <>
            <MealCardSkeleton />
            <MealCardSkeleton />
            <MealCardSkeleton />
          </>
        ) : isError ? (
          <View style={{ alignItems: "center", marginTop: 40 }}>
            <Ionicons
              name="cloud-offline-outline"
              size={48}
              color={colors.danger}
            />
            <Text
              style={{
                color: colors.danger,
                marginTop: spacing.md,
                fontSize: 16,
              }}
            >
              Failed to load meals
            </Text>
            <TouchableOpacity
              onPress={() => refetch()}
              style={{
                marginTop: spacing.md,
                backgroundColor: colors.primary,
                paddingHorizontal: 20,
                paddingVertical: 8,
                borderRadius: radius.sm,
              }}
            >
              <Text style={{ color: "#fff", fontWeight: "600" }}>Retry</Text>
            </TouchableOpacity>
          </View>
        ) : filteredMeals && filteredMeals.length > 0 ? (
          (() => {
            const mealTypeOrder = ["Breakfast", "Lunch", "Dinner", "Snack"];
            const grouped: Record<string, MealLog[]> = {};
            for (const meal of filteredMeals) {
              const key = meal.mealType || "Other";
              if (!grouped[key]) grouped[key] = [];
              grouped[key].push(meal);
            }
            const sortedTypes = Object.keys(grouped).sort(
              (a, b) =>
                (mealTypeOrder.indexOf(a) === -1
                  ? 99
                  : mealTypeOrder.indexOf(a)) -
                (mealTypeOrder.indexOf(b) === -1
                  ? 99
                  : mealTypeOrder.indexOf(b)),
            );
            return sortedTypes.map((type) => {
              const mealsInGroup = grouped[type];
              const totalCals = mealsInGroup.reduce(
                (s, m) => s + m.totalCalories,
                0,
              );
              return (
                <View
                  key={type}
                  style={{
                    backgroundColor: colors.card,
                    borderRadius: radius.md,
                    padding: spacing.lg,
                    marginBottom: spacing.sm,
                    ...shadow,
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
                    <View
                      style={{ flexDirection: "row", alignItems: "center" }}
                    >
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
                        <Text style={{ fontSize: 18 }}>
                          {mealTypeEmoji[type] ?? "🍽️"}
                        </Text>
                      </View>
                      <View>
                        <Text
                          style={{
                            fontSize: 15,
                            fontWeight: "600",
                            color: colors.text,
                          }}
                        >
                          {type}
                        </Text>
                        <Text
                          style={{
                            fontSize: 12,
                            color: colors.textMuted,
                            marginTop: 1,
                          }}
                        >
                          {mealsInGroup.length}{" "}
                          {mealsInGroup.length === 1 ? "entry" : "entries"}
                        </Text>
                      </View>
                    </View>
                    <Text
                      style={{
                        fontSize: 16,
                        fontWeight: "700",
                        color: colors.text,
                      }}
                    >
                      {totalCals}{" "}
                      <Text style={{ fontSize: 12, color: colors.textMuted }}>
                        cal
                      </Text>
                    </Text>
                  </View>
                  {mealsInGroup.map((meal) => (
                    <View key={meal.id}>
                      <View
                        style={{
                          flexDirection: "row",
                          alignItems: "center",
                          justifyContent: "space-between",
                          paddingTop: 6,
                          paddingBottom: 2,
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
                        <View style={{ flexDirection: "row", gap: 10 }}>
                          <TouchableOpacity onPress={() => openEdit(meal)}>
                            <Ionicons
                              name="pencil-outline"
                              size={16}
                              color={colors.secondary}
                            />
                          </TouchableOpacity>
                          <TouchableOpacity
                            onPress={() => handleDelete(meal.id)}
                          >
                            <Ionicons
                              name="trash-outline"
                              size={16}
                              color={colors.danger}
                            />
                          </TouchableOpacity>
                        </View>
                      </View>
                      {meal.items.map((item) => (
                        <Pressable
                          key={item.id}
                          onPress={() => handleItemTap(item)}
                          onLongPress={() => openItemSheet(item)}
                          style={({ pressed }) => ({
                            flexDirection: "row" as const,
                            justifyContent: "space-between" as const,
                            alignItems: "center" as const,
                            marginTop: 4,
                            paddingTop: 6,
                            paddingBottom: 4,
                            paddingHorizontal: 4,
                            borderTopWidth: 1,
                            borderTopColor: colors.divider,
                            borderRadius: radius.sm,
                            backgroundColor: pressed
                              ? colors.borderLight
                              : "transparent",
                          })}
                        >
                          <Text
                            style={{ color: colors.textSecondary, flex: 1 }}
                          >
                            {item.foodName}
                            <Text style={{ color: colors.textMuted }}>
                              {" "}
                              · {item.servings} {item.servingUnit}
                            </Text>
                          </Text>
                          <View
                            style={{
                              flexDirection: "row",
                              alignItems: "center",
                              gap: 6,
                            }}
                          >
                            <Text
                              style={{
                                color: colors.textSecondary,
                                fontWeight: "500",
                              }}
                            >
                              {item.calories} cal
                            </Text>
                            <Ionicons
                              name="chevron-forward"
                              size={14}
                              color={colors.textLight}
                            />
                          </View>
                        </Pressable>
                      ))}
                    </View>
                  ))}
                </View>
              );
            });
          })()
        ) : (
          <View style={{ alignItems: "center", paddingVertical: 48 }}>
            <Ionicons
              name="restaurant-outline"
              size={48}
              color={colors.textLight}
            />
            <Text
              style={{
                color: colors.textMuted,
                marginTop: spacing.md,
                fontSize: 15,
              }}
            >
              {filterMealType
                ? "No meals found"
                : "No meals logged for this date"}
            </Text>
          </View>
        )}
      </View>

      </ScrollView>

      {/* Food Info Bottom Sheet */}
      <BottomSheet
        visible={!!sheetItem}
        onClose={() => setSheetItem(null)}
        maxHeight="70%"
      >
        <Text style={{ ...fonts.h3, marginBottom: 4 }}>
          {sheetItem?.foodName}
        </Text>
        <Text
          style={{
            fontSize: 13,
            color: colors.textMuted,
            marginBottom: spacing.lg,
          }}
        >
          {sheetItem?.calories} cal · {sheetItem?.proteinG}g protein ·{" "}
          {sheetItem?.carbsG}g carbs · {sheetItem?.fatG}g fat
        </Text>

        {sheetLoading ? (
          <View style={{ alignItems: "center", paddingVertical: spacing.xxl }}>
            <ActivityIndicator size="large" color={colors.primary} />
            <Text
              style={{
                color: colors.textMuted,
                marginTop: spacing.md,
                fontSize: 13,
              }}
            >
              Looking up food details…
            </Text>
          </View>
        ) : sheetProduct && sheetReport ? (
          <ScrollView
            showsVerticalScrollIndicator={false}
            style={{ maxHeight: 400 }}
          >
            {/* Quick badges row */}
            <View
              style={{
                flexDirection: "row",
                gap: 8,
                marginBottom: spacing.lg,
                flexWrap: "wrap",
              }}
            >
              {sheetProduct.novaGroup != null && (
                <View
                  style={{
                    backgroundColor: colors.borderLight,
                    borderRadius: radius.sm,
                    paddingHorizontal: 10,
                    paddingVertical: 6,
                  }}
                >
                  <Text style={{ fontSize: 11, color: colors.textMuted }}>
                    NOVA
                  </Text>
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "700",
                      color: colors.text,
                    }}
                  >
                    {sheetProduct.novaGroup}
                  </Text>
                </View>
              )}
              {sheetProduct.nutriScore &&
                !sheetProduct.nutriScore.toLowerCase().includes("not") && (
                  <View
                    style={{
                      backgroundColor: colors.borderLight,
                      borderRadius: radius.sm,
                      paddingHorizontal: 10,
                      paddingVertical: 6,
                    }}
                  >
                    <Text style={{ fontSize: 11, color: colors.textMuted }}>
                      NutriScore
                    </Text>
                    <Text
                      style={{
                        fontSize: 16,
                        fontWeight: "700",
                        color: ratingColor(sheetProduct.nutriScore),
                        textTransform: "uppercase",
                      }}
                    >
                      {sheetProduct.nutriScore}
                    </Text>
                  </View>
                )}
              {sheetReport.gutRisk && (
                <View
                  style={{
                    backgroundColor: colors.borderLight,
                    borderRadius: radius.sm,
                    paddingHorizontal: 10,
                    paddingVertical: 6,
                  }}
                >
                  <Text style={{ fontSize: 11, color: colors.textMuted }}>
                    Gut Score
                  </Text>
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "700",
                      color: colors.text,
                    }}
                  >
                    {sheetReport.gutRisk.gutScore}/100
                  </Text>
                </View>
              )}
              {sheetReport.fodmap && (
                <View
                  style={{
                    backgroundColor: colors.borderLight,
                    borderRadius: radius.sm,
                    paddingHorizontal: 10,
                    paddingVertical: 6,
                  }}
                >
                  <Text style={{ fontSize: 11, color: colors.textMuted }}>
                    FODMAP
                  </Text>
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "700",
                      color: colors.text,
                    }}
                  >
                    {sheetReport.fodmap.fodmapRating}
                  </Text>
                </View>
              )}
            </View>

            {/* Allergens */}
            {sheetProduct.allergensTags.length > 0 && (
              <View style={{ marginBottom: spacing.lg }}>
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "600",
                    color: colors.textSecondary,
                    marginBottom: 6,
                  }}
                >
                  Allergens
                </Text>
                <View
                  style={{
                    flexDirection: "row",
                    flexWrap: "wrap",
                    gap: 6,
                  }}
                >
                  {sheetProduct.allergensTags.map((tag) => (
                    <View
                      key={tag}
                      style={{
                        backgroundColor: "#fef2f2",
                        borderRadius: radius.sm,
                        paddingHorizontal: 8,
                        paddingVertical: 4,
                      }}
                    >
                      <Text style={{ fontSize: 12, color: "#b91c1c" }}>
                        {tag
                          .replace("en:", "")
                          .split(/[\s-]+/)
                          .map(
                            (w) =>
                              w.charAt(0).toUpperCase() +
                              w.slice(1).toLowerCase(),
                          )
                          .join(" ")}
                      </Text>
                    </View>
                  ))}
                </View>
              </View>
            )}

            {/* Additives */}
            {sheetReport.additives.length > 0 && (
              <View style={{ marginBottom: spacing.lg }}>
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "600",
                    color: colors.textSecondary,
                    marginBottom: 6,
                  }}
                >
                  Additives ({sheetReport.additives.length})
                </Text>
                {sheetReport.additives.slice(0, 5).map((add) => (
                  <View
                    key={add.id}
                    style={{
                      flexDirection: "row",
                      justifyContent: "space-between",
                      alignItems: "center",
                      paddingVertical: 6,
                      borderBottomWidth: 1,
                      borderBottomColor: colors.divider,
                    }}
                  >
                    <View style={{ flex: 1 }}>
                      <Text
                        style={{
                          fontSize: 13,
                          fontWeight: "500",
                          color: colors.text,
                        }}
                      >
                        {add.name}
                        {add.eNumber ? ` (${add.eNumber})` : ""}
                      </Text>
                      {add.healthConcerns ? (
                        <Text
                          style={{
                            fontSize: 11,
                            color: colors.textMuted,
                            marginTop: 1,
                          }}
                          numberOfLines={1}
                        >
                          {add.healthConcerns}
                        </Text>
                      ) : null}
                    </View>
                    <View
                      style={{
                        backgroundColor: colors.borderLight,
                        borderRadius: radius.sm,
                        paddingHorizontal: 6,
                        paddingVertical: 2,
                        marginLeft: 8,
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 10,
                          fontWeight: "600",
                          color: ratingColor(add.cspiRating),
                        }}
                      >
                        {add.cspiRating}
                      </Text>
                    </View>
                  </View>
                ))}
                {sheetReport.additives.length > 5 && (
                  <Text
                    style={{
                      fontSize: 12,
                      color: colors.textMuted,
                      marginTop: 6,
                    }}
                  >
                    +{sheetReport.additives.length - 5} more
                  </Text>
                )}
              </View>
            )}

            {/* Ingredients */}
            {sheetProduct.ingredients && (
              <View style={{ marginBottom: spacing.lg }}>
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "600",
                    color: colors.textSecondary,
                    marginBottom: 4,
                  }}
                >
                  Ingredients
                </Text>
                <Text
                  style={{ fontSize: 12, color: colors.textMuted }}
                  numberOfLines={3}
                >
                  {sheetProduct.ingredients}
                </Text>
              </View>
            )}

            {/* Full details button */}
            <TouchableOpacity
              onPress={() => {
                setSheetItem(null);
                router.push(`/food/${sheetProduct.id}`);
              }}
              style={{
                backgroundColor: colors.primary,
                borderRadius: radius.sm,
                paddingVertical: 12,
                alignItems: "center",
                marginBottom: spacing.md,
              }}
            >
              <Text style={{ color: "#fff", fontWeight: "600", fontSize: 15 }}>
                Full Details →
              </Text>
            </TouchableOpacity>
          </ScrollView>
        ) : (
          <View style={{ alignItems: "center", paddingVertical: spacing.lg }}>
            <Ionicons
              name="information-circle-outline"
              size={32}
              color={colors.textLight}
            />
            <Text
              style={{
                color: colors.textMuted,
                marginTop: spacing.sm,
                fontSize: 13,
                textAlign: "center",
              }}
            >
              No detailed product info found for this item.
            </Text>
          </View>
        )}
      </BottomSheet>

      {/* Edit Meal Modal */}
      <BottomSheet visible={!!editingMeal} onClose={() => setEditingMeal(null)}>
        <Text style={{ ...fonts.h3, marginBottom: spacing.md }}>Edit Meal</Text>
        <View
          style={{ flexDirection: "row", marginBottom: spacing.lg, gap: 6 }}
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
                  backgroundColor: active ? colors.primary : colors.borderLight,
                  alignItems: "center",
                }}
              >
                <Text
                  style={{
                    fontSize: 12,
                    fontWeight: "600",
                    color: active ? "#fff" : colors.textMuted,
                  }}
                >
                  {mealTypeEmoji[t]} {t}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>
        <ScrollView style={{ maxHeight: 360 }}>
          {editItems.map((item, idx) => {
            const cfg = editItemConfigs[idx] ?? {
              servingG: item.servingWeightG ?? 100,
              multiplier: 1,
              customText: "",
            };
            const totalG = cfg.servingG * cfg.multiplier;
            const origG = item.servingWeightG ?? 100;
            const scale = totalG / origG;
            const origServing = item.servingWeightG ?? 100;
            const presets: { label: string; grams: number }[] = [
              { label: `${origServing}g`, grams: origServing },
              { label: "50g", grams: 50 },
              { label: "100g", grams: 100 },
              { label: "150g", grams: 150 },
              { label: "200g", grams: 200 },
            ];
            const seen = new Set<number>();
            const uniquePresets = presets.filter((p) => {
              if (seen.has(p.grams)) return false;
              seen.add(p.grams);
              return true;
            });
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
                    onPress={() => removeEditItem(idx)}
                    style={{ marginLeft: 8 }}
                  >
                    <Ionicons
                      name="close-circle"
                      size={20}
                      color={colors.danger}
                    />
                  </TouchableOpacity>
                </View>
                <Text
                  style={{
                    fontSize: 11,
                    fontWeight: "600",
                    color: colors.textMuted,
                    marginBottom: 4,
                  }}
                >
                  Serving size:
                </Text>
                <View
                  style={{
                    flexDirection: "row",
                    flexWrap: "wrap",
                    gap: 6,
                    marginBottom: 6,
                  }}
                >
                  {uniquePresets.map((preset) => (
                    <TouchableOpacity
                      key={preset.label}
                      onPress={() => {
                        setEditItemConfigs((prev) => ({
                          ...prev,
                          [idx]: {
                            ...cfg,
                            servingG: preset.grams,
                            customText: "",
                          },
                        }));
                      }}
                      style={{
                        paddingHorizontal: 10,
                        paddingVertical: 5,
                        borderRadius: 6,
                        backgroundColor:
                          cfg.servingG === preset.grams && cfg.customText === ""
                            ? colors.primary
                            : colors.borderLight,
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 11,
                          fontWeight: "600",
                          color:
                            cfg.servingG === preset.grams &&
                            cfg.customText === ""
                              ? "#fff"
                              : colors.textMuted,
                        }}
                      >
                        {preset.label}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </View>
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    gap: 6,
                    marginBottom: 6,
                  }}
                >
                  <Text
                    style={{
                      fontSize: 11,
                      fontWeight: "600",
                      color: colors.textMuted,
                    }}
                  >
                    Custom (g):
                  </Text>
                  <TextInput
                    value={cfg.customText}
                    onChangeText={(v) => {
                      const n = Number(v);
                      setEditItemConfigs((prev) => ({
                        ...prev,
                        [idx]: {
                          ...cfg,
                          customText: v,
                          ...(n > 0 ? { servingG: n } : {}),
                        },
                      }));
                    }}
                    keyboardType="numeric"
                    placeholder="e.g. 75"
                    placeholderTextColor={colors.textLight}
                    style={{
                      flex: 1,
                      borderWidth: 1,
                      borderColor:
                        cfg.customText !== "" ? colors.primary : colors.border,
                      borderRadius: 6,
                      paddingHorizontal: 8,
                      paddingVertical: 3,
                      fontSize: 12,
                      color: colors.text,
                      backgroundColor: colors.card,
                    }}
                  />
                </View>
                <Text
                  style={{
                    fontSize: 11,
                    fontWeight: "600",
                    color: colors.textMuted,
                    marginBottom: 4,
                  }}
                >
                  Multiplier:
                </Text>
                <View style={{ flexDirection: "row", gap: 6, marginBottom: 6 }}>
                  {[1, 2, 3, 4, 5].map((m) => (
                    <TouchableOpacity
                      key={m}
                      onPress={() => {
                        setEditItemConfigs((prev) => ({
                          ...prev,
                          [idx]: { ...cfg, multiplier: m },
                        }));
                      }}
                      style={{
                        flex: 1,
                        paddingVertical: 5,
                        borderRadius: 6,
                        backgroundColor:
                          cfg.multiplier === m
                            ? colors.primary
                            : colors.borderLight,
                        alignItems: "center",
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 11,
                          fontWeight: "600",
                          color:
                            cfg.multiplier === m ? "#fff" : colors.textMuted,
                        }}
                      >
                        {m}×
                      </Text>
                    </TouchableOpacity>
                  ))}
                </View>
                <Text style={{ fontSize: 11, color: colors.textMuted }}>
                  {totalG}g total · {Math.round(item.calories * scale)} cal ·{" "}
                  {Math.round(item.proteinG * scale)}g P ·{" "}
                  {Math.round(item.carbsG * scale)}g C ·{" "}
                  {Math.round(item.fatG * scale)}g F
                </Text>
              </View>
            );
          })}
        </ScrollView>
        <View
          style={{
            flexDirection: "row",
            justifyContent: "flex-end",
            marginTop: spacing.lg,
            gap: 12,
          }}
        >
          <TouchableOpacity
            onPress={() => setEditingMeal(null)}
            style={{ paddingHorizontal: 20, paddingVertical: 10 }}
          >
            <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
              Cancel
            </Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={saveEdit}
            disabled={updateMutation.isPending}
            style={{
              backgroundColor: colors.primary,
              paddingHorizontal: 20,
              paddingVertical: 10,
              borderRadius: radius.sm,
            }}
          >
            {updateMutation.isPending ? (
              <ActivityIndicator color="#fff" size="small" />
            ) : (
              <Text style={{ color: "#fff", fontWeight: "600" }}>Save</Text>
            )}
          </TouchableOpacity>
        </View>
      </BottomSheet>

      {/* Swap Food Search Modal */}
      <BottomSheet
        visible={swapIndex !== null}
        onClose={() => {
          setSwapIndex(null);
          setSwapSearch("");
        }}
        maxHeight="80%"
      >
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: spacing.md,
          }}
        >
          <Text style={{ fontSize: 16, fontWeight: "700", color: colors.text }}>
            Swap food item
          </Text>
          <TouchableOpacity
            onPress={() => {
              setSwapIndex(null);
              setSwapSearch("");
            }}
          >
            <Ionicons name="close" size={24} color={colors.textMuted} />
          </TouchableOpacity>
        </View>
        <TextInput
          placeholder="Search foods..."
          placeholderTextColor={colors.textLight}
          value={swapSearch}
          onChangeText={setSwapSearch}
          autoFocus
          style={{
            backgroundColor: colors.bg,
            borderRadius: radius.sm,
            padding: spacing.md,
            fontSize: 15,
            color: colors.text,
            borderWidth: 1,
            borderColor: colors.border,
            marginBottom: spacing.md,
          }}
        />
        <ScrollView
          style={{ maxHeight: 400 }}
          keyboardShouldPersistTaps="handled"
        >
          {swapSearchResults.isLoading && (
            <ActivityIndicator
              color={colors.primary}
              style={{ marginVertical: spacing.lg }}
            />
          )}
          {swapSearchResults.data?.map((food) => (
            <TouchableOpacity
              key={food.id || food.name}
              onPress={() => selectSwapFood(food)}
              style={{
                paddingVertical: spacing.sm,
                paddingHorizontal: spacing.md,
                borderBottomWidth: 1,
                borderBottomColor: colors.borderLight,
              }}
            >
              <Text
                style={{
                  fontSize: 14,
                  fontWeight: "600",
                  color: colors.text,
                }}
                numberOfLines={1}
              >
                {food.name}
              </Text>
              <Text
                style={{
                  fontSize: 11,
                  color: colors.textMuted,
                  marginTop: 2,
                }}
              >
                {food.calories100g != null
                  ? `${Math.round(food.calories100g)} cal/100g`
                  : ""}
                {food.brand ? ` · ${food.brand}` : ""}
                {food.dataSource ? ` · ${food.dataSource}` : ""}
              </Text>
            </TouchableOpacity>
          ))}
          {debouncedSwapSearch.length >= 2 &&
            !swapSearchResults.isLoading &&
            swapSearchResults.data?.length === 0 && (
              <Text
                style={{
                  textAlign: "center",
                  color: colors.textMuted,
                  padding: spacing.lg,
                }}
              >
                No results found
              </Text>
            )}
          {debouncedSwapSearch.length < 2 && (
            <Text
              style={{
                textAlign: "center",
                color: colors.textMuted,
                padding: spacing.lg,
              }}
            >
              Type at least 2 characters to search
            </Text>
          )}
        </ScrollView>
      </BottomSheet>
    </SafeScreen>
  );
}
