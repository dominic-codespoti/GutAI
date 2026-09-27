import React, { useEffect, useMemo, useState } from "react";
import {
  ActivityIndicator,
  Linking,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  TextInput,
  TouchableOpacity,
  View,
  useWindowDimensions,
} from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ionicons } from "@expo/vector-icons";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import Animated, { FadeInDown, useReducedMotion } from "react-native-reanimated";
import { foodApi, mealDraftApi } from "../../src/api";
import type {
  FoodProduct,
  GroundingCandidate,
  MealDraft,
  MealDraftCommitRequest,
  MealDraftCommitResult,
  NutritionPer100g,
} from "../../src/types";
import {
  buildCommitRequest,
  canSave,
  candidateBasis,
  draftTotals,
  effectiveBasis,
  initialRows,
  rowNeedsChoice,
  rowNutrition,
  type DraftRowState,
} from "../../src/utils/mealDraft";
import { formatServingHint, titleCaseFoodName } from "../../src/utils/foodDisplay";
import { useThemeColors } from "../../src/stores/theme";
import { useAuthStore } from "../../src/stores/auth";
import { toast } from "../../src/stores/toast";
import * as haptics from "../../src/utils/haptics";
import { BottomSheet } from "../BottomSheet";
import { CountUpText } from "../CountUpText";
import { MealTypePicker } from "../MealTypePicker";
import { FoodSearchResult } from "../FoodSearchResult";
import type { ThemeColors } from "../../src/utils/theme";
import { radius, spacing, sourceChipColors } from "../../src/utils/theme";

