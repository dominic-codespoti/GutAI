import React, { useState, useEffect } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
  Platform,
  StyleSheet,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import * as Haptics from "expo-haptics";
import { BottomSheet } from "../BottomSheet";
import { MealTypePicker } from "../MealTypePicker";
import { useThemeColors } from "../../src/stores/theme";
import { radius, spacing } from "../../src/utils/theme";
import type { MealScanDraft, MealScanItem, MealScanConfirmItem } from "../../src/types";

interface MealScanReviewSheetProps {
  draft: MealScanDraft | null;
  visible: boolean;
  onClose: () => void;
  onConfirm: (data: { mealType: string; items: MealScanConfirmItem[] }) => Promise<void>;
  isConfirming?: boolean;
}

export function MealScanReviewSheet({
  draft,
  visible,
  onClose,
  onConfirm,
  isConfirming = false,
}: MealScanReviewSheetProps) {
  const c = useThemeColors();
  const [mealType, setMealType] = useState<string>("Lunch");
  const [items, setItems] = useState<MealScanItem[]>([]);

  useEffect(() => {
    if (draft?.items) {
      setItems(draft.items);
    }
  }, [draft]);

  if (!draft) return null;

  const handleGramsChange = (itemId: string, newGrams: number) => {
    const clamped = Math.max(5, Math.min(3000, Math.round(newGrams)));
    setItems((prev) =>
      prev.map((item) => {
        if (item.itemId !== itemId) return item;
        const oldGrams = item.grams || 100;
        const ratio = clamped / (oldGrams || 1);

        // Scale calories & macros proportionally if present
        return {
          ...item,
          grams: clamped,
          calories: item.calories != null ? Math.round(item.calories * ratio) : null,
          proteinG: item.proteinG != null ? Math.round(item.proteinG * ratio * 10) / 10 : null,
          carbsG: item.carbsG != null ? Math.round(item.carbsG * ratio * 10) / 10 : null,
          fatG: item.fatG != null ? Math.round(item.fatG * ratio * 10) / 10 : null,
          fiberG: item.fiberG != null ? Math.round(item.fiberG * ratio * 10) / 10 : null,
          sugarG: item.sugarG != null ? Math.round(item.sugarG * ratio * 10) / 10 : null,
          sodiumMg: item.sodiumMg != null ? Math.round(item.sodiumMg * ratio) : null,
        };
      })
    );
  };

  const handleSwapCandidate = (itemId: string, candidateName: string) => {
    setItems((prev) =>
      prev.map((item) => {
        if (item.itemId !== itemId) return item;
        return {
          ...item,
          name: candidateName,
          canonicalName: candidateName,
          source: "user-selected",
        };
      })
    );
    if (Platform.OS !== "web") {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    }
  };

  const handleRemoveItem = (itemId: string) => {
    setItems((prev) => prev.filter((i) => i.itemId !== itemId));
    if (Platform.OS !== "web") {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    }
  };

  const handleSave = async () => {
    if (items.length === 0) return;
    const confirmItems: MealScanConfirmItem[] = items.map((i) => ({
      itemId: i.itemId,
      name: i.canonicalName || i.name,
      grams: i.grams,
      foodProductId: i.foodProductId,
      source: i.source,
      sourceUrl: i.sourceUrl,
      matchConfidence: i.matchConfidence,
      visionConfidence: i.visionConfidence,
      calories: i.calories,
      proteinG: i.proteinG,
      carbsG: i.carbsG,
      fatG: i.fatG,
      fiberG: i.fiberG,
      sugarG: i.sugarG,
      sodiumMg: i.sodiumMg,
    }));

    await onConfirm({ mealType, items: confirmItems });
  };

  // Totals
  const totalCalories = items.reduce((sum, i) => sum + (i.calories || 0), 0);
  const totalProtein = items.reduce((sum, i) => sum + (i.proteinG || 0), 0);
  const totalCarbs = items.reduce((sum, i) => sum + (i.carbsG || 0), 0);
  const totalFat = items.reduce((sum, i) => sum + (i.fatG || 0), 0);

  const confidencePct = Math.round(draft.overallConfidence * 100);
  const isHighConf = draft.overallConfidence >= 0.8;
  const isMedConf = draft.overallConfidence >= 0.6 && draft.overallConfidence < 0.8;

  return (
    <BottomSheet visible={visible} onClose={onClose} maxHeight="90%">
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 24 }}
      >
        {/* Header */}
        <View style={styles.headerRow}>
          <View style={{ flex: 1 }}>
            <Text style={[styles.title, { color: c.text }]}>Meal Scan Review</Text>
            <Text style={{ fontSize: 13, color: c.textMuted, marginTop: 2 }}>
              Review portions and matches before saving.
            </Text>
          </View>

          {/* Confidence Badge */}
          <View
            style={[
              styles.confidenceBadge,
              {
                backgroundColor: isHighConf
                  ? c.primaryBg
                  : isMedConf
                  ? c.warningBg
                  : c.dangerBg,
                borderColor: isHighConf
                  ? c.primaryBorder
                  : isMedConf
                  ? c.warningBorder
                  : c.dangerBorder,
              },
            ]}
          >
            <Ionicons
              name={isHighConf ? "checkmark-circle" : isMedConf ? "alert-circle" : "help-circle"}
              size={14}
              color={isHighConf ? c.primaryLight : isMedConf ? c.warning : c.danger}
            />
            <Text
              style={{
                fontSize: 12,
                fontWeight: "700",
                color: isHighConf ? c.primaryLight : isMedConf ? c.warning : c.danger,
                marginLeft: 4,
              }}
            >
              {confidencePct}% Conf.
            </Text>
          </View>
        </View>

        {/* Warnings / Scale cues */}
        {draft.warnings && draft.warnings.length > 0 && (
          <View
            style={[
              styles.warningBox,
              { backgroundColor: c.warningBg, borderColor: c.warningBorder },
            ]}
          >
            <Ionicons name="information-circle-outline" size={18} color={c.warning} />
            <View style={{ flex: 1, marginLeft: 8 }}>
              {draft.warnings.map((w, idx) => (
                <Text key={idx} style={{ color: c.textSecondary, fontSize: 12, lineHeight: 16 }}>
                  {w}
                </Text>
              ))}
            </View>
          </View>
        )}

        {/* Items List */}
        <Text style={[styles.sectionHeader, { color: c.text }]}>Detected Components</Text>

        {items.map((item) => {
          const isGrounded = item.source !== "ai";
          const sourceBadge = getSourceBadge(item.source, c);

          return (
            <View
              key={item.itemId}
              style={[
                styles.itemCard,
                {
                  backgroundColor: c.card,
                  borderColor: c.border,
                },
              ]}
            >
              {/* Top Row: Name + Source Chip + Delete */}
              <View style={styles.itemTopRow}>
                <View style={{ flex: 1 }}>
                  <Text style={[styles.itemName, { color: c.text }]}>{item.name}</Text>
                  {item.canonicalName && item.canonicalName.toLowerCase() !== item.name.toLowerCase() && (
                    <Text style={{ fontSize: 12, color: c.textMuted, marginTop: 1 }}>
                      Matched: {item.canonicalName}
                    </Text>
                  )}
                </View>

                <View
                  style={[
                    styles.sourceChip,
                    { backgroundColor: sourceBadge.bg, borderColor: sourceBadge.border },
                  ]}
                >
                  <Text style={{ fontSize: 10, fontWeight: "700", color: sourceBadge.text }}>
                    {sourceBadge.label}
                  </Text>
                </View>

                <TouchableOpacity
                  onPress={() => handleRemoveItem(item.itemId)}
                  style={{ padding: 4, marginLeft: 8 }}
                >
                  <Ionicons name="trash-outline" size={18} color={c.textMuted} />
                </TouchableOpacity>
              </View>

              {/* Health Badges if available */}
              {(item.fodmap_status || item.gut_rating) && (
                <View style={styles.healthRow}>
                  {item.fodmap_status && (
                    <View
                      style={[
                        styles.healthBadge,
                        {
                          backgroundColor:
                            item.fodmap_status === "LowFodmap" ? c.primaryBg : c.warningBg,
                          borderColor:
                            item.fodmap_status === "LowFodmap"
                              ? c.primaryBorder
                              : c.warningBorder,
                        },
                      ]}
                    >
                      <Text
                        style={{
                          fontSize: 10,
                          fontWeight: "700",
                          color:
                            item.fodmap_status === "LowFodmap" ? c.primaryLight : c.warning,
                        }}
                      >
                        {item.fodmap_status === "LowFodmap" ? "Low FODMAP" : "FODMAP Warning"}
                      </Text>
                    </View>
                  )}
                  {item.gut_rating && (
                    <View
                      style={[
                        styles.healthBadge,
                        { backgroundColor: c.bg, borderColor: c.border },
                      ]}
                    >
                      <Text style={{ fontSize: 10, fontWeight: "600", color: c.textSecondary }}>
                        Gut: {item.gut_rating}
                      </Text>
                    </View>
                  )}
                </View>
              )}

              {/* Gram Stepper & Quick Adjustment */}
              <View style={styles.stepperRow}>
                <Text style={{ fontSize: 13, fontWeight: "600", color: c.textSecondary }}>
                  Portion:
                </Text>

                <View style={styles.stepperControls}>
                  <TouchableOpacity
                    onPress={() => handleGramsChange(item.itemId, item.grams - 20)}
                    style={[styles.stepBtn, { borderColor: c.border, backgroundColor: c.bg }]}
                  >
                    <Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>−</Text>
                  </TouchableOpacity>

                  <View style={[styles.gramInputBox, { borderColor: c.border }]}>
                    <Text style={{ fontSize: 14, fontWeight: "700", color: c.text }}>
                      {item.grams} g
                    </Text>
                  </View>

                  <TouchableOpacity
                    onPress={() => handleGramsChange(item.itemId, item.grams + 20)}
                    style={[styles.stepBtn, { borderColor: c.border, backgroundColor: c.bg }]}
                  >
                    <Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>+</Text>
                  </TouchableOpacity>
                </View>

                {/* Quick ±25% buttons */}
                <TouchableOpacity
                  onPress={() => handleGramsChange(item.itemId, item.grams * 0.75)}
                  style={[styles.quickNudgeBtn, { borderColor: c.borderLight }]}
                >
                  <Text style={{ fontSize: 10, color: c.textMuted, fontWeight: "600" }}>-25%</Text>
                </TouchableOpacity>

                <TouchableOpacity
                  onPress={() => handleGramsChange(item.itemId, item.grams * 1.25)}
                  style={[styles.quickNudgeBtn, { borderColor: c.borderLight }]}
                >
                  <Text style={{ fontSize: 10, color: c.textMuted, fontWeight: "600" }}>+25%</Text>
                </TouchableOpacity>
              </View>

              {/* Macro Summary Row */}
              <View style={[styles.macroRow, { borderTopColor: c.borderLight }]}>
                <Text style={{ fontSize: 13, fontWeight: "700", color: c.text }}>
                  {item.calories != null ? `${item.calories} kcal` : "Unresolved (tap candidate)"}
                </Text>
                {item.calories != null && (
                  <Text style={{ fontSize: 12, color: c.textMuted }}>
                    P: {item.proteinG ?? 0}g · C: {item.carbsG ?? 0}g · F: {item.fatG ?? 0}g
                  </Text>
                )}
              </View>

              {/* Alternative Candidates for quick swap */}
              {item.candidateNames && item.candidateNames.length > 1 && (
                <View style={styles.candidatesRow}>
                  <Text style={{ fontSize: 11, color: c.textMuted, marginBottom: 4 }}>
                    Tap to swap match:
                  </Text>
                  <ScrollView horizontal showsHorizontalScrollIndicator={false}>
                    {item.candidateNames.map((cand, cIdx) => (
                      <TouchableOpacity
                        key={cIdx}
                        onPress={() => handleSwapCandidate(item.itemId, cand)}
                        style={[
                          styles.candidateChip,
                          {
                            backgroundColor:
                              item.canonicalName === cand || item.name === cand
                                ? c.primaryBg
                                : c.bg,
                            borderColor:
                              item.canonicalName === cand || item.name === cand
                                ? c.primaryBorder
                                : c.border,
                          },
                        ]}
                      >
                        <Text
                          style={{
                            fontSize: 11,
                            fontWeight: "600",
                            color:
                              item.canonicalName === cand || item.name === cand
                                ? c.primaryLight
                                : c.textSecondary,
                          }}
                        >
                          {cand}
                        </Text>
                      </TouchableOpacity>
                    ))}
                  </ScrollView>
                </View>
              )}
            </View>
          );
        })}

        {/* Meal Type Picker */}
        <View style={{ marginTop: 16 }}>
          <Text style={[styles.sectionHeader, { color: c.text, marginBottom: 8 }]}>
            Log As
          </Text>
          <MealTypePicker selected={mealType} onSelect={setMealType} />
        </View>

        {/* Total Summary Banner */}
        <View style={[styles.totalsCard, { backgroundColor: c.card, borderColor: c.border }]}>
          <Text style={{ fontSize: 14, fontWeight: "700", color: c.text }}>Total Nutrition</Text>
          <View style={styles.totalStatsRow}>
            <View style={{ alignItems: "center" }}>
              <Text style={{ fontSize: 18, fontWeight: "800", color: c.primaryLight }}>
                {totalCalories}
              </Text>
              <Text style={{ fontSize: 11, color: c.textMuted }}>Calories</Text>
            </View>
            <View style={{ alignItems: "center" }}>
              <Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>
                {Math.round(totalProtein * 10) / 10}g
              </Text>
              <Text style={{ fontSize: 11, color: c.textMuted }}>Protein</Text>
            </View>
            <View style={{ alignItems: "center" }}>
              <Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>
                {Math.round(totalCarbs * 10) / 10}g
              </Text>
              <Text style={{ fontSize: 11, color: c.textMuted }}>Carbs</Text>
            </View>
            <View style={{ alignItems: "center" }}>
              <Text style={{ fontSize: 16, fontWeight: "700", color: c.text }}>
                {Math.round(totalFat * 10) / 10}g
              </Text>
              <Text style={{ fontSize: 11, color: c.textMuted }}>Fat</Text>
            </View>
          </View>
        </View>

        {/* Actions */}
        <TouchableOpacity
          onPress={handleSave}
          disabled={isConfirming || items.length === 0}
          style={[
            styles.saveBtn,
            {
              backgroundColor: c.primary,
              opacity: isConfirming || items.length === 0 ? 0.5 : 1,
            },
          ]}
        >
          {isConfirming ? (
            <ActivityIndicator color="#fff" />
          ) : (
            <Text style={{ color: "#fff", fontWeight: "700", fontSize: 16 }}>
              Save & Log Meal
            </Text>
          )}
        </TouchableOpacity>

        <TouchableOpacity
          onPress={onClose}
          disabled={isConfirming}
          style={{ alignItems: "center", paddingVertical: 12, marginTop: 4 }}
        >
          <Text style={{ color: c.textMuted, fontSize: 14, fontWeight: "600" }}>
            Discard Scan
          </Text>
        </TouchableOpacity>
      </ScrollView>
    </BottomSheet>
  );
}

