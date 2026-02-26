import { useState, useCallback, useRef } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  RefreshControl,
  Animated,
  Pressable,
  StyleSheet,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
import { useAuthStore } from "../../src/stores/auth";
import { mealApi, symptomApi, userApi, insightApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { DashboardSkeleton } from "../../components/SkeletonLoader";
import { ErrorState } from "../../components/ErrorState";
import { useRouter } from "expo-router";
import {
  colors,
  shadow,
  shadowMd,
  radius,
  spacing,
  fonts,
  mealTypeEmoji,
} from "../../src/utils/theme";
import Svg, { Circle } from "react-native-svg";

function CalorieRing({
  eaten,
  goal,
  size = 140,
}: {
  eaten: number;
  goal: number;
  size?: number;
}) {
  const strokeWidth = 12;
  const r = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * r;
  const progress = Math.min(eaten / goal, 1);
  const strokeDashoffset = circumference * (1 - progress);
  const overGoal = eaten > goal;

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
          stroke={colors.borderLight}
          strokeWidth={strokeWidth}
          fill="none"
        />
        <Circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          stroke={overGoal ? colors.danger : colors.primaryLight}
          strokeWidth={strokeWidth}
          fill="none"
          strokeDasharray={`${circumference}`}
          strokeDashoffset={strokeDashoffset}
          strokeLinecap="round"
        />
      </Svg>
      <View style={{ position: "absolute", alignItems: "center" }}>
        <Text
          style={{
            fontSize: 32,
            fontWeight: "800",
            color: overGoal ? colors.danger : colors.primary,
          }}
        >
          {eaten}
        </Text>
        <Text style={{ fontSize: 12, color: colors.textMuted, marginTop: -2 }}>
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
  const pct = Math.min(value / goal, 1);
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
            color: colors.textSecondary,
          }}
        >
          {label}
        </Text>
        <Text style={{ fontSize: 13, color: colors.textMuted }}>
          <Text style={{ fontWeight: "700", color }}>{Math.round(value)}</Text>{" "}
          / {goal}
          {unit}
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
            backgroundColor: color,
            borderRadius: 3,
            width: `${pct * 100}%`,
          }}
        />
      </View>
    </View>
  );
}

