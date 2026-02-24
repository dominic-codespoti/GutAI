import { useState, useCallback } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  TextInput,
  ActivityIndicator,
  Modal,
  RefreshControl,
  Pressable,
} from "react-native";
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
import { MEAL_TYPES } from "../../src/utils/constants";
import { shiftDate, formatDateLabel } from "../../src/utils/date";
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

const ratingColor = (rating: string | null) => {
  switch (rating?.toLowerCase()) {
    case "safe":
    case "a":
      return colors.primary;
    case "low concern":
    case "b":
      return "#4ade80";
    case "moderate concern":
    case "c":
      return "#facc15";
    case "high concern":
    case "d":
      return "#fb923c";
    case "avoid":
    case "e":
      return colors.danger;
    default:
      return colors.textMuted;
  }
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
        items: editItems.map((it) => ({
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
        })),
      },
    });
  };

  return (
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
              <Text style={{ fontWeight: "700", fontSize: 15, color: colors.text, marginBottom: spacing.md }}>
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
                  <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: 6 }}>
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
                      onPress={() => removeParsedItem(idx)}
                      style={{ marginLeft: 8, padding: 4 }}
                    >
                      <Ionicons name="close-circle" size={20} color={colors.danger} />
                    </TouchableOpacity>
                  </View>
                  <View style={{ flexDirection: "row", gap: 6, marginBottom: 4 }}>
                    {[
                      { label: "Cal", field: "calories", value: item.calories },
                      { label: "Prot", field: "proteinG", value: item.proteinG },
                      { label: "Carbs", field: "carbsG", value: item.carbsG },
                      { label: "Fat", field: "fatG", value: item.fatG },
                    ].map(({ label, field, value }) => (
                      <View key={field} style={{ flex: 1 }}>
                        <Text style={{ fontSize: 9, color: colors.textMuted, marginBottom: 1 }}>{label}</Text>
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
                      { label: "Qty", field: "servingQuantity", value: item.servingQuantity },
                    ].map(({ label, field, value }) => (
                      <View key={field} style={{ flex: 1 }}>
                        <Text style={{ fontSize: 9, color: colors.textMuted, marginBottom: 1 }}>{label}</Text>
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
              <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginTop: spacing.sm }}>
                <Text style={{ fontSize: 12, color: colors.textMuted }}>
                  Total: {Math.round(parsedItems.reduce((s, i) => s + i.calories, 0))} cal
                </Text>
                <View style={{ flexDirection: "row", gap: 8 }}>
                  <TouchableOpacity
                    onPress={() => { setParsedItems([]); setShowParsedReview(false); }}
                    style={{ paddingHorizontal: 14, paddingVertical: 8 }}
                  >
                    <Text style={{ color: colors.textMuted, fontWeight: "600" }}>Cancel</Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={() => parsedItems.length > 0 && parsedMealMutation.mutate()}
                    disabled={parsedMealMutation.isPending || parsedItems.length === 0}
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
                      <Text style={{ color: "#fff", fontWeight: "600", fontSize: 14 }}>
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
                  Text input
                </Text>
              </TouchableOpacity>
              <TouchableOpacity
                onPress={() => setShowManualInput(true)}
                style={{
                  flex: 1,
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  padding: 14,
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "center",
                  borderWidth: 1,
                  borderColor: colors.border,
                  ...shadow,
                }}
              >
                <Ionicons
                  name="add-outline"
                  size={18}
                  color={colors.textSecondary}
                />
                <Text
                  style={{
                    color: colors.textSecondary,
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
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  padding: 14,
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "center",
                  borderWidth: 1,
                  borderColor: colors.border,
                  ...shadow,
                }}
              >
                <Ionicons
                  name="barcode-outline"
                  size={18}
                  color={colors.textSecondary}
                />
                <Text
                  style={{
                    color: colors.textSecondary,
                    fontWeight: "600",
                    marginLeft: 6,
                    fontSize: 14,
                  }}
                >
                  Barcode
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
          filteredMeals.map((meal) => (
            <View
              key={meal.id}
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
                    <Text style={{ fontSize: 18 }}>
                      {mealTypeEmoji[meal.mealType] ?? "🍽️"}
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
                      {meal.mealType}
                    </Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.textMuted,
                        marginTop: 1,
                      }}
                    >
                      {new Date(meal.loggedAt).toLocaleTimeString([], {
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </Text>
                  </View>
                </View>
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    gap: 10,
                  }}
                >
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "700",
                      color: colors.text,
                    }}
                  >
                    {meal.totalCalories}
                  </Text>
                  <Text style={{ fontSize: 12, color: colors.textMuted }}>
                    cal
                  </Text>
                  <TouchableOpacity onPress={() => openEdit(meal)}>
                    <Ionicons
                      name="pencil-outline"
                      size={18}
                      color={colors.secondary}
                    />
                  </TouchableOpacity>
                  <TouchableOpacity onPress={() => handleDelete(meal.id)}>
                    <Ionicons
                      name="trash-outline"
                      size={18}
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
                    marginTop: 8,
                    paddingTop: 8,
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
                  <Text style={{ color: colors.textSecondary, flex: 1 }}>
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
          ))
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

      {/* Food Info Bottom Sheet */}
      <Modal
        visible={!!sheetItem}
        animationType="slide"
        transparent
        onRequestClose={() => setSheetItem(null)}
      >
        <Pressable
          style={{
            flex: 1,
            backgroundColor: "rgba(0,0,0,0.4)",
            justifyContent: "flex-end",
          }}
          onPress={() => setSheetItem(null)}
        >
          <Pressable
            style={{
              backgroundColor: colors.card,
              borderTopLeftRadius: radius.xl,
              borderTopRightRadius: radius.xl,
              padding: spacing.xxl,
              maxHeight: "70%",
            }}
            onPress={() => {}}
          >
            <View
              style={{
                width: 36,
                height: 4,
                borderRadius: 2,
                backgroundColor: colors.borderLight,
                alignSelf: "center",
                marginBottom: spacing.lg,
              }}
            />
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
              <View
                style={{ alignItems: "center", paddingVertical: spacing.xxl }}
              >
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
                  <Text
                    style={{ color: "#fff", fontWeight: "600", fontSize: 15 }}
                  >
                    Full Details →
                  </Text>
                </TouchableOpacity>
              </ScrollView>
            ) : (
              <View
                style={{ alignItems: "center", paddingVertical: spacing.lg }}
              >
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
          </Pressable>
        </Pressable>
      </Modal>

      {/* Edit Meal Modal */}
      <Modal visible={!!editingMeal} animationType="slide" transparent>
        <View
          style={{
            flex: 1,
            backgroundColor: "rgba(0,0,0,0.5)",
            justifyContent: "flex-end",
          }}
        >
          <View
            style={{
              backgroundColor: colors.card,
              borderTopLeftRadius: radius.xl,
              borderTopRightRadius: radius.xl,
              padding: spacing.xxl,
              maxHeight: "85%",
            }}
          >
            <Text style={{ ...fonts.h3, marginBottom: spacing.md }}>
              Edit Meal
            </Text>
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
                      backgroundColor: active
                        ? colors.primary
                        : colors.borderLight,
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
              {editItems.map((item, idx) => (
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
                    <TextInput
                      value={item.foodName}
                      onChangeText={(v) => updateEditItem(idx, "foodName", v)}
                      style={{
                        flex: 1,
                        fontWeight: "600",
                        fontSize: 15,
                        color: colors.text,
                      }}
                    />
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
                  <View style={{ flexDirection: "row", gap: 6 }}>
                    {[
                      { label: "Cal", field: "calories", val: item.calories },
                      {
                        label: "Protein",
                        field: "proteinG",
                        val: item.proteinG,
                      },
                      { label: "Carbs", field: "carbsG", val: item.carbsG },
                      { label: "Fat", field: "fatG", val: item.fatG },
                    ].map(({ label, field, val }) => (
                      <View key={field} style={{ flex: 1 }}>
                        <Text style={{ fontSize: 10, color: colors.textMuted }}>
                          {label}
                        </Text>
                        <TextInput
                          value={String(val)}
                          onChangeText={(v) => updateEditItem(idx, field, v)}
                          keyboardType="numeric"
                          style={editFieldStyle}
                        />
                      </View>
                    ))}
                  </View>
                  <View style={{ flexDirection: "row", gap: 6, marginTop: 6 }}>
                    {[
                      { label: "Fiber", field: "fiberG", val: item.fiberG },
                      { label: "Sugar", field: "sugarG", val: item.sugarG },
                      {
                        label: "Na (mg)",
                        field: "sodiumMg",
                        val: item.sodiumMg,
                      },
                    ].map(({ label, field, val }) => (
                      <View key={field} style={{ flex: 1 }}>
                        <Text style={{ fontSize: 10, color: colors.textMuted }}>
                          {label}
                        </Text>
                        <TextInput
                          value={String(val)}
                          onChangeText={(v) => updateEditItem(idx, field, v)}
                          keyboardType="numeric"
                          style={editFieldStyle}
                        />
                      </View>
                    ))}
                  </View>
                </View>
              ))}
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
          </View>
        </View>
      </Modal>
    </ScrollView>
  );
}
