import { useState, useCallback, useEffect } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  RefreshControl,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
import { useAuthStore } from "../../src/stores/auth";
import { mealApi, symptomApi, userApi, insightApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { DashboardSkeleton } from "../../components/SkeletonLoader";
import { ErrorState } from "../../components/ErrorState";
import { useRouter } from "expo-router";
import Svg, { Circle } from "react-native-svg";
import ReanimatedObj, {
  useSharedValue,
  useAnimatedProps,
  useAnimatedStyle,
  withTiming,
  Easing,
} from "react-native-reanimated";
import { SafeScreen } from "../../components/SafeScreen";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import { radius, spacing, mealTypeEmoji } from "../../src/utils/theme";
import { toLocalDateStr } from "../../src/utils/date";

const AnimatedCircle = ReanimatedObj.createAnimatedComponent(Circle);
const AnimatedView = ReanimatedObj.createAnimatedComponent(View);

function CalorieRing({
  eaten,
  goal,
  size = 140,
}: {
  eaten: number;
  goal: number;
  size?: number;
}) {
  const c = useThemeColors();
  const strokeWidth = 12;
  const r = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * r;
  const progress = Math.min(eaten / goal, 1);
  const overGoal = eaten > goal;

  const animProgress = useSharedValue(0);

  useEffect(() => {
    animProgress.value = 0;
    animProgress.value = withTiming(progress, {
      duration: 1000,
      easing: Easing.out(Easing.cubic),
    });
  }, [eaten, goal]);

  const animatedProps = useAnimatedProps(() => ({
    strokeDashoffset: circumference * (1 - animProgress.value),
  }));

  return (
    <View
      style={{
        alignItems: "center",
        justifyContent: "center",
        width: size,
        height: size,
      }}
    >
      <Svg
        width={size}
        height={size}
        style={{ transform: [{ rotate: "-90deg" }] }}
      >
        <Circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          stroke={c.border}
          strokeWidth={strokeWidth}
          fill="none"
        />
        <AnimatedCircle
          cx={size / 2}
          cy={size / 2}
          r={r}
          stroke={overGoal ? c.danger : c.primaryLight}
          strokeWidth={strokeWidth}
          fill="none"
          strokeDasharray={`${circumference}`}
          strokeLinecap="round"
          animatedProps={animatedProps}
        />
      </Svg>
      <View style={{ position: "absolute", alignItems: "center" }}>
        <Text
          style={{
            fontSize: 32,
            fontWeight: "800",
            color: overGoal ? c.danger : c.primary,
          }}
        >
          {eaten}
        </Text>
        <Text style={{ fontSize: 12, color: c.textMuted, marginTop: -2 }}>
          of {goal} cal
        </Text>
      </View>
    </View>
  );
}

function MacroBar({
  label,
  value,
  goal,
  color,
  unit = "g",
}: {
  label: string;
  value: number;
  goal: number;
  color: string;
  unit?: string;
}) {
  const c = useThemeColors();
  const pct = Math.min(value / goal, 1);
  const animWidth = useSharedValue(0);

  useEffect(() => {
    animWidth.value = 0;
    animWidth.value = withTiming(pct, {
      duration: 800,
      easing: Easing.out(Easing.cubic),
    });
  }, [value, goal]);

  const barStyle = useAnimatedStyle(() => ({
    height: 6,
    backgroundColor: color,
    borderRadius: 3,
    width: `${animWidth.value * 100}%`,
  }));

  return (
    <View style={{ marginBottom: 14 }}>
      <View
        style={{
          flexDirection: "row",
          justifyContent: "space-between",
          marginBottom: 4,
        }}
      >
        <Text
          style={{
            fontSize: 13,
            fontWeight: "600",
            color: c.textSecondary,
          }}
        >
          {label}
        </Text>
        <Text style={{ fontSize: 13, color: c.textMuted }}>
          <Text style={{ fontWeight: "700", color }}>{Math.round(value)}</Text>{" "}
          / {goal}
          {unit}
        </Text>
      </View>
      <View
        style={{
          height: 6,
          backgroundColor: c.border,
          borderRadius: 3,
        }}
      >
        <AnimatedView style={barStyle} />
      </View>
    </View>
  );
}

export default function DashboardScreen() {
  const c = useThemeColors();
  const f = useThemeFonts();
  const { shadow: sh, shadowMd: shMd } = useThemeShadow();
  const [macrosExpanded, setMacrosExpanded] = useState(false);

  const user = useAuthStore((s) => s.user);
  const today = toLocalDateStr();
  const router = useRouter();

  const {
    data: meals,
    isLoading: loadingMeals,
    isError: mealsError,
    refetch: refetchMeals,
  } = useQuery({
    queryKey: ["meals", today],
    queryFn: () => mealApi.list(today).then((r) => r.data),
  });

  const {
    data: summary,
    isLoading: loadingSummary,
    refetch: refetchSummary,
  } = useQuery({
    queryKey: ["daily-summary", today],
    queryFn: () => mealApi.dailySummary(today).then((r) => r.data),
  });

  const {
    data: todaysSymptoms,
    isLoading: loadingSymptoms,
    refetch: refetchSymptoms,
  } = useQuery({
    queryKey: ["symptoms-today", today],
    queryFn: () => symptomApi.list({ date: today }).then((r) => r.data),
  });

  const { data: alerts } = useQuery({
    queryKey: ["alerts"],
    queryFn: () => userApi.getAlerts().then((r) => r.data),
  });

  const { data: triggerFoods } = useQuery({
    queryKey: ["trigger-foods-dashboard"],
    queryFn: () => insightApi.triggerFoods(30).then((r) => r.data),
    staleTime: 5 * 60 * 1000,
  });

  const { data: streak } = useQuery({
    queryKey: ["streak"],
    queryFn: () => mealApi.streak().then((r) => r.data),
    staleTime: 10 * 60 * 1000,
  });

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all([refetchMeals(), refetchSummary(), refetchSymptoms()]);
    setRefreshing(false);
  }, [refetchMeals, refetchSummary, refetchSymptoms]);

  const isLoading = loadingMeals || loadingSummary;

  if (isLoading) {
    return (
      <ScrollView style={{ flex: 1, backgroundColor: c.bg }}>
        <DashboardSkeleton />
      </ScrollView>
    );
  }

  if (mealsError) {
    return (
      <ScrollView
        style={{ flex: 1, backgroundColor: c.bg }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={c.primary}
          />
        }
      >
        <ErrorState message="Failed to load dashboard" onRetry={onRefresh} />
      </ScrollView>
    );
  }

  const caloriesEaten = Math.round(summary?.totalCalories ?? 0);
  const calorieGoal = Math.round(
    summary?.calorieGoal ?? user?.dailyCalorieGoal ?? 2000,
  );
  const caloriesRemaining = Math.max(calorieGoal - caloriesEaten, 0);
  const mealCount = meals?.length ?? 0;
  const symptomCount = todaysSymptoms?.length ?? 0;

  const greeting = (() => {
    const h = new Date().getHours();
    if (h < 12) return "Good morning";
    if (h < 17) return "Good afternoon";
    return "Good evening";
  })();

  const fabActions = [
    {
      label: "Food Lookup",
      icon: "search" as const,
      color: c.accent,
      route: "/(tabs)/scan" as const,
    },
    {
      label: "Log Symptom",
      icon: "pulse" as const,
      color: c.secondary,
      route: "/(tabs)/symptoms" as const,
    },
    {
      label: "Log Meal",
      icon: "restaurant" as const,
      color: c.primary,
      route: "/(tabs)/meals" as const,
    },
  ];

  return (
    <SafeScreen edges={[]}>
      <ScrollView
        style={{ flex: 1 }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={c.primary}
          />
        }
        showsVerticalScrollIndicator={false}
      >
        <View style={{ padding: spacing.xl }}>
          {/* Header */}
          <View style={{ marginBottom: spacing.lg }}>
            <Text style={{ fontSize: 15, color: c.textMuted }}>
              {greeting},
            </Text>
            <Text style={f.h1}>{user?.displayName ?? "there"} 👋</Text>
          </View>

          {/* Alerts */}
          {alerts && alerts.length > 0 && (
            <TouchableOpacity
              onPress={() => router.push("/(tabs)/profile")}
              style={{
                backgroundColor: c.dangerBg,
                borderRadius: radius.md,
                padding: 14,
                marginBottom: spacing.lg,
                flexDirection: "row",
                alignItems: "center",
                borderWidth: 1,
                borderColor: c.dangerBorder,
              }}
            >
              <Ionicons name="warning" size={18} color={c.danger} />
              <Text
                style={{
                  color: c.danger,
                  fontWeight: "600",
                  marginLeft: 8,
                  flex: 1,
                  fontSize: 14,
                }}
              >
                {alerts.length} food additive alert
                {alerts.length !== 1 ? "s" : ""} active
              </Text>
              <Ionicons name="chevron-forward" size={16} color={c.danger} />
            </TouchableOpacity>
          )}

          {/* Stats Row */}
          <View
            style={{ flexDirection: "row", gap: 10, marginBottom: spacing.lg }}
          >
            <View
              style={{
                flex: 1,
                backgroundColor: c.primaryBg,
                borderRadius: radius.md,
                padding: 14,
                alignItems: "center",
                justifyContent: "center",
                ...sh,
              }}
            >
              <Text
                style={{
                  fontSize: 24,
                  fontWeight: "800",
                  color: c.primary,
                }}
              >
                {mealCount}
              </Text>
              <Text
                style={{
                  fontSize: 11,
                  color: c.primary,
                  fontWeight: "600",
                  textTransform: "uppercase",
                  letterSpacing: 0.5,
                  textAlign: "center",
                }}
              >
                Meals
              </Text>
            </View>
            <View
              style={{
                flex: 1,
                backgroundColor: symptomCount > 0 ? c.warningBg : c.primaryBg,
                borderRadius: radius.md,
                padding: 14,
                alignItems: "center",
                justifyContent: "center",
                ...sh,
              }}
            >
              <Text
                style={{
                  fontSize: 24,
                  fontWeight: "800",
                  color: symptomCount > 0 ? c.warning : c.primary,
                }}
              >
                {symptomCount}
              </Text>
              <Text
                style={{
                  fontSize: 11,
                  color: symptomCount > 0 ? c.warning : c.primary,
                  fontWeight: "600",
                  textTransform: "uppercase",
                  letterSpacing: 0.5,
                  textAlign: "center",
                }}
              >
                Symptoms
              </Text>
            </View>
            <View
              style={{
                flex: 1,
                backgroundColor: c.secondaryBg,
                borderRadius: radius.md,
                padding: 14,
                alignItems: "center",
                justifyContent: "center",
                ...sh,
              }}
            >
              <Text
                style={{
                  fontSize: 24,
                  fontWeight: "800",
                  color: c.secondary,
                }}
              >
                {caloriesRemaining}
              </Text>
              <Text
                style={{
                  fontSize: 11,
                  color: c.secondary,
                  fontWeight: "600",
                  textTransform: "uppercase",
                  letterSpacing: 0.5,
                  textAlign: "center",
                }}
              >
                Cal Left
              </Text>
            </View>
            {streak && streak.currentStreak > 0 && (
              <View
                style={{
                  flex: 1,
                  backgroundColor: c.warningBg,
                  borderRadius: radius.md,
                  padding: 14,
                  alignItems: "center",
                  justifyContent: "center",
                  ...sh,
                }}
              >
                <Text
                  style={{
                    fontSize: 24,
                    fontWeight: "800",
                    color: c.warning,
                  }}
                >
                  {streak.currentStreak} 🔥
                </Text>
                <Text
                  style={{
                    fontSize: 11,
                    color: c.warning,
                    fontWeight: "600",
                    textTransform: "uppercase",
                    letterSpacing: 0.5,
                    textAlign: "center",
                  }}
                >
                  Streak
                </Text>
              </View>
            )}
          </View>

          {/* Calorie Ring Card */}
          <View
            style={{
              backgroundColor: c.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...shMd,
            }}
          >
            <Text style={{ ...f.h4, marginBottom: spacing.lg }}>
              Today's Calories
            </Text>
            <View style={{ alignItems: "center", marginBottom: spacing.lg }}>
              <CalorieRing eaten={caloriesEaten} goal={calorieGoal} />
            </View>
            <View
              style={{ flexDirection: "row", justifyContent: "space-around" }}
            >
              <View style={{ alignItems: "center" }}>
                <Text
                  style={{
                    fontSize: 20,
                    fontWeight: "700",
                    color: c.primary,
                  }}
                >
                  {caloriesEaten}
                </Text>
                <Text style={f.caption}>eaten</Text>
              </View>
              <View style={{ width: 1, backgroundColor: c.border }} />
              <View style={{ alignItems: "center" }}>
                <Text
                  style={{
                    fontSize: 20,
                    fontWeight: "700",
                    color: caloriesRemaining > 0 ? c.secondary : c.danger,
                  }}
                >
                  {caloriesRemaining}
                </Text>
                <Text style={f.caption}>remaining</Text>
              </View>
            </View>
          </View>

          {/* Macros Card */}
          <View
            style={{
              backgroundColor: c.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...sh,
            }}
          >
            <View
              style={{
                justifyContent: "space-between",
                flexDirection: "row",
                alignItems: "center",
                alignContent: "center",
                marginBottom: spacing.md,
              }}
            >
              <Text style={{ ...f.h4, marginBottom: spacing.lg }}>Macros</Text>

              <Ionicons
                name={macrosExpanded ? "chevron-up" : "chevron-down"}
                onPress={() => {
                  setMacrosExpanded((prev) => !prev);
                }}
                size={20}
                color={c.secondary}
              />
            </View>
            <MacroBar
              label="Protein"
              value={summary?.totalProteinG ?? 0}
              goal={user?.dailyProteinGoalG ?? 150}
              color={c.protein}
            />
            <MacroBar
              label="Carbs"
              value={summary?.totalCarbsG ?? 0}
              goal={user?.dailyCarbGoalG ?? 250}
              color={c.carbs}
            />
            <MacroBar
              label="Fat"
              value={summary?.totalFatG ?? 0}
              goal={user?.dailyFatGoalG ?? 65}
              color={c.fat}
            />

            {macrosExpanded && (
              <>
                <MacroBar
                  label="Fiber"
                  value={summary?.totalFiberG ?? 0}
                  goal={user?.dailyFiberGoalG ?? 30}
                  color={c.fiber}
                />
                <View
                  style={{
                    height: 1,
                    backgroundColor: c.divider,
                    marginVertical: 4,
                  }}
                />
                <MacroBar
                  label="Sugar"
                  value={summary?.totalSugarG ?? 0}
                  goal={50}
                  color={c.sugar}
                />
                <MacroBar
                  label="Sodium"
                  value={summary?.totalSodiumMg ?? 0}
                  goal={2300}
                  color={c.sodium}
                  unit="mg"
                />
              </>
            )}
          </View>

          {/* Today's Meals */}
          <View style={{ marginBottom: spacing.xl }}>
            <View
              style={{
                flexDirection: "row",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: spacing.md,
              }}
            >
              <Text style={f.h3}>Today's Meals</Text>
              <TouchableOpacity onPress={() => router.push("/(tabs)/meals")}>
                <Text
                  style={{
                    fontSize: 13,
                    color: c.secondary,
                    fontWeight: "600",
                  }}
                >
                  See all →
                </Text>
              </TouchableOpacity>
            </View>
            {meals && meals.length > 0 ? (
              meals.map((meal) => (
                <TouchableOpacity
                  key={meal.id}
                  onPress={() => router.push("/(tabs)/meals")}
                  style={{
                    backgroundColor: c.card,
                    borderRadius: radius.md,
                    padding: spacing.lg,
                    marginBottom: spacing.sm,
                    flexDirection: "row",
                    alignItems: "center",
                    ...sh,
                  }}
                >
                  <View
                    style={{
                      width: 40,
                      height: 40,
                      borderRadius: radius.sm,
                      backgroundColor: c.primaryBg,
                      alignItems: "center",
                      justifyContent: "center",
                      marginRight: spacing.md,
                    }}
                  >
                    <Text style={{ fontSize: 20 }}>
                      {mealTypeEmoji[meal.mealType] ?? "🍽️"}
                    </Text>
                  </View>
                  <View style={{ flex: 1 }}>
                    <Text
                      style={{
                        fontSize: 15,
                        fontWeight: "600",
                        color: c.text,
                      }}
                    >
                      {meal.mealType}
                    </Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: c.textMuted,
                        marginTop: 2,
                      }}
                    >
                      {meal.items.length} item
                      {meal.items.length !== 1 ? "s" : ""} ·{" "}
                      {new Date(meal.loggedAt).toLocaleTimeString([], {
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </Text>
                  </View>
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "700",
                      color: c.text,
                    }}
                  >
                    {meal.totalCalories}
                  </Text>
                  <Text
                    style={{
                      fontSize: 12,
                      color: c.textMuted,
                      marginLeft: 2,
                    }}
                  >
                    cal
                  </Text>
                </TouchableOpacity>
              ))
            ) : (
              <View
                style={{
                  backgroundColor: c.card,
                  borderRadius: radius.md,
                  padding: spacing.xxxl,
                  alignItems: "center",
                  ...sh,
                }}
              >
                <Ionicons
                  name="restaurant-outline"
                  size={36}
                  color={c.textLight}
                />
                <Text
                  style={{
                    color: c.textMuted,
                    marginTop: spacing.sm,
                    fontSize: 14,
                  }}
                >
                  No meals logged today
                </Text>
                <TouchableOpacity
                  onPress={() => router.push("/(tabs)/meals")}
                  style={{
                    marginTop: spacing.md,
                    backgroundColor: c.primaryBg,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                  }}
                >
                  <Text
                    style={{
                      color: c.primary,
                      fontWeight: "600",
                      fontSize: 13,
                    }}
                  >
                    Log your first meal
                  </Text>
                </TouchableOpacity>
              </View>
            )}
          </View>

          {/* Today's Symptoms */}
          <View style={{ marginBottom: spacing.xl }}>
            <View
              style={{
                flexDirection: "row",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: spacing.md,
              }}
            >
              <Text style={f.h3}>Today's Symptoms</Text>
              <TouchableOpacity onPress={() => router.push("/(tabs)/symptoms")}>
                <Text
                  style={{
                    fontSize: 13,
                    color: c.secondary,
                    fontWeight: "600",
                  }}
                >
                  See all →
                </Text>
              </TouchableOpacity>
            </View>
            {todaysSymptoms && todaysSymptoms.length > 0 ? (
              todaysSymptoms.map((log) => (
                <TouchableOpacity
                  key={log.id}
                  onPress={() => router.push("/(tabs)/symptoms")}
                  style={{
                    backgroundColor: c.card,
                    borderRadius: radius.md,
                    padding: 14,
                    marginBottom: spacing.sm,
                    flexDirection: "row",
                    alignItems: "center",
                    ...sh,
                  }}
                >
                  <Text style={{ fontSize: 22, marginRight: spacing.md }}>
                    {log.icon}
                  </Text>
                  <View style={{ flex: 1 }}>
                    <Text
                      style={{
                        fontSize: 15,
                        fontWeight: "600",
                        color: c.text,
                      }}
                    >
                      {log.symptomName}
                    </Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: c.textMuted,
                        marginTop: 2,
                      }}
                    >
                      {new Date(log.occurredAt).toLocaleTimeString([], {
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                      {log.notes ? ` · ${log.notes}` : ""}
                    </Text>
                  </View>
                  <View
                    style={{
                      backgroundColor:
                        log.severity <= 3
                          ? c.primaryBg
                          : log.severity <= 6
                            ? c.warningBg
                            : c.dangerBg,
                      borderRadius: 6,
                      paddingHorizontal: 8,
                      paddingVertical: 4,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 12,
                        fontWeight: "700",
                        color:
                          log.severity <= 3
                            ? c.primary
                            : log.severity <= 6
                              ? c.warning
                              : c.danger,
                      }}
                    >
                      {log.severity}/10
                    </Text>
                  </View>
                </TouchableOpacity>
              ))
            ) : (
              <View
                style={{
                  backgroundColor: c.card,
                  borderRadius: radius.md,
                  padding: spacing.xxxl,
                  alignItems: "center",
                  ...sh,
                }}
              >
                <Text style={{ fontSize: 32 }}>😊</Text>
                <Text
                  style={{
                    color: c.primary,
                    marginTop: spacing.sm,
                    fontWeight: "600",
                    fontSize: 14,
                  }}
                >
                  No symptoms today — great!
                </Text>
                <TouchableOpacity
                  onPress={() => router.push("/(tabs)/symptoms")}
                  style={{
                    marginTop: spacing.md,
                    backgroundColor: c.primaryBg,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                  }}
                >
                  <Text
                    style={{
                      color: c.primary,
                      fontWeight: "600",
                      fontSize: 13,
                    }}
                  >
                    Log a symptom
                  </Text>
                </TouchableOpacity>
              </View>
            )}
          </View>

          {/* Trigger Foods */}
          <View
            style={{
              backgroundColor: c.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...shMd,
            }}
          >
            <Text style={{ ...f.h4, marginBottom: spacing.md }}>
              ⚡ Trigger Foods
            </Text>
            {triggerFoods && triggerFoods.length > 0 ? (
              triggerFoods.slice(0, 5).map((tf) => (
                <View
                  key={tf.food}
                  style={{
                    flexDirection: "row",
                    justifyContent: "space-between",
                    alignItems: "center",
                    paddingVertical: 8,
                    borderTopWidth: 1,
                    borderTopColor: c.divider,
                  }}
                >
                  <View style={{ flex: 1 }}>
                    <Text
                      style={{ fontWeight: "600", color: c.text, fontSize: 14 }}
                    >
                      {tf.food}
                    </Text>
                    <Text style={{ ...f.caption, fontSize: 11 }}>
                      {tf.symptoms.join(", ")}
                    </Text>
                  </View>
                  <View
                    style={{
                      backgroundColor: c.dangerBg,
                      paddingHorizontal: 8,
                      paddingVertical: 2,
                      borderRadius: radius.sm,
                    }}
                  >
                    <Text
                      style={{
                        color: c.danger,
                        fontSize: 11,
                        fontWeight: "600",
                      }}
                    >
                      {tf.totalOccurrences}×
                    </Text>
                  </View>
                </View>
              ))
            ) : (
              <View
                style={{ alignItems: "center", paddingVertical: spacing.md }}
              >
                <Ionicons
                  name="sparkles-outline"
                  size={28}
                  color={c.secondary}
                />
                <Text
                  style={{
                    ...f.caption,
                    marginTop: spacing.sm,
                    textAlign: "center",
                    lineHeight: 18,
                  }}
                >
                  No trigger foods detected yet.{"\n"}Keep logging meals &
                  symptoms!
                </Text>
              </View>
            )}
          </View>
        </View>
      </ScrollView>
    </SafeScreen>
  );
}
