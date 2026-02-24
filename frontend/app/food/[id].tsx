import { useState, useCallback, useEffect } from "react";
import {
  View,
  Text,
  ScrollView,
  ActivityIndicator,
  TouchableOpacity,
  RefreshControl,
  Modal,
  Image,
  TextInput,
} from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { foodApi, mealApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { ErrorState } from "../../components/ErrorState";
import { ratingColor } from "../../src/utils/colors";
import { MEAL_TYPES } from "../../src/utils/constants";
import { toast } from "../../src/stores/toast";
import type { PersonalizedScore } from "../../src/types";

const gutScoreColor = (score: number) => {
  if (score >= 80) return "#22c55e";
  if (score >= 60) return "#f59e0b";
  if (score >= 40) return "#f97316";
  return "#ef4444";
};

const fodmapScoreColor = (score: number) => {
  if (score >= 80) return "#22c55e";
  if (score >= 60) return "#f59e0b";
  if (score >= 40) return "#f97316";
  return "#ef4444";
};

const giColor = (category: string) => {
  if (category === "Low") return "#22c55e";
  if (category === "Medium") return "#f59e0b";
  if (category === "High") return "#ef4444";
  return "#94a3b8";
};

const confidenceColor = (confidence: string) => {
  if (confidence === "High") return "#22c55e";
  if (confidence === "Medium") return "#f59e0b";
  return "#94a3b8";
};

const personalScoreColor = (score: number) => {
  if (score >= 80) return "#22c55e";
  if (score >= 60) return "#3b82f6";
  if (score >= 40) return "#f59e0b";
  if (score >= 20) return "#f97316";
  return "#ef4444";
};

const ratingEmoji = (rating: string) => {
  switch (rating) {
    case "Excellent":
      return "🟢";
    case "Good":
      return "🔵";
    case "Fair":
      return "🟡";
    case "Poor":
      return "🟠";
    case "Avoid":
      return "🔴";
    default:
      return "⚪";
  }
};

export default function FoodDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const queryClient = useQueryClient();
  const [showAddToMeal, setShowAddToMeal] = useState(false);
  const [addToMealType, setAddToMealType] = useState<string>("Lunch");
  const [servingSize, setServingSize] = useState(100);
  const [customServing, setCustomServing] = useState("");

  const {
    data: product,
    isLoading,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["food-product", id],
    queryFn: () => foodApi.get(id!).then((r) => r.data),
    enabled: !!id,
  });

  useEffect(() => {
    if (product?.servingQuantity) {
      setServingSize(Math.round(product.servingQuantity));
    }
  }, [product?.servingQuantity]);

  const { data: report, isLoading: loadingReport } = useQuery({
    queryKey: ["safety-report", id],
    queryFn: () => foodApi.safetyReport(id!).then((r) => r.data),
    enabled: !!id,
  });

  const { data: personalScore } = useQuery({
    queryKey: ["personalized-score", id],
    queryFn: () => foodApi.personalizedScore(id!).then((r) => r.data),
    enabled: !!id,
  });

  const { data: userProfile } = useQuery({
    queryKey: ["user-profile"],
    queryFn: async () => {
      const { userApi } = await import("../../src/api");
      return userApi.getProfile().then((r) => r.data);
    },
  });

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await refetch();
    setRefreshing(false);
  }, [refetch]);

  const addToMealMutation = useMutation({
    mutationFn: () =>
      mealApi.create({
        mealType: addToMealType,
        loggedAt: new Date().toISOString(),
        items: [
          {
            foodName: product!.name,
            barcode: product!.barcode ?? undefined,
            foodProductId:
              product!.id !== "00000000-0000-0000-0000-000000000000"
                ? product!.id
                : undefined,
            servings: 1,
            servingUnit: `${servingSize}g`,
            servingWeightG: servingSize,
            calories: Math.round(
              ((product!.calories100g ?? 0) * servingSize) / 100,
            ),
            proteinG: Math.round(
              ((product!.protein100g ?? 0) * servingSize) / 100,
            ),
            carbsG: Math.round(((product!.carbs100g ?? 0) * servingSize) / 100),
            fatG: Math.round(((product!.fat100g ?? 0) * servingSize) / 100),
            fiberG: Math.round(((product!.fiber100g ?? 0) * servingSize) / 100),
            sugarG: Math.round(((product!.sugar100g ?? 0) * servingSize) / 100),
            sodiumMg: Math.round(
              ((product!.sodium100g ?? 0) * servingSize) / 100,
            ),
          },
        ],
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      setShowAddToMeal(false);
      toast.success("Added to meal!");
    },
    onError: () => toast.error("Failed to add to meal"),
  });

  if (isLoading) {
    return (
      <View
        style={{
          flex: 1,
          justifyContent: "center",
          alignItems: "center",
          backgroundColor: "#f8fafc",
        }}
      >
        <ActivityIndicator size="large" color="#22c55e" />
      </View>
    );
  }

  if (isError || !product) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <ErrorState message="Failed to load product" onRetry={refetch} />
      </View>
    );
  }

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
        <View
          style={{
            backgroundColor: "#fff",
            borderRadius: 16,
            padding: 20,
            marginBottom: 12,
          }}
        >
          {product.imageUrl && (
            <Image
              source={{ uri: product.imageUrl }}
              style={{
                width: "100%",
                height: 200,
                borderRadius: 12,
                marginBottom: 12,
                backgroundColor: "#fff",
              }}
              resizeMode="contain"
            />
          )}
          <Text style={{ fontSize: 22, fontWeight: "700", color: "#0f172a" }}>
            {product.name}
          </Text>
          {product.brand && (
            <Text style={{ fontSize: 15, color: "#64748b", marginTop: 4 }}>
              {product.brand}
            </Text>
          )}
          {product.barcode && (
            <Text style={{ fontSize: 13, color: "#94a3b8", marginTop: 4 }}>
              Barcode: {product.barcode}
            </Text>
          )}
          {product.dataSource && (
            <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 2 }}>
              Source: {product.dataSource}
            </Text>
          )}
        </View>

        {/* Safety Badges */}
        <View style={{ flexDirection: "row", gap: 8, marginBottom: 12 }}>
          {product.safetyRating && (
            <View
              style={{
                backgroundColor: ratingColor(product.safetyRating) + "18",
                borderRadius: 8,
                paddingHorizontal: 14,
                paddingVertical: 10,
                alignItems: "center",
                flex: 1,
              }}
            >
              <Text
                style={{
                  fontSize: 18,
                  fontWeight: "700",
                  color: ratingColor(product.safetyRating),
                }}
              >
                {product.safetyRating}
              </Text>
              <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                Safety
              </Text>
            </View>
          )}
          {report?.gutRisk && (
            <View
              style={{
                backgroundColor: gutScoreColor(report.gutRisk.gutScore) + "18",
                borderRadius: 8,
                paddingHorizontal: 14,
                paddingVertical: 10,
                alignItems: "center",
                flex: 1,
              }}
            >
              <Text
                style={{
                  fontSize: 18,
                  fontWeight: "700",
                  color: gutScoreColor(report.gutRisk.gutScore),
                }}
              >
                {report.gutRisk.gutScore}
              </Text>
              <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                Gut Score
              </Text>
            </View>
          )}
          {report?.fodmap && (
            <View
              style={{
                backgroundColor:
                  fodmapScoreColor(report.fodmap.fodmapScore) + "18",
                borderRadius: 8,
                paddingHorizontal: 14,
                paddingVertical: 10,
                alignItems: "center",
                flex: 1,
              }}
            >
              <Text
                style={{
                  fontSize: 18,
                  fontWeight: "700",
                  color: fodmapScoreColor(report.fodmap.fodmapScore),
                }}
              >
                {report.fodmap.fodmapScore}
              </Text>
              <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                FODMAP
              </Text>
            </View>
          )}
          {report?.glycemic?.estimatedGI != null && (
            <View
              style={{
                backgroundColor: giColor(report.glycemic.giCategory) + "18",
                borderRadius: 8,
                paddingHorizontal: 14,
                paddingVertical: 10,
                alignItems: "center",
                flex: 1,
              }}
            >
              <Text
                style={{
                  fontSize: 18,
                  fontWeight: "700",
                  color: giColor(report.glycemic.giCategory),
                }}
              >
                GI {report.glycemic.estimatedGI}
              </Text>
              <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                Glycemic
              </Text>
            </View>
          )}
          {product.novaGroup != null && (
            <View
              style={{
                backgroundColor: product.novaGroup >= 4 ? "#fee2e2" : "#dcfce7",
                borderRadius: 8,
                paddingHorizontal: 14,
                paddingVertical: 10,
                alignItems: "center",
                flex: 1,
              }}
            >
              <Text
                style={{
                  fontSize: 18,
                  fontWeight: "700",
                  color: product.novaGroup >= 4 ? "#ef4444" : "#22c55e",
                }}
              >
                NOVA {product.novaGroup}
              </Text>
              <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                Processing
              </Text>
            </View>
          )}
          {product.nutriScore &&
            !product.nutriScore.toLowerCase().includes("not") && (
              <View
                style={{
                  backgroundColor: "#eff6ff",
                  borderRadius: 8,
                  paddingHorizontal: 14,
                  paddingVertical: 10,
                  alignItems: "center",
                  flex: 1,
                }}
              >
                <Text
                  style={{ fontSize: 18, fontWeight: "700", color: "#3b82f6" }}
                >
                  {product.nutriScore.toUpperCase()}
                </Text>
                <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                  Nutri-Score
                </Text>
              </View>
            )}
        </View>

        {/* Personalized Gut Health Score */}
        {personalScore && (
          <View
            style={{
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: 12,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: "#334155",
                  flex: 1,
                }}
              >
                🎯 Your Gut Health Score
              </Text>
              <View
                style={{
                  backgroundColor:
                    personalScoreColor(personalScore.compositeScore) + "18",
                  borderRadius: 12,
                  paddingHorizontal: 10,
                  paddingVertical: 4,
                }}
              >
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "700",
                    color: personalScoreColor(personalScore.compositeScore),
                  }}
                >
                  {ratingEmoji(personalScore.rating)} {personalScore.rating}
                </Text>
              </View>
            </View>

            {/* Big Score */}
            <View style={{ alignItems: "center", marginBottom: 16 }}>
              <Text
                style={{
                  fontSize: 48,
                  fontWeight: "800",
                  color: personalScoreColor(personalScore.compositeScore),
                }}
              >
                {personalScore.compositeScore}
              </Text>
              <Text style={{ fontSize: 13, color: "#64748b" }}>out of 100</Text>
            </View>

            {/* Component Breakdown */}
            {personalScore.explanations.map((exp) => {
              const barColor =
                exp.rawScore >= 80
                  ? "#22c55e"
                  : exp.rawScore >= 60
                    ? "#3b82f6"
                    : exp.rawScore >= 40
                      ? "#f59e0b"
                      : "#ef4444";
              return (
                <View key={exp.component} style={{ marginBottom: 10 }}>
                  <View
                    style={{
                      flexDirection: "row",
                      justifyContent: "space-between",
                      marginBottom: 3,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color: "#334155",
                      }}
                    >
                      {exp.component} ({exp.weight}%)
                    </Text>
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "700",
                        color: barColor,
                      }}
                    >
                      {exp.rawScore}/100
                    </Text>
                  </View>
                  <View
                    style={{
                      height: 6,
                      backgroundColor: "#f1f5f9",
                      borderRadius: 3,
                    }}
                  >
                    <View
                      style={{
                        height: 6,
                        width: `${Math.min(exp.rawScore, 100)}%`,
                        backgroundColor: barColor,
                        borderRadius: 3,
                      }}
                    />
                  </View>
                  <Text
                    style={{ fontSize: 11, color: "#94a3b8", marginTop: 2 }}
                  >
                    {exp.explanation}
                  </Text>
                </View>
              );
            })}

            {/* Personal Trigger Penalty */}
            {personalScore.personalTriggerPenalty > 0 && (
              <View
                style={{
                  backgroundColor: "#fef2f2",
                  borderRadius: 8,
                  padding: 10,
                  marginTop: 4,
                  marginBottom: 8,
                }}
              >
                <Text
                  style={{ fontSize: 12, fontWeight: "600", color: "#dc2626" }}
                >
                  ⚡ Personal Trigger Penalty: -
                  {personalScore.personalTriggerPenalty} pts
                </Text>
                <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                  Based on your symptom history with similar foods.
                </Text>
              </View>
            )}

            {/* Personal Warnings */}
            {personalScore.personalWarnings.length > 0 && (
              <View style={{ marginTop: 4 }}>
                {personalScore.personalWarnings.map((warning, i) => (
                  <Text
                    key={i}
                    style={{ fontSize: 12, color: "#dc2626", marginBottom: 3 }}
                  >
                    {warning}
                  </Text>
                ))}
              </View>
            )}

            {/* Summary */}
            <Text
              style={{
                fontSize: 13,
                color: "#475569",
                marginTop: 8,
                lineHeight: 18,
              }}
            >
              {personalScore.summary}
            </Text>
          </View>
        )}

        {/* Gut Risk Assessment */}
        {report?.gutRisk && report.gutRisk.flagCount > 0 && (
          <View
            style={{
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: 8,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: "#334155",
                  flex: 1,
                }}
              >
                🫃 Gut Risk Assessment
              </Text>
              <View
                style={{
                  backgroundColor:
                    gutScoreColor(report.gutRisk.gutScore) + "18",
                  borderRadius: 12,
                  paddingHorizontal: 10,
                  paddingVertical: 4,
                }}
              >
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "700",
                    color: gutScoreColor(report.gutRisk.gutScore),
                  }}
                >
                  {report.gutRisk.gutRating}
                </Text>
              </View>
            </View>
            <Text
              style={{
                fontSize: 13,
                color: "#64748b",
                marginBottom: 12,
                lineHeight: 18,
              }}
            >
              {report.gutRisk.summary}
            </Text>

            {report.gutRisk.highRiskCount > 0 && (
              <View style={{ flexDirection: "row", gap: 8, marginBottom: 8 }}>
                <View
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
                      color: "#dc2626",
                    }}
                  >
                    🔴 {report.gutRisk.highRiskCount} High Risk
                  </Text>
                </View>
                {report.gutRisk.mediumRiskCount > 0 && (
                  <View
                    style={{
                      backgroundColor: "#fefce8",
                      borderRadius: 6,
                      paddingHorizontal: 8,
                      paddingVertical: 4,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 11,
                        fontWeight: "600",
                        color: "#ca8a04",
                      }}
                    >
                      🟡 {report.gutRisk.mediumRiskCount} Medium
                    </Text>
                  </View>
                )}
              </View>
            )}

            {report.gutRisk.flags.map((flag, i) => (
              <View
                key={`${flag.code}-${i}`}
                style={{
                  borderLeftWidth: 3,
                  borderLeftColor:
                    flag.riskLevel === "High"
                      ? "#ef4444"
                      : flag.riskLevel === "Medium"
                        ? "#f59e0b"
                        : "#94a3b8",
                  paddingLeft: 12,
                  paddingVertical: 8,
                  marginBottom: 8,
                }}
              >
                <View
                  style={{ flexDirection: "row", alignItems: "center", gap: 6 }}
                >
                  <Text
                    style={{
                      fontWeight: "600",
                      color: "#0f172a",
                      fontSize: 14,
                    }}
                  >
                    {flag.name}
                  </Text>
                  {flag.code ? (
                    <Text style={{ fontSize: 11, color: "#94a3b8" }}>
                      ({flag.code})
                    </Text>
                  ) : null}
                </View>
                <View style={{ flexDirection: "row", gap: 8, marginTop: 3 }}>
                  <Text
                    style={{
                      fontSize: 11,
                      fontWeight: "600",
                      color:
                        flag.riskLevel === "High"
                          ? "#dc2626"
                          : flag.riskLevel === "Medium"
                            ? "#ca8a04"
                            : "#64748b",
                    }}
                  >
                    {flag.riskLevel} Risk
                  </Text>
                  <Text style={{ fontSize: 11, color: "#94a3b8" }}>
                    {flag.category}
                  </Text>
                  <Text style={{ fontSize: 11, color: "#94a3b8" }}>
                    via {flag.source}
                  </Text>
                </View>
                <Text
                  style={{
                    fontSize: 12,
                    color: "#475569",
                    marginTop: 4,
                    lineHeight: 17,
                  }}
                >
                  {flag.explanation}
                </Text>
              </View>
            ))}
          </View>
        )}

        {/* FODMAP Assessment */}
        {report?.fodmap && report.fodmap.triggerCount > 0 && (
          <View
            style={{
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: 8,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: "#334155",
                  flex: 1,
                }}
              >
                🧪 FODMAP Assessment
              </Text>
              <View
                style={{
                  backgroundColor:
                    fodmapScoreColor(report.fodmap.fodmapScore) + "18",
                  borderRadius: 12,
                  paddingHorizontal: 10,
                  paddingVertical: 4,
                }}
              >
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "700",
                    color: fodmapScoreColor(report.fodmap.fodmapScore),
                  }}
                >
                  {report.fodmap.fodmapRating}
                </Text>
              </View>
            </View>
            <Text
              style={{
                fontSize: 13,
                color: "#64748b",
                marginBottom: 12,
                lineHeight: 18,
              }}
            >
              {report.fodmap.summary}
            </Text>

            {(report.fodmap.highCount > 0 ||
              report.fodmap.moderateCount > 0) && (
              <View style={{ flexDirection: "row", gap: 8, marginBottom: 8 }}>
                {report.fodmap.highCount > 0 && (
                  <View
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
                        color: "#dc2626",
                      }}
                    >
                      🔴 {report.fodmap.highCount} High
                    </Text>
                  </View>
                )}
                {report.fodmap.moderateCount > 0 && (
                  <View
                    style={{
                      backgroundColor: "#fefce8",
                      borderRadius: 6,
                      paddingHorizontal: 8,
                      paddingVertical: 4,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 11,
                        fontWeight: "600",
                        color: "#ca8a04",
                      }}
                    >
                      🟡 {report.fodmap.moderateCount} Moderate
                    </Text>
                  </View>
                )}
                {report.fodmap.lowCount > 0 && (
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
                        color: "#16a34a",
                      }}
                    >
                      🟢 {report.fodmap.lowCount} Low
                    </Text>
                  </View>
                )}
              </View>
            )}

            {report.fodmap.categories.length > 0 && (
              <View
                style={{
                  flexDirection: "row",
                  flexWrap: "wrap",
                  gap: 6,
                  marginBottom: 10,
                }}
              >
                {report.fodmap.categories.map((cat) => (
                  <View
                    key={cat}
                    style={{
                      backgroundColor: "#eff6ff",
                      borderRadius: 6,
                      paddingHorizontal: 8,
                      paddingVertical: 3,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 10,
                        fontWeight: "600",
                        color: "#3b82f6",
                      }}
                    >
                      {cat}
                    </Text>
                  </View>
                ))}
              </View>
            )}

            {report.fodmap.triggers.map((trigger, i) => (
              <View
                key={`fodmap-${i}`}
                style={{
                  borderLeftWidth: 3,
                  borderLeftColor:
                    trigger.severity === "High"
                      ? "#ef4444"
                      : trigger.severity === "Moderate"
                        ? "#f59e0b"
                        : "#94a3b8",
                  paddingLeft: 12,
                  paddingVertical: 8,
                  marginBottom: 8,
                }}
              >
                <Text
                  style={{ fontWeight: "600", color: "#0f172a", fontSize: 14 }}
                >
                  {trigger.name}
                </Text>
                <View style={{ flexDirection: "row", gap: 8, marginTop: 3 }}>
                  <Text
                    style={{
                      fontSize: 11,
                      fontWeight: "600",
                      color:
                        trigger.severity === "High"
                          ? "#dc2626"
                          : trigger.severity === "Moderate"
                            ? "#ca8a04"
                            : "#16a34a",
                    }}
                  >
                    {trigger.severity}
                  </Text>
                  <Text style={{ fontSize: 11, color: "#94a3b8" }}>
                    {trigger.category} · {trigger.subCategory}
                  </Text>
                </View>
                <Text
                  style={{
                    fontSize: 12,
                    color: "#475569",
                    marginTop: 4,
                    lineHeight: 17,
                  }}
                >
                  {trigger.explanation}
                </Text>
              </View>
            ))}
          </View>
        )}

        {/* Glycemic Assessment */}
        {report?.glycemic && report.glycemic.estimatedGI != null && (
          <View
            style={{
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: 8,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: "#334155",
                  flex: 1,
                }}
              >
                📊 Glycemic Assessment
              </Text>
              <View
                style={{
                  backgroundColor: giColor(report.glycemic.giCategory) + "18",
                  borderRadius: 12,
                  paddingHorizontal: 10,
                  paddingVertical: 4,
                }}
              >
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "700",
                    color: giColor(report.glycemic.giCategory),
                  }}
                >
                  GI {report.glycemic.estimatedGI} ·{" "}
                  {report.glycemic.giCategory}
                </Text>
              </View>
            </View>

            <Text
              style={{
                fontSize: 13,
                color: "#64748b",
                marginBottom: 12,
                lineHeight: 18,
              }}
            >
              {report.glycemic.gutImpactSummary}
            </Text>

            <View style={{ flexDirection: "row", gap: 8, marginBottom: 12 }}>
              <View
                style={{
                  backgroundColor: giColor(report.glycemic.giCategory) + "18",
                  borderRadius: 8,
                  paddingHorizontal: 12,
                  paddingVertical: 8,
                  alignItems: "center",
                  flex: 1,
                }}
              >
                <Text
                  style={{
                    fontSize: 16,
                    fontWeight: "700",
                    color: giColor(report.glycemic.giCategory),
                  }}
                >
                  {report.glycemic.estimatedGI}
                </Text>
                <Text style={{ fontSize: 10, color: "#64748b", marginTop: 2 }}>
                  GI
                </Text>
              </View>
              {report.glycemic.estimatedGL != null && (
                <View
                  style={{
                    backgroundColor: giColor(report.glycemic.glCategory) + "18",
                    borderRadius: 8,
                    paddingHorizontal: 12,
                    paddingVertical: 8,
                    alignItems: "center",
                    flex: 1,
                  }}
                >
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "700",
                      color: giColor(report.glycemic.glCategory),
                    }}
                  >
                    {report.glycemic.estimatedGL}
                  </Text>
                  <Text
                    style={{ fontSize: 10, color: "#64748b", marginTop: 2 }}
                  >
                    GL
                  </Text>
                </View>
              )}
            </View>

            {report.glycemic.matches.length > 0 && (
              <View style={{ marginBottom: 10 }}>
                <Text
                  style={{
                    fontSize: 12,
                    fontWeight: "600",
                    color: "#64748b",
                    marginBottom: 6,
                  }}
                >
                  Matched Foods ({report.glycemic.matchCount})
                </Text>
                {report.glycemic.matches.map((m, i) => (
                  <View
                    key={`gi-${i}`}
                    style={{
                      flexDirection: "row",
                      justifyContent: "space-between",
                      paddingVertical: 5,
                      borderBottomWidth: 1,
                      borderBottomColor: "#f1f5f9",
                    }}
                  >
                    <Text style={{ fontSize: 13, color: "#334155" }}>
                      {m.food}
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
                          fontSize: 12,
                          fontWeight: "600",
                          color: giColor(m.giCategory),
                        }}
                      >
                        GI {m.gi}
                      </Text>
                      <Text style={{ fontSize: 10, color: "#94a3b8" }}>
                        {m.source}
                      </Text>
                    </View>
                  </View>
                ))}
              </View>
            )}

            {report.glycemic.recommendations.length > 0 && (
              <View>
                <Text
                  style={{
                    fontSize: 12,
                    fontWeight: "600",
                    color: "#64748b",
                    marginBottom: 6,
                  }}
                >
                  Recommendations
                </Text>
                {report.glycemic.recommendations.map((rec, i) => (
                  <View
                    key={`gi-rec-${i}`}
                    style={{ flexDirection: "row", marginBottom: 4 }}
                  >
                    <Text
                      style={{ fontSize: 12, color: "#22c55e", marginRight: 6 }}
                    >
                      💡
                    </Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: "#475569",
                        lineHeight: 17,
                        flex: 1,
                      }}
                    >
                      {rec}
                    </Text>
                  </View>
                ))}
              </View>
            )}
          </View>
        )}

        {/* Gut-Friendly Substitutions */}
        {report?.substitutions && report.substitutions.suggestionCount > 0 && (
          <View
            style={{
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: 8,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: "#334155",
                  flex: 1,
                }}
              >
                🔄 Gut-Friendly Substitutions
              </Text>
              <View
                style={{
                  backgroundColor: "#eff6ff",
                  borderRadius: 12,
                  paddingHorizontal: 10,
                  paddingVertical: 4,
                }}
              >
                <Text
                  style={{ fontSize: 13, fontWeight: "700", color: "#3b82f6" }}
                >
                  {report.substitutions.suggestionCount}
                </Text>
              </View>
            </View>
            <Text
              style={{
                fontSize: 13,
                color: "#64748b",
                marginBottom: 12,
                lineHeight: 18,
              }}
            >
              {report.substitutions.summary}
            </Text>

            {report.substitutions.suggestions.map((sub, i) => (
              <View
                key={`sub-${i}`}
                style={{
                  borderLeftWidth: 3,
                  borderLeftColor: confidenceColor(sub.confidence),
                  paddingLeft: 12,
                  paddingVertical: 8,
                  marginBottom: 8,
                  backgroundColor: "#f8fafc",
                  borderRadius: 8,
                  borderTopLeftRadius: 0,
                  borderBottomLeftRadius: 0,
                }}
              >
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    gap: 8,
                    flexWrap: "wrap",
                  }}
                >
                  <Text
                    style={{
                      fontWeight: "600",
                      color: "#ef4444",
                      fontSize: 13,
                    }}
                  >
                    {sub.original}
                  </Text>
                  <Text style={{ fontSize: 13, color: "#94a3b8" }}>→</Text>
                  <Text
                    style={{
                      fontWeight: "600",
                      color: "#22c55e",
                      fontSize: 13,
                    }}
                  >
                    {sub.substitute}
                  </Text>
                </View>
                <View style={{ flexDirection: "row", gap: 6, marginTop: 4 }}>
                  <View
                    style={{
                      backgroundColor: "#eff6ff",
                      borderRadius: 4,
                      paddingHorizontal: 6,
                      paddingVertical: 2,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 10,
                        fontWeight: "600",
                        color: "#3b82f6",
                      }}
                    >
                      {sub.category}
                    </Text>
                  </View>
                  <View
                    style={{
                      backgroundColor: confidenceColor(sub.confidence) + "18",
                      borderRadius: 4,
                      paddingHorizontal: 6,
                      paddingVertical: 2,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 10,
                        fontWeight: "600",
                        color: confidenceColor(sub.confidence),
                      }}
                    >
                      {sub.confidence}
                    </Text>
                  </View>
                </View>
                <Text
                  style={{
                    fontSize: 12,
                    color: "#475569",
                    marginTop: 4,
                    lineHeight: 17,
                  }}
                >
                  {sub.reason}
                </Text>
                <Text style={{ fontSize: 11, color: "#16a34a", marginTop: 3 }}>
                  ✅ {sub.gutBenefit}
                </Text>
              </View>
            ))}
          </View>
        )}

        {/* Serving Size */}
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
              fontSize: 16,
              fontWeight: "600",
              color: "#334155",
              marginBottom: 8,
            }}
          >
            Serving Size
          </Text>
          <View
            style={{
              flexDirection: "row",
              flexWrap: "wrap",
              gap: 8,
              marginBottom: 8,
            }}
          >
            {(() => {
              const presets: { label: string; grams: number }[] = [];
              if (product.servingQuantity && product.servingSize) {
                presets.push({
                  label: `1 serving (${product.servingSize})`,
                  grams: Math.round(product.servingQuantity),
                });
              }
              [50, 100, 150, 200, 250].forEach((g) =>
                presets.push({ label: `${g}g`, grams: g }),
              );
              return presets.map((p) => (
                <TouchableOpacity
                  key={p.label}
                  onPress={() => {
                    setServingSize(p.grams);
                    setCustomServing("");
                  }}
                  style={{
                    paddingHorizontal: 12,
                    paddingVertical: 8,
                    borderRadius: 8,
                    backgroundColor:
                      servingSize === p.grams && !customServing
                        ? "#22c55e"
                        : "#f1f5f9",
                  }}
                >
                  <Text
                    style={{
                      fontSize: 12,
                      fontWeight: "600",
                      color:
                        servingSize === p.grams && !customServing
                          ? "#fff"
                          : "#64748b",
                    }}
                  >
                    {p.label}
                  </Text>
                </TouchableOpacity>
              ));
            })()}
          </View>
          <View style={{ flexDirection: "row", alignItems: "center", gap: 8 }}>
            <Text style={{ fontSize: 13, color: "#64748b" }}>Custom:</Text>
            <TextInput
              placeholder="grams"
              value={customServing}
              onChangeText={(v) => {
                setCustomServing(v);
                const n = Number(v);
                if (n > 0) setServingSize(n);
              }}
              keyboardType="numeric"
              style={{
                borderWidth: 1,
                borderColor: "#e2e8f0",
                borderRadius: 8,
                paddingHorizontal: 12,
                paddingVertical: 6,
                fontSize: 14,
                width: 80,
                textAlign: "center",
                color: "#0f172a",
              }}
            />
            <Text style={{ fontSize: 13, color: "#94a3b8" }}>g</Text>
          </View>
        </View>

        {/* Nutrition per {servingSize}g */}
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
              fontSize: 16,
              fontWeight: "600",
              color: "#334155",
              marginBottom: 12,
            }}
          >
            Nutrition per {servingSize}g
          </Text>
          <NutrientRow
            label="Calories"
            value={
              product.calories100g != null
                ? Math.round((product.calories100g * servingSize) / 100)
                : null
            }
            unit="kcal"
          />
          <NutrientRow
            label="Protein"
            value={
              product.protein100g != null
                ? Math.round(((product.protein100g * servingSize) / 100) * 10) /
                  10
                : null
            }
            unit="g"
          />
          <NutrientRow
            label="Carbohydrates"
            value={
              product.carbs100g != null
                ? Math.round(((product.carbs100g * servingSize) / 100) * 10) /
                  10
                : null
            }
            unit="g"
          />
          <NutrientRow
            label="Fat"
            value={
              product.fat100g != null
                ? Math.round(((product.fat100g * servingSize) / 100) * 10) / 10
                : null
            }
            unit="g"
          />
          <NutrientRow
            label="Fiber"
            value={
              product.fiber100g != null
                ? Math.round(((product.fiber100g * servingSize) / 100) * 10) /
                  10
                : null
            }
            unit="g"
          />
          <NutrientRow
            label="Sugar"
            value={
              product.sugar100g != null
                ? Math.round(((product.sugar100g * servingSize) / 100) * 10) /
                  10
                : null
            }
            unit="g"
          />
          <NutrientRow
            label="Sodium"
            value={
              product.sodium100g != null
                ? Math.round(((product.sodium100g * servingSize) / 100) * 10) /
                  10
                : null
            }
            unit="g"
          />
        </View>

        {/* Ingredients */}
        {product.ingredients && (
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
                fontSize: 16,
                fontWeight: "600",
                color: "#334155",
                marginBottom: 8,
              }}
            >
              Ingredients
            </Text>
            <Text style={{ fontSize: 14, color: "#475569", lineHeight: 20 }}>
              {product.ingredients}
            </Text>
          </View>
        )}

        {/* Allergens */}
        {product.allergensTags && product.allergensTags.length > 0 && (
          <View
            style={{
              backgroundColor: "#fef2f2",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <Text
              style={{
                fontSize: 16,
                fontWeight: "600",
                color: "#dc2626",
                marginBottom: 8,
              }}
            >
              ⚠️ Allergens
            </Text>
            <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 6 }}>
              {product.allergensTags.map((tag) => (
                <View
                  key={tag}
                  style={{
                    backgroundColor: "#fee2e2",
                    borderRadius: 6,
                    paddingHorizontal: 10,
                    paddingVertical: 4,
                  }}
                >
                  <Text
                    style={{
                      fontSize: 13,
                      fontWeight: "600",
                      color: "#dc2626",
                    }}
                  >
                    {tag
                      .replace("en:", "")
                      .split(/[\s-]+/)
                      .map(
                        (w) =>
                          w.charAt(0).toUpperCase() + w.slice(1).toLowerCase(),
                      )
                      .join(" ")}
                  </Text>
                </View>
              ))}
            </View>
          </View>
        )}

        {/* Allergen Cross-Reference Warning */}
        {product.allergensTags &&
          product.allergensTags.length > 0 &&
          userProfile?.allergies &&
          userProfile.allergies.length > 0 &&
          (() => {
            const matchedAllergens = product.allergensTags.filter((tag) =>
              userProfile.allergies.some(
                (allergy) =>
                  tag.toLowerCase().includes(allergy.toLowerCase()) ||
                  allergy
                    .toLowerCase()
                    .includes(tag.replace("en:", "").toLowerCase()),
              ),
            );
            return matchedAllergens.length > 0 ? (
              <View
                style={{
                  backgroundColor: "#fef2f2",
                  borderRadius: 12,
                  padding: 16,
                  marginBottom: 12,
                  borderWidth: 2,
                  borderColor: "#dc2626",
                }}
              >
                <Text
                  style={{
                    fontSize: 16,
                    fontWeight: "700",
                    color: "#dc2626",
                    marginBottom: 8,
                  }}
                >
                  🚨 ALLERGEN MATCH
                </Text>
                <Text style={{ fontSize: 14, color: "#dc2626" }}>
                  This product contains allergens matching your profile:{" "}
                  {matchedAllergens.map((t) => t.replace("en:", "")).join(", ")}
                </Text>
              </View>
            ) : null;
          })()}

        {/* Additives from safety report */}
        {report && report.additives.length > 0 && (
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
                fontSize: 16,
                fontWeight: "600",
                color: "#334155",
                marginBottom: 12,
              }}
            >
              Additives ({report.additives.length})
            </Text>
            {report.additives.map((add) => (
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
                <Text style={{ fontWeight: "600", color: "#0f172a" }}>
                  {add.name} {add.eNumber ? `(${add.eNumber})` : ""}
                </Text>
                <Text style={{ fontSize: 12, color: "#64748b", marginTop: 2 }}>
                  CSPI: {add.cspiRating} · US: {add.usStatus} · EU:{" "}
                  {add.euStatus}
                </Text>
                {add.healthConcerns && (
                  <Text
                    style={{ fontSize: 12, color: "#ef4444", marginTop: 2 }}
                  >
                    ⚠ {add.healthConcerns}
                  </Text>
                )}
                {add.bannedInCountries.length > 0 && (
                  <Text
                    style={{ fontSize: 11, color: "#f97316", marginTop: 2 }}
                  >
                    Banned in: {add.bannedInCountries.join(", ")}
                  </Text>
                )}
                {add.description && (
                  <Text
                    style={{ fontSize: 12, color: "#475569", marginTop: 4 }}
                  >
                    {add.description}
                  </Text>
                )}
                {add.efsaAdiMgPerKgBw != null && (
                  <Text
                    style={{ fontSize: 11, color: "#94a3b8", marginTop: 2 }}
                  >
                    EFSA ADI: {add.efsaAdiMgPerKgBw} mg/kg bw
                  </Text>
                )}
              </View>
            ))}
          </View>
        )}

        {/* Add to Meal */}
        {showAddToMeal ? (
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
              Add {servingSize}g to meal:
            </Text>
            <Text style={{ fontSize: 12, color: "#64748b", marginBottom: 8 }}>
              {Math.round(((product.calories100g ?? 0) * servingSize) / 100)}{" "}
              cal ·{" "}
              {Math.round(((product.protein100g ?? 0) * servingSize) / 100)}g P
              · {Math.round(((product.carbs100g ?? 0) * servingSize) / 100)}g C
              · {Math.round(((product.fat100g ?? 0) * servingSize) / 100)}g F
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
            <View
              style={{
                flexDirection: "row",
                justifyContent: "flex-end",
                gap: 8,
              }}
            >
              <TouchableOpacity
                onPress={() => setShowAddToMeal(false)}
                style={{ paddingHorizontal: 12, paddingVertical: 8 }}
              >
                <Text style={{ color: "#64748b", fontWeight: "600" }}>
                  Cancel
                </Text>
              </TouchableOpacity>
              <TouchableOpacity
                onPress={() => addToMealMutation.mutate()}
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
            onPress={() => setShowAddToMeal(true)}
            style={{
              backgroundColor: "#22c55e",
              borderRadius: 12,
              padding: 14,
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "center",
              marginBottom: 12,
            }}
          >
            <Ionicons name="add-circle-outline" size={20} color="#fff" />
            <Text
              style={{
                color: "#fff",
                fontWeight: "700",
                marginLeft: 8,
                fontSize: 15,
              }}
            >
              Add to Meal
            </Text>
          </TouchableOpacity>
        )}

        {loadingReport && (
          <ActivityIndicator
            size="small"
            color="#22c55e"
            style={{ marginTop: 8 }}
          />
        )}
      </View>
    </ScrollView>
  );
}

function NutrientRow({
  label,
  value,
  unit,
}: {
  label: string;
  value: number | null;
  unit: string;
}) {
  if (value == null) return null;
  return (
    <View
      style={{
        flexDirection: "row",
        justifyContent: "space-between",
        paddingVertical: 6,
        borderBottomWidth: 1,
        borderBottomColor: "#f1f5f9",
      }}
    >
      <Text style={{ color: "#475569", fontSize: 14 }}>{label}</Text>
      <Text style={{ fontWeight: "600", color: "#0f172a", fontSize: 14 }}>
        {value} {unit}
      </Text>
    </View>
  );
}
