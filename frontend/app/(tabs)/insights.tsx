import {
  View,
  Text,
  ScrollView,
  ActivityIndicator,
  RefreshControl,
  TouchableOpacity,
} from "react-native";
import Animated, { FadeInDown, useReducedMotion } from "react-native-reanimated";
import { useQuery } from "@tanstack/react-query";
import { insightApi, symptomApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import type {
  Correlation,
  SymptomLog,
  NutritionTrend,
  AdditiveExposure,
  TriggerFood,
  FoodDiaryAnalysis,
  EliminationDietStatus,
  MealTypeNutrition,
  FoodSymptomPattern,
} from "../../src/types";
import { useCallback, useState } from "react";
import { InsightsSkeleton } from "../../components/SkeletonLoader";
import { ErrorState } from "../../components/ErrorState";
import { CollapsibleCard } from "../../components/CollapsibleCard";
import { TrendChart, type TrendMetric } from "../../src/components/charts/TrendChart";
import { MacroDonut } from "../../src/components/charts/MacroDonut";
import { HBarList } from "../../src/components/charts/HBarList";
import {
  severityColor,
  cspiColor,
  confidenceColor,
  confidenceIcon,
} from "../../src/utils/colors";
import { radius, spacing } from "../../src/utils/theme";
import { mealTypeEmoji } from "../../src/utils/theme";
import { shiftDate, toLocalDateStr } from "../../src/utils/date";
import { getDeviceTimezoneId } from "../../src/utils/timezone";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import { SafeScreen } from "../../components/SafeScreen";
import { useRouter } from "expo-router";
import { useShareCard } from "../../components/share/ShareCardPortal";

export default function InsightsScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const reduced = useReducedMotion();
  const { shadow, shadowMd } = useThemeShadow();
  const [period, setPeriod] = useState(30);
  const timezoneId = getDeviceTimezoneId();
  const router = useRouter();
  const shareCard = useShareCard();
  const [showAllCorrelations, setShowAllCorrelations] = useState(false);
  const [showAllPatterns, setShowAllPatterns] = useState(false);
  const [selectedTrendMetric, setSelectedTrendMetric] = useState<TrendMetric>("calories");

  const {
    data: trends,
    isLoading: loadingTrends,
    isError: trendsError,
    refetch: refetchTrends,
  } = useQuery({
    queryKey: ["nutrition-trends", period, timezoneId],
    queryFn: () => insightApi.nutritionTrends(period).then((r) => r.data),
  });

  const {
    data: mealTypeMacros,
    isLoading: loadingMealTypeMacros,
    refetch: refetchMealTypeMacros,
  } = useQuery({
    queryKey: ["nutrition-by-meal-type", period, timezoneId],
    queryFn: () => insightApi.nutritionByMealType(period).then((r) => r.data),
  });

  const {
    data: exposure,
    isLoading: loadingExposure,
    isError: exposureError,
    refetch: refetchExposure,
  } = useQuery({
    queryKey: ["additive-exposure", period, timezoneId],
    queryFn: () => insightApi.additiveExposure(period).then((r) => r.data),
  });

  const {
    data: correlations,
    isLoading: loadingCorr,
    isError: corrError,
    refetch: refetchCorr,
  } = useQuery({
    queryKey: ["correlations", period, timezoneId],
    queryFn: () => insightApi.correlations(period).then((r) => r.data),
  });

  const todayStr = toLocalDateStr();
  const periodStart = shiftDate(todayStr, -period);

  const {
    data: recentSymptoms,
    isLoading: loadingSymptoms,
    refetch: refetchSymptoms,
  } = useQuery({
    queryKey: ["symptom-history", period, timezoneId],
    queryFn: () =>
      symptomApi
        .history({ from: periodStart, to: todayStr })
        .then((r) => r.data),
  });

  const {
    data: triggerFoods,
    isLoading: loadingTrigger,
    refetch: refetchTrigger,
  } = useQuery({
    queryKey: ["trigger-foods", period, timezoneId],
    queryFn: () => insightApi.triggerFoods(period).then((r) => r.data),
  });

  const {
    data: diaryAnalysis,
    isLoading: loadingDiary,
    refetch: refetchDiary,
  } = useQuery({
    queryKey: ["food-diary-analysis", period, timezoneId],
    queryFn: () => insightApi.foodDiaryAnalysis(period).then((r) => r.data),
  });

  const {
    data: elimination,
    isLoading: loadingElimination,
    refetch: refetchElimination,
  } = useQuery({
    queryKey: ["elimination-diet-status", timezoneId],
    queryFn: () => insightApi.eliminationDietStatus().then((r) => r.data),
  });

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all([
      refetchTrends(),
      refetchMealTypeMacros(),
      refetchExposure(),
      refetchCorr(),
      refetchSymptoms(),
      refetchTrigger(),
      refetchDiary(),
      refetchElimination(),
    ]);
    setRefreshing(false);
  }, [
    refetchTrends,
    refetchMealTypeMacros,
    refetchExposure,
    refetchCorr,
    refetchSymptoms,
    refetchTrigger,
    refetchDiary,
    refetchElimination,
  ]);

  const symptomsByDate = (recentSymptoms ?? []).reduce<
    Record<string, SymptomLog[]>
  >((acc, s) => {
    const date = toLocalDateStr(new Date(s.occurredAt));
    (acc[date] ??= []).push(s);
    return acc;
  }, {});

  const sortedDates = Object.keys(symptomsByDate).sort((a, b) =>
    b.localeCompare(a),
  );

  const totalCorrelations = correlations?.length ?? 0;
  const totalTriggers = triggerFoods?.length ?? 0;
  const totalSymptoms = recentSymptoms?.length ?? 0;

  const visibleCorrelations = showAllCorrelations
    ? correlations
    : correlations?.slice(0, 5);
  return (
    <SafeScreen edges={[]}>
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={colors.primary}
          />
        }
      >
        <View style={{ padding: spacing.xl }}>
          {/* Header */}
          <Text
            style={{ ...fonts.h1, marginBottom: 4 }}
            accessibilityRole="header"
          >
            Insights
          </Text>
          <Text style={{ ...fonts.caption, marginBottom: spacing.lg }}>
            {`Food ↔ symptom patterns from the last ${period} days`}
          </Text>

          {/* Period Selector */}
          <View
            style={{ flexDirection: "row", marginBottom: spacing.lg, gap: 6 }}
          >
            {[7, 14, 30, 90].map((d) => {
              const active = period === d;
              return (
                <TouchableOpacity
                  key={d}
                  onPress={() => setPeriod(d)}
                  accessibilityRole="radio"
                  accessibilityState={{ selected: active }}
                  accessibilityLabel={`${d} day period`}
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
                  <Text
                    style={{
                      fontSize: 14,
                      fontWeight: "700",
                      color: active
                        ? colors.textOnPrimary
                        : colors.textSecondary,
                    }}
                  >
                    {d}d
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* Summary Stats */}
          <View
            style={{ flexDirection: "row", gap: 10, marginBottom: spacing.xl }}
          >
            <View
              style={{
                flex: 1,
                backgroundColor: colors.card,
                borderRadius: radius.md,
                padding: 14,
                alignItems: "center",
                ...shadow,
              }}
            >
              <Text
                style={{
                  fontSize: 24,
                  fontWeight: "800",
                  color: colors.danger,
                }}
              >
                {totalTriggers}
              </Text>
              <Text
                style={{
                  fontSize: 11,
                  color: colors.textMuted,
                  fontWeight: "500",
                }}
              >
                triggers
              </Text>
            </View>
            <View
              style={{
                flex: 1,
                backgroundColor: colors.card,
                borderRadius: radius.md,
                padding: 14,
                alignItems: "center",
                ...shadow,
              }}
            >
              <Text
                style={{
                  fontSize: 24,
                  fontWeight: "800",
                  color: colors.accent,
                }}
              >
                {totalCorrelations}
              </Text>
              <Text
                style={{
                  fontSize: 11,
                  color: colors.textMuted,
                  fontWeight: "500",
                }}
              >
                correlations
              </Text>
            </View>
            <View
              style={{
                flex: 1,
                backgroundColor: colors.card,
                borderRadius: radius.md,
                padding: 14,
                alignItems: "center",
                ...shadow,
              }}
            >
              <Text
                style={{
                  fontSize: 24,
                  fontWeight: "800",
                  color: colors.warning,
                }}
              >
                {totalSymptoms}
              </Text>
              <Text
                style={{
                  fontSize: 11,
                  color: colors.textMuted,
                  fontWeight: "500",
                }}
              >
                symptoms
              </Text>
            </View>
          </View>

          {/* Trigger Foods */}
          <Animated.View
            entering={reduced ? undefined : FadeInDown.delay(0)}
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...shadowMd,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: spacing.lg,
              }}
            >
              <Text style={{ fontSize: 20, marginRight: spacing.sm }}>🎯</Text>
              <Text style={fonts.h3} accessibilityRole="header">
                Top Trigger Foods
              </Text>
              <View style={{ flex: 1 }} />
              {triggerFoods && triggerFoods.length > 0 ? (
                <TouchableOpacity
                  onPress={() =>
                    shareCard({
                      template: "triggerFoods",
                      data: {
                        periodLabel: `last ${period} days`,
                        items: triggerFoods.map((tf) => ({
                          food: tf.food,
                          count: tf.totalOccurrences,
                        })),
                      },
                    })
                  }
                  accessibilityRole="button"
                  accessibilityLabel="Share top trigger foods"
                  hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
                >
                  <Ionicons name="share-outline" size={20} color={colors.primary} />
                </TouchableOpacity>
              ) : null}
            </View>
            {loadingTrigger ? (
              <InsightsSkeleton />
            ) : triggerFoods && triggerFoods.length > 0 ? (
              triggerFoods.map((tf: TriggerFood, i: number) => (
                <View
                  key={tf.food}
                  style={{
                    backgroundColor: colors.bg,
                    borderRadius: radius.md,
                    padding: 14,
                    marginBottom: spacing.sm,
                    borderLeftWidth: 4,
                    borderLeftColor: confidenceColor(tf.worstConfidence),
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
                        fontSize: 15,
                        fontWeight: "600",
                        color: colors.text,
                        flex: 1,
                      }}
                    >
                      {i + 1}. {tf.food}
                    </Text>
                    <View
                      style={{
                        backgroundColor:
                          confidenceColor(tf.worstConfidence) + "18",
                        borderRadius: 6,
                        paddingHorizontal: 8,
                        paddingVertical: 3,
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 12,
                          fontWeight: "700",
                          color: confidenceColor(tf.worstConfidence),
                        }}
                      >
                        {tf.totalOccurrences}×
                      </Text>
                    </View>
                  </View>
                  <Text
                    style={{
                      fontSize: 12,
                      color: colors.textSecondary,
                      marginTop: 4,
                    }}
                  >
                    Triggers: {tf.symptoms.join(", ")} · Avg severity:{" "}
                    {Number(tf.avgSeverity).toFixed(1)}
                  </Text>
                </View>
              ))
            ) : (
              <View
                style={{ alignItems: "center", paddingVertical: spacing.xl }}
              >
                <Ionicons
                  name="flag-outline"
                  size={36}
                  color={colors.textLight}
                />
                <Text style={{ ...fonts.caption, marginTop: spacing.sm }}>
                  No trigger foods identified yet
                </Text>
              </View>
            )}
          </Animated.View>

          {/* Correlations */}
          <Animated.View
            entering={reduced ? undefined : FadeInDown.delay(50)}
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...shadowMd,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: spacing.lg,
              }}
            >
              <Text style={{ fontSize: 20, marginRight: spacing.sm }}>🔍</Text>
              <Text style={fonts.h3} accessibilityRole="header">
                Correlations
              </Text>
            </View>

            {loadingCorr ? (
              <InsightsSkeleton />
            ) : corrError ? (
              <ErrorState
                message="Failed to load correlations"
                onRetry={refetchCorr}
              />
            ) : correlations && correlations.length > 0 ? (
              <>
                {visibleCorrelations!.map((c, i) => (
                  <View
                    key={`${c.foodOrAdditive}-${c.symptomName}-${i}`}
                    style={{
                      backgroundColor: colors.bg,
                      borderRadius: radius.md,
                      padding: spacing.lg,
                      marginBottom: spacing.sm,
                      borderLeftWidth: 4,
                      borderLeftColor: confidenceColor(c.confidence),
                    }}
                  >
                    <View
                      style={{
                        flexDirection: "row",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      <View style={{ flex: 1 }}>
                        <Text
                          style={{
                            fontSize: 15,
                            fontWeight: "600",
                            color: colors.text,
                          }}
                        >
                          {c.foodOrAdditive}
                        </Text>
                        <Text
                          style={{
                            fontSize: 13,
                            color: colors.textSecondary,
                            marginTop: 2,
                          }}
                        >
                          → {c.symptomName}
                        </Text>
                      </View>
                      <View style={{ alignItems: "center" }}>
                        <Ionicons
                          name={confidenceIcon(c.confidence)}
                          size={20}
                          color={confidenceColor(c.confidence)}
                        />
                        <Text
                          style={{
                            fontSize: 10,
                            fontWeight: "700",
                            color: confidenceColor(c.confidence),
                            marginTop: 2,
                          }}
                        >
                          {c.confidence}
                        </Text>
                      </View>
                    </View>
                    <View
                      style={{
                        flexDirection: "row",
                        marginTop: spacing.md,
                        gap: 4,
                      }}
                    >
                      {[
                        {
                          val: c.occurrences,
                          label: "times",
                          color: colors.text,
                        },
                        {
                          val: c.totalMeals,
                          label: "meals",
                          color: colors.text,
                        },
                        {
                          val: `${c.frequencyPercent.toFixed(0)}%`,
                          label: "freq",
                          color: colors.secondary,
                        },
                        {
                          val: `${c.baselineFrequencyPercent.toFixed(0)}%`,
                          label: "baseline",
                          color: colors.textMuted,
                        },
                        {
                          val: c.averageSeverity.toFixed(1),
                          label: "severity",
                          color: severityColor(c.averageSeverity),
                        },
                      ].map(({ val, label, color }) => (
                        <View
                          key={label}
                          style={{ flex: 1, alignItems: "center" }}
                        >
                          <Text
                            style={{ fontSize: 16, fontWeight: "700", color }}
                          >
                            {val}
                          </Text>
                          <Text style={fonts.small}>{label}</Text>
                        </View>
                      ))}
                    </View>
                    <Text
                      style={{
                        fontSize: 11,
                        color: colors.textMuted,
                        marginTop: spacing.sm,
                        lineHeight: 15,
                      }}
                    >
                      {c.attributionMethod === "UserLinked"
                        ? "Based on symptoms you linked directly to this meal."
                        : "Inferred from a 1-6 hour onset window, not user-confirmed."}
                      {c.limitations.length > 0 ? ` ${c.limitations.join(" ")}` : ""}
                    </Text>
                  </View>
                ))}
                {correlations.length > 5 && (
                  <TouchableOpacity
                    onPress={() => setShowAllCorrelations(!showAllCorrelations)}
                    style={{
                      alignItems: "center",
                      paddingVertical: spacing.sm,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color: colors.primary,
                      }}
                    >
                      {showAllCorrelations
                        ? "Show less"
                        : `Show all ${correlations.length} correlations`}
                    </Text>
                  </TouchableOpacity>
                )}
              </>
            ) : (
              <View
                style={{ alignItems: "center", paddingVertical: spacing.xl }}
              >
                <Ionicons
                  name="analytics-outline"
                  size={36}
                  color={colors.textLight}
                />
                <Text style={{ ...fonts.caption, marginTop: spacing.sm }}>
                  No correlations found yet
                </Text>
                <Text style={{ ...fonts.small, marginTop: 4 }}>
                  Log meals and symptoms to discover patterns
                </Text>
              </View>
            )}
          </Animated.View>

          {/* Nutrition Trends */}
          <Animated.View
            entering={reduced ? undefined : FadeInDown.delay(100)}
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...shadowMd,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: spacing.lg,
              }}
            >
              <Text style={{ fontSize: 20, marginRight: spacing.sm }}>📊</Text>
              <Text style={fonts.h3} accessibilityRole="header">
                Nutrition Trends
              </Text>
              <View style={{ flex: 1 }} />
              {trends && trends.length >= 2 ? (
                <TouchableOpacity
                  onPress={() =>
                    shareCard({
                      template: "weeklySummary",
                      data: {
                        rangeLabel: `last ${period} days`,
                        mealsLogged:
                          trends?.reduce((s, t) => s + (t.mealCount || 0), 0) ?? 0,
                        avgCalories: trends?.length
                          ? trends.reduce((s, t) => s + (t.calories || 0), 0) /
                            trends.length
                          : null,
                        topTrigger: triggerFoods?.[0]?.food ?? null,
                      },
                    })
                  }
                  accessibilityRole="button"
                  accessibilityLabel="Share weekly nutrition summary"
                  hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
                >
                  <Ionicons name="share-outline" size={20} color={colors.primary} />
                </TouchableOpacity>
              ) : null}
            </View>

            {loadingTrends ? (
              <InsightsSkeleton />
            ) : trendsError ? (
              <ErrorState
                message="Failed to load trends"
                onRetry={refetchTrends}
              />
            ) : trends && trends.length > 0 ? (
              <View>
                <View
                  style={{
                    flexDirection: "row",
                    marginBottom: spacing.md,
                    gap: 6,
                  }}
                >
                  {(
                    [
                      { key: "calories", label: "Calories" },
                      { key: "protein", label: "Protein" },
                      { key: "carbs", label: "Carbs" },
                      { key: "fat", label: "Fat" },
                    ] as const
                  ).map(({ key, label }) => {
                    const active = selectedTrendMetric === key;
                    return (
                      <TouchableOpacity
                        key={key}
                        onPress={() => setSelectedTrendMetric(key)}
                        accessibilityRole="radio"
                        accessibilityState={{ selected: active }}
                        accessibilityLabel={label}
                        style={{
                          flex: 1,
                          paddingVertical: 8,
                          borderRadius: radius.md,
                          backgroundColor: active ? colors.primary : colors.card,
                          alignItems: "center",
                          ...shadow,
                          borderWidth: active ? 0 : 1,
                          borderColor: colors.borderLight,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 12,
                            fontWeight: "700",
                            color: active
                              ? colors.textOnPrimary
                              : colors.textSecondary,
                          }}
                        >
                          {label}
                        </Text>
                      </TouchableOpacity>
                    );
                  })}
                </View>
                <TrendChart
                  data={trends}
                  metric={selectedTrendMetric}
                  color={
                    selectedTrendMetric === "calories"
                      ? colors.primary
                      : selectedTrendMetric === "protein"
                      ? colors.protein
                      : selectedTrendMetric === "carbs"
                      ? colors.carbs
                      : colors.fat
                  }
                  onDayPress={(date) =>
                    router.push({
                      pathname: "/(tabs)/meals",
                      params: { date },
                    })
                  }
                />
              </View>
            ) : (
              <View
                style={{ alignItems: "center", paddingVertical: spacing.xl }}
              >
                <Ionicons
                  name="bar-chart-outline"
                  size={36}
                  color={colors.textLight}
                />
                <Text style={{ ...fonts.caption, marginTop: spacing.sm }}>
                  No nutrition data yet
                </Text>
              </View>
            )}
          </Animated.View>

          {/* Macro Breakdown by Meal Type */}
          {!loadingMealTypeMacros &&
            mealTypeMacros &&
            mealTypeMacros.length > 0 && (
              <Animated.View
                entering={reduced ? undefined : FadeInDown.delay(150)}
                style={{
                  backgroundColor: colors.card,
                  borderRadius: radius.lg,
                  padding: spacing.xl,
                  marginBottom: spacing.lg,
                  ...shadowMd,
                }}
              >
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    marginBottom: spacing.lg,
                  }}
                >
                  <Text style={{ fontSize: 20, marginRight: spacing.sm }}>
                    🍽️
                  </Text>
                  <Text style={fonts.h3} accessibilityRole="header">
                    Macros by Meal
                  </Text>
                </View>

                <MacroDonut data={mealTypeMacros} />
              </Animated.View>
            )}

          {/* Additive Exposure - only show when data exists */}
          {!loadingExposure && (
            <Animated.View
              entering={reduced ? undefined : FadeInDown.delay(200)}
              style={{
                backgroundColor: colors.card,
                borderRadius: radius.lg,
                padding: spacing.xl,
                marginBottom: spacing.lg,
                ...shadowMd,
              }}
            >
              <View
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  marginBottom: spacing.lg,
                }}
              >
                <Text style={{ fontSize: 20, marginRight: spacing.sm }}>
                  🧪
                </Text>
                <Text style={fonts.h3} accessibilityRole="header">
                  Additive Exposure
                </Text>
              </View>

              {exposure && exposure.length > 0 ? (
                <HBarList
                  items={exposure.map((e: AdditiveExposure) => ({
                    label: e.additive,
                    value: e.count,
                    rating: e.cspiRating,
                  }))}
                />
              ) : (
                <View
                  style={{ alignItems: "center", paddingVertical: spacing.md }}
                >
                  <Ionicons
                    name="shield-checkmark-outline"
                    size={32}
                    color={colors.secondary}
                  />
                  <Text
                    style={{
                      ...fonts.caption,
                      marginTop: spacing.sm,
                      textAlign: "center",
                    }}
                  >
                    No additives detected in your recent meals — nice! 🎉
                  </Text>
                </View>
              )}
            </Animated.View>
          )}

          {/* Food Diary Analysis - only timing insights & recommendations (patterns shown above) */}
          {!loadingDiary && (
            <Animated.View
              entering={reduced ? undefined : FadeInDown.delay(250)}
              style={{
                backgroundColor: colors.card,
                borderRadius: radius.lg,
                padding: spacing.xl,
                marginBottom: spacing.lg,
                ...shadowMd,
              }}
            >
              <View
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  marginBottom: spacing.lg,
                }}
              >
                <Text style={{ fontSize: 20, marginRight: spacing.sm }}>
                  💡
                </Text>
                <Text style={fonts.h3} accessibilityRole="header">
                  Insights & Tips
                </Text>
              </View>

              {diaryAnalysis &&
              (diaryAnalysis.timingInsights.length > 0 ||
                diaryAnalysis.recommendations.length > 0) ? (
                <>
                  {diaryAnalysis.timingInsights.length > 0 && (
                    <View style={{ marginBottom: 12 }}>
                      <Text
                        style={{
                          fontSize: 14,
                          fontWeight: "600",
                          color: colors.text,
                          marginBottom: 8,
                        }}
                      >
                        ⏱️ Timing Insights
                      </Text>
                      {diaryAnalysis.timingInsights.map((t, i) => (
                        <View
                          key={i}
                          style={{
                            backgroundColor: colors.bg,
                            borderRadius: radius.sm,
                            padding: 10,
                            marginBottom: 4,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 12,
                              fontWeight: "600",
                              color: colors.textSecondary,
                            }}
                          >
                            {t.category}
                          </Text>
                          <Text
                            style={{
                              fontSize: 12,
                              color: colors.text,
                              marginTop: 2,
                            }}
                          >
                            {t.insight}
                          </Text>
                        </View>
                      ))}
                    </View>
                  )}

                  {diaryAnalysis.recommendations.length > 0 && (
                    <View>
                      <Text
                        style={{
                          fontSize: 14,
                          fontWeight: "600",
                          color: colors.text,
                          marginBottom: 8,
                        }}
                      >
                        📋 Recommendations
                      </Text>
                      {diaryAnalysis.recommendations.map((rec, i) => (
                        <View
                          key={i}
                          style={{
                            flexDirection: "row",
                            gap: 8,
                            marginBottom: 6,
                          }}
                        >
                          <Ionicons
                            name="chevron-forward"
                            size={14}
                            color={colors.primary}
                            style={{ marginTop: 2 }}
                          />
                          <Text
                            style={{
                              fontSize: 12,
                              color: colors.text,
                              flex: 1,
                              lineHeight: 17,
                            }}
                          >
                            {rec}
                          </Text>
                        </View>
                      ))}
                    </View>
                  )}
                </>
              ) : (
                <View
                  style={{ alignItems: "center", paddingVertical: spacing.md }}
                >
                  <Ionicons
                    name="bulb-outline"
                    size={32}
                    color={colors.accent}
                  />
                  <Text
                    style={{
                      ...fonts.caption,
                      marginTop: spacing.sm,
                      textAlign: "center",
                      lineHeight: 18,
                    }}
                  >
                    Log a few more meals and we'll surface personalized tips
                    here
                  </Text>
                </View>
              )}
            </Animated.View>
          )}

          {/* Diary Patterns */}
          {!loadingDiary && diaryAnalysis && (
            <Animated.View
              entering={reduced ? undefined : FadeInDown.delay(300)}
            >
              <CollapsibleCard
              title="Diary Patterns"
              emoji="🔍"
              badge={
                diaryAnalysis.patternsFound > 0
                  ? `${diaryAnalysis.patternsFound} pattern${diaryAnalysis.patternsFound === 1 ? "" : "s"}`
                  : undefined
              }
              badgeColor={colors.primary}
              defaultOpen={diaryAnalysis.patternsFound > 0}
            >
              {diaryAnalysis.patterns && diaryAnalysis.patterns.length > 0 ? (
                <>
                  {(showAllPatterns
                    ? diaryAnalysis.patterns
                    : diaryAnalysis.patterns.slice(0, 5)
                  ).map((p: FoodSymptomPattern, i: number) => {
                    const food = p.foodName;
                    const symptomList = p.symptomName;
                    const occurrences = p.occurrences;
                    const avgSev = Number(p.averageSeverity);
                    const confidence = p.confidence || "Low";
                    return (
                      <View
                        key={`${food}-${symptomList}-${i}`}
                        style={{
                          backgroundColor: colors.bg,
                          borderRadius: radius.md,
                          padding: 12,
                          marginBottom: spacing.sm,
                          borderLeftWidth: 4,
                          borderLeftColor: confidenceColor(confidence),
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
                              fontSize: 14,
                              fontWeight: "700",
                              color: colors.text,
                              flex: 1,
                              marginRight: 8,
                            }}
                          >
                            {food}
                          </Text>
                          <View
                            style={{
                              backgroundColor:
                                confidenceColor(confidence) + "18",
                              borderRadius: 6,
                              paddingHorizontal: 8,
                              paddingVertical: 2,
                            }}
                          >
                            <Text
                              style={{
                                fontSize: 11,
                                fontWeight: "700",
                                color: confidenceColor(confidence),
                              }}
                            >
                              {confidence}
                            </Text>
                          </View>
                        </View>
                        <Text
                          style={{
                            fontSize: 12,
                            color: colors.textSecondary,
                            marginTop: 3,
                          }}
                        >
                          Symptoms: {symptomList}
                        </Text>
                        <Text
                          style={{
                            fontSize: 11,
                            color: colors.textMuted,
                            marginTop: 2,
                          }}
                        >
                          {occurrences}× occurrences · Avg severity:{" "}
                          {Number(avgSev).toFixed(1)}
                        </Text>
                      </View>
                    );
                  })}
                  {diaryAnalysis.patterns.length > 5 && (
                    <TouchableOpacity
                      onPress={() => setShowAllPatterns(!showAllPatterns)}
                      style={{
                        alignItems: "center",
                        paddingVertical: spacing.sm,
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 13,
                          fontWeight: "600",
                          color: colors.primary,
                        }}
                      >
                        {showAllPatterns
                          ? "Show less"
                          : `Show all ${diaryAnalysis.patterns.length} patterns`}
                      </Text>
                    </TouchableOpacity>
                  )}
                </>
              ) : (
                <View
                  style={{ alignItems: "center", paddingVertical: spacing.md }}
                >
                  <Text
                    style={{
                      fontSize: 13,
                      color: colors.textMuted,
                      textAlign: "center",
                    }}
                  >
                    No repeating food-symptom patterns yet
                  </Text>
                </View>
              )}
              </CollapsibleCard>
            </Animated.View>
          )}

          {/* Elimination Diet Status */}
          <Animated.View
            entering={reduced ? undefined : FadeInDown.delay(300)}
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.xxxl,
              ...shadowMd,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                marginBottom: spacing.lg,
              }}
            >
              <Text style={{ fontSize: 20, marginRight: spacing.sm }}>🥗</Text>
              <Text style={fonts.h3} accessibilityRole="header">
                Elimination Diet
              </Text>
            </View>

            {loadingElimination ? (
              <ActivityIndicator size="large" color={colors.primary} />
            ) : elimination ? (
              <View>
                {/* Phase Badge */}
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    marginBottom: 12,
                  }}
                >
                  <View
                    style={{
                      backgroundColor:
                        elimination.phase === "Not Started"
                          ? colors.borderLight
                          : elimination.phase === "Assessment"
                            ? colors.warningBg
                            : elimination.phase === "Elimination"
                              ? colors.dangerBg
                              : elimination.phase === "Reintroduction"
                                ? colors.secondaryBg
                                : colors.primaryBg,
                      borderRadius: 8,
                      paddingHorizontal: 12,
                      paddingVertical: 6,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 14,
                        fontWeight: "700",
                        color:
                          elimination.phase === "Not Started"
                            ? colors.textMuted
                            : elimination.phase === "Assessment"
                              ? colors.warning
                              : elimination.phase === "Elimination"
                                ? colors.danger
                                : elimination.phase === "Reintroduction"
                                  ? colors.secondary
                                  : colors.primary,
                      }}
                    >
                      Phase: {elimination.phase}
                    </Text>
                  </View>
                </View>

                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textSecondary,
                    lineHeight: 18,
                    marginBottom: 12,
                  }}
                >
                  {elimination.summary}
                </Text>

                {/* Foods to Eliminate */}
                {elimination.foodsToEliminate.length > 0 && (
                  <View style={{ marginBottom: 12 }}>
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color: colors.danger,
                        marginBottom: 6,
                      }}
                    >
                      🚫 Foods to Eliminate
                    </Text>
                    <View
                      style={{ flexDirection: "row", flexWrap: "wrap", gap: 6 }}
                    >
                      {elimination.foodsToEliminate.map((f) => (
                        <View
                          key={f}
                          style={{
                            backgroundColor: colors.dangerBg,
                            borderRadius: 6,
                            paddingHorizontal: 10,
                            paddingVertical: 4,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 12,
                              fontWeight: "600",
                              color: colors.danger,
                            }}
                          >
                            {f}
                          </Text>
                        </View>
                      ))}
                    </View>
                  </View>
                )}

                {/* Foods to Reintroduce */}
                {elimination.foodsToReintroduce.length > 0 && (
                  <View style={{ marginBottom: 12 }}>
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color: colors.secondary,
                        marginBottom: 6,
                      }}
                    >
                      🔄 Consider Reintroducing
                    </Text>
                    <View
                      style={{ flexDirection: "row", flexWrap: "wrap", gap: 6 }}
                    >
                      {elimination.foodsToReintroduce.map((f) => (
                        <View
                          key={f}
                          style={{
                            backgroundColor: colors.secondaryBg,
                            borderRadius: 6,
                            paddingHorizontal: 10,
                            paddingVertical: 4,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 12,
                              fontWeight: "600",
                              color: colors.secondary,
                            }}
                          >
                            {f}
                          </Text>
                        </View>
                      ))}
                    </View>
                  </View>
                )}

                {/* Safe Foods */}
                {elimination.safeFoods.length > 0 && (
                  <View style={{ marginBottom: 12 }}>
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color: colors.primary,
                        marginBottom: 6,
                      }}
                    >
                      ✅ Safe Foods
                    </Text>
                    <View
                      style={{ flexDirection: "row", flexWrap: "wrap", gap: 6 }}
                    >
                      {elimination.safeFoods.slice(0, 10).map((f) => (
                        <View
                          key={f}
                          style={{
                            backgroundColor: colors.primaryBg,
                            borderRadius: 6,
                            paddingHorizontal: 10,
                            paddingVertical: 4,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 12,
                              fontWeight: "600",
                              color: colors.primary,
                            }}
                          >
                            {f}
                          </Text>
                        </View>
                      ))}
                    </View>
                  </View>
                )}

                {/* Reintroduction Results */}
                {elimination.reintroductionResults.length > 0 && (
                  <View style={{ marginBottom: 12 }}>
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color: colors.text,
                        marginBottom: 6,
                      }}
                    >
                      🧪 Reintroduction Results
                    </Text>
                    {elimination.reintroductionResults.map((r) => (
                      <View
                        key={r.foodName}
                        style={{
                          flexDirection: "row",
                          alignItems: "center",
                          backgroundColor: colors.bg,
                          borderRadius: radius.sm,
                          padding: 10,
                          marginBottom: 4,
                        }}
                      >
                        <View style={{ flex: 1, marginRight: 8 }}>
                          <Text
                            style={{
                              fontSize: 13,
                              fontWeight: "600",
                              color: colors.text,
                            }}
                          >
                            {r.foodName}
                          </Text>
                          {(r.averageSeverity != null || r.testCount != null) && (
                            <Text
                              style={{
                                fontSize: 11,
                                color: colors.textMuted,
                                marginTop: 2,
                              }}
                            >
                              {r.averageSeverity != null && r.testCount != null
                                ? `Avg severity ${Number(r.averageSeverity).toFixed(1)} · ${r.testCount} ${r.testCount === 1 ? "test" : "tests"}`
                                : r.averageSeverity != null
                                  ? `Avg severity ${Number(r.averageSeverity).toFixed(1)}`
                                  : `${r.testCount} ${r.testCount === 1 ? "test" : "tests"}`}
                            </Text>
                          )}
                        </View>
                        <View
                          style={{
                            backgroundColor:
                              r.result === "Tolerated"
                                ? colors.primaryBg
                                : colors.dangerBg,
                            borderRadius: 4,
                            paddingHorizontal: 8,
                            paddingVertical: 2,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 11,
                              fontWeight: "700",
                              color:
                                r.result === "Tolerated"
                                  ? colors.primary
                                  : colors.danger,
                            }}
                          >
                            {r.result === "Tolerated" ? "✓" : "✗"} {r.result}
                          </Text>
                        </View>
                      </View>
                    ))}
                  </View>
                )}

                {/* Elimination Recommendations */}
                {elimination.recommendations.length > 0 && (
                  <View>
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color: colors.text,
                        marginBottom: 6,
                      }}
                    >
                      💡 Next Steps
                    </Text>
                    {elimination.recommendations.map((rec, i) => (
                      <View
                        key={i}
                        style={{
                          flexDirection: "row",
                          gap: 8,
                          marginBottom: 4,
                        }}
                      >
                        <Ionicons
                          name="chevron-forward"
                          size={12}
                          color={colors.primary}
                          style={{ marginTop: 2 }}
                        />
                        <Text
                          style={{
                            fontSize: 12,
                            color: colors.text,
                            flex: 1,
                            lineHeight: 17,
                          }}
                        >
                          {rec}
                        </Text>
                      </View>
                    ))}
                  </View>
                )}
              </View>
            ) : (
              <View
                style={{ alignItems: "center", paddingVertical: spacing.xl }}
              >
                <Ionicons
                  name="nutrition-outline"
                  size={36}
                  color={colors.textLight}
                />
                <Text style={{ ...fonts.caption, marginTop: spacing.sm }}>
                  No elimination data yet
                </Text>
              </View>
            )}
          </Animated.View>
        </View>

        <TouchableOpacity
          onPress={() => router.push("/sources")}
          style={{
            flexDirection: "row",
            alignItems: "center",
            justifyContent: "center",
            gap: 6,
            paddingVertical: spacing.lg,
            marginBottom: spacing.xl,
          }}
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
      </ScrollView>
    </SafeScreen>
  );
}