export function MealDraftReviewSheet(props: {
  draft: MealDraft | null;
  visible: boolean;
  onClose: () => void;
  onCommitted?: (result: MealDraftCommitResult, draft: MealDraft, request: MealDraftCommitRequest) => void;
  onDiscarded?: (draft: MealDraft) => void;
}) {
  const { draft, visible, onClose, onCommitted, onDiscarded } = props;
  const reduced = useReducedMotion();
  const preferredFoodRegion = useAuthStore((state) => state.user?.preferredFoodRegion ?? "Default");
  const searchRegion =
    preferredFoodRegion === "Au" ? "AU" : preferredFoodRegion === "Us" ? "US" : undefined;
  const c = useThemeColors();
  const insets = useSafeAreaInsets();
  const { height } = useWindowDimensions();
  const queryClient = useQueryClient();
  const [rows, setRows] = useState<DraftRowState[]>([]);
  const [mealType, setMealType] = useState("Lunch");
  const [searchItemId, setSearchItemId] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);
  const [unresolvedItemIds, setUnresolvedItemIds] = useState<string[]>([]);
  const [discarding, setDiscarding] = useState(false);
  const sheetHeight = Math.round(Math.max(height - insets.top, 0) * 0.9);

  useEffect(() => {
    if (!draft) {
      setRows([]);
      return;
    }
    setRows(initialRows(draft));
    setMealType(draft.mealType || currentMealType());
    setSearchItemId(null);
    setUnresolvedItemIds([]);
    setSearch("");
    setServerError(null);
  }, [draft]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 350);
    return () => clearTimeout(timer);
  }, [search]);

  const searchQuery = useQuery({
    queryKey: ["meal-draft-food-search", debouncedSearch, searchRegion],
    queryFn: ({ signal }) => foodApi.search(debouncedSearch, signal, searchRegion).then((r) => r.data),
    enabled: searchItemId !== null && debouncedSearch.trim().length >= 2,
    retry: false,
    staleTime: 5 * 60 * 1000,
  });

  const commitMutation = useMutation({
    mutationFn: ({ id, request }: { id: string; request: MealDraftCommitRequest; draft: MealDraft }) => mealDraftApi.commit(id, request),
    onSuccess: async ({ data }, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["meals"] }),
        queryClient.invalidateQueries({ queryKey: ["daily-summary"] }),
        queryClient.invalidateQueries({ queryKey: ["recent-foods"] }),
        queryClient.invalidateQueries({ queryKey: ["streak"] }),
        queryClient.invalidateQueries({ queryKey: ["meal-drafts"] }),
      ]);
      toast.success("Meal logged!");
      haptics.success();
      onCommitted?.(data, variables.draft, variables.request);
    },
    onError: (error: unknown) => {
      const { status, message, itemIds } = errorDetails(error, "Could not log meal.");
      if (status === 422) {
        setServerError(message);
        setUnresolvedItemIds(itemIds ?? []);
      } else toast.error(message);
    },
  });

  const totals = useMemo(() => draftTotals(rows), [rows]);
  const saveAllowed = canSave(rows);

  if (!draft) return null;

  const updateRow = (itemId: string, update: Partial<DraftRowState>) => {
    setRows((previous) => previous.map((row) => row.item.itemId === itemId ? { ...row, ...update } : row));
    if ("selectedCandidateKey" in update || "replacement" in update || "logWithoutCalories" in update || update.included === false) {
      setUnresolvedItemIds((previous) => previous.filter((id) => id !== itemId));
    }
  };
  const setGrams = (itemId: string, value: number) => {
    const grams = Math.max(0, Math.min(5000, Math.round(value * 10) / 10));
    updateRow(itemId, { grams });
  };
  const removeRow = (itemId: string) => updateRow(itemId, { included: false });
  const chooseCandidate = (itemId: string, key: string) => {
    updateRow(itemId, { selectedCandidateKey: key, replacement: null, logWithoutCalories: false });
    haptics.light();
  };
  const selectFood = (food: FoodProduct) => {
    if (!food.id || food.id === "00000000-0000-0000-0000-000000000000") {
      toast.info("This result can't be linked right now — try another");
      return;
    }
    const basis = foodBasis(food);
    if (!basis) {
      toast.info("This result has no nutrition basis — try another");
      return;
    }
    if (searchItemId) {
      updateRow(searchItemId, {
        replacement: { foodProductId: food.id, name: food.name, basis },
        selectedCandidateKey: null,
        logWithoutCalories: false,
      });
    }
    setSearchItemId(null);
    setSearch("");
    haptics.light();
  };
  const save = () => {
    if (!canSave(rows) || commitMutation.isPending) return;
    const request = buildCommitRequest(rows, mealType, draft.loggedAt);
    setServerError(null);
    commitMutation.mutate({ id: draft.draftId, request, draft });
  };
  const discard = async () => {
    if (discarding) return;
    setDiscarding(true);
    try {
      await mealDraftApi.discard(draft.draftId);
      await queryClient.invalidateQueries({ queryKey: ["meal-drafts"] });
      onDiscarded?.(draft);
      onClose();
    } catch (error: unknown) {
      toast.error(errorDetails(error, "Could not discard draft.").message);
    } finally {
      setDiscarding(false);
    }
  };

  const confidencePct = Math.round(draft.overallConfidence * 100);
  const isHighConf = draft.overallConfidence >= 0.8;
  const isMedConf = draft.overallConfidence >= 0.6 && draft.overallConfidence < 0.8;

  return (
    <BottomSheet visible={visible} onClose={onClose} fillHeight sheetStyle={{ height: sheetHeight }}>
      <ScrollView style={{ flex: 1, minHeight: 0 }} showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 24 }} keyboardShouldPersistTaps="handled">
        <Animated.View entering={reduced ? undefined : FadeInDown} style={styles.headerRow}>
          <View style={{ flex: 1 }}>
            <Text style={[styles.title, { color: c.text }]}>Review meal</Text>
            <Text style={{ fontSize: 13, color: c.textMuted, marginTop: 2 }}>Review portions and matches before saving.</Text>
          </View>
          <View style={[styles.confidenceBadge, { backgroundColor: isHighConf ? c.primaryBg : isMedConf ? c.warningBg : c.dangerBg, borderColor: isHighConf ? c.primaryBorder : isMedConf ? c.warningBorder : c.dangerBorder }]}>
            <Ionicons name={isHighConf ? "checkmark-circle" : isMedConf ? "alert-circle" : "help-circle"} size={14} color={isHighConf ? c.primaryLight : isMedConf ? c.warning : c.danger} />
            <Text style={{ fontSize: 12, fontWeight: "700", color: isHighConf ? c.primaryLight : isMedConf ? c.warning : c.danger, marginLeft: 4 }}>{confidencePct}% Conf.</Text>
          </View>
        </Animated.View>

        {!!draft.warnings?.length && <View style={[styles.warningBox, { backgroundColor: c.warningBg, borderColor: c.warningBorder }]}>
          <Ionicons name="information-circle-outline" size={18} color={c.warning} />
          <View style={{ flex: 1, marginLeft: 8 }}>{draft.warnings.map((warning, index) => <Text key={`${index}-${warning}`} style={{ color: c.textSecondary, fontSize: 12, lineHeight: 16 }}>{warning}</Text>)}</View>
        </View>}

        <Text style={[styles.sectionHeader, { color: c.text }]}>Meal items</Text>
        {rows.map((row) => {
          const item = row.item;
          const selected = item.grounding?.candidates.find((candidate) => candidate.candidate_key === row.selectedCandidateKey);
          const displayName = row.replacement?.name ?? selected?.name ?? ((item.foodProductId || item.per100g || item.grounding?.auto_selected) ? item.canonicalName || item.name : item.name);
          const basis = effectiveBasis(row);
          const nutrition = rowNutrition(row);
          const candidates = (item.grounding?.candidates ?? []).filter((candidate) => candidate.candidate_key && candidateBasis(candidate));
          const previewCandidateKey = item.grounding?.candidates[0]?.candidate_key;
          const badge = provenanceBadge(row.replacement || selected ? "Sourced" : item.nutritionProvenance, row.replacement ? "db" : selected?.source ?? item.source, selected?.source_url ?? item.sourceUrl, !basis, c);
          const unresolved = unresolvedItemIds.includes(item.itemId);
          const isNeedsChoice = item.needsChoice === true;
          return <View key={item.itemId} style={[styles.itemCard, { backgroundColor: c.card, borderColor: unresolved ? c.dangerBorder : c.border, opacity: row.included ? 1 : 0.65 }]}>
            <View style={styles.itemTopRow}>
              {item.isInferred ? <Switch accessibilityLabel={`Likely added: ${displayName}`} value={row.included} onValueChange={(included) => updateRow(item.itemId, { included })} /> : null}
              <View style={{ flex: 1 }}>
                <Text style={[styles.itemName, { color: c.text }]}>{item.isInferred ? `Likely added: ${titleCaseFoodName(displayName)}` : titleCaseFoodName(displayName)}</Text>
                {!!item.canonicalName && item.canonicalName.toLowerCase() !== item.name.toLowerCase() && !selected && !row.replacement && <Text style={{ fontSize: 12, color: c.textMuted, marginTop: 1 }}>Matched: {titleCaseFoodName(item.canonicalName)}</Text>}
              </View>
              <TouchableOpacity onPress={() => badge.link ? void Linking.openURL(badge.link).catch(() => toast.error("Could not open source link.")) : undefined} disabled={!badge.link} style={[styles.sourceChip, { backgroundColor: badge.bg, borderColor: badge.border }]}>
                <Text style={{ fontSize: 10, fontWeight: "700", color: badge.text }}>{badge.label}</Text>
              </TouchableOpacity>
              <TouchableOpacity onPress={() => removeRow(item.itemId)} accessibilityRole="button" accessibilityLabel={`Remove ${displayName} from meal`} style={{ padding: 4, marginLeft: 6 }}><Ionicons name="close-circle-outline" size={20} color={c.textMuted} /></TouchableOpacity>
            </View>
            {item.isGarnish && <Text style={{ fontSize: 11, color: c.textMuted, fontStyle: "italic", marginTop: 4 }}>Garnish / seasoning — small amount detected</Text>}
            {(item.fodmap_status || item.gut_rating) && <View style={styles.healthRow}>
              {item.fodmap_status && <View style={[styles.healthBadge, item.fodmap_status === "NoKnownTriggersDetected" ? { backgroundColor: c.primaryBg, borderColor: c.primaryBorder } : item.fodmap_status === "PotentialTriggersDetected" ? { backgroundColor: c.warningBg, borderColor: c.warningBorder } : { backgroundColor: c.bg, borderColor: c.border }]}><Text style={{ fontSize: 10, fontWeight: "700", color: item.fodmap_status === "NoKnownTriggersDetected" ? c.primaryLight : item.fodmap_status === "PotentialTriggersDetected" ? c.warning : c.textMuted }}>{item.fodmap_status === "NoKnownTriggersDetected" ? "No Known Triggers" : item.fodmap_status === "PotentialTriggersDetected" ? "FODMAP Warning" : "Not Enough Info"}</Text></View>}
              {item.gut_rating && <View style={[styles.healthBadge, { backgroundColor: c.bg, borderColor: c.border }]}><Text style={{ fontSize: 10, fontWeight: "600", color: c.textSecondary }}>Gut: {item.gut_rating}</Text></View>}
            </View>}
            <View style={styles.stepperRow}>
              <Text style={{ fontSize: 13, fontWeight: "600", color: c.textSecondary }}>Portion:</Text>
              <TouchableOpacity onPress={() => setGrams(item.itemId, row.grams - 20)} accessibilityRole="button" accessibilityLabel={`Decrease ${displayName} by 20 grams`} style={[styles.stepBtn, { borderColor: c.border, backgroundColor: c.bg }]}><Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>−</Text></TouchableOpacity>
              <PortionGramInput grams={row.grams} onCommit={(value) => setGrams(item.itemId, value)} c={c} />
              <TouchableOpacity onPress={() => setGrams(item.itemId, row.grams + 20)} accessibilityRole="button" accessibilityLabel={`Increase ${displayName} by 20 grams`} style={[styles.stepBtn, { borderColor: c.border, backgroundColor: c.bg }]}><Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>+</Text></TouchableOpacity>
              <TouchableOpacity onPress={() => setGrams(item.itemId, row.grams * 0.75)} style={[styles.quickNudgeBtn, { borderColor: c.borderLight }]}><Text style={{ fontSize: 10, color: c.textMuted, fontWeight: "600" }}>-25%</Text></TouchableOpacity>
              <TouchableOpacity onPress={() => setGrams(item.itemId, row.grams * 1.25)} style={[styles.quickNudgeBtn, { borderColor: c.borderLight }]}><Text style={{ fontSize: 10, color: c.textMuted, fontWeight: "600" }}>+25%</Text></TouchableOpacity>
            </View>
            {formatServingHint(item) && <Text style={{ fontSize: 11, color: c.textMuted, marginTop: 6, fontStyle: "italic" }}>{formatServingHint(item)}</Text>}
            {item.portionLowGrams != null && item.portionHighGrams != null && <View style={styles.portionHelperRow}>
              <TouchableOpacity onPress={() => setGrams(item.itemId, (item.portionLowGrams! + item.portionHighGrams!) / 2)} style={[styles.rangeChip, { borderColor: c.borderLight, backgroundColor: c.bg }]}><Text style={{ fontSize: 11, color: c.textSecondary, fontWeight: "600" }}>Range {Math.round(item.portionLowGrams)}–{Math.round(item.portionHighGrams)} g · reset midpoint</Text></TouchableOpacity>
              {item.portionConfidence != null && <Text style={{ fontSize: 11, color: c.textMuted }}>{Math.round(item.portionConfidence * 100)}% confidence</Text>}
            </View>}
            <View style={[styles.macroRow, { borderTopColor: c.borderLight }]}>
              <Text style={{ fontSize: 13, fontWeight: "700", color: c.text }}>{nutrition ? `${nutrition.calories} kcal · ${nutrition.proteinG} P · ${nutrition.carbsG} C · ${nutrition.fatG} F` : "Needs a match"}</Text>
              <TouchableOpacity onPress={() => { setSearchItemId(item.itemId); setSearch(item.name); setDebouncedSearch(item.name); }}><Text style={{ color: c.primaryLight, fontWeight: "700", fontSize: 12 }}>Search</Text></TouchableOpacity>
            </View>
            {!basis && <View style={styles.noNutritionRow}>
              <Text style={{ flex: 1, fontSize: 12, color: c.textMuted }}>Log this item without calories</Text>
              <Switch accessibilityLabel={`Log ${displayName} without calories`} value={row.logWithoutCalories === true} onValueChange={(value) => updateRow(item.itemId, { logWithoutCalories: value })} />
            </View>}
            {candidates.length > 0 && <View style={styles.candidatesRow}>
              <Text style={{ fontSize: 11, color: c.textMuted, marginBottom: 4 }}>{isNeedsChoice ? "Pick the right match:" : "Tap to select a match:"}</Text>
              <ScrollView horizontal showsHorizontalScrollIndicator={false}>{candidates.map((candidate, candidateIndex) => {
                const isPreview = draft.origin === "nlp" && isNeedsChoice && !!item.per100g && candidate.candidate_key === previewCandidateKey;
                const label = isPreview ? `Keep: ${titleCaseFoodName(candidate.name)}` : undefined;
                return <CandidateChip key={candidate.candidate_key ?? `${candidate.name}-${candidateIndex}`} candidate={candidate} label={label} active={candidate.candidate_key === row.selectedCandidateKey} c={c} onPress={() => chooseCandidate(item.itemId, candidate.candidate_key!)} />;
              })}</ScrollView>
            </View>}
            {searchItemId === item.itemId && <View style={styles.searchBox}>
              <TextInput value={search} onChangeText={setSearch} placeholder="Search foods..." placeholderTextColor={c.textLight} autoCapitalize="none" autoCorrect={false} returnKeyType="search" style={[styles.searchInput, { color: c.text, borderColor: c.border, backgroundColor: c.bg }]} />
              {searchQuery.isLoading && <ActivityIndicator color={c.primary} style={{ marginVertical: spacing.sm }} />}
              {searchQuery.isError && <Text style={{ color: c.textMuted, fontSize: 12 }}>Search unavailable. Check your connection and try again.</Text>}
              {searchQuery.data?.map((food) => <FoodSearchResult key={food.id || food.name} product={food} onPress={() => selectFood(food)} onDetailPress={() => undefined} style={{ marginTop: spacing.sm }} />)}
              {debouncedSearch.trim().length < 2 && <Text style={{ color: c.textMuted, fontSize: 12, paddingVertical: spacing.sm }}>Type at least 2 characters to search</Text>}
            </View>}
          </View>;
        })}

        <View style={{ marginTop: 16 }}><Text style={[styles.sectionHeader, { color: c.text, marginBottom: 8 }]}>Log As</Text><MealTypePicker selected={mealType} onSelect={setMealType} /></View>
        <View style={[styles.totalsCard, { backgroundColor: c.card, borderColor: c.border }]}>
          <Text style={{ fontSize: 14, fontWeight: "700", color: c.text }}>Total Nutrition</Text>
          <View style={styles.totalStatsRow}>
            <Total value={totals.calories} label="Calories" c={c} counted />
            <Total value={totals.proteinG} label="Protein" c={c} />
            <Total value={totals.carbsG} label="Carbs" c={c} />
            <Total value={totals.fatG} label="Fat" c={c} />
          </View>
          {totals.itemsWithoutNutrition > 0 && <Text style={{ color: c.warning, fontSize: 12, fontWeight: "700", textAlign: "center", marginTop: 8 }}>+{totals.itemsWithoutNutrition} without calories</Text>}
        </View>
        {serverError && <View style={[styles.warningBox, { backgroundColor: c.dangerBg, borderColor: c.dangerBorder }]}><Ionicons name="alert-circle-outline" size={18} color={c.danger} /><Text style={{ color: c.danger, fontSize: 13, flex: 1, marginLeft: 8 }}>{serverError}</Text></View>}
        {!saveAllowed && <Text style={{ color: c.textMuted, fontSize: 12, textAlign: "center", marginBottom: 8 }}>{rows.some(rowNeedsChoice) ? "Pick the right match or choose “Log without calories” before saving." : rows.some((row) => row.included) ? "Match each included item or choose “Log without calories” before saving." : "Include at least one item to save this meal."}</Text>}
        <TouchableOpacity onPress={save} disabled={!saveAllowed || commitMutation.isPending || discarding} style={[styles.saveBtn, { backgroundColor: c.primary, opacity: !saveAllowed || commitMutation.isPending || discarding ? 0.5 : 1 }]}>
          {commitMutation.isPending ? <ActivityIndicator color={c.textOnPrimary} /> : <Text style={{ color: c.textOnPrimary, fontWeight: "700", fontSize: 16 }}>Save & Log Meal</Text>}
        </TouchableOpacity>
        <TouchableOpacity onPress={() => void discard()} disabled={discarding || commitMutation.isPending} style={{ alignItems: "center", paddingVertical: 12, marginTop: 4 }}>
          {discarding ? <ActivityIndicator color={c.textMuted} /> : <Text style={{ color: c.textMuted, fontSize: 14, fontWeight: "600" }}>Discard draft</Text>}
        </TouchableOpacity>
      </ScrollView>
    </BottomSheet>
  );
}

