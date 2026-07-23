import { useCallback, useState } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  RefreshControl,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useQuery } from "@tanstack/react-query";
import { mealApi } from "../../src/api";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { mealSheet } from "../../src/stores/mealSheet";
import { spacing } from "../../src/utils/theme";
import { useThemeColors } from "../../src/stores/theme";
import { SafeScreen } from "../../components/SafeScreen";
import { MealCardSkeleton } from "../../components/SkeletonLoader";
import { MealDateNav } from "../../components/meals/MealDateNav";
import { DailySummary } from "../../components/meals/DailySummary";
import { QuickAddRow } from "../../components/meals/QuickAddRow";
import { SwipeHint } from "../../components/meals/SwipeHint";
import { MealGroup } from "../../components/meals/MealGroup";
import { MealFab } from "../../components/meals/MealFab";
import { useDefaultMealFabActions } from "../../components/meals/useDefaultMealFabActions";
import { MealTemplatesSheet } from "../../components/meals/MealTemplatesSheet";
import { saveMealTemplate } from "../../src/utils/mealTemplates";
import { buildLoggedAt } from "../../src/utils/date";
import { toast } from "../../src/stores/toast";
import type { MealLog, MealTemplate } from "../../src/types";

const MEAL_TYPE_ORDER = ["Breakfast", "Lunch", "Dinner", "Snack"];

export default function MealsScreen() {
  const colors = useThemeColors();
  const [templatesVisible, setTemplatesVisible] = useState(false);
  const selectedDate = useMealSheetStore((s) => s.selectedDate);
  const fabActions = useDefaultMealFabActions();

  const { createMeal, deleteMeal, removeItem } = useMealMutations();

  const {
    data: meals,
    isLoading,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["meals", selectedDate],
    queryFn: () => mealApi.list(selectedDate).then((r) => r.data),
  });

  const {
    data: dailySummary,
    refetch: refetchSummary,
  } = useQuery({
    queryKey: ["daily-summary", selectedDate],
    queryFn: () => mealApi.dailySummary(selectedDate).then((r) => r.data),
  });

  const [refreshing, setRefreshing] = useState(false);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all([refetch(), refetchSummary()]);
    setRefreshing(false);
  }, [refetch, refetchSummary]);

  const handleEdit = (meal: MealLog) => mealSheet.openEdit(meal);
  const handleSaveTemplate = async (meal: MealLog) => {
    await saveMealTemplate(meal);
    toast.success("Saved as meal template");
  };
  const handleUseTemplate = (template: MealTemplate) => {
    createMeal.mutate({
      mealType: template.mealType,
      loggedAt: buildLoggedAt(selectedDate),
      notes: template.notes,
      items: template.items,
    }, { onSuccess: () => setTemplatesVisible(false) });
  };
  const handleCopy = (meal: MealLog) => mealSheet.openCopy(meal);
  const handleDelete = (mealId: string) => deleteMeal.mutate(mealId);
  const handleSwapItem = (meal: MealLog, idx: number) =>
    mealSheet.openSwap(meal, idx, "edit");
  const handleDeleteItem = (meal: MealLog, idx: number) =>
    removeItem(meal, idx);

  // Group meals by type, always show all sections
  const grouped = (() => {
    const map: Record<string, MealLog[]> = {};
    for (const meal of meals ?? []) {
      const key = meal.mealType || "Other";
      (map[key] ??= []).push(meal);
    }
    return MEAL_TYPE_ORDER.map((type) => [type, map[type] ?? []] as [string, MealLog[]]);
  })();

  return (
    <SafeScreen edges={[]}>
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} />
        }
        showsVerticalScrollIndicator={false}
      >
        <View style={{ padding: spacing.xl }}>
          <MealDateNav />

          {dailySummary && <DailySummary summary={dailySummary} />}

          <QuickAddRow />

          <TouchableOpacity
            onPress={() => setTemplatesVisible(true)}
            accessibilityRole="button"
            accessibilityLabel="Open meal templates"
            style={{ flexDirection: "row", alignItems: "center", gap: 6, marginBottom: spacing.md }}
          >
            <Ionicons name="copy-outline" size={17} color={colors.primary} />
            <Text style={{ color: colors.primary, fontWeight: "700", fontSize: 13 }}>Meal templates</Text>
          </TouchableOpacity>
          <SwipeHint />

          {isLoading ? (
            <>
              <MealCardSkeleton />
              <MealCardSkeleton />
              <MealCardSkeleton />
            </>
          ) : isError ? (
            <View style={{ alignItems: "center", marginTop: 40 }}>
              <Ionicons name="cloud-offline-outline" size={48} color={colors.danger} />
              <Text style={{ color: colors.danger, marginTop: spacing.md, fontSize: 16 }}>
                Failed to load meals
              </Text>
              <TouchableOpacity
                onPress={() => refetch()}
                style={{ marginTop: spacing.md, backgroundColor: colors.primary, paddingHorizontal: 20, paddingVertical: 8, borderRadius: 8 }}
              >
                <Text style={{ color: colors.textOnPrimary, fontWeight: "600" }}>Retry</Text>
              </TouchableOpacity>
            </View>
          ) : (
            grouped.map(([type, typeMeals]) => (
              <MealGroup
                key={type}
                type={type}
                onSaveTemplate={handleSaveTemplate}
                meals={typeMeals}
                totalCalories={typeMeals.reduce((sum, m) => sum + m.totalCalories, 0)}
                onEdit={handleEdit}
                onCopy={handleCopy}
                onDelete={handleDelete}
                onSwapItem={handleSwapItem}
                onDeleteItem={handleDeleteItem}
              />
            ))
          )}
        </View>
      </ScrollView>

      <MealFab actions={fabActions} />
      <MealTemplatesSheet
        visible={templatesVisible}
        onClose={() => setTemplatesVisible(false)}
        onUse={handleUseTemplate}
      />

    </SafeScreen>
  );
}
