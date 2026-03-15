import { useState, useEffect, useCallback } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
  RefreshControl,
  BackHandler,
  Platform,
  Keyboard,
  KeyboardAvoidingView,
} from "react-native";
import { Image } from "expo-image";
import * as Haptics from "expo-haptics";
import * as haptics from "../../src/utils/haptics";
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
import type { FoodProduct, SafetyReport } from "../../src/types";
import { useRouter } from "expo-router";
import { MEAL_TYPES } from "../../src/utils/constants";
import { ratingColor } from "../../src/utils/colors";
import { maybeRequestReview } from "../../src/utils/review";
import { useThemeColors } from "../../src/stores/theme";
import { SearchResultSkeleton } from "../../components/SkeletonLoader";

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
      queryClient.invalidateQueries({ queryKey: ["recent-foods"] });
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

  return (
    <View style={{ flex: 1 }}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
        keyboardVerticalOffset={Platform.OS === "ios" ? 0 : 0}
      >
        <ScrollView
          style={{ flex: 1, backgroundColor: colors.bg }}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="on-drag"
          refreshControl={
            <RefreshControl
              refreshing={refreshing}
              onRefresh={onRefresh}
              tintColor={colors.primaryLight}
            />
          }
        >
          {/* Barcode Input */}
          <View
            style={{
              backgroundColor: colors.card,
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            {/* 9. Section heading: Barcode Lookup */}
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
              {/* 2. Camera button */}
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
                <Ionicons
                  name="camera"
                  size={24}
                  color={colors.textOnPrimary}
                />
              </TouchableOpacity>
            </View>
            {barcodeQuery.isLoading && <SearchResultSkeleton count={1} />}
            {barcodeQuery.isError && (
              <Text
                style={{ color: colors.danger, fontSize: 13, marginTop: 8 }}
              >
                Product not found for this barcode
              </Text>
            )}
            {barcodeQuery.data && (
              <TouchableOpacity
                onPress={() => handleSelectProduct(barcodeQuery.data!)}
                accessibilityRole="button"
                accessibilityLabel="Select barcode result"
                style={{ marginTop: 12 }}
              >
                {/* 3. Barcode result */}
                <FoodSearchResult
                  product={barcodeQuery.data}
                  onPress={handleProductPress}
                />
              </TouchableOpacity>
            )}
          </View>

          {/* Search */}
          <View
            style={{
              backgroundColor: colors.card,
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            {/* 9. Section heading: Search Foods */}
            <Text
              style={{
                fontSize: 14,
                fontWeight: "600",
                color: colors.textSecondary,
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
              }}
            />
            {searchResults.isLoading && <SearchResultSkeleton count={3} />}
            {searchResults.isError && (
              <Text
                style={{ color: colors.danger, fontSize: 13, marginTop: 8 }}
              >
                Search failed — try again
              </Text>
            )}
            {pendingLookup.isLoading && <SearchResultSkeleton count={1} />}
            {searchResults.data?.map((product, index) => (
              <FoodSearchResult
                key={product.barcode || `search-${index}`}
                product={product}
                onPress={handleProductPress}
              />
            ))}
          </View>

          {/* Safety Report */}
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
                calories={Math.round(
                  safetyReport.data.product.calories100g ?? 0,
                )}
                proteinG={Math.round(
                  safetyReport.data.product.protein100g ?? 0,
                )}
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
                          <Text
                            style={{ fontWeight: "600", color: colors.text }}
                          >
                            {add.name} {add.eNumber ? `(${add.eNumber})` : ""}
                          </Text>
                          <Text
                            style={{
                              fontSize: 12,
                              color: colors.textSecondary,
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
                            marginTop: 2,
                          }}
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
        </ScrollView>
      </KeyboardAvoidingView>

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