function currentMealType(): string {
  const hour = new Date().getHours();
  if (hour < 11) return "Breakfast";
  if (hour < 15) return "Lunch";
  if (hour < 21) return "Dinner";
  return "Snack";
}

function foodBasis(food: FoodProduct): NutritionPer100g | null {
  if (food.calories100g == null) return null;
  return {
    caloriesKcal: food.calories100g,
    proteinG: food.protein100g ?? 0,
    carbsG: food.carbs100g ?? 0,
    fatG: food.fat100g ?? 0,
    fiberG: food.fiber100g,
    sugarG: food.sugar100g,
    sodiumMg: food.sodiumMg100g,
  };
}

function provenanceBadge(provenance: string | undefined, source: string, sourceUrl: string | null | undefined, noNutrition: boolean, c: ThemeColors) {
  const value = (provenance ?? "").toLowerCase();
  const origin = source.toLowerCase();
  let label: string;
  let colors: { bg: string; border: string; text: string };
  if (noNutrition) {
    label = "No nutrition";
    colors = { bg: c.bg, border: c.border, text: c.textMuted };
  } else if (value === "web" || origin === "web") {
    label = "Web ↗";
    colors = sourceChipColors.web;
  } else if (value === "estimated" || origin === "estimate") {
    label = "Estimate";
    colors = sourceChipColors.ai;
  } else if (value === "modelestimated" || origin === "ai") {
    label = "AI";
    colors = sourceChipColors.ai;
  } else if (origin === "usda") {
    label = "USDA";
    colors = sourceChipColors.usda;
  } else if (origin === "off" || origin === "openfoodfacts") {
    label = "OFF";
    colors = sourceChipColors.off;
  } else if (origin === "au") {
    label = "AU";
    colors = sourceChipColors.au;
  } else if (origin === "db" || origin === "database") {
    label = "DB";
    colors = { bg: c.primaryBg, border: c.primaryBorder, text: c.primaryLight };
  } else {
    label = "AI";
    colors = sourceChipColors.ai;
  }
  return { label, ...colors, link: label === "Web ↗" ? sourceUrl : undefined };
}

