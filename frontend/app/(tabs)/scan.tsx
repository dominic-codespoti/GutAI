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
  Image,
} from "react-native";
import { CameraView, useCameraPermissions } from "expo-camera";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { foodApi, userApi, mealApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { toast } from "../../src/stores/toast";
import { ErrorState } from "../../components/ErrorState";
import type { FoodProduct, SafetyReport } from "../../src/types";
import { useRouter } from "expo-router";
import { MEAL_TYPES } from "../../src/utils/constants";
import { ratingColor } from "../../src/utils/colors";

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
  const [addToMealProduct, setAddToMealProduct] = useState<FoodProduct | null>(
    null,
  );

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
    placeholderData: (prev) => prev,
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
      const s = addToMealServingG;
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
            calories: Math.round(((product.calories100g ?? 0) * s) / 100),
            proteinG: Math.round(((product.protein100g ?? 0) * s) / 100),
            carbsG: Math.round(((product.carbs100g ?? 0) * s) / 100),
            fatG: Math.round(((product.fat100g ?? 0) * s) / 100),
            fiberG: Math.round(((product.fiber100g ?? 0) * s) / 100),
            sugarG: Math.round(((product.sugar100g ?? 0) * s) / 100),
            sodiumMg: Math.round(((product.sodium100g ?? 0) * s) / 100),
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
      <View style={{ padding: 20 }}>
        <Text
          style={{
            fontSize: 20,
            fontWeight: "700",
            color: "#0f172a",
            marginBottom: 16,
          }}
        >
          Food Lookup
        </Text>

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
                Safety Report
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
            <Text style={{ fontSize: 16, color: "#334155", marginBottom: 12 }}>
              {safetyReport.data.product.name}
            </Text>

            <View
              style={{
                flexDirection: "row",
                justifyContent: "space-around",
                marginBottom: 16,
              }}
            >
              {safetyReport.data.safetyRating && (
                <View style={{ alignItems: "center" }}>
                  <Text
                    style={{
                      fontSize: 24,
                      fontWeight: "700",
                      color: ratingColor(safetyReport.data.safetyRating),
                    }}
                  >
                    {safetyReport.data.safetyRating}
                  </Text>
                  <Text style={{ fontSize: 12, color: "#64748b" }}>Safety</Text>
                </View>
              )}
              {safetyReport.data.novaGroup && (
                <View style={{ alignItems: "center" }}>
                  <Text
                    style={{
                      fontSize: 24,
                      fontWeight: "700",
                      color:
                        safetyReport.data.novaGroup >= 4
                          ? "#ef4444"
                          : "#22c55e",
                    }}
                  >
                    {safetyReport.data.novaGroup}
                  </Text>
                  <Text style={{ fontSize: 12, color: "#64748b" }}>NOVA</Text>
                </View>
              )}
              {safetyReport.data.nutriScore &&
                !safetyReport.data.nutriScore.toLowerCase().includes("not") && (
                  <View style={{ alignItems: "center" }}>
                    <Text
                      style={{
                        fontSize: 24,
                        fontWeight: "700",
                        color: "#3b82f6",
                      }}
                    >
                      {safetyReport.data.nutriScore.toUpperCase()}
                    </Text>
                    <Text style={{ fontSize: 12, color: "#64748b" }}>
                      Nutri-Score
                    </Text>
                  </View>
                )}
            </View>

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
                <View style={{ flexDirection: "row", marginBottom: 12 }}>
                  {MEAL_TYPES.map((type) => (
                    <TouchableOpacity
                      key={type}
                      onPress={() => setAddToMealType(type)}
                      style={{
                        flex: 1,
                        paddingVertical: 6,
                        borderRadius: 6,
                        marginHorizontal: 2,
                        backgroundColor:
                          addToMealType === type ? "#22c55e" : "#e2e8f0",
                        alignItems: "center",
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 11,
                          fontWeight: "600",
                          color: addToMealType === type ? "#fff" : "#64748b",
                        }}
                      >
                        {type}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </View>
                {/* Serving Size Presets */}
                <Text
                  style={{
                    fontSize: 12,
                    fontWeight: "600",
                    color: "#334155",
                    marginBottom: 6,
                  }}
                >
                  Serving size:
                </Text>
                <View
                  style={{
                    flexDirection: "row",
                    flexWrap: "wrap",
                    gap: 6,
                    marginBottom: 8,
                  }}
                >
                  {(() => {
                    const p = addToMealProduct ?? safetyReport.data?.product;
                    const presets: { label: string; grams: number }[] = [];
                    if (p?.servingQuantity && p.servingSize) {
                      presets.push({
                        label: `1 serving (${p.servingSize})`,
                        grams: Math.round(p.servingQuantity),
                      });
                    }
                    presets.push({ label: "50g", grams: 50 });
                    presets.push({ label: "100g", grams: 100 });
                    presets.push({ label: "150g", grams: 150 });
                    presets.push({ label: "200g", grams: 200 });
                    presets.push({ label: "250g", grams: 250 });
                    return presets.map((preset) => (
                      <TouchableOpacity
                        key={preset.label}
                        onPress={() => setAddToMealServingG(preset.grams)}
                        style={{
                          paddingHorizontal: 10,
                          paddingVertical: 6,
                          borderRadius: 6,
                          backgroundColor:
                            addToMealServingG === preset.grams
                              ? "#22c55e"
                              : "#e2e8f0",
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color:
                              addToMealServingG === preset.grams
                                ? "#fff"
                                : "#64748b",
                          }}
                        >
                          {preset.label}
                        </Text>
                      </TouchableOpacity>
                    ));
                  })()}
                </View>
                <Text
                  style={{ fontSize: 11, color: "#94a3b8", marginBottom: 8 }}
                >
                  {Math.round(
                    ((safetyReport.data?.product.calories100g ?? 0) *
                      addToMealServingG) /
                      100,
                  )}{" "}
                  cal ·{" "}
                  {Math.round(
                    ((safetyReport.data?.product.protein100g ?? 0) *
                      addToMealServingG) /
                      100,
                  )}
                  g P ·{" "}
                  {Math.round(
                    ((safetyReport.data?.product.carbs100g ?? 0) *
                      addToMealServingG) /
                      100,
                  )}
                  g C ·{" "}
                  {Math.round(
                    ((safetyReport.data?.product.fat100g ?? 0) *
                      addToMealServingG) /
                      100,
                  )}
                  g F
                </Text>
                <View
                  style={{
                    flexDirection: "row",
                    justifyContent: "flex-end",
                    gap: 8,
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
      </View>

      {/* Camera Modal */}
      <Modal visible={showCamera} animationType="slide">
        <View style={{ flex: 1, backgroundColor: "#000" }}>
          <View style={{ flex: 1 }}>
            <CameraView
              style={{ flex: 1 }}
              facing="back"
              barcodeScannerSettings={{
                barcodeTypes: [
                  "ean13",
                  "ean8",
                  "upc_a",
                  "upc_e",
                  "code128",
                  "code39",
                ],
              }}
              onBarcodeScanned={handleBarcodeScanned}
            />
            <View
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                right: 0,
                bottom: 0,
                justifyContent: "center",
                alignItems: "center",
              }}
            >
              <View
                style={{
                  width: 260,
                  height: 160,
                  borderWidth: 2,
                  borderColor: "#22c55e",
                  borderRadius: 12,
                  backgroundColor: "transparent",
                }}
              />
              <Text
                style={{
                  color: "#fff",
                  fontSize: 14,
                  marginTop: 16,
                  textAlign: "center",
                }}
              >
                Point camera at barcode
              </Text>
            </View>
          </View>
          <TouchableOpacity
            onPress={() => setShowCamera(false)}
            style={{
              backgroundColor: "#ef4444",
              paddingVertical: 16,
              alignItems: "center",
            }}
          >
            <Text style={{ color: "#fff", fontWeight: "700", fontSize: 16 }}>
              Close Camera
            </Text>
          </TouchableOpacity>
        </View>
      </Modal>
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
          resizeMode="contain"
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
              {product.calories100g} cal/100g
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
