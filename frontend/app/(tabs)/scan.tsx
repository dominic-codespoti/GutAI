import { useState, useEffect, useCallback } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  SectionList,
  ActivityIndicator,
  RefreshControl,
  BackHandler,
  Platform,
  KeyboardAvoidingView,
} from "react-native";
import * as Haptics from "expo-haptics";
import { CameraView, useCameraPermissions } from "expo-camera";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { foodApi, userApi, mealApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { toast } from "../../src/stores/toast";
import { ErrorState } from "../../components/ErrorState";
import { NutritionBar } from "../../components/NutritionBar";
import { MealTypePicker } from "../../components/MealTypePicker";
import { ServingSizeSelector } from "../../components/ServingSizeSelector";
import { FoodSearchResult } from "../../components/FoodSearchResult";
import { BottomSheet } from "../../components/BottomSheet";
import {
  scaleNutrition,
  nutritionSummaryText,
} from "../../src/utils/nutrition";
import { useFavorites } from "../../src/hooks/useFavorites";
import type { FavoriteFood, FoodProduct, RecentFood } from "../../src/types";
import { useRouter, useGlobalSearchParams } from "expo-router";
import { ratingColor, cspiEmoji } from "../../src/utils/colors";
import { maybeRequestReview } from "../../src/utils/review";
import { useThemeColors } from "../../src/stores/theme";
import { SearchResultSkeleton } from "../../components/SkeletonLoader";

const EMPTY_PRODUCT_ID = "00000000-0000-0000-0000-000000000000";

type BrowseFocus = "default" | "favorites" | "my-foods";

type FoodBrowseItem = {
  key: string;
  product: FoodProduct;
  allowDetails?: boolean;
  hideFavorite?: boolean;
};

type FoodBrowseSection = {
  key: string;
  title: string;
  subtitle: string;
  data: FoodBrowseItem[];
};

type SearchListHeaderProps = {
  browseFocus: BrowseFocus;
  onBrowseFocusChange: (next: BrowseFocus) => void;
};

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

export default function ScanScreen() {
  const colors = useThemeColors();
  const [barcode, setBarcode] = useState("");
  const [searchText, setSearchText] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [selectedProductId, setSelectedProductId] = useState<string | null>(
    null,
  );
  const [pendingBarcode, setPendingBarcode] = useState<string | null>(null);
  const [showCamera, setShowCamera] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();
  const queryClient = useQueryClient();
  const router = useRouter();

  const [addToMealType, setAddToMealType] = useState<string>("Lunch");
  const [showAddToMeal, setShowAddToMeal] = useState(false);
  const [addToMealServingG, setAddToMealServingG] = useState<number>(100);
  const [addToMealMultiplier, setAddToMealMultiplier] = useState<number>(1);
  const [customServingText, setCustomServingText] = useState<string>("");
  const [addToMealProduct, setAddToMealProduct] = useState<FoodProduct | null>(
    null,
  );

  const params = useGlobalSearchParams<{ tab?: string }>();
  const [browseFocus, setBrowseFocus] = useState<BrowseFocus>("default");

  useEffect(() => {
    if (params.tab === "favorites") {
      setBrowseFocus("favorites");
      return;
    }

    if (params.tab === "custom" || params.tab === "my-foods") {
      setBrowseFocus("my-foods");
      return;
    }

    setBrowseFocus("default");
  }, [params.tab]);

  const effectiveGrams = addToMealServingG * addToMealMultiplier;

  useEffect(() => {
    if (Platform.OS === "android") {
      const handler = BackHandler.addEventListener("hardwareBackPress", () => {
        if (showCamera) {
          setShowCamera(false);
          return true;
        }
        if (showAddToMeal) {
          setShowAddToMeal(false);
          return true;
        }
        if (selectedProductId) {
          setSelectedProductId(null);
          return true;
        }
        if (router.canGoBack()) {
          router.back();
          return true;
        }
        return false;
      });
      return () => handler.remove();
    }
  }, [showCamera, showAddToMeal, selectedProductId, router]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchText), 350);
    return () => clearTimeout(timer);
  }, [searchText]);

  const barcodeQuery = useQuery({
    queryKey: ["barcode", barcode],
    queryFn: () => foodApi.lookupBarcode(barcode).then((r) => r.data),
    enabled: barcode.length >= 8,
  });

  const searchResults = useQuery({
    queryKey: ["food-search", debouncedSearch],
    queryFn: ({ signal }) =>
      foodApi.search(debouncedSearch, signal).then((r) => r.data),
    enabled: debouncedSearch.length >= 2,
    staleTime: 5 * 60 * 1000,
  });

  const pendingLookup = useQuery({
    queryKey: ["barcode-lookup", pendingBarcode],
    queryFn: () => foodApi.lookupBarcode(pendingBarcode!).then((r) => r.data),
    enabled: !!pendingBarcode,
  });

  useEffect(() => {
    if (pendingLookup.data && pendingBarcode) {
      setSelectedProductId(pendingLookup.data.id);
      setPendingBarcode(null);
    }
  }, [pendingLookup.data, pendingBarcode]);

  const safetyReport = useQuery({
    queryKey: ["safety-report", selectedProductId],
    queryFn: () => foodApi.safetyReport(selectedProductId!).then((r) => r.data),
    enabled:
      !!selectedProductId &&
      selectedProductId !== "00000000-0000-0000-0000-000000000000",
  });

  const handleSelectProduct = (product: FoodProduct) => {
    setSearchText("");
    setDebouncedSearch("");
    const emptyGuid = "00000000-0000-0000-0000-000000000000";
    if (product.id && product.id !== emptyGuid) {
      setSelectedProductId(product.id);
    } else if (product.barcode) {
      setPendingBarcode(product.barcode);
    }
  };

  const handleBarcodeScanned = ({ data }: { data: string }) => {
    setShowCamera(false);
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
    setShowCamera(true);
  };

  const { data: alerts } = useQuery({
    queryKey: ["alerts"],
    queryFn: () => userApi.getAlerts().then((r) => r.data),
  });

  const addAlertMutation = useMutation({
    mutationFn: (additiveId: number) => userApi.addAlert(additiveId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["alerts"] });
      toast.success("Alert added");
    },
    onError: () => toast.error("Alert already exists"),
  });

  const alertIds = new Set((alerts ?? []).map((a) => a.additiveId));

  const {
    favorites,
    isLoading: favoritesLoading,
    refetch: refetchFavorites,
  } = useFavorites();

  const recentFoodsQuery = useQuery({
    queryKey: ["recent-foods"],
    queryFn: () => mealApi.recentFoods(20).then((r) => r.data),
    staleTime: 5 * 60 * 1000,
    select: (data) => [...data].sort((a, b) => b.logCount - a.logCount),
  });

  const customFoodsQuery = useQuery({
    queryKey: ["custom-foods"],
    queryFn: () => foodApi.customFoods().then((r) => r.data),
    staleTime: 5 * 60 * 1000,
  });

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all([
      searchResults.refetch(),
      safetyReport.refetch(),
      recentFoodsQuery.refetch(),
      refetchFavorites(),
      customFoodsQuery.refetch(),
    ]);
    setRefreshing(false);
  }, [
    customFoodsQuery,
    recentFoodsQuery,
    refetchFavorites,
    safetyReport,
    searchResults,
  ]);

  const addToMealMutation = useMutation({
    mutationFn: (product: FoodProduct) => {
      const s = effectiveGrams;
      const scaled = scaleNutrition(product, s);
      return mealApi.create({
        mealType: addToMealType,
        loggedAt: new Date().toISOString(),
        items: [
          {
            foodName: product.name,
            barcode: product.barcode ?? undefined,
            foodProductId:
              product.id !== "00000000-0000-0000-0000-000000000000"
                ? product.id
                : undefined,
            servings: 1,
            servingUnit: `${s}g`,
            servingWeightG: s,
            ...scaled,
          },
        ],
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      queryClient.invalidateQueries({ queryKey: ["recent-foods"] });
      queryClient.invalidateQueries({ queryKey: ["favorite-foods"] });
      queryClient.invalidateQueries({ queryKey: ["streak"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-foods-dashboard"] });
      queryClient.invalidateQueries({ queryKey: ["diary-analysis"] });
      queryClient.invalidateQueries({ queryKey: ["additive-exposure"] });
      queryClient.invalidateQueries({ queryKey: ["nutrition-trends"] });
      setShowAddToMeal(false);
      setAddToMealProduct(null);
      toast.success("Added to meal!");
      if (Platform.OS !== "web") {
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }
      maybeRequestReview();
    },
    onError: () => toast.error("Failed to add to meal"),
  });

  const handleDetailPress = (product: FoodProduct) => {
    router.push({
      pathname: "/food/[id]",
      params: { id: product.id },
    });
  };

  const handleProductPress = (product: FoodProduct) => {
    setAddToMealProduct(product);
    setAddToMealServingG(
      product.servingQuantity ? Math.round(product.servingQuantity) : 100,
    );
    setAddToMealMultiplier(1);
    setCustomServingText("");
    setShowAddToMeal(true);
  };

  const openCreateWithAi = () => router.push("/food/create");
  const isSearching = searchText.trim().length >= 2;

  useEffect(() => {
    if (!isSearching) {
      setSelectedProductId(null);
    }
  }, [browseFocus, isSearching]);

  const recentBrowseItems: FoodBrowseItem[] = (recentFoodsQuery.data ?? []).map(
    (food, index) => ({
      key: `recent-${food.foodProductId ?? food.foodName}-${index}`,
      product: mapRecentFoodToProduct(food),
      allowDetails: !!food.foodProductId,
      hideFavorite: !food.foodProductId,
    }),
  );
  const favoriteBrowseItems: FoodBrowseItem[] = favorites.map(
    (food, index) => ({
      key: `favorite-${food.foodProductId}-${index}`,
      product: mapFavoriteFoodToProduct(food),
      allowDetails: true,
    }),
  );
  const myFoodBrowseItems: FoodBrowseItem[] = (customFoodsQuery.data ?? []).map(
    (food, index) => ({
      key: `my-food-${food.id ?? index}`,
      product: food,
      allowDetails: !!food.id && food.id !== EMPTY_PRODUCT_ID,
      hideFavorite: true,
    }),
  );
  const catalogBrowseItems: FoodBrowseItem[] = (searchResults.data ?? []).map(
    (food, index) => ({
      key: `catalog-${food.id || food.barcode || index}`,
      product: food,
      allowDetails: !!food.id && food.id !== EMPTY_PRODUCT_ID,
      hideFavorite: !food.id || food.id === EMPTY_PRODUCT_ID,
    }),
  );

  const savedFoodSections: FoodBrowseSection[] = [
    {
      key: "recent",
      title: "Recent",
      subtitle: "Foods you've logged recently",
      data: recentBrowseItems,
    },
    {
      key: "favorites",
      title: "Favorites",
      subtitle: "Your go-to foods for quick logging",
      data: favoriteBrowseItems,
    },
    {
      key: "my-foods",
      title: "My Foods",
      subtitle: "Foods you created and saved",
      data: myFoodBrowseItems,
    },
  ];

  const focusedSavedSections =
    browseFocus === "default"
      ? savedFoodSections
      : savedFoodSections.filter((section) => section.key === browseFocus);

  const browseSections: FoodBrowseSection[] = [
    ...(isSearching && catalogBrowseItems.length > 0
      ? [
          {
            key: "catalog",
            title: "Catalog Results",
            subtitle: `Matches for \"${debouncedSearch.trim()}\"`,
            data: catalogBrowseItems,
          },
        ]
      : []),
    ...focusedSavedSections.filter((section) => section.data.length > 0),
  ];
  const isBrowseLoading =
    recentFoodsQuery.isLoading ||
    favoritesLoading ||
    customFoodsQuery.isLoading;

  const renderBarcodeLookupSection = () => (
    <View
      style={{
        backgroundColor: colors.card,
        borderRadius: 12,
        padding: 16,
        marginBottom: 12,
      }}
    >
      <Text
        style={{
          fontSize: 14,
          fontWeight: "600",
          color: colors.textSecondary,
          marginBottom: 8,
        }}
        accessibilityRole="header"
      >
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
          accessibilityRole="button"
          accessibilityLabel="Scan barcode with camera"
          style={{
            backgroundColor: colors.primaryLight,
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
        <View style={{ marginTop: 8 }}>
          <Text
            style={{
              color: colors.danger,
              fontSize: 13,
              marginBottom: 8,
            }}
          >
            Product not found for this barcode. Try search below.
          </Text>
        </View>
      )}
      {barcodeQuery.data && (
        <TouchableOpacity
          onPress={() => handleSelectProduct(barcodeQuery.data!)}
          accessibilityRole="button"
          accessibilityLabel="Select barcode result"
          style={{ marginTop: 12 }}
        >
          <FoodSearchResult
            product={barcodeQuery.data}
            onPress={handleProductPress}
          />
        </TouchableOpacity>
      )}
    </View>
  );

  const renderStandardListHeader = ({
    browseFocus,
    onBrowseFocusChange,
  }: SearchListHeaderProps) => (
    <>
      {renderBarcodeLookupSection()}
      <View
        style={{
          backgroundColor: colors.card,
          borderRadius: 12,
          padding: 16,
          marginBottom: 12,
        }}
      >
        <Text
          style={{
            fontSize: 14,
            fontWeight: "600",
            color: colors.textSecondary,
            marginTop: 16,
            marginBottom: 8,
          }}
          accessibilityRole="header"
        >
          Search Foods
        </Text>
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
            backgroundColor: colors.card,
          }}
        />
        {searchResults.isError && (
          <Text style={{ color: colors.danger, fontSize: 13, marginTop: 8 }}>
            Search failed - try again
          </Text>
        )}
        {searchResults.isLoading && <SearchResultSkeleton count={3} />}
        {pendingLookup.isLoading && <SearchResultSkeleton count={1} />}
      </View>

      <View
        style={{
          backgroundColor: colors.card,
          borderRadius: 12,
          padding: 4,
          marginBottom: 12,
          flexDirection: "row",
          gap: 4,
        }}
      >
        {[
          { id: "default" as const, label: "Browse" },
          { id: "favorites" as const, label: "Favorites" },
          { id: "my-foods" as const, label: "My Foods" },
        ].map((option) => {
          const isActive = browseFocus === option.id;

          return (
            <TouchableOpacity
              key={option.id}
              onPress={() => onBrowseFocusChange(option.id)}
              style={{
                flex: 1,
                borderRadius: 8,
                paddingVertical: 10,
                alignItems: "center",
                backgroundColor: isActive ? colors.primaryLight : "transparent",
              }}
            >
              <Text
                style={{
                  color: isActive ? colors.textOnPrimary : colors.textSecondary,
                  fontWeight: isActive ? "700" : "500",
                  fontSize: 14,
                }}
              >
                {option.label}
              </Text>
            </TouchableOpacity>
          );
        })}
      </View>
    </>
  );

  const renderSearchEmpty = () => {
    if (!isSearching || searchResults.isLoading || searchResults.isError) {
      return null;
    }

    return (
      <View
        style={{
          backgroundColor: colors.primaryBg,
          borderRadius: 12,
          borderWidth: 1,
          borderColor: colors.primaryBorder,
          padding: 16,
          marginTop: 20,
          marginBottom: 8,
        }}
      >
        <View style={{ flexDirection: "row", alignItems: "center", gap: 10 }}>
          <View
            style={{
              width: 40,
              height: 40,
              borderRadius: 20,
              backgroundColor: colors.primaryLight,
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            <Ionicons
              name="sparkles-outline"
              size={20}
              color={colors.textOnPrimary}
            />
          </View>
          <View style={{ flex: 1 }}>
            <Text
              style={{
                fontSize: 16,
                fontWeight: "700",
                color: colors.text,
              }}
            >
              Create with AI instead
            </Text>
            <Text
              style={{
                color: colors.textSecondary,
                fontSize: 13,
                lineHeight: 18,
                marginTop: 4,
              }}
            >
              Describe the food, take a photo, or choose one from your library.
            </Text>
          </View>
        </View>

        <TouchableOpacity
          onPress={openCreateWithAi}
          accessibilityRole="button"
          accessibilityLabel="Create new food with AI"
          style={{
            marginTop: 14,
            backgroundColor: colors.primaryLight,
            borderRadius: 10,
            paddingVertical: 12,
            alignItems: "center",
            justifyContent: "center",
            flexDirection: "row",
            gap: 8,
          }}
        >
          <Ionicons
            name="sparkles-outline"
            size={18}
            color={colors.textOnPrimary}
          />
          <Text style={{ color: colors.textOnPrimary, fontWeight: "700" }}>
            Create New Food with AI
          </Text>
        </TouchableOpacity>
      </View>
    );
  };

  const renderBrowseEmpty = () => {
    if (isSearching) {
      return null;
    }

    if (isBrowseLoading) {
      return (
        <View style={{ alignItems: "center", paddingVertical: 32 }}>
          <ActivityIndicator color={colors.primaryLight} />
        </View>
      );
    }

    if (browseFocus === "favorites") {
      return (
        <View
          style={{
            backgroundColor: colors.card,
            borderRadius: 12,
            padding: 20,
            alignItems: "center",
            marginTop: 8,
          }}
        >
          <Ionicons name="heart-outline" size={28} color={colors.textLight} />
          <Text
            style={{
              color: colors.text,
              fontWeight: "700",
              fontSize: 15,
              marginTop: 10,
            }}
          >
            No favorites yet
          </Text>
          <Text
            style={{
              color: colors.textSecondary,
              fontSize: 13,
              lineHeight: 18,
              textAlign: "center",
              marginTop: 6,
            }}
          >
            Tap the heart on a food to save it here for quick logging.
          </Text>
        </View>
      );
    }

    if (browseFocus === "my-foods") {
      return (
        <View
          style={{
            backgroundColor: colors.card,
            borderRadius: 12,
            padding: 20,
            alignItems: "center",
            marginTop: 8,
          }}
        >
          <Ionicons
            name="folder-open-outline"
            size={28}
            color={colors.textLight}
          />
          <Text
            style={{
              color: colors.text,
              fontWeight: "700",
              fontSize: 15,
              marginTop: 10,
            }}
          >
            No foods in My Foods yet
          </Text>
          <Text
            style={{
              color: colors.textSecondary,
              fontSize: 13,
              lineHeight: 18,
              textAlign: "center",
              marginTop: 6,
            }}
          >
            Save a food from the Create Food flow and it will show up here.
          </Text>
        </View>
      );
    }

    return (
      <View
        style={{
          backgroundColor: colors.card,
          borderRadius: 12,
          padding: 20,
          alignItems: "center",
          marginTop: 8,
        }}
      >
        <Ionicons
          name="restaurant-outline"
          size={28}
          color={colors.textLight}
        />
        <Text
          style={{
            color: colors.text,
            fontWeight: "700",
            fontSize: 15,
            marginTop: 10,
          }}
        >
          Start building your food library
        </Text>
        <Text
          style={{
            color: colors.textSecondary,
            fontSize: 13,
            lineHeight: 18,
            textAlign: "center",
            marginTop: 6,
          }}
        >
          Recent foods, favorites, and My Foods will appear here once you start
          logging and saving foods.
        </Text>
      </View>
    );
  };

  const renderStickyCreateWithAiButton = () => (
    <View
      style={{
        paddingHorizontal: 16,
        paddingTop: 12,
        paddingBottom: 12,
        borderTopWidth: 1,
        borderTopColor: colors.borderLight,
        backgroundColor: colors.bg,
      }}
    >
      <TouchableOpacity
        onPress={openCreateWithAi}
        accessibilityRole="button"
        accessibilityLabel="Create new food with AI"
        style={{
          backgroundColor: colors.primaryLight,
          borderRadius: 12,
          minHeight: 52,
          flexDirection: "row",
          alignItems: "center",
          justifyContent: "center",
          gap: 8,
        }}
      >
        <Ionicons
          name="sparkles-outline"
          size={18}
          color={colors.textOnPrimary}
        />
        <Text
          style={{
            color: colors.textOnPrimary,
            fontWeight: "700",
            fontSize: 15,
          }}
        >
          Create New Food with AI
        </Text>
      </TouchableOpacity>
    </View>
  );

  const renderStandardListFooter = () => (
    <>
      {safetyReport.isLoading && <SearchResultSkeleton count={2} />}
      {safetyReport.isError && (
        <ErrorState
          message="Failed to load safety report"
          onRetry={() => safetyReport.refetch()}
        />
      )}
      {safetyReport.data && (
        <View
          style={{
            backgroundColor: colors.card,
            borderRadius: 12,
            padding: 16,
            marginTop: 4,
          }}
        >
          <View
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <Text
              style={{
                fontSize: 18,
                fontWeight: "700",
                color: colors.text,
                marginBottom: 4,
              }}
            >
              {safetyReport.data.product.name}
            </Text>
            {selectedProductId && (
              <TouchableOpacity
                onPress={() => router.push(`/food/${selectedProductId}`)}
                accessibilityRole="link"
                accessibilityLabel="View full food details"
              >
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.protein,
                    fontWeight: "600",
                  }}
                >
                  Full Details →
                </Text>
              </TouchableOpacity>
            )}
          </View>

          <NutritionBar
            calories={Math.round(safetyReport.data.product.calories100g ?? 0)}
            proteinG={Math.round(safetyReport.data.product.protein100g ?? 0)}
            carbsG={Math.round(safetyReport.data.product.carbs100g ?? 0)}
            fatG={Math.round(safetyReport.data.product.fat100g ?? 0)}
            subtitle="per 100g"
          />

          {safetyReport.data.additives.length > 0 && (
            <>
              <Text
                style={{
                  fontSize: 14,
                  fontWeight: "600",
                  color: colors.textSecondary,
                  marginBottom: 8,
                }}
              >
                Additives ({safetyReport.data.additives.length})
              </Text>
              {safetyReport.data.additives.map((add) => (
                <View
                  key={add.id}
                  style={{
                    borderLeftWidth: 3,
                    borderLeftColor: ratingColor(add.cspiRating),
                    paddingLeft: 12,
                    paddingVertical: 8,
                    marginBottom: 8,
                  }}
                >
                  <View
                    style={{
                      flexDirection: "row",
                      justifyContent: "space-between",
                      alignItems: "flex-start",
                    }}
                  >
                    <View style={{ flex: 1 }}>
                      <View style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
                        <Text style={{ fontWeight: "600", color: colors.text }}>
                          {add.name} {add.eNumber ? `(${add.eNumber})` : ""}
                        </Text>
                        <View
                          style={{
                            backgroundColor: ratingColor(add.cspiRating) + "18",
                            paddingHorizontal: 6,
                            paddingVertical: 1,
                            borderRadius: 8,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 10,
                              fontWeight: "600",
                              color: ratingColor(add.cspiRating),
                            }}
                          >
                            {cspiEmoji(add.cspiRating)} {add.cspiRating}
                          </Text>
                        </View>
                      </View>
                    </View>
                    {!alertIds.has(add.id) ? (
                      <TouchableOpacity
                        onPress={() => addAlertMutation.mutate(add.id)}
                        accessibilityRole="button"
                        accessibilityLabel={`Add alert for ${add.name}`}
                        style={{
                          backgroundColor: colors.dangerBg,
                          borderRadius: 6,
                          paddingHorizontal: 8,
                          paddingVertical: 4,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color: colors.danger,
                          }}
                        >
                          + Alert
                        </Text>
                      </TouchableOpacity>
                    ) : (
                      <View
                        style={{
                          backgroundColor: colors.primaryBg,
                          borderRadius: 6,
                          paddingHorizontal: 8,
                          paddingVertical: 4,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color: colors.primaryLight,
                          }}
                        >
                          ✓ Alert
                        </Text>
                      </View>
                    )}
                  </View>
                  {add.healthConcerns && (
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.danger,
                        marginTop: 4,
                      }}
                      numberOfLines={2}
                    >
                      ⚠ {add.healthConcerns}
                    </Text>
                  )}
                </View>
              ))}
            </>
          )}

          {showAddToMeal ? (
            <View
              style={{
                marginTop: 12,
                backgroundColor: colors.bg,
                borderRadius: 8,
                padding: 12,
              }}
            >
              <Text
                style={{
                  fontSize: 13,
                  fontWeight: "600",
                  color: colors.textSecondary,
                  marginBottom: 8,
                }}
              >
                Add to meal:
              </Text>
              <MealTypePicker
                selected={addToMealType}
                onSelect={setAddToMealType}
              />
              <ServingSizeSelector
                servingG={addToMealServingG}
                onServingChange={setAddToMealServingG}
                customText={customServingText}
                onCustomTextChange={setCustomServingText}
                multiplier={addToMealMultiplier}
                onMultiplierChange={setAddToMealMultiplier}
                product={addToMealProduct ?? safetyReport.data?.product}
                summaryText={
                  safetyReport.data?.product
                    ? nutritionSummaryText(
                        scaleNutrition(
                          safetyReport.data.product,
                          effectiveGrams,
                        ),
                        effectiveGrams,
                      )
                    : undefined
                }
              />
              <View
                style={{
                  flexDirection: "row",
                  justifyContent: "flex-end",
                  gap: 8,
                  marginTop: 8,
                }}
              >
                <TouchableOpacity
                  onPress={() => {
                    setShowAddToMeal(false);
                    setAddToMealProduct(null);
                  }}
                  accessibilityRole="button"
                  accessibilityLabel="Cancel"
                  style={{ paddingHorizontal: 12, paddingVertical: 8 }}
                >
                  <Text
                    style={{
                      color: colors.textSecondary,
                      fontWeight: "600",
                    }}
                  >
                    Cancel
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={() =>
                    addToMealMutation.mutate(safetyReport.data!.product)
                  }
                  disabled={addToMealMutation.isPending}
                  accessibilityRole="button"
                  accessibilityLabel="Log meal"
                  style={{
                    backgroundColor: colors.primaryLight,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: 8,
                  }}
                >
                  {addToMealMutation.isPending ? (
                    <ActivityIndicator
                      color={colors.textOnPrimary}
                      size="small"
                    />
                  ) : (
                    <Text
                      style={{
                        color: colors.textOnPrimary,
                        fontWeight: "600",
                      }}
                    >
                      Log It
                    </Text>
                  )}
                </TouchableOpacity>
              </View>
            </View>
          ) : (
            <TouchableOpacity
              onPress={() => {
                setShowAddToMeal(true);
                setAddToMealMultiplier(1);
                setCustomServingText("");
                setAddToMealServingG(
                  safetyReport.data?.product.servingQuantity
                    ? Math.round(safetyReport.data.product.servingQuantity)
                    : 100,
                );
              }}
              accessibilityRole="button"
              accessibilityLabel="Add to meal"
              style={{
                marginTop: 12,
                backgroundColor: colors.primaryLight,
                borderRadius: 8,
                padding: 12,
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "center",
              }}
            >
              <Ionicons
                name="add-circle-outline"
                size={18}
                color={colors.textOnPrimary}
              />
              <Text
                style={{
                  color: colors.textOnPrimary,
                  fontWeight: "600",
                  marginLeft: 6,
                  fontSize: 14,
                }}
              >
                Add to Meal
              </Text>
            </TouchableOpacity>
          )}
        </View>
      )}
    </>
  );

  const renderBrowseSectionHeader = ({
    section,
  }: {
    section: FoodBrowseSection;
  }) => (
    <View style={{ paddingTop: 8, paddingBottom: 8 }}>
      <Text
        style={{
          fontSize: 16,
          fontWeight: "700",
          color: colors.text,
        }}
      >
        {section.title}
      </Text>
      <Text
        style={{
          fontSize: 12,
          color: colors.textSecondary,
          marginTop: 2,
        }}
      >
        {section.subtitle}
      </Text>
    </View>
  );

  if (showCamera) {
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
            onPress={() => setShowCamera(false)}
            accessibilityRole="button"
            accessibilityLabel="Close barcode scanner"
            style={{ alignSelf: "flex-start", padding: 4 }}
          >
            <Ionicons name="close" size={28} color="#fff" />
          </TouchableOpacity>
          <Text
            style={{
              color: "#fff",
              fontSize: 22,
              fontWeight: "700",
              marginTop: 12,
            }}
          >
            Scan Barcode
          </Text>
          <Text
            style={{
              color: "rgba(255,255,255,0.88)",
              fontSize: 14,
              marginTop: 6,
            }}
          >
            Center the barcode in view to look up the product.
          </Text>
        </View>
      </View>
    );
  }

  return (
    <View style={{ flex: 1 }}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
        keyboardVerticalOffset={Platform.OS === "ios" ? 0 : 0}
      >
        <SectionList
          style={{ flex: 1, backgroundColor: colors.bg }}
          sections={browseSections}
          keyExtractor={(item) => item.key}
          renderItem={({ item }) => (
            <FoodSearchResult
              product={item.product}
              onPress={handleProductPress}
              onDetailPress={item.allowDetails ? handleDetailPress : undefined}
              hideFavorite={item.hideFavorite}
            />
          )}
          renderSectionHeader={renderBrowseSectionHeader}
          ListHeaderComponent={
            <>
              {renderStandardListHeader({
                browseFocus,
                onBrowseFocusChange: setBrowseFocus,
              })}
              {renderSearchEmpty()}
            </>
          }
          ListEmptyComponent={renderBrowseEmpty}
          ListFooterComponent={renderStandardListFooter}
          contentContainerStyle={{
            paddingHorizontal: 16,
            paddingTop: 16,
            paddingBottom: 96,
          }}
          stickySectionHeadersEnabled={false}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="on-drag"
          showsVerticalScrollIndicator={false}
          refreshControl={
            <RefreshControl
              refreshing={refreshing}
              onRefresh={onRefresh}
              tintColor={colors.primaryLight}
            />
          }
        />
      </KeyboardAvoidingView>

      {!showAddToMeal && renderStickyCreateWithAiButton()}

      <BottomSheet
        visible={showAddToMeal && !!addToMealProduct}
        onClose={() => {
          setShowAddToMeal(false);
          setAddToMealProduct(null);
        }}
      >
        <Text
          style={{
            fontSize: 20,
            fontWeight: "700",
            color: colors.text,
            marginBottom: 4,
          }}
        >
          Add to Meal
        </Text>
        <Text
          style={{
            fontSize: 16,
            color: colors.textSecondary,
            marginBottom: 20,
          }}
        >
          {addToMealProduct?.name}
        </Text>

        <View style={{ marginBottom: 20 }}>
          <Text
            style={{
              fontSize: 14,
              fontWeight: "600",
              color: colors.textSecondary,
              marginBottom: 8,
            }}
          >
            Meal Type:
          </Text>
          <MealTypePicker
            selected={addToMealType}
            onSelect={setAddToMealType}
          />
        </View>

        <ServingSizeSelector
          servingG={addToMealServingG}
          onServingChange={setAddToMealServingG}
          customText={customServingText}
          onCustomTextChange={setCustomServingText}
          multiplier={addToMealMultiplier}
          onMultiplierChange={setAddToMealMultiplier}
          product={addToMealProduct}
          summaryText={
            addToMealProduct
              ? nutritionSummaryText(
                  scaleNutrition(addToMealProduct, effectiveGrams),
                  effectiveGrams,
                )
              : undefined
          }
        />

        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            gap: 12,
            marginTop: 24,
          }}
        >
          <TouchableOpacity
            onPress={() => {
              setShowAddToMeal(false);
              setAddToMealProduct(null);
            }}
            accessibilityRole="button"
            accessibilityLabel="Cancel"
            style={{
              flex: 1,
              paddingVertical: 14,
              borderRadius: 12,
              backgroundColor: colors.borderLight,
              alignItems: "center",
            }}
          >
            <Text
              style={{
                color: colors.textSecondary,
                fontWeight: "600",
                fontSize: 16,
              }}
            >
              Cancel
            </Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={() => addToMealMutation.mutate(addToMealProduct!)}
            disabled={addToMealMutation.isPending}
            accessibilityRole="button"
            accessibilityLabel="Log meal"
            style={{
              flex: 1,
              backgroundColor: colors.primaryLight,
              paddingVertical: 14,
              borderRadius: 12,
              alignItems: "center",
            }}
          >
            {addToMealMutation.isPending ? (
              <ActivityIndicator color={colors.textOnPrimary} size="small" />
            ) : (
              <Text
                style={{
                  color: colors.textOnPrimary,
                  fontWeight: "600",
                  fontSize: 16,
                }}
              >
                Log It
              </Text>
            )}
          </TouchableOpacity>
        </View>
      </BottomSheet>
    </View>
  );
}