function errorDetails(error: unknown, fallback: string): { status?: number; message: string; itemIds?: string[] } {
  if (!error || typeof error !== "object") return { message: fallback };
  const value = error as {
    message?: unknown;
    response?: { status?: unknown; data?: { error?: unknown; itemIds?: unknown } };
  };
  return {
    status: typeof value.response?.status === "number" ? value.response.status : undefined,
    message:
      (typeof value.response?.data?.error === "string" && value.response.data.error) ||
      (typeof value.message === "string" && value.message) ||
      fallback,
    itemIds: Array.isArray(value.response?.data?.itemIds)
      ? value.response.data.itemIds.filter((id): id is string => typeof id === "string")
      : undefined,
  };
}

function CandidateChip({ candidate, label, active, c, onPress }: { candidate: GroundingCandidate; label?: string; active: boolean; c: ThemeColors; onPress: () => void }) {
  return <TouchableOpacity onPress={onPress} accessibilityRole="button" accessibilityLabel={label ?? `Select ${candidate.name}`} style={[styles.candidateChip, { backgroundColor: active ? c.primaryBg : c.bg, borderColor: active ? c.primaryBorder : c.border }]}><Text style={{ fontSize: 11, fontWeight: "600", color: active ? c.primaryLight : c.textSecondary }}>{label ?? titleCaseFoodName(candidate.name)}</Text></TouchableOpacity>;
}

