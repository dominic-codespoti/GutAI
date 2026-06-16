import { useState, useEffect } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ActivityIndicator,
  ScrollView,
  Platform,
} from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { CameraView, useCameraPermissions } from "expo-camera";
import * as Haptics from "expo-haptics";
import { BottomSheet } from "../BottomSheet";
import { ServingSizeSelector } from "../ServingSizeSelector";
import { FoodSearchResult } from "../FoodSearchResult";
import { SwapSearchContent } from "./SwapSearchContent";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { useFavorites } from "../../src/hooks/useFavorites";
import { mealApi, foodApi } from "../../src/api";
import { toast } from "../../src/stores/toast";
import { useSubscriptionStore, presentPaywall } from "../../src/stores/subscription";
import { mapParsedItemToRequest } from "../../src/utils/mealMappers";
import { normalizeCustomFood, customFoodToMealItem } from "../../src/utils/customFood";
import type { AiGeneratedFood } from "../../src/utils/customFood";
import { radius, spacing } from "../../src/utils/theme";
import { useThemeColors } from "../../src/stores/theme";
import { buildLoggedAt } from "../../src/utils/date";
import { scaleNutrition, nutritionSummaryText } from "../../src/utils/nutrition";
import { maybeRequestReview } from "../../src/utils/review";
import { SearchResultSkeleton } from "../SkeletonLoader";
import type { ParsedFoodItem, FoodProduct, FavoriteFood, RecentFood } from "../../src/types";

const EMPTY_PRODUCT_ID = "00000000-0000-0000-0000-000000000000";
const MAX_CALORIES = 10000;
const MAX_MACRO_G = 2000;
const MAX_SODIUM_MG = 50000;
const MAX_SERVINGS = 100;

const sanitizeInt = (text: string): string => text.replace(/[^0-9]/g, "");
const sanitizeDecimal = (text: string): string => {
  const cleaned = text.replace(/[^0-9.]/g, "");
  const parts = cleaned.split(".");
  return parts.length <= 2 ? cleaned : parts[0] + "." + parts.slice(1).join("");
};

type LogMealTab = "describe" | "search" | "scan" | "manual";
type LogSubView = "menu" | "swap" | "add-to-meal" | "camera";

function toPer100g(value: number | null | undefined, servingWeightG?: number) {
  if (value == null) return null;
  if (servingWeightG && servingWeightG > 0) {
    return Math.round((value * 1000) / servingWeightG) / 10;
  }
  return value;
}

function mapFavoriteFoodToProduct(food: FavoriteFood): FoodProduct {
  return {
    id: food.foodProductId,
    barcode: null,
    name: food.foodName,
    brand: food.brand,
    ingredients: null,
    imageUrl: food.imageUrl,
    imageFrontUrl: null,
    imageIngredientsUrl: null,
    imageNutritionUrl: null,
    novaGroup: null,
    nutriScore: null,
    allergensTags: [],
    calories100g: food.calories100g,
    protein100g: food.protein100g,
    carbs100g: food.carbs100g,
    fat100g: food.fat100g,
    fiber100g: null,
    sugar100g: null,
    sodium100g: null,
    servingSize: food.servingSize,
    servingQuantity: food.servingQuantity ?? food.servingWeightG,
    safetyScore: null,
    safetyRating: null,
    foodKind: "Unknown",
    dataSource: null,
    sourceUrl: null,
    externalId: null,
    additives: [],
    matchConfidence: 1,
  };
}

function mapRecentFoodToProduct(food: RecentFood): FoodProduct {
  return {
    id: food.foodProductId ?? EMPTY_PRODUCT_ID,
    barcode: null,
    name: food.foodName,
    brand: null,
    ingredients: null,
    imageUrl: null,
    imageFrontUrl: null,
    imageIngredientsUrl: null,
    imageNutritionUrl: null,
    novaGroup: null,
    nutriScore: null,
    allergensTags: [],
    calories100g: toPer100g(food.calories, food.servingWeightG),
    protein100g: toPer100g(food.proteinG, food.servingWeightG),
    carbs100g: toPer100g(food.carbsG, food.servingWeightG),
    fat100g: toPer100g(food.fatG, food.servingWeightG),
    fiber100g: toPer100g(food.fiberG, food.servingWeightG),
    sugar100g: toPer100g(food.sugarG, food.servingWeightG),
    sodium100g: toPer100g(food.sodiumMg, food.servingWeightG),
    servingSize: food.servingUnit || "serving",
    servingQuantity: food.servingWeightG ?? null,
    safetyScore: null,
    safetyRating: null,
    foodKind: "Unknown",
    dataSource: null,
    sourceUrl: null,
    externalId: null,
    additives: [],
    matchConfidence: 1,
  };
}

