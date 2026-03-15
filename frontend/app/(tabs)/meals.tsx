import { useCallback, useState, useRef } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  RefreshControl,
  Animated,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { mealApi } from "../../src/api";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { mealSheet } from "../../src/stores/mealSheet";
import { MEAL_TYPES } from "../../src/utils/constants";
import { radius, spacing } from "../../src/utils/theme";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import { SafeScreen } from "../../components/SafeScreen";
import { MealCardSkeleton } from "../../components/SkeletonLoader";
import { MealDateNav } from "../../components/meals/MealDateNav";
import { MealTypeChips } from "../../components/meals/MealTypeChips";
import { DailySummary } from "../../components/meals/DailySummary";
import { QuickAddRow } from "../../components/meals/QuickAddRow";
import { SwipeHint } from "../../components/meals/SwipeHint";
import { AddMealSheet } from "../../components/meals/AddMealSheet";
import { EditMealSheet } from "../../components/meals/EditMealSheet";
import { CopyMealSheet } from "../../components/meals/CopyMealSheet";
import { ItemSwapSheet } from "../../components/meals/ItemSwapSheet";
import * as haptics from "../../src/utils/haptics";
import type { MealLog } from "../../src/types";
import { MealGroup } from "../../components/meals/MealGroup";

export default function MealsScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow, shadowMd } = useThemeShadow();
  const router = useRouter();
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const selectedMealType = useMealSheetStore((s) => s.selectedMealType);
  const { deleteMeal, removeItem } = useMealMutations();

  /* ---- FAB state ---- */
  const [fabOpen, setFabOpen] = useState(false);
  const fabAnim = useRef(new Animated.Value(0)).current;
  const toggleFab = () => {
    haptics.medium();
    const toValue = fabOpen ? 0 : 1;
    Animated.spring(fabAnim, {
      toValue,
      useNativeDriver: true,
      friction: 6,
    }).start();
    setFabOpen(!fabOpen);
  };
  const closeFab = () => {
    if (!fabOpen) return;
    Animated.spring(fabAnim, {
      toValue: 0,
      useNativeDriver: true,
      friction: 6,
    }).start();
    setFabOpen(false);
  };

  /* ---- Data ---- */
  const {
    data: meals,
    isLoading,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["meals", selectedDate],
    queryFn: () => mealApi.list(selectedDate).then((r) => r.data),
  });

  const { data: dailySummary } = useQuery({
    queryKey: ["daily-summary", selectedDate],
    queryFn: () => mealApi.dailySummary(selectedDate).then((r) => r.data),
  });

  /* ---- Filtering & grouping ---- */
  const [filterType, setFilterType] = useState<string | null>(null);
  const filtered = filterType
    ? (meals ?? []).filter((m) => m.mealType === filterType)
    : meals;

  const grouped = (() => {
    const map: Record<string, MealLog[]> = {};
    for (const m of filtered ?? []) {
      const key = m.mealType || "Other";
      (map[key] ??= []).push(m);
    }
    const order = ["Breakfast", "Lunch", "Dinner", "Snack"];
    return Object.entries(map).sort(
      ([a], [b]) =>
        (order.indexOf(a) === -1 ? 99 : order.indexOf(a)) -
        (order.indexOf(b) === -1 ? 99 : order.indexOf(b)),
    );
  })();

  /* ---- Pull-to-refresh ---- */
  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await refetch();
    setRefreshing(false);
  }, [refetch]);

  /* ---- Callbacks passed to MealGroup ---- */
  const handleEdit = (meal: MealLog) => mealSheet.openEdit(meal);
  const handleCopy = (meal: MealLog) => mealSheet.openCopy(meal);
  const handleDelete = (mealId: string) => deleteMeal.mutate(mealId);
  const handleSwapItem = (meal: MealLog, idx: number) =>
    mealSheet.openSwap(meal, idx, "edit");
  const handleDeleteItem = (meal: MealLog, idx: number) =>
    removeItem(meal, idx);

  /* ---- FAB actions ---- */
  const fabActions = [
    {
      icon: "chatbubble-outline" as const,
      label: "Describe",
      color: colors.primary,
      onPress: () => {
        closeFab();
        mealSheet.openAdd("add-describe");
      },
    },
    {
      icon: "create-outline" as const,
      label: "Manual",
      color: colors.secondary,
      onPress: () => {
        closeFab();
        mealSheet.openAdd("add-manual");
      },
    },
    {
      icon: "search-outline" as const,
      label: "Search",
      color: colors.accent,
      onPress: () => {
        closeFab();
        router.push("/(tabs)/scan");
      },
    },
  ];

  return (
    <SafeScreen edges={[]}>
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
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
          <MealDateNav />
          <MealTypeChips />

          {/* Daily Summary */}
          {dailySummary && <DailySummary summary={dailySummary} />}

          {/* Quick Add: favorites first, then recent foods */}
          <QuickAddRow />

          {/* Filter chips */}
          <View
            style={{ flexDirection: "row", marginBottom: spacing.md, gap: 6 }}
          >
            <TouchableOpacity
              onPress={() => {
                haptics.selection();
                setFilterType(null);
              }}
              accessibilityRole="button"
              accessibilityLabel="Show all meals"
              accessibilityState={{ selected: filterType === null }}
              style={{
                paddingVertical: 10,
                paddingHorizontal: 14,
                borderRadius: radius.full,
                backgroundColor:
                  filterType === null ? colors.primary : colors.borderLight,
                borderWidth: filterType === null ? 0 : 1,
                borderColor: colors.border,
              }}
            >
              <Text
                style={{
                  fontSize: 12,
                  fontWeight: "600",
                  color:
                    filterType === null
                      ? colors.textOnPrimary
                      : colors.textMuted,
                }}
              >
                All
              </Text>
            </TouchableOpacity>
            {MEAL_TYPES.map((t) => (
              <TouchableOpacity
                key={t}
                onPress={() => {
                  haptics.selection();
                  setFilterType(filterType === t ? null : t);
                }}
                accessibilityRole="button"
                accessibilityLabel={`Filter by ${t}`}
                accessibilityState={{ selected: filterType === t }}
                style={{
                  paddingVertical: 10,
                  paddingHorizontal: 14,
                  borderRadius: radius.full,
                  backgroundColor:
                    filterType === t ? colors.primary : colors.borderLight,
                  borderWidth: filterType === t ? 0 : 1,
                  borderColor: colors.border,
                }}
              >
                <Text
                  style={{
                    fontSize: 12,
                    fontWeight: "600",
                    color:
                      filterType === t
                        ? colors.textOnPrimary
                        : colors.textMuted,
                  }}
                >
                  {t}
                </Text>
              </TouchableOpacity>
            ))}
          </View>

          {/* Swipe hint (shown once) */}
          <SwipeHint />

          {/* Meal list */}
          {isLoading ? (
            <>
              <MealCardSkeleton />
              <MealCardSkeleton />
              <MealCardSkeleton />
            </>
          ) : isError ? (
            <View style={{ alignItems: "center", marginTop: 40 }}>
              <Ionicons
                name="cloud-offline-outline"
                size={48}
                color={colors.danger}
              />
              <Text
                style={{
                  color: colors.danger,
                  marginTop: spacing.md,
                  fontSize: 16,
                }}
              >
                Failed to load meals
              </Text>
              <TouchableOpacity
                onPress={() => refetch()}
                accessibilityRole="button"
                accessibilityLabel="Retry loading meals"
                style={{
                  marginTop: spacing.md,
                  backgroundColor: colors.primary,
                  paddingHorizontal: 20,
                  paddingVertical: 8,
                  borderRadius: radius.sm,
                }}
              >
                <Text
                  style={{ color: colors.textOnPrimary, fontWeight: "600" }}
                >
                  Retry
                </Text>
              </TouchableOpacity>
            </View>
          ) : grouped.length > 0 ? (
            grouped.map(([type, mealsInGroup]) => (
              <MealGroup
                key={type}
                type={type}
                meals={mealsInGroup}
                totalCalories={mealsInGroup.reduce(
                  (s, m) => s + m.totalCalories,
                  0,
                )}
                onEdit={handleEdit}
                onCopy={handleCopy}
                onDelete={handleDelete}
                onSwapItem={handleSwapItem}
                onDeleteItem={handleDeleteItem}
              />
            ))
          ) : (
            <View style={{ alignItems: "center", paddingVertical: 48 }}>
              <Ionicons
                name="restaurant-outline"
                size={48}
                color={colors.textLight}
              />
              <Text
                style={{
                  color: colors.textMuted,
                  marginTop: spacing.md,
                  fontSize: 15,
                }}
              >
                {filterType
                  ? "No meals found"
                  : "No meals logged for this date"}
              </Text>
              <Text
                style={{
                  color: colors.textLight,
                  marginTop: spacing.xs,
                  fontSize: 13,
                }}
              >
                Tap + to log your first meal
              </Text>
            </View>
          )}
        </View>
      </ScrollView>

      {/* FAB overlay backdrop */}
      {fabOpen && (
        <TouchableOpacity
          activeOpacity={1}
          onPress={closeFab}
          accessibilityLabel="Close menu"
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: colors.overlay,
          }}
        />
      )}

      {/* FAB actions */}
      {fabActions.map((action, i) => {
        const offset = (fabActions.length - i) * 64;
        return (
          <Animated.View
            key={action.label}
            style={{
              position: "absolute",
              bottom: 28,
              right: 24,
              opacity: fabAnim,
              transform: [
                {
                  translateY: fabAnim.interpolate({
                    inputRange: [0, 1],
                    outputRange: [0, -offset],
                  }),
                },
                {
                  scale: fabAnim.interpolate({
                    inputRange: [0, 1],
                    outputRange: [0.4, 1],
                  }),
                },
              ],
            }}
            pointerEvents={fabOpen ? "auto" : "none"}
          >
            <View
              style={{ flexDirection: "row", alignItems: "center", gap: 8 }}
            >
              <View
                style={{
                  backgroundColor: colors.card,
                  paddingHorizontal: 10,
                  paddingVertical: 4,
                  borderRadius: radius.sm,
                  ...shadow,
                }}
              >
                <Text
                  style={{
                    fontSize: 12,
                    fontWeight: "600",
                    color: colors.text,
                  }}
                >
                  {action.label}
                </Text>
              </View>
              <TouchableOpacity
                onPress={() => {
                  haptics.medium();
                  action.onPress();
                }}
                accessibilityRole="button"
                accessibilityLabel={action.label}
                activeOpacity={0.85}
                style={{
                  width: 44,
                  height: 44,
                  borderRadius: 22,
                  backgroundColor: action.color,
                  alignItems: "center",
                  justifyContent: "center",
                  ...shadowMd,
                }}
              >
                <Ionicons
                  name={action.icon}
                  size={22}
                  color={colors.textOnPrimary}
                />
              </TouchableOpacity>
            </View>
          </Animated.View>
        );
      })}

      {/* Main FAB */}
      <TouchableOpacity
        activeOpacity={0.85}
        onPress={toggleFab}
        accessibilityRole="button"
        accessibilityLabel={fabOpen ? "Close meal options" : "Add meal"}
        accessibilityState={{ expanded: fabOpen }}
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
          <Ionicons name="add" size={30} color={colors.textOnPrimary} />
        </Animated.View>
      </TouchableOpacity>

      {/* Bottom sheets */}
      <AddMealSheet />
      <EditMealSheet />
      <CopyMealSheet />
      <ItemSwapSheet />
    </SafeScreen>
  );
}