function Total({ value, label, c, counted = false }: { value: number; label: string; c: ThemeColors; counted?: boolean }) {
  return <View style={{ alignItems: "center" }}>{counted ? <CountUpText value={value} style={{ fontSize: 18, fontWeight: "800", color: c.primaryLight }} /> : <Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>{Math.round(value * 10) / 10}g</Text>}<Text style={{ fontSize: 11, color: c.textMuted }}>{label}</Text></View>;
}

function PortionGramInput({ grams, onCommit, c }: { grams: number; onCommit: (grams: number) => void; c: ThemeColors }) {
  const [text, setText] = useState(String(grams));
  const [focused, setFocused] = useState(false);
  // Leaving the field shows the committed (clamped, rounded) grams, which also discards invalid text.
  useEffect(() => { if (!focused) setText(String(grams)); }, [grams, focused]);
  return <View style={[styles.gramInputBox, { borderColor: focused ? c.primaryBorder : c.border, backgroundColor: c.bg }]}>
    {/* Commit each valid keystroke: the sheet's ScrollView uses keyboardShouldPersistTaps="handled", so on
        native, tapping Save does not blur this input, and a blur-only commit would save stale grams. */}
    <TextInput value={text} onChangeText={(value) => { setText(value); const parsed = Number.parseFloat(value.replace(",", ".")); if (Number.isFinite(parsed)) onCommit(parsed); }} onFocus={() => setFocused(true)} onBlur={() => setFocused(false)} keyboardType="decimal-pad" returnKeyType="done" selectTextOnFocus accessibilityLabel="Portion weight in grams" style={{ fontSize: 14, fontWeight: "700", color: c.text, minWidth: 34, textAlign: "center" }} />
    <Text style={{ fontSize: 12, fontWeight: "600", color: c.textMuted }}>g</Text>
  </View>;
}

