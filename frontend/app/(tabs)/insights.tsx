import {
  View,
  Text,
  ScrollView,
  ActivityIndicator,
  RefreshControl,
  TouchableOpacity,
} from "react-native";
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
} from "../../src/types";
import { useCallback, useState } from "react";
import { InsightsSkeleton } from "../../components/SkeletonLoader";
import { ErrorState } from "../../components/ErrorState";
import {
  severityColor,
  cspiColor,
  confidenceColor,
  confidenceIcon,
} from "../../src/utils/colors";
import { radius, spacing } from "../../src/utils/theme";
import { toLocalDateStr } from "../../src/utils/date";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import { SafeScreen } from "../../components/SafeScreen";
import { useRouter } from "expo-router";

export default function InsightsScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow, shadowMd } = useThemeShadow();
  const [period, setPeriod] = useState(30);
  const router = useRouter();
  const [showAllCorrelations, setShowAllCorrelations] = useState(false);
  const [showAllTrends, setShowAllTrends] = useState(false);
  const [showAllPatterns, setShowAllPatterns] = useState(false);

  const {
    data: trends,
    isLoading: loadingTrends,
    isError: trendsError,
    refetch: refetchTrends,
  } = useQuery({
    queryKey: ["nutrition-trends", period],
    queryFn: () => insightApi.nutritionTrends(period).then((r) => r.data),
  });

  const {
    data: exposure,
    isLoading: loadingExposure,
    isError: exposureError,
    refetch: refetchExposure,
  } = useQuery({
    queryKey: ["additive-exposure", period],
    queryFn: () => insightApi.additiveExposure(period).then((r) => r.data),
  });

  const {
    data: correlations,
    isLoading: loadingCorr,
    isError: corrError,
    refetch: refetchCorr,
  } = useQuery({
    queryKey: ["correlations", period],
    queryFn: () => insightApi.correlations(period).then((r) => r.data),
  });

  const periodStart = toLocalDateStr(new Date(Date.now() - period * 86400000));
  const todayStr = toLocalDateStr();

  const {
    data: recentSymptoms,
    isLoading: loadingSymptoms,
    refetch: refetchSymptoms,
  } = useQuery({
    queryKey: ["symptom-history", period],
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
    queryKey: ["trigger-foods", period],
    queryFn: () => insightApi.triggerFoods(period).then((r) => r.data),
  });

  const {
    data: diaryAnalysis,
    isLoading: loadingDiary,
    refetch: refetchDiary,
  } = useQuery({
    queryKey: ["food-diary-analysis", period],
    queryFn: () => insightApi.foodDiaryAnalysis(period).then((r) => r.data),
  });

  const {
    data: elimination,
    isLoading: loadingElimination,
    refetch: refetchElimination,
  } = useQuery({
    queryKey: ["elimination-diet-status"],
    queryFn: () => insightApi.eliminationDietStatus().then((r) => r.data),
  });

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all([
      refetchTrends(),
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
    const date = s.occurredAt.split("T")[0];
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
  const visibleTrends = showAllTrends
    ? [...(trends ?? [])].reverse()
    : trends?.slice(-7).reverse();

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
          <Text style={{ ...fonts.h1, marginBottom: 4 }}>Insights</Text>
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
          <View
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
              <Text style={fonts.h3}>Top Trigger Foods</Text>
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
          </View>

          {/* Correlations */}
          <View
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
              <Text style={fonts.h3}>Correlations</Text>
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
          </View>

          {/* Nutrition Trends */}
          <View
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
              <Text style={fonts.h3}>Nutrition Trends</Text>
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
                {visibleTrends!.map((day) => {
                  const source = showAllTrends ? trends : visibleTrends!;
                  const maxCal = Math.max(...source.map((t) => t.calories), 1);
                  const pct = Math.min((day.calories / maxCal) * 100, 100);
                  return (
                    <View key={day.date} style={{ marginBottom: spacing.sm }}>
                      <View
                        style={{
                          flexDirection: "row",
                          justifyContent: "space-between",
                          alignItems: "center",
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
                          {new Date(day.date + "T12:00:00").toLocaleDateString(
                            undefined,
                            {
                              weekday: "short",
                              month: "short",
                              day: "numeric",
                            },
                          )}
                        </Text>
                        <Text
                          style={{
                            fontSize: 13,
                            fontWeight: "700",
                            color: colors.text,
                          }}
                        >
                          {Math.round(day.calories)} cal
                        </Text>
                      </View>
                      <View
                        style={{
                          height: 6,
                          backgroundColor: colors.border,
                          borderRadius: 3,
                        }}
                      >
                        <View
                          style={{
                            height: 6,
                            backgroundColor: colors.primaryLight,
                            borderRadius: 3,
                            width: `${pct}%`,
                          }}
                        />
                      </View>
                      <View
                        style={{
                          flexDirection: "row",
                          justifyContent: "space-between",
                          marginTop: 4,
                        }}
                      >
                        <Text style={{ fontSize: 10, color: colors.protein }}>
                          P: {Math.round(day.protein)}g
                        </Text>
                        <Text style={{ fontSize: 10, color: colors.carbs }}>
                          C: {Math.round(day.carbs)}g
                        </Text>
                        <Text style={{ fontSize: 10, color: colors.fat }}>
                          F: {Math.round(day.fat)}g
                        </Text>
                        <Text style={{ fontSize: 10, color: colors.fiber }}>
                          Fb: {Math.round(day.fiber)}g
                        </Text>
                        <Text style={{ fontSize: 10, color: colors.textMuted }}>
                          {day.mealCount} meals
                        </Text>
                      </View>
                    </View>
                  );
                })}
                {trends.length > 7 && (
                  <TouchableOpacity
                    onPress={() => setShowAllTrends(!showAllTrends)}
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
                      {showAllTrends
                        ? "Show last 7 days"
                        : `Show all ${trends.length} days`}
                    </Text>
                  </TouchableOpacity>
                )}
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
          </View>

          {/* Additive Exposure - only show when data exists */}
          {!loadingExposure && (
            <View
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
                <Text style={fonts.h3}>Additive Exposure</Text>
              </View>

              {exposure && exposure.length > 0 ? (
                exposure.map((item: AdditiveExposure) => (
                  <View
                    key={item.additive}
                    style={{
                      backgroundColor: colors.bg,
                      borderRadius: radius.sm,
                      padding: spacing.md,
                      marginBottom: 4,
                      flexDirection: "row",
                      alignItems: "center",
                      justifyContent: "space-between",
                    }}
                  >
                    <View style={{ flex: 1 }}>
                      <Text
                        style={{
                          fontWeight: "600",
                          color: colors.text,
                          fontSize: 14,
                        }}
                      >
                        {item.additive}
                      </Text>
                      <Text
                        style={{
                          fontSize: 11,
                          color: cspiColor(item.cspiRating),
                          fontWeight: "600",
                        }}
                      >
                        {item.cspiRating}
                      </Text>
                    </View>
                    <View
                      style={{
                        backgroundColor: cspiColor(item.cspiRating) + "18",
                        borderRadius: 6,
                        paddingHorizontal: 10,
                        paddingVertical: 4,
                      }}
                    >
                      <Text
                        style={{
                          fontWeight: "700",
                          color: cspiColor(item.cspiRating),
                          fontSize: 14,
                        }}
                      >
                        {item.count}×
                      </Text>
                    </View>
                  </View>
                ))
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
            </View>
          )}

          {/* Food Diary Analysis - only timing insights & recommendations (patterns shown above) */}
          {!loadingDiary && (
            <View
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
                <Text style={fonts.h3}>Insights & Tips</Text>
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
            </View>
          )}

          {/* Elimination Diet Status */}
          <View
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
              <Text style={fonts.h3}>Elimination Diet</Text>
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
                        <Text
                          style={{
                            flex: 1,
                            fontSize: 13,
                            fontWeight: "600",
                            color: colors.text,
                          }}
                        >
                          {r.foodName}
                        </Text>
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
          </View>
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