export function LogMealSheet() {
  const colors = useThemeColors();
  const router = useRouter();
  const queryClient = useQueryClient();

  const mode = useMealSheetStore((s) => s.mode);
  const selectedMealType = useMealSheetStore((s) => s.selectedMealType);
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const setMealType = useMealSheetStore((s) => s.setMealType);
  const close = useMealSheetStore((s) => s.close);

  const visible = mode === "log-meal";
  const logOptions = useMealSheetStore((s) => s.logOptions);

  const [activeTab, setActiveTab] = useState<LogMealTab>("describe");
  const [subView, setSubView] = useState<LogSubView>("menu");
  const [swapIndex, setSwapIndex] = useState<number | null>(null);

  // Time
  const initialDate = new Date();
  const [mealHour, setMealHour] = useState(String(initialDate.getHours()).padStart(2, "0"));
  const [mealMinute, setMealMinute] = useState(String(initialDate.getMinutes()).padStart(2, "0"));

  // Describe state
  const [naturalText, setNaturalText] = useState("");
  const [parsedItems, setParsedItems] = useState<ParsedFoodItem[]>([]);
  const [parsedConfigs, setParsedConfigs] = useState<
    Record<number, { servingG: number; multiplier: number; customText: string }>
  >({});
  const [showReview, setShowReview] = useState(false);
  const [aiPreview, setAiPreview] = useState<AiGeneratedFood | null>(null);

  // Manual state
  const [manualName, setManualName] = useState("");
  const [manualCalories, setManualCalories] = useState("");
  const [manualProtein, setManualProtein] = useState("0");
  const [manualCarbs, setManualCarbs] = useState("0");
  const [manualFat, setManualFat] = useState("0");
  const [manualFiber, setManualFiber] = useState("0");
  const [manualSugar, setManualSugar] = useState("0");
  const [manualSodium, setManualSodium] = useState("0");
  const [manualServings, setManualServings] = useState("1");

  // Search state
  const [searchText, setSearchText] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  // Scan state
  const [barcode, setBarcode] = useState("");
  const [pendingBarcode, setPendingBarcode] = useState<string | null>(null);
  const [permission, requestPermission] = useCameraPermissions();

  // Add-to-meal state
  const [addToMealProduct, setAddToMealProduct] = useState<FoodProduct | null>(null);
  const [addToMealServingG, setAddToMealServingG] = useState<number>(100);
  const [addToMealMultiplier, setAddToMealMultiplier] = useState<number>(1);
  const [addToMealCustomText, setAddToMealCustomText] = useState("");

  const { createMeal } = useMealMutations();
  const { isPro } = useSubscriptionStore();

  // Queries
  const barcodeQuery = useQuery({
    queryKey: ["log-sheet-barcode", barcode],
    queryFn: () => foodApi.lookupBarcode(barcode).then((r) => r.data),
    enabled: barcode.length >= 8,
  });

  const searchResults = useQuery({
    queryKey: ["log-sheet-search", debouncedSearch],
    queryFn: ({ signal }) => foodApi.search(debouncedSearch, signal).then((r) => r.data),
    enabled: debouncedSearch.length >= 2,
    staleTime: 5 * 60 * 1000,
  });

  const pendingLookup = useQuery({
    queryKey: ["log-sheet-pending-lookup", pendingBarcode],
    queryFn: () => foodApi.lookupBarcode(pendingBarcode!).then((r) => r.data),
    enabled: !!pendingBarcode,
  });

  useEffect(() => {
    if (pendingLookup.data && pendingBarcode) {
      setBarcode(pendingBarcode);
      setPendingBarcode(null);
    }
  }, [pendingLookup.data, pendingBarcode]);

  const { favorites, isLoading: favoritesLoading } = useFavorites();

  const recentFoodsQuery = useQuery({
    queryKey: ["log-sheet-recent-foods"],
    queryFn: () => mealApi.recentFoods(20).then((r) => r.data),
    staleTime: 5 * 60 * 1000,
    select: (data) => [...data].sort((a, b) => b.logCount - a.logCount),
  });

  const customFoodsQuery = useQuery({
    queryKey: ["custom-foods"],
    queryFn: () => foodApi.customFoods().then((r) => r.data),
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchText), 350);
    return () => clearTimeout(timer);
  }, [searchText]);

  // Reset active tab when sheet opens with initialTab option
  useEffect(() => {
    if (visible && logOptions?.initialTab) {
      setActiveTab(logOptions.initialTab);
    }
  }, [visible, logOptions?.initialTab]);

  // Parse mutation
  const parseMutation = useMutation({
    mutationFn: (text: string) =>
      mealApi.parseNatural({ text, mealType: selectedMealType }).then((r) => r.data),
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
      setParsedItems(parsed);
      setNaturalText("");
      setShowReview(true);
      toast.success(
        parsed.length === 1
          ? `Found "${parsed[0].name}" — review & log`
          : `Found ${parsed.length} food items`,
      );
    },
    onError: () => toast.error("Could not parse meal. Try being more specific."),
  });

  // AI describe mutation (Create Custom Food shortcut)
  const aiDescribeMutation = useMutation({
    mutationFn: (text: string) => foodApi.describeFood(text).then((r) => r.data),
    onSuccess: (data) => {
      setAiPreview(data);
      toast.success(`Generated "${data.name || data.brandName || "food"}"`);
    },
    onError: () => {
      toast.error("Could not generate food details from that description. Try being more specific.");
    },
  });

  const handleLogManual = () => {
    if (!manualName.trim()) return;
    const cal = Number(manualCalories) || 0;
    const servings = Number(manualServings) || 1;
    if (cal > MAX_CALORIES) {
      toast.error(`Calories must be ${MAX_CALORIES.toLocaleString()} or less`);
      return;
    }
    if (servings > MAX_SERVINGS || servings < 0.1) {
      toast.error(`Servings must be between 0.1 and ${MAX_SERVINGS}`);
      return;
    }
    for (const { label, val } of [
      { label: "Protein", val: Number(manualProtein) || 0 },
      { label: "Carbs", val: Number(manualCarbs) || 0 },
      { label: "Fat", val: Number(manualFat) || 0 },
      { label: "Fiber", val: Number(manualFiber) || 0 },
      { label: "Sugar", val: Number(manualSugar) || 0 },
    ]) {
      if (val > MAX_MACRO_G) {
        toast.error(`${label} must be ${MAX_MACRO_G.toLocaleString()}g or less`);
        return;
      }
    }
    if ((Number(manualSodium) || 0) > MAX_SODIUM_MG) {
      toast.error(`Sodium must be ${MAX_SODIUM_MG.toLocaleString()}mg or less`);
      return;
    }
    createMeal.mutate({
      mealType: selectedMealType,
      loggedAt: buildLoggedAt(selectedDate, Number(mealHour), Number(mealMinute)),
      items: [{
        foodName: manualName.trim(),
        servings,
        servingUnit: "serving",
        calories: cal,
        proteinG: Number(manualProtein) || 0,
        carbsG: Number(manualCarbs) || 0,
        fatG: Number(manualFat) || 0,
        fiberG: Number(manualFiber) || 0,
        sugarG: Number(manualSugar) || 0,
        sodiumMg: Number(manualSodium) || 0,
      }],
    });
    resetForm();
  };

  const handleLogParsed = () => {
    if (parsedItems.length === 0) return;
    createMeal.mutate({
      mealType: selectedMealType,
      loggedAt: buildLoggedAt(selectedDate, Number(mealHour), Number(mealMinute)),
      items: parsedItems.map((it, idx) => mapParsedItemToRequest(it, parsedConfigs[idx])),
    });
    resetForm();
  };

  const handleAiDescribe = async () => {
    if (!isPro) {
      const ok = await presentPaywall();
      if (!ok) return;
    }
    const trimmed = naturalText.trim();
    if (trimmed.length < 8) {
      toast.error("Type at least 8 characters first.");
      return;
    }
    if (aiDescribeMutation.isPending) return;
    aiDescribeMutation.mutate(trimmed);
  };

  const handleAiLog = async () => {
    if (!aiPreview) return;
    try {
      const normalized = normalizeCustomFood(aiPreview);
      const saved = await foodApi.createCustomFood(normalized);
      const customFoodId = (saved.data as any).id;
      queryClient.invalidateQueries({ queryKey: ["custom-foods"] });
      createMeal.mutate({
        mealType: selectedMealType,
        loggedAt: buildLoggedAt(selectedDate, Number(mealHour), Number(mealMinute)),
        items: [customFoodToMealItem({ ...normalized, id: customFoodId })],
      });
      setAiPreview(null);
    } catch {
      toast.error("Failed to log AI-generated food.");
    }
  };

  const clearAiPreview = () => setAiPreview(null);

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
              foodProductId: food.id !== EMPTY_PRODUCT_ID ? food.id : undefined,
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
    setSubView("menu");
    toast.success(`Swapped to "${food.name}"`);
  };

  // Add to meal mutation (for search/scan results)
  const addToMealMutation = useMutation({
    mutationFn: (product: FoodProduct) => {
      const s = addToMealServingG * addToMealMultiplier;
      const scaled = scaleNutrition(product, s);
      return mealApi.create({
        mealType: selectedMealType,
        loggedAt: buildLoggedAt(selectedDate, Number(mealHour), Number(mealMinute)),
        items: [{
          foodName: product.name,
          barcode: product.barcode ?? undefined,
          foodProductId: product.id !== EMPTY_PRODUCT_ID ? product.id : undefined,
          servings: 1,
          servingUnit: `${s}g`,
          servingWeightG: s,
          ...scaled,
        }],
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      queryClient.invalidateQueries({ queryKey: ["recent-foods"] });
      queryClient.invalidateQueries({ queryKey: ["favorite-foods"] });
      queryClient.invalidateQueries({ queryKey: ["streak"] });
      setSubView("menu");
      setAddToMealProduct(null);
      toast.success("Added to meal!");
      if (Platform.OS !== "web") {
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }
      maybeRequestReview();
    },
    onError: () => toast.error("Failed to add to meal"),
  });

  const handleProductPress = (product: FoodProduct) => {
    setAddToMealProduct(product);
    setAddToMealServingG(
      product.servingQuantity ? Math.round(product.servingQuantity) : 100,
    );
    setAddToMealMultiplier(1);
    setAddToMealCustomText("");
    setSubView("add-to-meal");
  };

  const handleBarcodeScanned = ({ data }: { data: string }) => {
    setSubView("menu");
    setBarcode(data);
    if (Platform.OS !== "web") {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    }
  };

  const openCamera = async () => {
    if (!permission?.granted) {
      const result = await requestPermission();
      if (!result.granted) {
        toast.error("Camera permission is required to scan barcodes");
        return;
      }
    }
    setSubView("camera");
  };

  const resetForm = () => {
    setNaturalText("");
    setParsedItems([]);
    setParsedConfigs({});
    setShowReview(false);
    setAiPreview(null);
    setManualName("");
    setManualCalories("");
    setManualProtein("0");
    setManualCarbs("0");
    setManualFat("0");
    setManualFiber("0");
    setManualSugar("0");
    setManualSodium("0");
    setManualServings("1");
    setSearchText("");
    setDebouncedSearch("");
    setBarcode("");
    setPendingBarcode(null);
    setSubView("menu");
    setSwapIndex(null);
    setAddToMealProduct(null);
    const now = new Date();
    setMealHour(String(now.getHours()).padStart(2, "0"));
    setMealMinute(String(now.getMinutes()).padStart(2, "0"));
  };

  const handleClose = () => {
    resetForm();
    close();
  };

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

  const tabs: { key: LogMealTab; label: string; icon: string }[] = [
    { key: "describe", label: "Describe", icon: "chatbubble-outline" },
    { key: "search", label: "Search", icon: "search-outline" },
    { key: "scan", label: "Scan", icon: "barcode-outline" },
    { key: "manual", label: "Manual", icon: "create-outline" },
  ];

  // ── Camera full overlay ──
  if (subView === "camera") {
    return (
      <View style={{ flex: 1, backgroundColor: "#000" }}>
        <CameraView
          style={{ flex: 1 }}
          facing="back"
          barcodeScannerSettings={{ barcodeTypes: ["ean13", "ean8", "upc_a"] }}
          onBarcodeScanned={handleBarcodeScanned}
        />
        <View
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            right: 0,
            paddingTop: 56,
            paddingHorizontal: 20,
            paddingBottom: 20,
            backgroundColor: "rgba(0,0,0,0.35)",
          }}
        >
          <TouchableOpacity
            onPress={() => setSubView("menu")}
            style={{ alignSelf: "flex-start", padding: 4 }}
          >
            <Ionicons name="close" size={28} color="#fff" />
          </TouchableOpacity>
          <Text style={{ color: "#fff", fontSize: 22, fontWeight: "700", marginTop: 12 }}>
            Scan Barcode
          </Text>
          <Text style={{ color: "rgba(255,255,255,0.88)", fontSize: 14, marginTop: 6 }}>
            Center the barcode in view to look up the product.
          </Text>
        </View>
      </View>
    );
  }

  return (
    <BottomSheet
      visible={visible}
      onClose={handleClose}
      maxHeight="100%"
      paddingTop={spacing.lg}
      paddingHorizontal={spacing.lg}
      paddingBottom={0}
      contentStyle={{ minHeight: 256 }}
    >
      {subView === "swap" ? (
        <SwapSearchContent
          initialSearch={swapIndex !== null ? (parsedItems[swapIndex]?.name ?? "") : ""}
          onSelect={handleSwapSelect}
          onBack={() => setSubView("menu")}
        />
      ) : subView === "add-to-meal" && addToMealProduct ? (
        <View style={{ flex: 1 }}>
          <TouchableOpacity
            onPress={() => setSubView("menu")}
            style={{ flexDirection: "row", alignItems: "center", marginBottom: spacing.md }}
          >
            <Ionicons name="chevron-back" size={20} color={colors.primary} />
            <Text style={{ color: colors.primary, fontWeight: "600", marginLeft: 4 }}>
              Back
            </Text>
          </TouchableOpacity>
          <Text style={{ fontSize: 20, fontWeight: "700", color: colors.text, marginBottom: 4 }}>
            {addToMealProduct.name}
          </Text>
          {addToMealProduct.brand && (
            <Text style={{ fontSize: 14, color: colors.textSecondary, marginBottom: 16 }}>
              {addToMealProduct.brand}
            </Text>
          )}
          <ServingSizeSelector
            servingG={addToMealServingG}
            onServingChange={setAddToMealServingG}
            customText={addToMealCustomText}
            onCustomTextChange={setAddToMealCustomText}
            multiplier={addToMealMultiplier}
            onMultiplierChange={setAddToMealMultiplier}
            product={addToMealProduct}
            summaryText={nutritionSummaryText(
              scaleNutrition(addToMealProduct, addToMealServingG * addToMealMultiplier),
              addToMealServingG * addToMealMultiplier,
            )}
          />
          <View style={{ flexDirection: "row", gap: 12, marginTop: 24 }}>
            <TouchableOpacity
              onPress={() => {
                setSubView("menu");
                setAddToMealProduct(null);
              }}
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
              onPress={() => addToMealMutation.mutate(addToMealProduct)}
              disabled={addToMealMutation.isPending}
              style={{
                flex: 1,
                backgroundColor: colors.primary,
                paddingVertical: 14,
                borderRadius: 12,
                alignItems: "center",
              }}
            >
              {addToMealMutation.isPending ? (
                <ActivityIndicator color={colors.textOnPrimary} size="small" />
              ) : (
                <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 16 }}>
                  Log It
                </Text>
              )}
            </TouchableOpacity>
          </View>
        </View>
      ) : (
        <View style={{ flex: 1 }}>
          {/* ── Shared header: meal type · date · time ── */}
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "space-between",
              marginBottom: spacing.md,
              paddingVertical: spacing.sm,
            }}
          >
            <Text style={{ fontSize: 13, fontWeight: "600", color: colors.textMuted }}>
              {selectedDate} · {selectedMealType}
            </Text>
            <View style={{ flexDirection: "row", alignItems: "center", gap: 4 }}>
              <TextInput
                value={mealHour}
                onChangeText={(t) => {
                  const cleaned = t.replace(/[^0-9]/g, "").slice(0, 2);
                  const num = Number(cleaned);
                  if (cleaned.length <= 1 || (num >= 0 && num <= 23)) setMealHour(cleaned);
                }}
                keyboardType="number-pad"
                maxLength={2}
                selectTextOnFocus
                style={{
                  width: 36,
                  borderWidth: 1,
                  borderColor: colors.border,
                  borderRadius: radius.sm,
                  padding: 4,
                  fontSize: 14,
                  fontWeight: "700",
                  color: colors.text,
                  textAlign: "center",
                  backgroundColor: colors.bg,
                }}
              />
              <Text style={{ fontSize: 16, fontWeight: "700", color: colors.textSecondary }}>:</Text>
              <TextInput
                value={mealMinute}
                onChangeText={(t) => {
                  const cleaned = t.replace(/[^0-9]/g, "").slice(0, 2);
                  const num = Number(cleaned);
                  if (cleaned.length <= 1 || (num >= 0 && num <= 59)) setMealMinute(cleaned);
                }}
                keyboardType="number-pad"
                maxLength={2}
                selectTextOnFocus
                style={{
                  width: 36,
                  borderWidth: 1,
                  borderColor: colors.border,
                  borderRadius: radius.sm,
                  padding: 4,
                  fontSize: 14,
                  fontWeight: "700",
                  color: colors.text,
                  textAlign: "center",
                  backgroundColor: colors.bg,
                }}
              />
              <Text style={{ fontSize: 11, color: colors.textMuted, marginLeft: 2 }}>
                {Number(mealHour) >= 12 ? "PM" : "AM"}
              </Text>
            </View>
          </View>

          {/* ── Tab bar ── */}
          <View
            style={{
              flexDirection: "row",
              marginBottom: spacing.md,
              backgroundColor: colors.bg,
              borderRadius: radius.sm,
              padding: 3,
            }}
          >
            {tabs.map(({ key, label, icon }) => (
              <TouchableOpacity
                key={key}
                onPress={() => {
                  setActiveTab(key);
                  setShowReview(false);
                }}
                accessibilityRole="tab"
                accessibilityState={{ selected: activeTab === key }}
                accessibilityLabel={label}
                style={{
                  flex: 1,
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: 4,
                  paddingVertical: 8,
                  borderRadius: radius.sm - 2,
                  backgroundColor: activeTab === key ? colors.card : "transparent",
                }}
              >
                <Ionicons
                  name={icon as any}
                  size={14}
                  color={activeTab === key ? colors.primary : colors.textMuted}
                />
                <Text
                  style={{
                    fontSize: 12,
                    fontWeight: "600",
                    color: activeTab === key ? colors.text : colors.textMuted,
                  }}
                >
                  {label}
                </Text>
              </TouchableOpacity>
            ))}
          </View>

          {/* ════════════════════════════════════════
             DESCRIBE TAB
             ════════════════════════════════════════ */}
          {activeTab === "describe" && !showReview && !aiPreview && (
            <View>
              <TextInput
                placeholder='e.g. "2 eggs, toast with butter, orange juice"'
                placeholderTextColor={colors.textLight}
                value={naturalText}
                onChangeText={setNaturalText}
                multiline
                autoCapitalize="sentences"
                maxLength={500}
                accessibilityLabel="Describe your meal"
                style={{
                  fontSize: 15,
                  minHeight: 72,
                  color: colors.text,
                  backgroundColor: colors.bg,
                  borderRadius: radius.sm,
                  padding: spacing.sm,
                  borderWidth: 1,
                  borderColor: colors.border,
                }}
              />
              <View style={{ flexDirection: "row", justifyContent: "flex-end", marginTop: spacing.sm, gap: 8 }}>
                <TouchableOpacity
                  onPress={handleClose}
                  style={{ paddingHorizontal: 14, paddingVertical: 8 }}
                >
                  <Text style={{ color: colors.textMuted, fontWeight: "600" }}>Cancel</Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={() => naturalText && parseMutation.mutate(naturalText)}
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
                    <ActivityIndicator color={colors.textOnPrimary} size="small" />
                  ) : (
                    <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 14 }}>
                      Parse & Log
                    </Text>
                  )}
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={handleAiDescribe}
                  disabled={aiDescribeMutation.isPending || !naturalText.trim()}
                  style={{
                    backgroundColor: colors.accent,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                    opacity: naturalText.trim() ? 1 : 0.5,
                    flexDirection: "row",
                    alignItems: "center",
                    gap: 4,
                  }}
                >
                  {aiDescribeMutation.isPending ? (
                    <ActivityIndicator color={colors.textOnPrimary} size="small" />
                  ) : (
                    <>
                      <Ionicons name="sparkles" size={14} color={colors.textOnPrimary} />
                      <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 14 }}>
                        Describe with AI
                      </Text>
                    </>
                  )}
                </TouchableOpacity>
              </View>
            </View>
          )}

          {activeTab === "describe" && aiPreview && (
            <View>
              <View style={{ flexDirection: "row", alignItems: "center", marginBottom: spacing.md, gap: 6 }}>
                <Ionicons name="sparkles" size={18} color={colors.warning} />
                <Text style={{ fontWeight: "700", fontSize: 15, color: colors.text }}>
                  AI Generated Food
                </Text>
              </View>

              <View
                style={{
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  borderWidth: 1,
                  borderColor: colors.borderLight,
                  padding: spacing.lg,
                  marginBottom: spacing.md,
                }}
              >
                <Text style={{ fontSize: 18, fontWeight: "700", color: colors.text }}>
                  {aiPreview.name || "Food"}
                </Text>
                {aiPreview.brandName && (
                  <Text style={{ fontSize: 13, color: colors.textSecondary, marginTop: 2 }}>
                    {aiPreview.brandName}
                  </Text>
                )}

                <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 8, marginTop: spacing.md }}>
                  <View style={{ backgroundColor: colors.bg, borderRadius: radius.sm, paddingHorizontal: 8, paddingVertical: 4 }}>
                    <Text style={{ fontSize: 12, color: colors.textMuted }}>Serving: {aiPreview.servingSize}{aiPreview.servingSizeUnit}</Text>
                  </View>
                  <View style={{ backgroundColor: colors.primaryBg, borderRadius: radius.sm, paddingHorizontal: 8, paddingVertical: 4 }}>
                    <Text style={{ fontSize: 12, fontWeight: "600", color: colors.primary }}>{aiPreview.calories} cal</Text>
                  </View>
                  <View style={{ backgroundColor: colors.bg, borderRadius: radius.sm, paddingHorizontal: 8, paddingVertical: 4 }}>
                    <Text style={{ fontSize: 12, color: colors.textMuted }}>P: {aiPreview.proteinG}g</Text>
                  </View>
                  <View style={{ backgroundColor: colors.bg, borderRadius: radius.sm, paddingHorizontal: 8, paddingVertical: 4 }}>
                    <Text style={{ fontSize: 12, color: colors.textMuted }}>C: {aiPreview.carbG}g</Text>
                  </View>
                  <View style={{ backgroundColor: colors.bg, borderRadius: radius.sm, paddingHorizontal: 8, paddingVertical: 4 }}>
                    <Text style={{ fontSize: 12, color: colors.textMuted }}>F: {aiPreview.fatG}g</Text>
                  </View>
                  {aiPreview.fiberG != null && aiPreview.fiberG > 0 && (
                    <View style={{ backgroundColor: colors.bg, borderRadius: radius.sm, paddingHorizontal: 8, paddingVertical: 4 }}>
                      <Text style={{ fontSize: 12, color: colors.textMuted }}>Fiber: {aiPreview.fiberG}g</Text>
                    </View>
                  )}
                  {aiPreview.sodiumMg != null && aiPreview.sodiumMg > 0 && (
                    <View style={{ backgroundColor: colors.bg, borderRadius: radius.sm, paddingHorizontal: 8, paddingVertical: 4 }}>
                      <Text style={{ fontSize: 12, color: colors.textMuted }}>Na: {aiPreview.sodiumMg}mg</Text>
                    </View>
                  )}
                </View>

                {aiPreview.extractionConfidence != null && (
                  <View style={{ flexDirection: "row", alignItems: "center", gap: 6, marginTop: spacing.md }}>
                    <Ionicons name="information-circle-outline" size={14} color={colors.textMuted} />
                    <Text style={{ fontSize: 12, color: colors.textMuted }}>
                      {aiPreview.extractionConfidence >= 0.85 ? "High" : aiPreview.extractionConfidence >= 0.6 ? "Medium" : "Low"} confidence ({Math.round(aiPreview.extractionConfidence * 100)}%)
                    </Text>
                  </View>
                )}

                <Text style={{ fontSize: 11, color: colors.textMuted, marginTop: spacing.sm, lineHeight: 16 }}>
                  This food will be saved to My Foods and logged to {selectedDate} · {selectedMealType}.
                </Text>
              </View>

              <View style={{ flexDirection: "row", justifyContent: "flex-end", gap: 8 }}>
                <TouchableOpacity
                  onPress={clearAiPreview}
                  style={{ paddingHorizontal: 14, paddingVertical: 8 }}
                >
                  <Text style={{ color: colors.textMuted, fontWeight: "600" }}>Cancel</Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={handleAiLog}
                  disabled={createMeal.isPending || aiDescribeMutation.isPending}
                  style={{
                    backgroundColor: colors.primary,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                    opacity: createMeal.isPending ? 0.5 : 1,
                    flexDirection: "row",
                    alignItems: "center",
                    gap: 4,
                  }}
                >
                  {createMeal.isPending ? (
                    <ActivityIndicator color={colors.textOnPrimary} size="small" />
                  ) : (
                    <>
                      <Ionicons name="sparkles" size={14} color={colors.textOnPrimary} />
                      <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 14 }}>
                        Log to {selectedMealType}
                      </Text>
                    </>
                  )}
                </TouchableOpacity>
              </View>
            </View>
          )}

          {activeTab === "describe" && showReview && (
            <ScrollView style={{ maxHeight: 420 }} keyboardShouldPersistTaps="handled">
              <Text style={{ fontWeight: "700", fontSize: 15, color: colors.text, marginBottom: spacing.md }}>
                {parsedItems.length} {parsedItems.length === 1 ? "item" : "items"} found — review & edit
              </Text>
              {parsedItems.map((item, idx) => {
                const cfg = parsedConfigs[idx] ?? { servingG: item.servingWeightG || 100, multiplier: 1, customText: "1" };
                const totalG = cfg.servingG * cfg.multiplier;
                const scale = totalG / (item.servingWeightG || 100);
                return (
                  <View key={idx} style={{ borderWidth: 1, borderColor: colors.borderLight, borderRadius: radius.sm, padding: spacing.md, marginBottom: spacing.sm }}>
                    <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: 6 }}>
                      <Text style={{ fontWeight: "600", fontSize: 14, color: colors.text, flex: 1 }} numberOfLines={1}>
                        {item.name}
                      </Text>
                      <TouchableOpacity onPress={() => { setSwapIndex(idx); setSubView("swap"); }} style={{ marginLeft: 8 }}>
                        <Ionicons name="swap-horizontal" size={20} color={colors.primary} />
                      </TouchableOpacity>
                      <TouchableOpacity onPress={() => removeParsedItem(idx)} style={{ marginLeft: 8 }}>
                        <Ionicons name="close-circle" size={22} color={colors.danger} />
                      </TouchableOpacity>
                    </View>
                    <ServingSizeSelector
                      servingG={cfg.servingG}
                      onServingChange={(g) => setParsedConfigs((prev) => ({ ...prev, [idx]: { ...prev[idx], servingG: g } }))}
                      customText={cfg.customText}
                      onCustomTextChange={(t) => setParsedConfigs((prev) => ({ ...prev, [idx]: { ...prev[idx], customText: t } }))}
                      multiplier={cfg.multiplier}
                      onMultiplierChange={(m) => setParsedConfigs((prev) => ({ ...prev, [idx]: { ...prev[idx], multiplier: m } }))}
                      product={{ servingQuantity: item.servingWeightG || item.servingQuantity || 1, servingSize: item.servingSize || "serving" }}
                      summaryText={`${totalG}g total · ${Math.round(item.calories * scale)} cal · ${Math.round(item.proteinG * scale * 10) / 10}g P · ${Math.round(item.carbsG * scale * 10) / 10}g C · ${Math.round(item.fatG * scale * 10) / 10}g F`}
                    />
                  </View>
                );
              })}
              <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginTop: spacing.sm }}>
                <Text style={{ fontSize: 12, color: colors.textMuted }}>
                  Total: {Math.round(parsedItems.reduce((acc, it, idx) => {
                    const cfg = parsedConfigs[idx];
                    if (!cfg) return acc;
                    return acc + it.calories * (cfg.servingG * cfg.multiplier) / (it.servingWeightG ?? 100);
                  }, 0))} cal
                </Text>
                <View style={{ flexDirection: "row", gap: 8 }}>
                  <TouchableOpacity onPress={() => { setParsedItems([]); setShowReview(false); }} style={{ paddingHorizontal: 14, paddingVertical: 8 }}>
                    <Text style={{ color: colors.textMuted, fontWeight: "600" }}>Cancel</Text>
                  </TouchableOpacity>
                  <TouchableOpacity onPress={handleLogParsed} disabled={createMeal.isPending || parsedItems.length === 0} style={{ backgroundColor: colors.primary, paddingHorizontal: 16, paddingVertical: 8, borderRadius: radius.sm }}>
                    {createMeal.isPending ? (
                      <ActivityIndicator color={colors.textOnPrimary} size="small" />
                    ) : (
                      <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 14 }}>
                        Log Meal ({parsedItems.length} {parsedItems.length === 1 ? "item" : "items"})
                      </Text>
                    )}
                  </TouchableOpacity>
                </View>
              </View>
            </ScrollView>
          )}

          {/* ════════════════════════════════════════
             SEARCH TAB
             ════════════════════════════════════════ */}
          {activeTab === "search" && (
            <View style={{ flex: 1 }}>
              <TextInput
                placeholder="Search by name..."
                value={searchText}
                onChangeText={setSearchText}
                autoCapitalize="none"
                autoCorrect={false}
                returnKeyType="search"
                maxLength={200}
                style={{
                  borderWidth: 1,
                  borderColor: colors.border,
                  borderRadius: 8,
                  padding: 12,
                  fontSize: 16,
                  marginBottom: 12,
                }}
              />

              {/* Results area */}
              <ScrollView
                style={{ maxHeight: 420 }}
                keyboardShouldPersistTaps="handled"
                showsVerticalScrollIndicator={false}
              >
                {searchText.trim().length >= 2 ? (
                  <>
                    {searchResults.isLoading && <SearchResultSkeleton count={3} />}
                    {searchResults.data && searchResults.data.length === 0 && (
                      <View style={{ backgroundColor: colors.primaryBg, borderRadius: 12, borderWidth: 1, borderColor: colors.primaryBorder, padding: 16, marginTop: 8 }}>
                        <Text style={{ fontSize: 15, fontWeight: "700", color: colors.text, marginBottom: 4 }}>
                          No results for "{searchText.trim()}"
                        </Text>
                        <Text style={{ fontSize: 13, color: colors.textSecondary, marginBottom: 12 }}>
                          Try describing it or create a custom food.
                        </Text>
                        <View style={{ flexDirection: "row", gap: 8 }}>
                          <TouchableOpacity
                            onPress={() => { setSearchText(""); setDebouncedSearch(""); setActiveTab("describe"); }}
                            style={{ flex: 1, backgroundColor: colors.primaryLight, paddingVertical: 10, borderRadius: 8, alignItems: "center" }}
                          >
                            <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 13 }}>Describe It Instead</Text>
                          </TouchableOpacity>
                          <TouchableOpacity
                            onPress={() => { handleClose(); router.push("/food/create"); }}
                            style={{ flex: 1, borderWidth: 1, borderColor: colors.primary, paddingVertical: 10, borderRadius: 8, alignItems: "center" }}
                          >
                            <Text style={{ color: colors.primary, fontWeight: "600", fontSize: 13 }}>Create Custom Food</Text>
                          </TouchableOpacity>
                        </View>
                      </View>
                    )}
                    {searchResults.data && searchResults.data.length > 0 && (
                      <View style={{ marginTop: 8 }}>
                        <Text style={{ fontSize: 12, fontWeight: "600", color: colors.textMuted, marginBottom: 8, textTransform: "uppercase" }}>
                          Catalog Results
                        </Text>
                        {searchResults.data.map((product: FoodProduct, idx: number) => (
                          <FoodSearchResult
                            key={product.id || product.barcode || idx}
                            product={product}
                            onPress={handleProductPress}
                            onDetailPress={() => router.push(`/food/${product.id}`)}
                          />
                        ))}
                      </View>
                    )}
                  </>
                ) : (
                  <>
                    {/* Browse sections: Recent, Favorites, My Foods */}
                    {[
                      { key: "recent", title: "Recent", data: (recentFoodsQuery.data ?? []).map((f: RecentFood, i: number) => ({ key: `r-${f.foodProductId ?? f.foodName}-${i}`, product: mapRecentFoodToProduct(f) })) },
                      { key: "favorites", title: "Favorites", data: favorites.map((f: FavoriteFood, i: number) => ({ key: `fav-${f.foodProductId}-${i}`, product: mapFavoriteFoodToProduct(f) })) },
                      { key: "my-foods", title: "My Foods", data: (customFoodsQuery.data ?? []).map((f: FoodProduct, i: number) => ({ key: `mf-${f.id ?? i}`, product: f })) },
                    ].map((section) =>
                      section.data.length > 0 && (
                        <View key={section.key} style={{ marginBottom: 16 }}>
                          <Text style={{ fontSize: 15, fontWeight: "700", color: colors.text, marginBottom: 6 }}>{section.title}</Text>
                          {section.data.map((item: { key: string; product: FoodProduct }) => (
                            <FoodSearchResult
                              key={item.key}
                              product={item.product}
                              onPress={handleProductPress}
                              onDetailPress={item.product.id && item.product.id !== EMPTY_PRODUCT_ID ? () => router.push(`/food/${item.product.id}`) : undefined}
                            />
                          ))}
                        </View>
                      )
                    )}

                    {/* My Foods empty state */}
                    {customFoodsQuery.data && customFoodsQuery.data.length === 0 && !recentFoodsQuery.data?.length && !favorites.length && (
                      <View style={{ alignItems: "center", paddingVertical: 24 }}>
                        <Ionicons name="restaurant-outline" size={28} color={colors.textLight} />
                        <Text style={{ color: colors.textMuted, fontSize: 14, marginTop: 8, marginBottom: 12 }}>
                          No foods yet. Log a meal or create a custom food.
                        </Text>
                        <TouchableOpacity
                          onPress={() => { handleClose(); router.push("/food/create"); }}
                          style={{ backgroundColor: colors.primary, paddingHorizontal: 16, paddingVertical: 10, borderRadius: 8 }}
                        >
                          <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 13 }}>Create Custom Food</Text>
                        </TouchableOpacity>
                      </View>
                    )}
                  </>
                )}
              </ScrollView>
            </View>
          )}

          {/* ════════════════════════════════════════
             SCAN TAB
             ════════════════════════════════════════ */}
          {activeTab === "scan" && (
            <View>
              <Text style={{ fontSize: 14, fontWeight: "600", color: colors.textSecondary, marginBottom: 8 }}>
                Barcode Lookup
              </Text>
              <View style={{ flexDirection: "row", gap: 8 }}>
                <TextInput
                  placeholder="Enter barcode number"
                  value={barcode}
                  onChangeText={setBarcode}
                  keyboardType="number-pad"
                  returnKeyType="search"
                  autoCorrect={false}
                  maxLength={20}
                  style={{
                    flex: 1,
                    borderWidth: 1,
                    borderColor: colors.border,
                    borderRadius: 8,
                    padding: 12,
                    fontSize: 16,
                  }}
                />
                <TouchableOpacity
                  onPress={openCamera}
                  accessibilityLabel="Scan barcode with camera"
                  style={{
                    backgroundColor: colors.primary,
                    borderRadius: 8,
                    width: 48,
                    alignItems: "center",
                    justifyContent: "center",
                  }}
                >
                  <Ionicons name="camera" size={24} color={colors.textOnPrimary} />
                </TouchableOpacity>
              </View>

              {barcodeQuery.isLoading && <SearchResultSkeleton count={1} />}
              {barcodeQuery.isError && (
                <Text style={{ color: colors.danger, fontSize: 13, marginTop: 8 }}>
                  Product not found for this barcode. Try search instead.
                </Text>
              )}
              {barcodeQuery.data && (
                <TouchableOpacity
                  onPress={() => handleProductPress(barcodeQuery.data!)}
                  style={{ marginTop: 12 }}
                >
                  <FoodSearchResult
                    product={barcodeQuery.data}
                    onPress={handleProductPress}
                  />
                </TouchableOpacity>
              )}
            </View>
          )}

          {/* ════════════════════════════════════════
             MANUAL TAB
             ════════════════════════════════════════ */}
          {activeTab === "manual" && (
            <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
              <TextInput
                placeholder="Food name (e.g. Chicken breast)"
                placeholderTextColor={colors.textLight}
                value={manualName}
                onChangeText={setManualName}
                autoCapitalize="words"
                maxLength={200}
                accessibilityLabel="Food name"
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
                  { label: "Calories", value: manualCalories, set: (t: string) => setManualCalories(sanitizeInt(t)), ml: 5 },
                  { label: "Protein", value: manualProtein, set: (t: string) => setManualProtein(sanitizeInt(t)), ml: 4 },
                  { label: "Carbs", value: manualCarbs, set: (t: string) => setManualCarbs(sanitizeInt(t)), ml: 4 },
                  { label: "Fat", value: manualFat, set: (t: string) => setManualFat(sanitizeInt(t)), ml: 4 },
                ].map(({ label, value, set, ml }) => (
                  <View key={label} style={{ flex: 1 }}>
                    <Text style={{ fontSize: 10, color: colors.textMuted, marginBottom: 2 }}>{label}</Text>
                    <TextInput
                      placeholder="0"
                      value={value}
                      onChangeText={set}
                      keyboardType="numeric"
                      autoCorrect={false}
                      maxLength={ml}
                      accessibilityLabel={label}
                      style={editFieldStyle}
                    />
                  </View>
                ))}
              </View>
              <View style={{ flexDirection: "row", justifyContent: "flex-end", marginTop: spacing.sm, gap: 8 }}>
                <TouchableOpacity onPress={handleClose} style={{ paddingHorizontal: 14, paddingVertical: 8 }}>
                  <Text style={{ color: colors.textMuted, fontWeight: "600" }}>Cancel</Text>
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
                    <ActivityIndicator color={colors.textOnPrimary} size="small" />
                  ) : (
                    <Text style={{ color: colors.textOnPrimary, fontWeight: "600", fontSize: 14 }}>Log Meal</Text>
                  )}
                </TouchableOpacity>
              </View>
            </ScrollView>
          )}
        </View>
      )}
    </BottomSheet>
  );
}