const styles = StyleSheet.create({
  headerRow: { flexDirection: "row", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 12 },
  title: { fontSize: 20, fontWeight: "700" },
  confidenceBadge: { flexDirection: "row", alignItems: "center", paddingHorizontal: 8, paddingVertical: 4, borderRadius: 8, borderWidth: 1 },
  warningBox: { flexDirection: "row", alignItems: "flex-start", padding: 10, borderRadius: 8, borderWidth: 1, marginBottom: 16 },
  sectionHeader: { fontSize: 15, fontWeight: "700", marginBottom: 8 },
  itemCard: { borderRadius: radius.md, borderWidth: 1, padding: spacing.md, marginBottom: 10 },
  itemTopRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 6 },
  itemName: { fontSize: 15, fontWeight: "700" },
  sourceChip: { paddingHorizontal: 6, paddingVertical: 2, borderRadius: 6, borderWidth: 1 },
  healthRow: { flexDirection: "row", gap: 6, marginTop: 6 },
  healthBadge: { paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4, borderWidth: 1 },
  stepperRow: { flexDirection: "row", alignItems: "center", marginTop: 10, gap: 6, flexWrap: "wrap" },
  stepBtn: { width: 28, height: 28, borderRadius: 6, borderWidth: 1, alignItems: "center", justifyContent: "center" },
  quickNudgeBtn: { paddingHorizontal: 6, paddingVertical: 4, borderRadius: 4, borderWidth: 1 },
  gramInputBox: { flexDirection: "row", alignItems: "center", gap: 2, paddingHorizontal: 8, paddingVertical: 4, borderRadius: 6, borderWidth: 1, minWidth: 62 },
  portionHelperRow: { flexDirection: "row", alignItems: "center", flexWrap: "wrap", gap: 6, marginTop: 6 },
  rangeChip: { paddingHorizontal: 7, paddingVertical: 3, borderRadius: 6, borderWidth: 1 },
  macroRow: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", borderTopWidth: 1, paddingTop: 8, marginTop: 10 },
  noNutritionRow: { flexDirection: "row", alignItems: "center", marginTop: 4 },
  candidatesRow: { marginTop: 8 },
  candidateChip: { paddingHorizontal: 8, paddingVertical: 4, borderRadius: 6, borderWidth: 1, marginRight: 6 },
  searchBox: { marginTop: 10, padding: spacing.sm, borderRadius: radius.sm, borderWidth: 1, borderColor: "transparent" },
  searchHeader: { flexDirection: "row", alignItems: "center", marginBottom: spacing.sm },
  searchInput: { borderWidth: 1, borderRadius: radius.sm, padding: spacing.sm, fontSize: 14 },
  totalsCard: { borderRadius: radius.md, borderWidth: 1, padding: spacing.md, marginTop: 16, marginBottom: 16 },
  totalStatsRow: { flexDirection: "row", justifyContent: "space-around", marginTop: 10 },
  saveBtn: { paddingVertical: 14, borderRadius: radius.md, alignItems: "center", justifyContent: "center" },
});
