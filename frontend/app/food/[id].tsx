import { useState, useCallback, useEffect } from "react";
import {
  View,
  Text,
  ScrollView,
  ActivityIndicator,
  TouchableOpacity,
  RefreshControl,
  TextInput,
  BackHandler,
  Platform,
  KeyboardAvoidingView,
} from "react-native";
import { Image } from "expo-image";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { foodApi, mealApi, insightApi, userApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { ErrorState } from "../../components/ErrorState";
import { MealTypePicker } from "../../components/MealTypePicker";
import { InfoTooltip } from "../../components/InfoTooltip";
import { CollapsibleCard } from "../../components/CollapsibleCard";
import { ratingColor, riskLevelColor, riskLevelEmoji, cspiEmoji } from "../../src/utils/colors";
import {
  scaleNutrition,
  nutritionSummaryText,
  buildServingPresets,
} from "../../src/utils/nutrition";
import { toast } from "../../src/stores/toast";
import type { PersonalizedScore } from "../../src/types";
import { maybeRequestReview } from "../../src/utils/review";
import {
  maybeWriteMealToPlatform,
  mealWriteFromResponse,
} from "../../src/services/health";
import { SafeScreen } from "../../components/SafeScreen";
import { Linking } from "react-native";
import { SourceChip } from "../../components/SourceChip";
import { useShareCard } from "../../components/share/ShareCardPortal";
import { useThemeColors } from "../../src/stores/theme";
import type { ThemeColors } from "../../src/utils/theme";
import {
  FoodDetailSkeleton,
  SearchResultSkeleton,
} from "../../components/SkeletonLoader";
import { useFavorites } from "../../src/hooks/useFavorites";
import * as haptics from "../../src/utils/haptics";
import { getDeviceTimezoneId } from "../../src/utils/timezone";

const gutScoreColor = (score: number, c: ThemeColors) => {
  if (score >= 80) return c.primaryLight;
  if (score >= 60) return c.warning;
  if (score >= 40) return c.sugar;
  return c.danger;
};

const fodmapStatusColor = (status: string, c: ThemeColors) => {
  if (status === "PotentialTriggersDetected") return c.warning;
  if (status === "NoKnownTriggersDetected") return c.primaryLight;
  return c.textMuted; // InsufficientInformation — never render this as a "safe" green result
};

const fodmapStatusLabel = (status: string) => {
  if (status === "PotentialTriggersDetected") return "Triggers Found";
  if (status === "NoKnownTriggersDetected") return "None Known";
  return "Not Enough Info";
};

const giColor = (category: string, c: ThemeColors) => {
  if (category === "Low") return c.primaryLight;
  if (category === "Medium") return c.warning;
  if (category === "High") return c.danger;
  return c.textMuted;
};

const confidenceColor = (confidence: string, c: ThemeColors) => {
  if (confidence === "High") return c.primaryLight;
  if (confidence === "Medium") return c.warning;
  return c.textMuted;
};

const personalScoreColor = (score: number, c: ThemeColors) => {
  if (score >= 80) return c.primaryLight;
  if (score >= 60) return c.protein;
  if (score >= 40) return c.warning;
  if (score >= 20) return c.sugar;
  return c.danger;
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
  const colors = useThemeColors();
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const queryClient = useQueryClient();
  const timezoneId = getDeviceTimezoneId();
  const { isFavorite, toggleFavorite } = useFavorites();
  const shareCard = useShareCard();
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
    queryKey: ["personalized-score", id, timezoneId],
    queryFn: () => foodApi.personalizedScore(id!).then((r) => r.data),
    enabled: !!id,
  });

  const { data: userProfile } = useQuery({
    queryKey: ["user-profile"],
    queryFn: () => userApi.getProfile().then((r) => r.data),
  });

  const { data: alerts } = useQuery({
    queryKey: ["alerts"],
    queryFn: () => userApi.getAlerts().then((r) => r.data),
  });

  const alertIds = new Set((alerts ?? []).map((a) => a.additiveId));

  const addAlertMutation = useMutation({
    mutationFn: (additive: { id: number; name: string; cspiRating?: string }) =>
      userApi.addAlert(additive.id),
    onMutate: async (additive) => {
      await queryClient.cancelQueries({ queryKey: ["alerts"] });
      const previousAlerts = queryClient.getQueryData<any[]>(["alerts"]);
      queryClient.setQueryData<any[]>(["alerts"], (old) => [
        ...(old ?? []),
        {
          additiveId: additive.id,
          name: additive.name,
          cspiRating: additive.cspiRating ?? "Caution",
          alertEnabled: true,
        },
      ]);
      return { previousAlerts };
    },
    onError: (_err, _vars, context) => {
      if (context?.previousAlerts) {
        queryClient.setQueryData(["alerts"], context.previousAlerts);
      }
      toast.error("Failed to add alert");
    },
    onSuccess: (_data, additive) => {
      toast.success(`Watching ${additive.name}`);
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["alerts"] });
    },
  });

  const removeAlertMutation = useMutation({
    mutationFn: (additive: { id: number; name: string }) =>
      userApi.removeAlert(additive.id),
    onMutate: async (additive) => {
      await queryClient.cancelQueries({ queryKey: ["alerts"] });
      const previousAlerts = queryClient.getQueryData<any[]>(["alerts"]);
      queryClient.setQueryData<any[]>(["alerts"], (old) =>
        (old ?? []).filter((a) => a.additiveId !== additive.id),
      );
      return { previousAlerts };
    },
    onError: (_err, _vars, context) => {
      if (context?.previousAlerts) {
        queryClient.setQueryData(["alerts"], context.previousAlerts);
      }
      toast.error("Failed to remove alert");
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["alerts"] });
    },
  });

  const handleToggleAlert = (add: { id: number; name: string; cspiRating?: string }) => {
    haptics.selection();
    if (alertIds.has(add.id)) {
      removeAlertMutation.mutate({ id: add.id, name: add.name });
    } else {
      addAlertMutation.mutate({ id: add.id, name: add.name, cspiRating: add.cspiRating });
    }
  };

  const handleShareReport = () => {
    if (!product || loadingReport) return;

    const flagged = (report?.additives ?? []).filter(
      (a) =>
        ["Caution", "Avoid", "Cut Back"].includes(a.cspiRating) ||
        a.healthConcerns ||
        a.bannedInCountries.length > 0,
    );
    const additiveSource =
      flagged.length > 0 ? flagged : (report?.additives ?? []);

    shareCard({
      template: "safetyReport",
      data: {
        name: product.name,
        brand: product.brand,
        score:
          personalScore?.compositeScore != null
            ? personalScore.compositeScore
            : (report?.safetyScore ?? null),
        scoreLabel:
          personalScore?.compositeScore != null
            ? "Your Gut Score"
            : "Safety Score",
        gutRating: report?.gutRisk?.gutRating ?? null,
        fodmapStatus: report?.fodmap
          ? fodmapStatusLabel(report.fodmap.status)
          : null,
        giText: report?.glycemic
          ? report.glycemic.estimatedGI != null
            ? `GI ${report.glycemic.estimatedGI} (${report.glycemic.giCategory})`
            : report.glycemic.giCategory
          : null,
        flaggedAdditives: additiveSource.map(
          (a) => `${a.name} (${a.cspiRating})`,
        ),
      },
    });
  };

  const conditions = userProfile?.gutConditions ?? [];
  const hasConditions = conditions.length > 0;
  const fodmapRelevant = conditions.some((c) =>
    ["IBS", "FODMAP Sensitive", "SIBO"].includes(c),
  );
  const gutRiskRelevant = conditions.some((c) =>
    ["Crohn's Disease", "Ulcerative Colitis", "IBS"].includes(c),
  );
  const glycemicRelevant = conditions.some((c) => c === "GERD");

  const { data: triggerFoods } = useQuery({
    queryKey: ["trigger-foods", timezoneId],
    queryFn: () => insightApi.triggerFoods(90).then((r) => r.data),
  });

  const foodTrigger = product
    ? triggerFoods?.find(
        (t) => t.food.toLowerCase() === product.name.toLowerCase(),
      )
    : undefined;

  const dietPrefs = userProfile?.dietaryPreferences ?? [];
  const dietWarnings: string[] = [];
  if (product) {
    const name = product.name.toLowerCase();
    const ingredients = (product.ingredients ?? "").toLowerCase();
    const allergens = (product.allergensTags ?? []).map((a) => a.toLowerCase());
    if (dietPrefs.includes("Vegan") || dietPrefs.includes("Vegetarian")) {
      const animal = [
        "meat",
        "chicken",
        "beef",
        "pork",
        "fish",
        "salmon",
        "tuna",
        "shrimp",
        "bacon",
        "turkey",
      ];
      if (animal.some((a) => name.includes(a) || ingredients.includes(a)))
        dietWarnings.push(
          dietPrefs.includes("Vegan")
            ? "May not be vegan"
            : "May not be vegetarian",
        );
      if (
        dietPrefs.includes("Vegan") &&
        (allergens.some((a) => a.includes("milk") || a.includes("egg")) ||
          [
            "milk",
            "egg",
            "cheese",
            "butter",
            "cream",
            "honey",
            "whey",
            "casein",
          ].some((d) => ingredients.includes(d)))
      )
        dietWarnings.push("Contains animal-derived ingredients");
    }
    if (
      dietPrefs.includes("Gluten-Free") &&
      (allergens.some((a) => a.includes("wheat") || a.includes("gluten")) ||
        ["wheat", "barley", "rye", "gluten"].some((g) =>
          ingredients.includes(g),
        ))
    )
      dietWarnings.push("May contain gluten");
    if (
      dietPrefs.includes("Keto") &&
      product.carbs100g != null &&
      product.carbs100g > 15
    )
      dietWarnings.push(
        `High carbs for keto (${Math.round(product.carbs100g)}g per serving)`,
      );
    if (
      dietPrefs.includes("Low-FODMAP") &&
      report?.fodmap?.status === "PotentialTriggersDetected"
    )
      dietWarnings.push("Not Low-FODMAP friendly");
  }

  const hasExcessFructoseOrPolyolTrigger =
    (report?.fodmap?.categories.some((cat) =>
      /Monosaccharide|Fructose|Polyol/i.test(cat),
    ) ?? false) ||
    (report?.fodmap?.triggers.some(
      (t) =>
        /Monosaccharide|Fructose|Polyol/i.test(t.category) ||
        /Monosaccharide|Fructose|Polyol/i.test(t.subCategory),
    ) ?? false);
  const hasLowOrMedGi =
    report?.glycemic?.giCategory === "Low" ||
    report?.glycemic?.giCategory === "Medium";
  const showGiFodmapNote =
    hasExcessFructoseOrPolyolTrigger && hasLowOrMedGi;

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await refetch();
    setRefreshing(false);
  }, [refetch]);

  const addToMealMutation = useMutation({
    mutationFn: () => {
      const scaled = scaleNutrition(product!, servingSize);
      return mealApi.create({
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
            ...scaled,
          },
        ],
      });
    },
    onSuccess: (res) => {
      queryClient.invalidateQueries({ queryKey: ["meals"] });
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      queryClient.invalidateQueries({ queryKey: ["recent-foods"] });
      queryClient.invalidateQueries({ queryKey: ["streak"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-foods-dashboard"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-foods"] });
      queryClient.invalidateQueries({ queryKey: ["food-diary-analysis"] });
      queryClient.invalidateQueries({ queryKey: ["nutrition-trends"] });
      setShowAddToMeal(false);
      toast.success("Added to meal!");
      haptics.success();
      maybeRequestReview();
      if (res?.data) {
        maybeWriteMealToPlatform(mealWriteFromResponse(res.data));
      }
    },
    onError: () => {
      toast.error("Failed to add to meal");
      haptics.error();
    },
  });

  useEffect(() => {
    if (Platform.OS === "android") {
      const handler = BackHandler.addEventListener("hardwareBackPress", () => {
        if (showAddToMeal) {
          setShowAddToMeal(false);
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
  }, [showAddToMeal, router]);

  if (isLoading) {
    return (
      <View
        style={{
          flex: 1,
          backgroundColor: colors.bg,
        }}
      >
        <FoodDetailSkeleton />
      </View>
    );
  }

  if (isError || !product) {
    return (
      <View style={{ flex: 1, backgroundColor: colors.bg }}>
        <ErrorState message="Failed to load product" onRetry={refetch} />
      </View>
    );
  }

  return (
    <SafeScreen edges={["bottom"]}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
        keyboardVerticalOffset={Platform.OS === "ios" ? 0 : 0}
      >
        <ScrollView
          style={{ flex: 1, backgroundColor: colors.bg }}
          keyboardShouldPersistTaps="handled"
          refreshControl={
            <RefreshControl
              refreshing={refreshing}
              onRefresh={onRefresh}
              tintColor={colors.primaryLight}
            />
          }
        >
          <View style={{ padding: 20 }}>
            <View
              style={{
                backgroundColor: colors.card,
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
                    backgroundColor: colors.card,
                  }}
                  contentFit="contain"
                  transition={300}
                  placeholder={{ blurhash: "L6PZfSi_.AyE_3t7t7R**0o#DgR4" }}
                  cachePolicy="memory-disk"
                />
              )}
              <View
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <Text
                  style={{
                    fontSize: 22,
                    fontWeight: "700",
                    color: colors.text,
                    flex: 1,
                  }}
                >
                  {product.name}
                </Text>
                <View style={{ flexDirection: "row", alignItems: "center", gap: 4 }}>
                  <TouchableOpacity
                    onPress={handleShareReport}
                    disabled={loadingReport}
                    style={{
                      padding: 6,
                      opacity: loadingReport ? 0.4 : 1,
                    }}
                    hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}
                    accessibilityRole="button"
                    accessibilityLabel="Share safety report"
                    accessibilityState={{ disabled: loadingReport }}
                  >
                    <Ionicons
                      name="share-outline"
                      size={22}
                      color={colors.textSecondary}
                    />
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={() => {
                      haptics.selection();
                      toggleFavorite(product.id);
                    }}
                    style={{ padding: 6, marginLeft: 4 }}
                    hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}
                    accessibilityRole="button"
                    accessibilityLabel={
                      isFavorite(product.id)
                        ? "Remove from favorites"
                        : "Add to favorites"
                    }
                    accessibilityState={{ selected: isFavorite(product.id) }}
                  >
                    <Ionicons
                      name={isFavorite(product.id) ? "heart" : "heart-outline"}
                      size={24}
                      color={
                        isFavorite(product.id) ? colors.danger : colors.textMuted
                      }
                    />
                  </TouchableOpacity>
                </View>
              </View>
              {product.brand && (
                <Text
                  style={{
                    fontSize: 15,
                    color: colors.textSecondary,
                    marginTop: 4,
                  }}
                >
                  {product.brand}
                </Text>
              )}
              <SourceChip
                source={product.dataSource}
                url={product.sourceUrl}
                style={{ marginTop: 6 }}
              />
              {(product.sourceVersion || product.retrievedAt) && (
                <Text style={{ fontSize: 11, color: colors.textMuted, marginTop: 4 }}>
                  {product.sourceVersion ? `Version: ${product.sourceVersion}` : ""}
                  {product.retrievedAt
                    ? ` · Retrieved ${new Date(product.retrievedAt).toLocaleDateString()}`
                    : ""}
                </Text>
              )}
              {product.barcode && (
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textMuted,
                    marginTop: 4,
                  }}
                >
                  Barcode: {product.barcode}
                </Text>
              )}
            </View>

            {/* Hero Score Section */}
            {personalScore && (
              <View
                style={{
                  backgroundColor: colors.card,
                  borderRadius: 16,
                  padding: 20,
                  marginBottom: 12,
                  alignItems: "center",
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
                      fontSize: 15,
                      fontWeight: "600",
                      color: colors.text,
                    }}
                  >
                    🎯 Your Gut Health Score
                  </Text>
                  <InfoTooltip
                    title="Gut Health Score"
                    body="A personalized 0–100 composite score based on FODMAP risk, additive safety, processing level (NOVA), fiber content, allergen match, and sugar alcohols — weighted for your profile."
                  />
                </View>
                <Text
                  accessibilityRole="header"
                  accessibilityLabel={`Your Gut Health Score: ${personalScore.compositeScore} out of 100, rated ${personalScore.rating}`}
                  style={{
                    fontSize: 56,
                    fontWeight: "800",
                    color: personalScoreColor(
                      personalScore.compositeScore,
                      colors,
                    ),
                  }}
                >
                  {personalScore.compositeScore}
                </Text>
                <View
                  style={{
                    backgroundColor:
                      personalScoreColor(personalScore.compositeScore, colors) +
                      "18",
                    borderRadius: 16,
                    paddingHorizontal: 14,
                    paddingVertical: 4,
                    marginTop: 4,
                  }}
                >
                  <Text
                    style={{
                      fontSize: 14,
                      fontWeight: "700",
                      color: personalScoreColor(
                        personalScore.compositeScore,
                        colors,
                      ),
                    }}
                  >
                    {ratingEmoji(personalScore.rating)} {personalScore.rating}
                  </Text>
                </View>
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textSecondary,
                    marginTop: 10,
                    textAlign: "center",
                    lineHeight: 18,
                    paddingHorizontal: 8,
                  }}
                >
                  {personalScore.summary}
                </Text>
              </View>
            )}

            {/* Compact Score Badges — only show the most useful secondary scores */}
            <View style={{ flexDirection: "row", gap: 8, marginBottom: 12 }}>
              {report?.fodmap && (
                <View
                  style={{
                    backgroundColor:
                      fodmapStatusColor(report.fodmap.status, colors) + "18",
                    borderRadius: 10,
                    paddingHorizontal: 14,
                    paddingVertical: 10,
                    alignItems: "center",
                    flex: 1,
                  }}
                >
                  <Text
                    style={{
                      fontSize: 14,
                      fontWeight: "700",
                      color: fodmapStatusColor(report.fodmap.status, colors),
                    }}
                  >
                    {fodmapStatusLabel(report.fodmap.status)}
                  </Text>
                  <Text
                    style={{
                      fontSize: 11,
                      color: colors.textSecondary,
                      marginTop: 2,
                    }}
                  >
                    FODMAP
                  </Text>
                </View>
              )}
              {report?.glycemic?.estimatedGI != null && (
                <View
                  style={{
                    backgroundColor:
                      giColor(report.glycemic.giCategory, colors) + "18",
                    borderRadius: 10,
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
                      color: giColor(report.glycemic.giCategory, colors),
                    }}
                  >
                    GI {report.glycemic.estimatedGI}
                  </Text>
                  <Text
                    style={{
                      fontSize: 11,
                      color: colors.textSecondary,
                      marginTop: 2,
                    }}
                  >
                    Glycemic
                  </Text>
                </View>
              )}
              {product.safetyRating && (
                <View
                  style={{
                    backgroundColor: ratingColor(product.safetyRating) + "18",
                    borderRadius: 10,
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
                  <Text
                    style={{
                      fontSize: 11,
                      color: colors.textSecondary,
                      marginTop: 2,
                    }}
                  >
                    Safety
                  </Text>
                </View>
              )}
            </View>

            {/* Secondary labels row: NOVA + Nutri-Score if available */}
            {(product.novaGroup != null ||
              (product.nutriScore &&
                !product.nutriScore.toLowerCase().includes("not"))) && (
              <View style={{ flexDirection: "row", gap: 8, marginBottom: 12 }}>
                {product.novaGroup != null && (
                  <View
                    style={{
                      backgroundColor:
                        product.novaGroup >= 4
                          ? colors.dangerBg
                          : colors.primaryBg,
                      borderRadius: 10,
                      paddingHorizontal: 14,
                      paddingVertical: 8,
                      alignItems: "center",
                      flex: 1,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 14,
                        fontWeight: "700",
                        color:
                          product.novaGroup >= 4
                            ? colors.danger
                            : colors.primaryLight,
                      }}
                    >
                      NOVA {product.novaGroup}
                    </Text>
                    <Text
                      style={{
                        fontSize: 10,
                        color: colors.textSecondary,
                        marginTop: 1,
                      }}
                    >
                      Processing
                    </Text>
                  </View>
                )}
                {product.nutriScore &&
                  !product.nutriScore.toLowerCase().includes("not") && (
                    <View
                      style={{
                        backgroundColor: colors.secondaryBg,
                        borderRadius: 10,
                        paddingHorizontal: 14,
                        paddingVertical: 8,
                        alignItems: "center",
                        flex: 1,
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 14,
                          fontWeight: "700",
                          color: colors.protein,
                        }}
                      >
                        {product.nutriScore.toUpperCase()}
                      </Text>
                      <Text
                        style={{
                          fontSize: 10,
                          color: colors.textSecondary,
                          marginTop: 1,
                        }}
                      >
                        Nutri-Score
                      </Text>
                    </View>
                  )}
              </View>
            )}

            {/* Score Breakdown — collapsible */}
            {personalScore && personalScore.explanations.length > 0 && (
              <CollapsibleCard
                title="Score Breakdown"
                emoji="📊"
                badge={`${personalScore.compositeScore}/100`}
                badgeColor={personalScoreColor(
                  personalScore.compositeScore,
                  colors,
                )}
                defaultOpen={hasConditions}
              >
                {personalScore.explanations.map((exp) => {
                  const barColor =
                    exp.rawScore >= 80
                      ? colors.primaryLight
                      : exp.rawScore >= 60
                        ? colors.protein
                        : exp.rawScore >= 40
                          ? colors.warning
                          : colors.danger;
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
                            color: colors.text,
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
                          backgroundColor: colors.borderLight,
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
                        style={{
                          fontSize: 11,
                          color: colors.textMuted,
                          marginTop: 2,
                        }}
                      >
                        {exp.explanation}
                      </Text>
                    </View>
                  );
                })}

                {personalScore.personalTriggerPenalty > 0 && (
                  <View
                    style={{
                      backgroundColor: colors.dangerBg,
                      borderRadius: 8,
                      padding: 10,
                      marginTop: 4,
                      marginBottom: 8,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 12,
                        fontWeight: "600",
                        color: colors.danger,
                      }}
                    >
                      ⚡ Personal Trigger Penalty: -
                      {personalScore.personalTriggerPenalty} pts
                    </Text>
                    <Text
                      style={{
                        fontSize: 11,
                        color: colors.textSecondary,
                        marginTop: 2,
                      }}
                    >
                      Based on your symptom history with similar foods.
                    </Text>
                  </View>
                )}

                {personalScore.personalWarnings.length > 0 && (
                  <View style={{ marginTop: 4 }}>
                    {personalScore.personalWarnings.map((warning, i) => (
                      <Text
                        key={i}
                        style={{
                          fontSize: 12,
                          color: colors.danger,
                          marginBottom: 3,
                        }}
                      >
                        {warning}
                      </Text>
                    ))}
                  </View>
                )}
                <Text
                  style={{
                    fontSize: 10,
                    color: colors.textMuted,
                    marginTop: 10,
                    fontStyle: "italic",
                  }}
                >
                  Methodology: FODMAP (Monash University), NOVA (Monteiro et
                  al.), GI (Sydney University)
                </Text>
              </CollapsibleCard>
            )}

            {/* Gut Risk Assessment — collapsible */}
            {report?.gutRisk && report.gutRisk.flagCount > 0 && (
              <CollapsibleCard
                title="Gut Risk Assessment"
                emoji="🫃"
                badge={report.gutRisk.gutRating}
                badgeColor={gutScoreColor(report.gutRisk.gutScore, colors)}
                defaultOpen={gutRiskRelevant}
              >
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textSecondary,
                    marginBottom: 12,
                    lineHeight: 18,
                  }}
                >
                  {report.gutRisk.summary}
                </Text>

                {report.gutRisk.highRiskCount > 0 && (
                  <View
                    style={{ flexDirection: "row", gap: 8, marginBottom: 8 }}
                  >
                    <View
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
                        🔴 {report.gutRisk.highRiskCount} High Risk
                      </Text>
                    </View>
                    {report.gutRisk.mediumRiskCount > 0 && (
                      <View
                        style={{
                          backgroundColor: colors.warningBg,
                          borderRadius: 6,
                          paddingHorizontal: 8,
                          paddingVertical: 4,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color: colors.warning,
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
                      borderLeftColor: riskLevelColor(flag.riskLevel),
                      paddingLeft: 12,
                      paddingVertical: 8,
                      marginBottom: 8,
                    }}
                  >
                    <View
                      style={{
                        flexDirection: "row",
                        alignItems: "center",
                        gap: 6,
                      }}
                    >
                      <Text
                        style={{
                          fontWeight: "600",
                          color: colors.text,
                          fontSize: 14,
                        }}
                      >
                        {flag.name}
                      </Text>
                      {flag.code ? (
                        <Text style={{ fontSize: 11, color: colors.textMuted }}>
                          ({flag.code})
                        </Text>
                      ) : null}
                    </View>
                    <View
                      style={{ flexDirection: "row", gap: 8, marginTop: 3 }}
                    >
                      <Text
                        style={{
                          fontSize: 11,
                          fontWeight: "600",
                          color: riskLevelColor(flag.riskLevel),
                        }}
                      >
                        {riskLevelEmoji(flag.riskLevel)} {flag.riskLevel} Risk
                      </Text>
                      <Text style={{ fontSize: 11, color: colors.textMuted }}>
                        {flag.category}
                      </Text>
                      <Text style={{ fontSize: 11, color: colors.textMuted }}>
                        via {flag.source}
                      </Text>
                    </View>
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.textSecondary,
                        marginTop: 4,
                        lineHeight: 17,
                      }}
                    >
                      {flag.explanation}
                    </Text>
                  </View>
                ))}
                <Text
                  style={{
                    fontSize: 10,
                    color: colors.textMuted,
                    marginTop: 10,
                    fontStyle: "italic",
                  }}
                >
                  Additive safety data from CSPI & EFSA evaluations
                </Text>
              </CollapsibleCard>
            )}

            {/* FODMAP Assessment — collapsible */}
            {report?.fodmap &&
              (report.fodmap.triggerCount > 0 ||
                report.fodmap.status === "InsufficientInformation") && (
              <CollapsibleCard
                title="FODMAP Assessment"
                emoji="🧪"
                badge={fodmapStatusLabel(report.fodmap.status)}
                badgeColor={fodmapStatusColor(report.fodmap.status, colors)}
                defaultOpen={fodmapRelevant}
              >
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textSecondary,
                    marginBottom: 12,
                    lineHeight: 18,
                  }}
                >
                  {report.fodmap.summary}
                </Text>

                {report.fodmap.status === "InsufficientInformation" &&
                  report.fodmap.missingEvidence.length > 0 && (
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.textMuted,
                        marginBottom: 12,
                        fontStyle: "italic",
                      }}
                    >
                      Missing: {report.fodmap.missingEvidence.join(", ")}
                    </Text>
                  )}

                {(report.fodmap.highCount > 0 ||
                  report.fodmap.moderateCount > 0) && (
                  <View
                    style={{ flexDirection: "row", gap: 8, marginBottom: 8 }}
                  >
                    {report.fodmap.highCount > 0 && (
                      <View
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
                          🔴 {report.fodmap.highCount} High
                        </Text>
                      </View>
                    )}
                    {report.fodmap.moderateCount > 0 && (
                      <View
                        style={{
                          backgroundColor: colors.warningBg,
                          borderRadius: 6,
                          paddingHorizontal: 8,
                          paddingVertical: 4,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color: colors.warning,
                          }}
                        >
                          🟡 {report.fodmap.moderateCount} Moderate
                        </Text>
                      </View>
                    )}
                    {report.fodmap.lowCount > 0 && (
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
                            color: colors.primary,
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
                          backgroundColor: colors.secondaryBg,
                          borderRadius: 6,
                          paddingHorizontal: 8,
                          paddingVertical: 3,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 10,
                            fontWeight: "600",
                            color: colors.protein,
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
                          ? colors.danger
                          : trigger.severity === "Moderate"
                            ? colors.warning
                            : colors.textMuted,
                      paddingLeft: 12,
                      paddingVertical: 8,
                      marginBottom: 8,
                    }}
                  >
                    <Text
                      style={{
                        fontWeight: "600",
                        color: colors.text,
                        fontSize: 14,
                      }}
                    >
                      {trigger.name}
                    </Text>
                    <View
                      style={{ flexDirection: "row", gap: 8, marginTop: 3 }}
                    >
                      <Text
                        style={{
                          fontSize: 11,
                          fontWeight: "600",
                          color:
                            trigger.severity === "High"
                              ? colors.danger
                              : trigger.severity === "Moderate"
                                ? colors.warning
                                : colors.primary,
                        }}
                      >
                        {trigger.severity}
                      </Text>
                      <Text style={{ fontSize: 11, color: colors.textMuted }}>
                        {trigger.category} · {trigger.subCategory}
                      </Text>
                    </View>
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.textSecondary,
                        marginTop: 4,
                        lineHeight: 17,
                      }}
                    >
                      {trigger.explanation}
                    </Text>
                  </View>
                ))}
                <Text
                  style={{
                    fontSize: 10,
                    color: colors.textMuted,
                    marginTop: 10,
                    fontStyle: "italic",
                  }}
                >
                  Based on Monash University FODMAP research
                </Text>
              </CollapsibleCard>
            )}

            {/* Glycemic Assessment — collapsible */}
            {report?.glycemic && report.glycemic.estimatedGI != null && (
              <CollapsibleCard
                title="Glycemic Assessment"
                emoji="📊"
                badge={`GI ${report.glycemic.estimatedGI} · ${report.glycemic.giCategory}`}
                badgeColor={giColor(report.glycemic.giCategory, colors)}
                defaultOpen={glycemicRelevant}
              >
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textSecondary,
                    marginBottom: 12,
                    lineHeight: 18,
                  }}
                >
                  {report.glycemic.gutImpactSummary}
                </Text>

                <View
                  style={{ flexDirection: "row", gap: 8, marginBottom: 12 }}
                >
                  <View
                    style={{
                      backgroundColor:
                        giColor(report.glycemic.giCategory, colors) + "18",
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
                        color: giColor(report.glycemic.giCategory, colors),
                      }}
                    >
                      {report.glycemic.estimatedGI}
                    </Text>
                    <Text
                      style={{
                        fontSize: 10,
                        color: colors.textSecondary,
                        marginTop: 2,
                      }}
                    >
                      GI
                    </Text>
                  </View>
                  {report.glycemic.estimatedGL != null && (
                    <View
                      style={{
                        backgroundColor:
                          giColor(report.glycemic.glCategory, colors) + "18",
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
                          color: giColor(report.glycemic.glCategory, colors),
                        }}
                      >
                        {report.glycemic.estimatedGL}
                      </Text>
                      <Text
                        style={{
                          fontSize: 10,
                          color: colors.textSecondary,
                          marginTop: 2,
                        }}
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
                        color: colors.textSecondary,
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
                          borderBottomColor: colors.borderLight,
                        }}
                      >
                        <Text style={{ fontSize: 13, color: colors.text }}>
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
                              color: giColor(m.giCategory, colors),
                            }}
                          >
                            GI {m.gi}
                          </Text>
                          <Text
                            style={{ fontSize: 10, color: colors.textMuted }}
                          >
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
                        color: colors.textSecondary,
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
                          style={{
                            fontSize: 12,
                            color: colors.primaryLight,
                            marginRight: 6,
                          }}
                        >
                          💡
                        </Text>
                        <Text
                          style={{
                            fontSize: 12,
                            color: colors.textSecondary,
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
                {showGiFodmapNote && (
                  <Text
                    style={{
                      fontSize: 11,
                      color: colors.textMuted,
                      marginTop: 8,
                      lineHeight: 16,
                    }}
                  >
                    Note: a low glycemic index does not mean low FODMAP — fructose- and polyol-rich foods score low on GI but can still trigger gut symptoms.
                  </Text>
                )}
                <Text
                  style={{
                    fontSize: 10,
                    color: colors.textMuted,
                    marginTop: 10,
                    fontStyle: "italic",
                  }}
                >
                  GI values from Sydney University International GI Tables
                </Text>
              </CollapsibleCard>
            )}

            {/* Gut-Friendly Substitutions */}
            {report?.substitutions &&
              report.substitutions.suggestionCount > 0 && (
                <View
                  style={{
                    backgroundColor: colors.card,
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
                        color: colors.text,
                        flex: 1,
                      }}
                      accessibilityRole="header"
                    >
                      🔄 Gut-Friendly Substitutions
                    </Text>
                    <View
                      style={{
                        backgroundColor: colors.secondaryBg,
                        borderRadius: 12,
                        paddingHorizontal: 10,
                        paddingVertical: 4,
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 13,
                          fontWeight: "700",
                          color: colors.protein,
                        }}
                      >
                        {report.substitutions.suggestionCount}
                      </Text>
                    </View>
                  </View>
                  <Text
                    style={{
                      fontSize: 13,
                      color: colors.textSecondary,
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
                        borderLeftColor: confidenceColor(
                          sub.confidence,
                          colors,
                        ),
                        paddingLeft: 12,
                        paddingVertical: 8,
                        marginBottom: 8,
                        backgroundColor: colors.bg,
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
                            color: colors.danger,
                            fontSize: 13,
                          }}
                        >
                          {sub.original}
                        </Text>
                        <Text style={{ fontSize: 13, color: colors.textMuted }}>
                          →
                        </Text>
                        <Text
                          style={{
                            fontWeight: "600",
                            color: colors.primaryLight,
                            fontSize: 13,
                          }}
                        >
                          {sub.substitute}
                        </Text>
                      </View>
                      <View
                        style={{
                          flexDirection: "row",
                          gap: 6,
                          marginTop: 4,
                        }}
                      >
                        <View
                          style={{
                            backgroundColor: colors.secondaryBg,
                            borderRadius: 4,
                            paddingHorizontal: 6,
                            paddingVertical: 2,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 10,
                              fontWeight: "600",
                              color: colors.protein,
                            }}
                          >
                            {sub.category}
                          </Text>
                        </View>
                        <View
                          style={{
                            backgroundColor:
                              confidenceColor(sub.confidence, colors) + "18",
                            borderRadius: 4,
                            paddingHorizontal: 6,
                            paddingVertical: 2,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 10,
                              fontWeight: "600",
                              color: confidenceColor(sub.confidence, colors),
                            }}
                          >
                            {sub.confidence}
                          </Text>
                        </View>
                      </View>
                      <Text
                        style={{
                          fontSize: 12,
                          color: colors.textSecondary,
                          marginTop: 4,
                          lineHeight: 17,
                        }}
                      >
                        {sub.reason}
                      </Text>
                      <Text
                        style={{
                          fontSize: 11,
                          color: colors.primary,
                          marginTop: 3,
                        }}
                      >
                        ✅ {sub.gutBenefit}
                      </Text>
                    </View>
                  ))}
                </View>
              )}

            {/* Serving Size */}
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
                  fontSize: 16,
                  fontWeight: "600",
                  color: colors.text,
                  marginBottom: 8,
                }}
                accessibilityRole="header"
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
                {buildServingPresets(product).map((p) => (
                  <TouchableOpacity
                    key={p.label}
                    onPress={() => {
                      haptics.selection();
                      setServingSize(p.grams);
                      setCustomServing("");
                    }}
                    accessibilityRole="button"
                    accessibilityLabel={`${p.label} serving`}
                    accessibilityState={{
                      selected: servingSize === p.grams && !customServing,
                    }}
                    style={{
                      backgroundColor:
                        servingSize === p.grams && !customServing
                          ? colors.primaryLight
                          : colors.borderLight,
                      borderWidth:
                        servingSize === p.grams && !customServing ? 0 : 1,
                      borderColor: colors.border,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 12,
                        fontWeight: "600",
                        color:
                          servingSize === p.grams && !customServing
                            ? colors.textOnPrimary
                            : colors.textSecondary,
                      }}
                    >
                      {p.label}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>
              <View
                style={{ flexDirection: "row", alignItems: "center", gap: 8 }}
              >
                <Text style={{ fontSize: 13, color: colors.textSecondary }}>
                  Custom:
                </Text>
                <TextInput
                  placeholder="grams"
                  value={customServing}
                  onChangeText={(v) => {
                    const numeric = v.replace(/[^0-9]/g, "");
                    setCustomServing(numeric);
                    const n = Number(numeric);
                    if (n > 0 && n <= 5000) setServingSize(n);
                  }}
                  keyboardType="numeric"
                  autoCorrect={false}
                  maxLength={4}
                  style={{
                    borderWidth: 1,
                    borderColor: colors.border,
                    borderRadius: 8,
                    paddingHorizontal: 12,
                    paddingVertical: 6,
                    fontSize: 14,
                    width: 80,
                    textAlign: "center",
                    color: colors.text,
                  }}
                />
                <Text style={{ fontSize: 13, color: colors.textMuted }}>g</Text>
              </View>
            </View>

            {/* Nutrition per {servingSize}g */}
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
                  fontSize: 16,
                  fontWeight: "600",
                  color: colors.text,
                  marginBottom: 12,
                }}
                accessibilityRole="header"
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
                colors={colors}
              />
              <NutrientRow
                label="Protein"
                value={
                  product.protein100g != null
                    ? Math.round(
                        ((product.protein100g * servingSize) / 100) * 10,
                      ) / 10
                    : null
                }
                unit="g"
                colors={colors}
              />
              <NutrientRow
                label="Carbohydrates"
                value={
                  product.carbs100g != null
                    ? Math.round(
                        ((product.carbs100g * servingSize) / 100) * 10,
                      ) / 10
                    : null
                }
                unit="g"
                colors={colors}
              />
              <NutrientRow
                label="Fat"
                value={
                  product.fat100g != null
                    ? Math.round(((product.fat100g * servingSize) / 100) * 10) /
                      10
                    : null
                }
                unit="g"
                colors={colors}
              />
              <NutrientRow
                label="Fiber"
                value={
                  product.fiber100g != null
                    ? Math.round(
                        ((product.fiber100g * servingSize) / 100) * 10,
                      ) / 10
                    : null
                }
                unit="g"
                colors={colors}
              />
              <NutrientRow
                label="Sugar"
                value={
                  product.sugar100g != null
                    ? Math.round(
                        ((product.sugar100g * servingSize) / 100) * 10,
                      ) / 10
                    : null
                }
                unit="g"
                colors={colors}
              />
              <NutrientRow
                label="Sodium"
                value={
                  product.sodiumMg100g != null
                    ? Math.round(
                        ((product.sodiumMg100g * servingSize) / 100) * 10,
                      ) / 10
                    : null
                }
                unit="mg"
                colors={colors}
              />
            </View>

            {/* Ingredients */}
            {product.foodKind === "WholeFood" ? null : product.ingredients ? (
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
                    fontSize: 16,
                    fontWeight: "600",
                    color: colors.text,
                    marginBottom: 8,
                  }}
                  accessibilityRole="header"
                >
                  Ingredients
                </Text>
                <Text
                  style={{
                    fontSize: 14,
                    color: colors.textSecondary,
                    lineHeight: 20,
                  }}
                >
                  {product.ingredients}
                </Text>
              </View>
            ) : product.foodKind === "Branded" ? (
              <View
                style={{
                  backgroundColor: colors.warningBg,
                  borderRadius: 12,
                  padding: 16,
                  marginBottom: 12,
                  flexDirection: "row",
                  alignItems: "center",
                  gap: 10,
                }}
              >
                <Text style={{ fontSize: 20 }}>⚠️</Text>
                <View style={{ flex: 1 }}>
                  <Text
                    style={{
                      fontSize: 14,
                      fontWeight: "600",
                      color: colors.warning,
                    }}
                  >
                    Ingredients unavailable
                  </Text>
                  <Text
                    style={{
                      fontSize: 13,
                      color: colors.warning,
                      marginTop: 2,
                    }}
                  >
                    Scan the barcode or add ingredients to improve analysis
                  </Text>
                </View>
              </View>
            ) : null}

            {/* Allergens */}
            {product.allergensTags && product.allergensTags.length > 0 && (
              <View
                style={{
                  backgroundColor: colors.dangerBg,
                  borderRadius: 12,
                  padding: 16,
                  marginBottom: 12,
                }}
              >
                <Text
                  style={{
                    fontSize: 16,
                    fontWeight: "600",
                    color: colors.danger,
                    marginBottom: 8,
                  }}
                  accessibilityRole="header"
                >
                  ⚠️ Allergens
                </Text>
                <View
                  style={{ flexDirection: "row", flexWrap: "wrap", gap: 6 }}
                >
                  {product.allergensTags.map((tag) => (
                    <View
                      key={tag}
                      style={{
                        backgroundColor: colors.dangerBg,
                        borderRadius: 6,
                        paddingHorizontal: 10,
                        paddingVertical: 4,
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 13,
                          fontWeight: "600",
                          color: colors.danger,
                        }}
                      >
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
                      backgroundColor: colors.dangerBg,
                      borderRadius: 12,
                      padding: 16,
                      marginBottom: 12,
                      borderWidth: 2,
                      borderColor: colors.danger,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 16,
                        fontWeight: "700",
                        color: colors.danger,
                        marginBottom: 8,
                      }}
                    >
                      🚨 ALLERGEN MATCH
                    </Text>
                    <Text style={{ fontSize: 14, color: colors.danger }}>
                      This product contains allergens matching your profile:{" "}
                      {matchedAllergens
                        .map((t) => t.replace("en:", ""))
                        .join(", ")}
                    </Text>
                  </View>
                ) : null;
              })()}

            {/* Diet Warnings — surfaces the preference-mismatch checks computed above,
                including the FODMAP-status-based check, which previously had no render call. */}
            {dietWarnings.length > 0 && (
              <View
                style={{
                  backgroundColor: colors.warningBg,
                  borderRadius: 12,
                  padding: 16,
                  marginBottom: 12,
                }}
              >
                <Text
                  style={{
                    fontSize: 16,
                    fontWeight: "600",
                    color: colors.warning,
                    marginBottom: 8,
                  }}
                  accessibilityRole="header"
                >
                  ⚠️ Diet Preference Warnings
                </Text>
                {dietWarnings.map((w) => (
                  <Text
                    key={w}
                    style={{ fontSize: 14, color: colors.warning, marginTop: 4 }}
                  >
                    • {w}
                  </Text>
                ))}
              </View>
            )}

            {/* Additives from safety report */}
            {report && report.additives.length > 0 && (
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
                    fontSize: 16,
                    fontWeight: "600",
                    color: colors.text,
                    marginBottom: 12,
                  }}
                  accessibilityRole="header"
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
                    <View style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
                      <Text style={{ fontWeight: "600", color: colors.text, flex: 1 }}>
                        {add.name} {add.eNumber ? `(${add.eNumber})` : ""}
                      </Text>
                      <View
                        style={{
                          backgroundColor: ratingColor(add.cspiRating) + "18",
                          paddingHorizontal: 8,
                          paddingVertical: 2,
                          borderRadius: 10,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color: ratingColor(add.cspiRating),
                          }}
                        >
                          {cspiEmoji(add.cspiRating)} {add.cspiRating}
                        </Text>
                      </View>
                      <TouchableOpacity
                        onPress={() => handleToggleAlert(add)}
                        style={{ padding: 4, marginLeft: 2 }}
                        hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
                        accessibilityRole="button"
                        accessibilityLabel={
                          alertIds.has(add.id)
                            ? `Remove alert for ${add.name}`
                            : `Watch ${add.name}`
                        }
                        accessibilityState={{ selected: alertIds.has(add.id) }}
                      >
                        <Ionicons
                          name={
                            alertIds.has(add.id)
                              ? "notifications"
                              : "notifications-outline"
                          }
                          size={18}
                          color={
                            alertIds.has(add.id)
                              ? colors.warning
                              : colors.textMuted
                          }
                        />
                      </TouchableOpacity>
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
                    {add.bannedInCountries.length > 0 && (
                      <Text
                        style={{
                          fontSize: 11,
                          color: colors.sugar,
                          marginTop: 2,
                        }}
                      >
                        🚫 Banned in: {add.bannedInCountries.join(", ")}
                      </Text>
                    )}
                    {add.evidenceSources && add.evidenceSources.length > 0 && (
                      <View style={{ marginTop: 5 }}>
                        <Text style={{ fontSize: 11, color: colors.textMuted }}>
                          Background sources:
                        </Text>
                        {add.evidenceSources.map((url) => (
                          <TouchableOpacity
                            key={url}
                            onPress={() => Linking.openURL(url)}
                            accessibilityRole="link"
                            accessibilityLabel={`Open evidence source ${url}`}
                          >
                            <Text style={{ fontSize: 11, color: colors.primary }} numberOfLines={1}>
                              {url}
                            </Text>
                          </TouchableOpacity>
                        ))}
                      </View>
                    )}
                    {!add.healthConcerns && add.description && (
                      <Text
                        style={{
                          fontSize: 12,
                          color: colors.textSecondary,
                          marginTop: 4,
                        }}
                        numberOfLines={2}
                      >
                        {add.description}
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
                  backgroundColor: colors.card,
                  borderRadius: 12,
                  padding: 16,
                  marginBottom: 12,
                }}
              >
                {foodTrigger && (
                  <View
                    style={{
                      backgroundColor: colors.dangerBg,
                      borderRadius: 8,
                      padding: 10,
                      marginBottom: 10,
                      flexDirection: "row",
                      alignItems: "center",
                    }}
                  >
                    <Text style={{ fontSize: 14, marginRight: 6 }}>⚠️</Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.danger,
                        fontWeight: "600",
                        flex: 1,
                      }}
                    >
                      This has triggered {foodTrigger.symptoms.join(", ")} for
                      you {foodTrigger.totalOccurrences}x before
                    </Text>
                  </View>
                )}
                <Text
                  style={{
                    fontSize: 14,
                    fontWeight: "600",
                    color: colors.text,
                    marginBottom: 8,
                  }}
                >
                  Add {servingSize}g to meal:
                </Text>
                <Text
                  style={{
                    fontSize: 12,
                    color: colors.textSecondary,
                    marginBottom: 8,
                  }}
                >
                  {nutritionSummaryText(
                    scaleNutrition(product, servingSize),
                    servingSize,
                  )}
                </Text>
                <MealTypePicker
                  selected={addToMealType}
                  onSelect={setAddToMealType}
                />
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
                    accessibilityRole="button"
                    accessibilityLabel="Cancel"
                  >
                    <Text
                      style={{ color: colors.textSecondary, fontWeight: "600" }}
                    >
                      Cancel
                    </Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={() => addToMealMutation.mutate()}
                    disabled={addToMealMutation.isPending}
                    style={{
                      backgroundColor: colors.primaryLight,
                      paddingHorizontal: 16,
                      paddingVertical: 8,
                      borderRadius: 8,
                    }}
                    accessibilityRole="button"
                    accessibilityLabel="Log meal"
                    accessibilityState={{
                      disabled: addToMealMutation.isPending,
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
                  haptics.light();
                  setShowAddToMeal(true);
                }}
                style={{
                  backgroundColor: colors.primaryLight,
                  borderRadius: 12,
                  padding: 14,
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "center",
                  marginBottom: 12,
                }}
                accessibilityRole="button"
                accessibilityLabel="Add to meal"
              >
                <Ionicons
                  name="add-circle-outline"
                  size={20}
                  color={colors.textOnPrimary}
                />
                <Text
                  style={{
                    color: colors.textOnPrimary,
                    fontWeight: "700",
                    marginLeft: 8,
                    fontSize: 15,
                  }}
                >
                  Add to Meal
                </Text>
              </TouchableOpacity>
            )}

            {loadingReport && <SearchResultSkeleton count={2} />}

            <TouchableOpacity
              onPress={() => router.push("/sources")}
              style={{
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "center",
                gap: 6,
                paddingVertical: 16,
                marginTop: 8,
              }}
              accessibilityRole="link"
              accessibilityLabel="Sources and medical disclaimer"
            >
              <Ionicons
                name="information-circle-outline"
                size={14}
                color={colors.textMuted}
              />
              <Text
                style={{
                  fontSize: 12,
                  color: colors.textMuted,
                  textDecorationLine: "underline",
                }}
              >
                Sources & Medical Disclaimer
              </Text>
            </TouchableOpacity>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeScreen>
  );
}

function NutrientRow({
  label,
  value,
  unit,
  colors,
}: {
  label: string;
  value: number | null;
  unit: string;
  colors: ThemeColors;
}) {
  if (value == null) return null;
  return (
    <View
      style={{
        flexDirection: "row",
        justifyContent: "space-between",
        paddingVertical: 6,
        borderBottomWidth: 1,
        borderBottomColor: colors.borderLight,
      }}
    >
      <Text style={{ color: colors.textSecondary, fontSize: 14 }}>{label}</Text>
      <Text
        style={{
          fontWeight: "600",
          color: colors.text,
          fontSize: 14,
        }}
      >
        {value} {unit}
      </Text>
    </View>
  );
}
