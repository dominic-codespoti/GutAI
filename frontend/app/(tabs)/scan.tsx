import { useState, useEffect, useCallback } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
  RefreshControl,
  Modal,
  BackHandler,
  Platform,
} from "react-native";
import { Image } from "expo-image";
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
import {
  scaleNutrition,
  nutritionSummaryText,
} from "../../src/utils/nutrition";
import type { FoodProduct, SafetyReport } from "../../src/types";
import { useRouter } from "expo-router";
import { MEAL_TYPES } from "../../src/utils/constants";
import { ratingColor } from "../../src/utils/colors";
import { maybeRequestReview } from "../../src/utils/review";

export default function ScanScreen() {
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
    const timer = setTimeout(() => setDebouncedSearch(searchText), 300);
    return () => clearTimeout(timer);
  }, [searchText]);

  const barcodeQuery = useQuery({
    queryKey: ["barcode", barcode],
    queryFn: () => foodApi.lookupBarcode(barcode).then((r) => r.data),
    enabled: barcode.length >= 8,
  });

  const searchResults = useQuery({
    queryKey: ["food-search", debouncedSearch],
    queryFn: () => foodApi.search(debouncedSearch).then((r) => r.data),
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

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all(
      [searchResults.refetch(), safetyReport.refetch()].filter(Boolean),
    );
    setRefreshing(false);
  }, [searchResults.refetch, safetyReport.refetch]);

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

  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: "#f8fafc" }}
      refreshControl={
        <RefreshControl
          refreshing={refreshing}
          onRefresh={onRefresh}
          tintColor="#22c55e"
        />
      }
    >
      {/* Barcode Input */}
      <View
        style={{
          backgroundColor: "#fff",
          borderRadius: 12,
          padding: 16,
          marginBottom: 12,
        }}
      >
        <Text
          style={{
            fontSize: 14,
            fontWeight: "600",
            color: "#334155",
            marginBottom: 8,
          }}
        >
          Barcode Lookup
        </Text>
        <View style={{ flexDirection: "row", gap: 8 }}>
          <TextInput
            placeholder="Enter barcode number"
            value={barcode}
            onChangeText={setBarcode}
            keyboardType="number-pad"
            style={{
              flex: 1,
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 8,
              padding: 12,
              fontSize: 16,
            }}
          />
          <TouchableOpacity
            onPress={openCamera}
            style={{
              backgroundColor: "#22c55e",
              borderRadius: 8,
              width: 48,
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            <Ionicons name="camera" size={24} color="#fff" />
          </TouchableOpacity>
        </View>
        {barcodeQuery.isLoading && (
          <ActivityIndicator style={{ marginTop: 8 }} />
        )}
        {barcodeQuery.isError && (
          <Text style={{ color: "#ef4444", fontSize: 13, marginTop: 8 }}>
            Product not found for this barcode
          </Text>
        )}
        {barcodeQuery.data && (
          <TouchableOpacity
            onPress={() => handleSelectProduct(barcodeQuery.data!)}
            style={{ marginTop: 12 }}
          >
            <ProductCard
              product={barcodeQuery.data}
              onDetailPress={() => {
                const pid = barcodeQuery.data!.id;
                if (pid && pid !== "00000000-0000-0000-0000-000000000000") {
                  router.push(`/food/${pid}`);
                }
              }}
            />
          </TouchableOpacity>
        )}
      </View>

      {/* Search */}
      <View
        style={{
          backgroundColor: "#fff",
          borderRadius: 12,
          padding: 16,
          marginBottom: 12,
        }}
      >
        <Text
          style={{
            fontSize: 14,
            fontWeight: "600",
            color: "#334155",
            marginBottom: 8,
          }}
        >
          Search Foods
        </Text>
        <TextInput
          placeholder="Search by name..."
          value={searchText}
          onChangeText={setSearchText}
          style={{
            borderWidth: 1,
            borderColor: "#e2e8f0",
            borderRadius: 8,
            padding: 12,
            fontSize: 16,
          }}
        />
        {searchResults.isLoading && (
          <ActivityIndicator style={{ marginTop: 8 }} />
        )}
        {searchResults.isError && (
          <Text style={{ color: "#ef4444", fontSize: 13, marginTop: 8 }}>
            Search failed — try again
          </Text>
        )}
        {pendingLookup.isLoading && (
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              marginTop: 8,
            }}
          >
            <ActivityIndicator size="small" />
            <Text style={{ marginLeft: 8, color: "#64748b", fontSize: 13 }}>
              Loading product details...
            </Text>
          </View>
        )}
        {searchResults.data?.map((product, index) => (
          <TouchableOpacity
            key={product.barcode || `search-${index}`}
            onPress={() => handleSelectProduct(product)}
            style={{ marginTop: 8 }}
          >
            <ProductCard
              product={product}
              onDetailPress={() => {
                const pid = product.id;
                if (pid && pid !== "00000000-0000-0000-0000-000000000000") {
                  router.push(`/food/${pid}`);
                }
              }}
            />
          </TouchableOpacity>
        ))}
      </View>

      {/* Safety Report */}
      {safetyReport.isLoading && (
        <ActivityIndicator
          size="large"
          color="#22c55e"
          style={{ marginVertical: 20 }}
        />
      )}
      {safetyReport.isError && (
        <ErrorState
          message="Failed to load safety report"
          onRetry={() => safetyReport.refetch()}
        />
      )}
      {safetyReport.data && (
        <View
          style={{
            backgroundColor: "#fff",
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
                color: "#0f172a",
                marginBottom: 4,
              }}
            >
              {safetyReport.data.product.name}
            </Text>
            {selectedProductId && (
              <TouchableOpacity
                onPress={() => router.push(`/food/${selectedProductId}`)}
              >
                <Text
                  style={{
                    fontSize: 13,
                    color: "#3b82f6",
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
                  color: "#334155",
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
                      <Text style={{ fontWeight: "600", color: "#0f172a" }}>
                        {add.name} {add.eNumber ? `(${add.eNumber})` : ""}
                      </Text>
                      <Text
                        style={{
                          fontSize: 12,
                          color: "#64748b",
                          marginTop: 2,
                        }}
                      >
                        CSPI: {add.cspiRating} · US: {add.usStatus} · EU:{" "}
                        {add.euStatus}
                      </Text>
                    </View>
                    {!alertIds.has(add.id) ? (
                      <TouchableOpacity
                        onPress={() => addAlertMutation.mutate(add.id)}
                        style={{
                          backgroundColor: "#fef2f2",
                          borderRadius: 6,
                          paddingHorizontal: 8,
                          paddingVertical: 4,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color: "#ef4444",
                          }}
                        >
                          + Alert
                        </Text>
                      </TouchableOpacity>
                    ) : (
                      <View
                        style={{
                          backgroundColor: "#f0fdf4",
                          borderRadius: 6,
                          paddingHorizontal: 8,
                          paddingVertical: 4,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color: "#22c55e",
                          }}
                        >
                          ✓ Alert
                        </Text>
                      </View>
                    )}
                  </View>
                  {add.healthConcerns && (
                    <Text
                      style={{ fontSize: 12, color: "#ef4444", marginTop: 2 }}
                    >
                      ⚠ {add.healthConcerns}
                    </Text>
                  )}
                </View>
              ))}
            </>
          )}

          {/* Quick Add to Meal */}
          {showAddToMeal ? (
            <View
              style={{
                marginTop: 12,
                backgroundColor: "#f8fafc",
                borderRadius: 8,
                padding: 12,
              }}
            >
              <Text
                style={{
                  fontSize: 13,
                  fontWeight: "600",
                  color: "#334155",
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
                  style={{ paddingHorizontal: 12, paddingVertical: 8 }}
                >
                  <Text style={{ color: "#64748b", fontWeight: "600" }}>
                    Cancel
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={() =>
                    addToMealMutation.mutate(safetyReport.data!.product)
                  }
                  disabled={addToMealMutation.isPending}
                  style={{
                    backgroundColor: "#22c55e",
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: 8,
                  }}
                >
                  {addToMealMutation.isPending ? (
                    <ActivityIndicator color="#fff" size="small" />
                  ) : (
                    <Text style={{ color: "#fff", fontWeight: "600" }}>
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
              style={{
                marginTop: 12,
                backgroundColor: "#22c55e",
                borderRadius: 8,
                padding: 12,
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "center",
              }}
            >
              <Ionicons name="add-circle-outline" size={18} color="#fff" />
              <Text
                style={{
                  color: "#fff",
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
    </ScrollView>
  );
}

function ProductCard({
  product,
  onDetailPress,
}: {
  product: FoodProduct;
  onDetailPress?: () => void;
}) {
  return (
    <View
      style={{
        backgroundColor: "#f8fafc",
        borderRadius: 8,
        padding: 12,
        borderWidth: 1,
        borderColor: "#e2e8f0",
        flexDirection: "row",
      }}
    >
      {product.imageUrl && (
        <Image
          source={{ uri: product.imageUrl }}
          style={{ width: 60, height: 60, borderRadius: 8, marginRight: 12 }}
          contentFit="contain"
          transition={200}
        />
      )}
      <View style={{ flex: 1 }}>
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            alignItems: "flex-start",
          }}
        >
          <View style={{ flex: 1 }}>
            <Text style={{ fontSize: 15, fontWeight: "600", color: "#0f172a" }}>
              {product.name}
            </Text>
            {product.brand && (
              <Text style={{ fontSize: 13, color: "#64748b" }}>
                {product.brand}
              </Text>
            )}
          </View>
          {onDetailPress && (
            <TouchableOpacity onPress={onDetailPress} style={{ padding: 4 }}>
              <Ionicons
                name="information-circle-outline"
                size={20}
                color="#3b82f6"
              />
            </TouchableOpacity>
          )}
        </View>
        <View style={{ flexDirection: "row", marginTop: 8 }}>
          {product.calories100g != null && (
            <Text style={{ fontSize: 12, color: "#334155", marginRight: 12 }}>
              {Math.round(product.calories100g)} cal/100g
            </Text>
          )}
          {product.safetyRating && (
            <Text style={{ fontSize: 12, fontWeight: "600", color: "#334155" }}>
              Safety: {product.safetyRating}
            </Text>
          )}
        </View>
      </View>
    </View>
  );
}