function getSourceBadge(source: string, c: ReturnType<typeof useThemeColors>) {
  switch (source.toLowerCase()) {
    case "usda":
      return { label: "USDA", bg: c.primaryBg, border: c.primaryBorder, text: c.primaryLight };
    case "openfoodfacts":
    case "off":
      return { label: "OFF", bg: c.primaryBg, border: c.primaryBorder, text: c.primaryLight };
    case "au":
      return { label: "AU DB", bg: c.primaryBg, border: c.primaryBorder, text: c.primaryLight };
    case "web":
      return { label: "Web ↗", bg: "#EDE9FE", border: "#DDD6FE", text: "#6D28D9" };
    default:
      return { label: "AI Estimate", bg: c.warningBg, border: c.warningBorder, text: c.warning };
  }
}

const styles = StyleSheet.create({
  headerRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    marginBottom: 12,
  },
  title: {
    fontSize: 20,
    fontWeight: "700",
  },
  confidenceBadge: {
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 8,
    borderWidth: 1,
  },
  warningBox: {
    flexDirection: "row",
    alignItems: "flex-start",
    padding: 10,
    borderRadius: 8,
    borderWidth: 1,
    marginBottom: 16,
  },
  sectionHeader: {
    fontSize: 15,
    fontWeight: "700",
    marginBottom: 8,
  },
  itemCard: {
    borderRadius: radius.md,
    borderWidth: 1,
    padding: spacing.md,
    marginBottom: 10,
  },
  itemTopRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  itemName: {
    fontSize: 15,
    fontWeight: "700",
  },
  sourceChip: {
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 6,
    borderWidth: 1,
  },
  healthRow: {
    flexDirection: "row",
    gap: 6,
    marginTop: 6,
  },
  healthBadge: {
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 4,
    borderWidth: 1,
  },
  stepperRow: {
    flexDirection: "row",
    alignItems: "center",
    marginTop: 10,
    gap: 8,
  },
  stepperControls: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
  },
  stepBtn: {
    width: 28,
    height: 28,
    borderRadius: 6,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  gramInputBox: {
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
    borderWidth: 1,
    minWidth: 55,
    alignItems: "center",
  },
  quickNudgeBtn: {
    paddingHorizontal: 6,
    paddingVertical: 4,
    borderRadius: 4,
    borderWidth: 1,
  },
  macroRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    borderTopWidth: 1,
    paddingTop: 8,
    marginTop: 10,
  },
  candidatesRow: {
    marginTop: 8,
  },
  candidateChip: {
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
    borderWidth: 1,
    marginRight: 6,
  },
  totalsCard: {
    borderRadius: radius.md,
    borderWidth: 1,
    padding: spacing.md,
    marginTop: 16,
    marginBottom: 16,
  },
  totalStatsRow: {
    flexDirection: "row",
    justifyContent: "space-around",
    marginTop: 10,
  },
  saveBtn: {
    paddingVertical: 14,
    borderRadius: radius.md,
    alignItems: "center",
    justifyContent: "center",
  },
});
