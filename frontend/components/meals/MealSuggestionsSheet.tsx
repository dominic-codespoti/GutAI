import React, { useEffect, useState } from "react";
import {
  ActivityIndicator,
  ScrollView,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import axios from "axios";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Ionicons } from "@expo/vector-icons";
import { mealDraftApi, mealSuggestionApi } from "../../src/api";
import type { MealDraft, MealSuggestion } from "../../src/types";
import {
  defaultSuggestionMealType,
  mealSuggestionCardViewModel,
  siblingSuggestionDraftIds,
  type SuggestionMealType,
} from "../../src/utils/mealSuggestions";
import { useThemeColors } from "../../src/stores/theme";
import { radius, spacing } from "../../src/utils/theme";
import { BottomSheet } from "../BottomSheet";
import { MealTypePicker } from "../MealTypePicker";
import { MealDraftReviewSheet } from "./MealDraftReviewSheet";


export function MealSuggestionsSheet({
  visible,
  onClose,
}: {
  visible: boolean;
  onClose: () => void;
}) {
  const colors = useThemeColors();
  const queryClient = useQueryClient();
  const [mealType, setMealType] = useState<SuggestionMealType>(() => defaultSuggestionMealType(new Date().getHours()));
  const [preferences, setPreferences] = useState("");
  const [suggestions, setSuggestions] = useState<MealSuggestion[] | null>(null);
  const [selectedDraft, setSelectedDraft] = useState<MealDraft | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (visible) {
      setMealType(defaultSuggestionMealType(new Date().getHours()));
      setPreferences("");
      setSuggestions(null);
      setErrorMessage(null);
    }
  }, [visible]);

  const request = useMutation({
    mutationFn: async () => mealSuggestionApi.suggest({
      mealType,
      ...(preferences.trim() ? { preferences: preferences.trim() } : {}),
    }).then(({ data }) => data),
    onMutate: () => {
      setSuggestions(null);
      setErrorMessage(null);
    },
    onSuccess: (result) => {
      setSuggestions(result.suggestions.slice(0, 3));
      setErrorMessage(null);
    },
    onError: (error: unknown) => {
      const status = axios.isAxiosError(error) ? error.response?.status : undefined;
      const serverMessage = axios.isAxiosError(error)
        ? (error.response?.data as { error?: string } | undefined)?.error
        : undefined;
      setSuggestions(null);
      setErrorMessage(status === 400 && serverMessage
        ? serverMessage
        : status === 404 || status === 503
          ? "Suggestions aren't available right now"
          : "Suggestions aren't available right now");
    },
  });

  const openDraft = (draft: MealDraft) => {
    setSelectedDraft(draft);
  };

  const handleCommitted = async (_draft: MealDraft) => {
    const siblings = siblingSuggestionDraftIds(suggestions ?? [], _draft.draftId);
    await Promise.allSettled(siblings.map((draftId) => mealDraftApi.discard(draftId)));
    await queryClient.invalidateQueries({ queryKey: ["meal-drafts"] });
    setSuggestions(null);
    setSelectedDraft(null);
    onClose();
  };

  return (
    <>
      <BottomSheet visible={visible && selectedDraft === null} onClose={onClose} fillHeight>
        <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: spacing.xl }}>
          <View style={{ flexDirection: "row", alignItems: "center", marginBottom: spacing.lg }}>
            <View style={{ flex: 1 }}>
              <Text style={{ fontSize: 21, fontWeight: "800", color: colors.text }}>What should I eat?</Text>
              <Text style={{ color: colors.textMuted, fontSize: 13, marginTop: 3 }}>Grounded ideas based on your remaining nutrition budget.</Text>
            </View>
            <TouchableOpacity onPress={onClose} accessibilityRole="button" accessibilityLabel="Close meal suggestions" style={{ padding: 8 }}>
              <Ionicons name="close" size={22} color={colors.textMuted} />
            </TouchableOpacity>
          </View>

          <Text style={{ color: colors.text, fontSize: 14, fontWeight: "700", marginBottom: spacing.sm }}>Meal type</Text>
          <MealTypePicker selected={mealType} onSelect={(value) => setMealType(value as SuggestionMealType)} />
          <Text style={{ color: colors.text, fontSize: 14, fontWeight: "700", marginBottom: spacing.sm }}>Preferences (optional)</Text>
          <TextInput
            value={preferences}
            onChangeText={(value) => setPreferences(value.slice(0, 200))}
            maxLength={200}
            placeholder="Anything you'd like or want to avoid?"
            placeholderTextColor={colors.textLight}
            accessibilityLabel="Meal preferences, optional, up to 200 characters"
            multiline
            style={{ minHeight: 76, borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, padding: spacing.md, color: colors.text, backgroundColor: colors.bg, textAlignVertical: "top" }}
          />
          <Text style={{ color: colors.textMuted, fontSize: 11, textAlign: "right", marginTop: 4 }}>{preferences.length}/200</Text>

          <TouchableOpacity
            accessibilityRole="button"
            onPress={() => request.mutate()}
            disabled={request.isPending}
            style={{ backgroundColor: colors.primary, borderRadius: radius.md, paddingVertical: 13, alignItems: "center", marginTop: spacing.md, marginBottom: spacing.lg, opacity: request.isPending ? 0.6 : 1 }}
          >
            {request.isPending ? <ActivityIndicator color={colors.textOnPrimary} /> : <Text style={{ color: colors.textOnPrimary, fontSize: 15, fontWeight: "700" }}>Suggest a meal</Text>}
          </TouchableOpacity>

          {errorMessage ? <Text accessibilityRole="alert" style={{ color: colors.danger, fontSize: 14, marginBottom: spacing.md }}>{errorMessage}</Text> : null}
          {suggestions?.length === 0 && <Text style={{ color: colors.textMuted, textAlign: "center", paddingVertical: spacing.xl }}>No suggestions fit your remaining budget right now</Text>}
          {suggestions?.map((suggestion) => {
            const card = mealSuggestionCardViewModel(suggestion);
            return (
              <TouchableOpacity
                key={card.draftId}
                onPress={() => openDraft(suggestion.draft)}
                accessibilityRole="button"
                accessibilityLabel={`Review ${card.title}, ${Math.round(card.calories)} kilocalories`}
                style={{ backgroundColor: colors.card, borderColor: colors.border, borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.md }}
              >
                <View style={{ flexDirection: "row", alignItems: "flex-start", justifyContent: "space-between", gap: spacing.sm }}>
                  <Text style={{ color: colors.text, fontSize: 16, fontWeight: "700", flex: 1 }}>{card.title}</Text>
                  <Ionicons name="chevron-forward" size={18} color={colors.textMuted} />
                </View>
                <Text style={{ color: colors.textSecondary, fontSize: 13, lineHeight: 18, marginTop: 5 }}>{card.rationale}</Text>
                <Text style={{ color: colors.primary, fontSize: 13, fontWeight: "700", marginTop: spacing.sm }}>
                  {Math.round(card.calories)} kcal · {card.proteinG} P · {card.carbsG} C · {card.fatG} F
                </Text>
                <Text style={{ color: colors.textMuted, fontSize: 12, lineHeight: 17, marginTop: 5 }}>
                  {card.items.map(({ name, grams }) => `${name} · ${grams} g`).join("\n")}
                </Text>
              </TouchableOpacity>
            );
          })}
          {suggestions && suggestions.length > 0 && (
            <Text style={{ color: colors.textMuted, fontSize: 11, textAlign: "center", marginTop: spacing.sm }}>Nutrition totals are from the server.</Text>
          )}
        </ScrollView>
      </BottomSheet>
      <MealDraftReviewSheet
        draft={selectedDraft}
        visible={selectedDraft !== null}
        onClose={() => setSelectedDraft(null)}
        onCommitted={(_result, draft) => { void handleCommitted(draft); }}
        onDiscarded={() => setSelectedDraft(null)}
      />
    </>
  );
}