export default function DashboardScreen() {
  const [fabOpen, setFabOpen] = useState(false);
  const [macrosExpanded, setMacrosExpanded] = useState(false);

  const fabAnim = useRef(new Animated.Value(0)).current;
  const user = useAuthStore((s) => s.user);
  const today = new Date().toISOString().split("T")[0];
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

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all([refetchMeals(), refetchSummary(), refetchSymptoms()]);
    setRefreshing(false);
  }, [refetchMeals, refetchSummary, refetchSymptoms]);

  const isLoading = loadingMeals || loadingSummary;

  if (isLoading) {
    return (
      <ScrollView style={{ flex: 1, backgroundColor: colors.bg }}>
        <DashboardSkeleton />
      </ScrollView>
    );
  }

  if (mealsError) {
    return (
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={colors.primary}
          />
        }
      >
        <ErrorState message="Failed to load dashboard" onRetry={onRefresh} />
      </ScrollView>
    );
  }

  const caloriesEaten = summary?.totalCalories ?? 0;
  const calorieGoal = summary?.calorieGoal ?? user?.dailyCalorieGoal ?? 2000;
  const caloriesRemaining = Math.max(calorieGoal - caloriesEaten, 0);
  const mealCount = meals?.length ?? 0;
  const symptomCount = todaysSymptoms?.length ?? 0;

  const greeting = (() => {
    const h = new Date().getHours();
    if (h < 12) return "Good morning";
    if (h < 17) return "Good afternoon";
    return "Good evening";
  })();

  const toggleFab = () => {
    const toValue = fabOpen ? 0 : 1;
    Animated.spring(fabAnim, {
      toValue,
      useNativeDriver: true,
      friction: 6,
    }).start();
    setFabOpen(!fabOpen);
  };

  const fabActions = [
    {
      label: "Food Lookup",
      icon: "search" as const,
      color: colors.accent,
      route: "/(tabs)/scan" as const,
    },
    {
      label: "Log Symptom",
      icon: "pulse" as const,
      color: colors.secondary,
      route: "/(tabs)/symptoms" as const,
    },
    {
      label: "Log Meal",
      icon: "restaurant" as const,
      color: colors.primary,
      route: "/(tabs)/meals" as const,
    },
  ];

  return (
    <View style={{ flex: 1, backgroundColor: colors.bg }}>
      <ScrollView
        style={{ flex: 1 }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={colors.primary}
          />
        }
        showsVerticalScrollIndicator={false}
      >
        <View style={{ padding: spacing.xl }}>
          {/* Header */}
          <View style={{ marginBottom: spacing.lg }}>
            <Text style={{ fontSize: 15, color: colors.textMuted }}>
              {greeting},
            </Text>
            <Text style={fonts.h1}>{user?.displayName ?? "there"} 👋</Text>
          </View>

          {/* Alerts */}
          {alerts && alerts.length > 0 && (
            <TouchableOpacity
              onPress={() => router.push("/(tabs)/profile")}
              style={{
                backgroundColor: colors.dangerBg,
                borderRadius: radius.md,
                padding: 14,
                marginBottom: spacing.lg,
                flexDirection: "row",
                alignItems: "center",
                borderWidth: 1,
                borderColor: colors.dangerBorder,
              }}
            >
              <Ionicons name="warning" size={18} color={colors.danger} />
              <Text
                style={{
                  color: colors.danger,
                  fontWeight: "600",
                  marginLeft: 8,
                  flex: 1,
                  fontSize: 14,
                }}
              >
                {alerts.length} food additive alert
                {alerts.length !== 1 ? "s" : ""} active
              </Text>
              <Ionicons
                name="chevron-forward"
                size={16}
                color={colors.danger}
              />
            </TouchableOpacity>
          )}

          {/* Stats Row */}
          <View
            style={{ flexDirection: "row", gap: 10, marginBottom: spacing.lg }}
          >
            <View
              style={{
                flex: 1,
                backgroundColor: colors.primaryBg,
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
                  color: colors.primary,
                }}
              >
                {mealCount}
              </Text>
              <Text
                style={{
                  fontSize: 12,
                  color: colors.primary,
                  fontWeight: "500",
                }}
              >
                meals today
              </Text>
            </View>
            <View
              style={{
                flex: 1,
                backgroundColor:
                  symptomCount > 0 ? colors.warningBg : colors.primaryBg,
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
                  color: symptomCount > 0 ? colors.warning : colors.primary,
                }}
              >
                {symptomCount}
              </Text>
              <Text
                style={{
                  fontSize: 12,
                  color: symptomCount > 0 ? colors.warning : colors.primary,
                  fontWeight: "500",
                }}
              >
                symptom{symptomCount !== 1 ? "s" : ""}
              </Text>
            </View>
            <View
              style={{
                flex: 1,
                backgroundColor: colors.secondaryBg,
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
                  color: colors.secondary,
                }}
              >
                {caloriesRemaining}
              </Text>
              <Text
                style={{
                  fontSize: 12,
                  color: colors.secondary,
                  fontWeight: "500",
                }}
              >
                cal left
              </Text>
            </View>
          </View>

          {/* Calorie Ring Card */}
          <View
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...shadowMd,
            }}
          >
            <Text style={{ ...fonts.h4, marginBottom: spacing.lg }}>
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
                    color: colors.primary,
                  }}
                >
                  {caloriesEaten}
                </Text>
                <Text style={fonts.caption}>eaten</Text>
              </View>
              <View style={{ width: 1, backgroundColor: colors.borderLight }} />
              <View style={{ alignItems: "center" }}>
                <Text
                  style={{
                    fontSize: 20,
                    fontWeight: "700",
                    color:
                      caloriesRemaining > 0 ? colors.secondary : colors.danger,
                  }}
                >
                  {caloriesRemaining}
                </Text>
                <Text style={fonts.caption}>remaining</Text>
              </View>
            </View>
          </View>

          {/* Macros Card */}
          <View
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              marginBottom: spacing.lg,
              ...shadow,
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
              <Text style={{ ...fonts.h4, marginBottom: spacing.lg }}>
                Macros
              </Text>

              <Ionicons
                name={macrosExpanded ? "chevron-up" : "chevron-down"}
                onPress={() => {
                  setMacrosExpanded((prev) => !prev);
                }}
                size={20}
                color={colors.secondary}
              />
            </View>
            <MacroBar
              label="Protein"
              value={summary?.totalProteinG ?? 0}
              goal={user?.dailyProteinGoalG ?? 150}
              color={colors.protein}
            />
            <MacroBar
              label="Carbs"
              value={summary?.totalCarbsG ?? 0}
              goal={user?.dailyCarbGoalG ?? 250}
              color={colors.carbs}
            />
            <MacroBar
              label="Fat"
              value={summary?.totalFatG ?? 0}
              goal={user?.dailyFatGoalG ?? 65}
              color={colors.fat}
            />

            {macrosExpanded && (
              <>
                <MacroBar
                  label="Fiber"
                  value={summary?.totalFiberG ?? 0}
                  goal={user?.dailyFiberGoalG ?? 30}
                  color={colors.fiber}
                />
                <View
                  style={{
                    height: 1,
                    backgroundColor: colors.divider,
                    marginVertical: 4,
                  }}
                />
                <MacroBar
                  label="Sugar"
                  value={summary?.totalSugarG ?? 0}
                  goal={50}
                  color={colors.sugar}
                />
                <MacroBar
                  label="Sodium"
                  value={summary?.totalSodiumMg ?? 0}
                  goal={2300}
                  color={colors.sodium}
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
              <Text style={fonts.h3}>Today's Meals</Text>
              <TouchableOpacity onPress={() => router.push("/(tabs)/meals")}>
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.secondary,
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
                    backgroundColor: colors.card,
                    borderRadius: radius.md,
                    padding: spacing.lg,
                    marginBottom: spacing.sm,
                    flexDirection: "row",
                    alignItems: "center",
                    ...shadow,
                  }}
                >
                  <View
                    style={{
                      width: 40,
                      height: 40,
                      borderRadius: radius.sm,
                      backgroundColor: colors.primaryBg,
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
                        color: colors.text,
                      }}
                    >
                      {meal.mealType}
                    </Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.textMuted,
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
                      color: colors.text,
                    }}
                  >
                    {meal.totalCalories}
                  </Text>
                  <Text
                    style={{
                      fontSize: 12,
                      color: colors.textMuted,
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
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  padding: spacing.xxxl,
                  alignItems: "center",
                  ...shadow,
                }}
              >
                <Ionicons
                  name="restaurant-outline"
                  size={36}
                  color={colors.textLight}
                />
                <Text
                  style={{
                    color: colors.textMuted,
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
                    backgroundColor: colors.primaryBg,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                  }}
                >
                  <Text
                    style={{
                      color: colors.primary,
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
              <Text style={fonts.h3}>Today's Symptoms</Text>
              <TouchableOpacity onPress={() => router.push("/(tabs)/symptoms")}>
                <Text
                  style={{
                    fontSize: 13,
                    color: colors.secondary,
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
                    backgroundColor: colors.card,
                    borderRadius: radius.md,
                    padding: 14,
                    marginBottom: spacing.sm,
                    flexDirection: "row",
                    alignItems: "center",
                    ...shadow,
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
                        color: colors.text,
                      }}
                    >
                      {log.symptomName}
                    </Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.textMuted,
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
                          ? colors.primaryBg
                          : log.severity <= 6
                            ? colors.warningBg
                            : colors.dangerBg,
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
                            ? colors.primary
                            : log.severity <= 6
                              ? colors.warning
                              : colors.danger,
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
                  backgroundColor: colors.card,
                  borderRadius: radius.md,
                  padding: spacing.xxxl,
                  alignItems: "center",
                  ...shadow,
                }}
              >
                <Text style={{ fontSize: 32 }}>😊</Text>
                <Text
                  style={{
                    color: colors.primary,
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
                    backgroundColor: colors.primaryBg,
                    paddingHorizontal: 16,
                    paddingVertical: 8,
                    borderRadius: radius.sm,
                  }}
                >
                  <Text
                    style={{
                      color: colors.primary,
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
        </View>
      </ScrollView>

      {/* FAB overlay */}
      {fabOpen && (
        <Pressable style={StyleSheet.absoluteFill} onPress={toggleFab}>
          <Animated.View
            style={[
              StyleSheet.absoluteFill,
              {
                backgroundColor: "#000",
                opacity: fabAnim.interpolate({
                  inputRange: [0, 1],
                  outputRange: [0, 0.3],
                }),
              },
            ]}
          />
        </Pressable>
      )}

      {/* FAB action items */}
      {fabActions.map((action, i) => {
        const translateY = fabAnim.interpolate({
          inputRange: [0, 1],
          outputRange: [0, -((i + 1) * 66)],
        });
        const scale = fabAnim.interpolate({
          inputRange: [0, 0.6, 1],
          outputRange: [0, 0, 1],
        });

        return (
          <Animated.View
            key={action.label}
            pointerEvents={fabOpen ? "auto" : "none"}
            style={{
              position: "absolute",
              bottom: 32,
              right: 28,
              transform: [{ translateY }, { scale }],
              flexDirection: "row",
              alignItems: "center",
            }}
          >
            <View
              style={{
                backgroundColor: colors.card,
                paddingHorizontal: 12,
                paddingVertical: 6,
                borderRadius: radius.sm,
                marginRight: 12,
                ...shadow,
              }}
            >
              <Text
                style={{ fontSize: 13, fontWeight: "600", color: colors.text }}
              >
                {action.label}
              </Text>
            </View>
            <TouchableOpacity
              onPress={() => {
                toggleFab();
                router.push(action.route);
              }}
              style={{
                width: 48,
                height: 48,
                borderRadius: 24,
                backgroundColor: action.color,
                alignItems: "center",
                justifyContent: "center",
                ...shadowMd,
              }}
            >
              <Ionicons name={action.icon} size={22} color="#fff" />
            </TouchableOpacity>
          </Animated.View>
        );
      })}

      {/* Main FAB button */}
      <TouchableOpacity
        activeOpacity={0.85}
        onPress={toggleFab}
        style={{
          position: "absolute",
          bottom: 28,
          right: 24,
          width: 56,
          height: 56,
          borderRadius: 28,
          backgroundColor: colors.primary,
          alignItems: "center",
          justifyContent: "center",
          ...shadowMd,
          elevation: 6,
        }}
      >
        <Animated.View
          style={{
            transform: [
              {
                rotate: fabAnim.interpolate({
                  inputRange: [0, 1],
                  outputRange: ["0deg", "45deg"],
                }),
              },
            ],
          }}
        >
          <Ionicons name="add" size={30} color="#fff" />
        </Animated.View>
      </TouchableOpacity>
    </View>
  );
}
