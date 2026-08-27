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
  useWindowDimensions,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import Animated, { FadeInDown, useReducedMotion } from "react-native-reanimated";
import * as haptics from "../../src/utils/haptics";
import { BottomSheet } from "../BottomSheet";
import { CountUpText } from "../CountUpText";
import { MealTypePicker } from "../MealTypePicker";
import { useThemeColors } from "../../src/stores/theme";
import type { ThemeColors } from "../../src/utils/theme";
import { radius, spacing, sourceChipColors } from "../../src/utils/theme";
import {
  formatServingHint,
  titleCaseFoodName,
} from "../../src/utils/foodDisplay";
import type {
  GroundingCandidate,
  MealScanDraft,
  MealScanItem,
  MealScanConfirmItem,
} from "../../src/types";

interface MealScanReviewSheetProps {
  draft: MealScanDraft | null;
  visible: boolean;
  onClose: () => void;
  onConfirm: (data: {
    mealType: string;
    items: MealScanConfirmItem[];
  }) => Promise<void>;
  isConfirming?: boolean;
}

export function MealScanReviewSheet({
  draft,
  visible,
  onClose,
  onConfirm,
  isConfirming = false,
}: MealScanReviewSheetProps) {
  const reduced = useReducedMotion();
  const c = useThemeColors();
  const { height: windowHeight } = useWindowDimensions();
  const insets = useSafeAreaInsets();
  const sheetHeight = Math.round(Math.max(windowHeight - insets.top, 0) * 0.9);
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
          calories:
            item.calories != null ? Math.round(item.calories * ratio) : null,
          proteinG:
            item.proteinG != null
              ? Math.round(item.proteinG * ratio * 10) / 10
              : null,
          carbsG:
            item.carbsG != null
              ? Math.round(item.carbsG * ratio * 10) / 10
              : null,
          fatG:
            item.fatG != null ? Math.round(item.fatG * ratio * 10) / 10 : null,
          fiberG:
            item.fiberG != null
              ? Math.round(item.fiberG * ratio * 10) / 10
              : null,
          sugarG:
            item.sugarG != null
              ? Math.round(item.sugarG * ratio * 10) / 10
              : null,
          sodiumMg:
            item.sodiumMg != null ? Math.round(item.sodiumMg * ratio) : null,
        };
      }),
    );
  };

  const handleSwapCandidate = async (
    itemId: string,
    candidate: GroundingCandidate,
  ) => {
    setItems((prev) =>
      prev.map((item) => {
        if (item.itemId !== itemId) return item;
        const factor = item.grams / 100;
        return {
          ...item,
          name: candidate.name,
          canonicalName: candidate.name,
          foodProductId: candidate.food_product_id ?? null,
          source: candidate.source,
          matchConfidence: candidate.match_confidence,
          calories:
            candidate.calories_100g == null
              ? null
              : Math.round(candidate.calories_100g * factor),
          proteinG:
            candidate.protein_100g == null
              ? null
              : Math.round(candidate.protein_100g * factor * 10) / 10,
          carbsG:
            candidate.carbs_100g == null
              ? null
              : Math.round(candidate.carbs_100g * factor * 10) / 10,
          fatG:
            candidate.fat_100g == null
              ? null
              : Math.round(candidate.fat_100g * factor * 10) / 10,
          fiberG:
            candidate.fiber_100g == null
              ? null
              : Math.round(candidate.fiber_100g * factor * 10) / 10,
          sugarG:
            candidate.sugar_100g == null
              ? null
              : Math.round(candidate.sugar_100g * factor * 10) / 10,
          sodiumMg:
            candidate.sodium_mg_100g == null
              ? null
              : Math.round(candidate.sodium_mg_100g * factor),
          grounding: item.grounding
            ? {
                ...item.grounding,
                resolution_status: "user_selected",
                selected_food_product_id: candidate.food_product_id ?? null,
                canonical_name: candidate.name,
                method: "user_selection",
                match_confidence: candidate.match_confidence,
              }
            : item.grounding,
        };
      }),
    );

    haptics.light();
  };

  const handleRemoveItem = (itemId: string) => {
    setItems((prev) => prev.filter((i) => i.itemId !== itemId));
    haptics.heavy();
  };

  const handleSave = async () => {
    if (items.length === 0) return;
    const confirmItems: MealScanConfirmItem[] = items.map((i) => ({
      itemId: i.itemId,
      name: titleCaseFoodName(i.canonicalName || i.name),
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
  const isMedConf =
    draft.overallConfidence >= 0.6 && draft.overallConfidence < 0.8;
  return (
    <BottomSheet
      visible={visible}
      onClose={onClose}
      fillHeight
      sheetStyle={{ height: sheetHeight }}
    >
      <ScrollView
        style={{ flex: 1, minHeight: 0 }}
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 24 }}
      >
        {/* Header */}
        <Animated.View
          entering={reduced ? undefined : FadeInDown.delay(0 * 90)}
          style={styles.headerRow}
        >
          <View style={{ flex: 1 }}>
            <Text style={[styles.title, { color: c.text }]}>
              Meal Scan Review
            </Text>
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
              name={
                isHighConf
                  ? "checkmark-circle"
                  : isMedConf
                    ? "alert-circle"
                    : "help-circle"
              }
              size={14}
              color={
                isHighConf ? c.primaryLight : isMedConf ? c.warning : c.danger
              }
            />
            <Text
              style={{
                fontSize: 12,
                fontWeight: "700",
                color: isHighConf
                  ? c.primaryLight
                  : isMedConf
                    ? c.warning
                    : c.danger,
                marginLeft: 4,
              }}
            >
              {confidencePct}% Conf.
            </Text>
          </View>
        </Animated.View>

        {/* Warnings / Scale cues */}
        {draft.warnings && draft.warnings.length > 0 && (
          <Animated.View
            entering={reduced ? undefined : FadeInDown.delay(1 * 90)}
            style={[
              styles.warningBox,
              { backgroundColor: c.warningBg, borderColor: c.warningBorder },
            ]}
          >
            <Ionicons
              name="information-circle-outline"
              size={18}
              color={c.warning}
            />
            <View style={{ flex: 1, marginLeft: 8 }}>
              {draft.warnings.map((w, idx) => (
                <Text
                  key={idx}
                  style={{
                    color: c.textSecondary,
                    fontSize: 12,
                    lineHeight: 16,
                  }}
                >
                  {w}
                </Text>
              ))}
            </View>
          </Animated.View>
        )}

        {/* Items List */}
        <Animated.View
          entering={reduced ? undefined : FadeInDown.delay(2 * 90)}
        >
          <Text style={[styles.sectionHeader, { color: c.text }]}>
            Detected Components
          </Text>


        {items.map((item) => {
          const isGrounded = item.source !== "ai";
          const sourceBadge = getSourceBadge(item.source, c);
          const displayName = titleCaseFoodName(item.name);
          const displayCanonicalName = item.canonicalName
            ? titleCaseFoodName(item.canonicalName)
            : null;
          const candidates: GroundingCandidate[] =
            item.grounding?.candidates && item.grounding.candidates.length > 0
              ? item.grounding.candidates
              : (item.candidateNames ?? []).map((candName) => ({
                  name: candName,
                  food_product_id: null,
                  source: "unknown",
                  match_confidence: 0,
                }));
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
                  <Text style={[styles.itemName, { color: c.text }]}>
                    {displayName}
                  </Text>
                  {item.canonicalName &&
                    item.canonicalName.toLowerCase() !==
                      item.name.toLowerCase() && (
                      <Text
                        style={{
                          fontSize: 12,
                          color: c.textMuted,
                          marginTop: 1,
                        }}
                      >
                        Matched: {displayCanonicalName}
                      </Text>
                    )}
                </View>

                <View
                  style={[
                    styles.sourceChip,
                    {
                      backgroundColor: sourceBadge.bg,
                      borderColor: sourceBadge.border,
                    },
                  ]}
                >
                  <Text
                    style={{
                      fontSize: 10,
                      fontWeight: "700",
                      color: sourceBadge.text,
                    }}
                  >
                    {sourceBadge.label}
                  </Text>
                </View>

                <TouchableOpacity
                  onPress={() => handleRemoveItem(item.itemId)}
                  accessibilityRole="button"
                  accessibilityLabel={`Exclude ${displayName} from meal`}
                  style={{ padding: 4, marginLeft: 8 }}
                >
                  <Ionicons
                    name="close-circle-outline"
                    size={20}
                    color={c.textMuted}
                  />
                </TouchableOpacity>
              </View>

              {item.isGarnish && (
                <View style={{ marginTop: 4 }}>
                  <Text
                    style={{
                      fontSize: 11,
                      color: c.textMuted,
                      fontStyle: "italic",
                    }}
                  >
                    Garnish / seasoning — small amount detected
                  </Text>
                </View>
              )}

              {/* Health Badges if available */}
              {(item.fodmap_status || item.gut_rating) && (
                <View style={styles.healthRow}>
                  {item.fodmap_status && (
                    <View
                      style={[
                        styles.healthBadge,
                        item.fodmap_status === "NoKnownTriggersDetected"
                          ? {
                              backgroundColor: c.primaryBg,
                              borderColor: c.primaryBorder,
                            }
                          : item.fodmap_status === "PotentialTriggersDetected"
                            ? {
                                backgroundColor: c.warningBg,
                                borderColor: c.warningBorder,
                              }
                            : {
                                backgroundColor: c.bg,
                                borderColor: c.border,
                              },
                      ]}
                    >
                      <Text
                        style={{
                          fontSize: 10,
                          fontWeight: "700",
                          color:
                            item.fodmap_status === "NoKnownTriggersDetected"
                              ? c.primaryLight
                              : item.fodmap_status ===
                                  "PotentialTriggersDetected"
                                ? c.warning
                                : c.textMuted,
                        }}
                      >
                        {item.fodmap_status === "NoKnownTriggersDetected"
                          ? "No Known Triggers"
                          : item.fodmap_status === "PotentialTriggersDetected"
                            ? "FODMAP Warning"
                            : "Not Enough Info"}
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
                      <Text
                        style={{
                          fontSize: 10,
                          fontWeight: "600",
                          color: c.textSecondary,
                        }}
                      >
                        Gut: {item.gut_rating}
                      </Text>
                    </View>
                  )}
                </View>
              )}

              {/* Portion controls */}
              <View style={styles.stepperRow}>
                <Text
                  style={{
                    fontSize: 13,
                    fontWeight: "600",
                    color: c.textSecondary,
                  }}
                >
                  Portion:
                </Text>

                <View style={styles.stepperControls}>
                  <TouchableOpacity
                    onPress={() =>
                      handleGramsChange(item.itemId, item.grams - 20)
                    }
                    accessibilityRole="button"
                    accessibilityLabel={`Decrease ${displayName} by 20 grams`}
                    style={[
                      styles.stepBtn,
                      { borderColor: c.border, backgroundColor: c.bg },
                    ]}
                  >
                    <Text
                      style={{ fontSize: 16, fontWeight: "700", color: c.text }}
                    >
                      −
                    </Text>
                  </TouchableOpacity>

                  <PortionGramInput
                    grams={item.grams}
                    onCommit={(grams) => handleGramsChange(item.itemId, grams)}
                    c={c}
                  />

                  <TouchableOpacity
                    onPress={() =>
                      handleGramsChange(item.itemId, item.grams + 20)
                    }
                    accessibilityRole="button"
                    accessibilityLabel={`Increase ${displayName} by 20 grams`}
                    style={[
                      styles.stepBtn,
                      { borderColor: c.border, backgroundColor: c.bg },
                    ]}
                  >
                    <Text
                      style={{ fontSize: 16, fontWeight: "700", color: c.text }}
                    >
                      +
                    </Text>
                  </TouchableOpacity>
                </View>

                <TouchableOpacity
                  onPress={() =>
                    handleGramsChange(item.itemId, item.grams * 0.75)
                  }
                  accessibilityRole="button"
                  accessibilityLabel={`Decrease ${displayName} by 25 percent`}
                  style={[styles.quickNudgeBtn, { borderColor: c.borderLight }]}
                >
                  <Text
                    style={{
                      fontSize: 10,
                      color: c.textMuted,
                      fontWeight: "600",
                    }}
                  >
                    -25%
                  </Text>
                </TouchableOpacity>

                <TouchableOpacity
                  onPress={() =>
                    handleGramsChange(item.itemId, item.grams * 1.25)
                  }
                  accessibilityRole="button"
                  accessibilityLabel={`Increase ${displayName} by 25 percent`}
                  style={[styles.quickNudgeBtn, { borderColor: c.borderLight }]}
                >
                  <Text
                    style={{
                      fontSize: 10,
                      color: c.textMuted,
                      fontWeight: "600",
                    }}
                  >
                    +25%
                  </Text>
                </TouchableOpacity>
              </View>
              {formatServingHint(item) && (
                <Text
                  style={{
                    fontSize: 11,
                    color: c.textMuted,
                    marginTop: 6,
                    fontStyle: "italic",
                  }}
                >
                  {formatServingHint(item)}
                </Text>
              )}

              {item.portionLowGrams != null &&
                item.portionHighGrams != null && (
                  <View style={styles.portionHelperRow}>
                    <TouchableOpacity
                      onPress={() =>
                        handleGramsChange(
                          item.itemId,
                          (item.portionLowGrams! + item.portionHighGrams!) / 2,
                        )
                      }
                      accessibilityRole="button"
                      accessibilityLabel={`Use estimated range midpoint for ${displayName}`}
                      style={[
                        styles.rangeChip,
                        { borderColor: c.borderLight, backgroundColor: c.bg },
                      ]}
                    >
                      <Text
                        style={{
                          fontSize: 11,
                          color: c.textSecondary,
                          fontWeight: "600",
                        }}
                      >
                        Range {Math.round(item.portionLowGrams)}–
                        {Math.round(item.portionHighGrams)} g
                      </Text>
                    </TouchableOpacity>

                    {(() => {
                      const original = draft.items.find(
                        (d) => d.itemId === item.itemId,
                      );
                      if (!original || original.grams === item.grams)
                        return null;
                      return (
                        <TouchableOpacity
                          onPress={() =>
                            handleGramsChange(item.itemId, original.grams)
                          }
                          accessibilityRole="button"
                          accessibilityLabel={`Reset ${displayName} to original estimate`}
                          style={[
                            styles.rangeChip,
                            {
                              borderColor: c.borderLight,
                              backgroundColor: c.bg,
                            },
                          ]}
                        >
                          <Text
                            style={{
                              fontSize: 11,
                              color: c.textMuted,
                              fontWeight: "600",
                            }}
                          >
                            Reset
                          </Text>
                        </TouchableOpacity>
                      );
                    })()}

                    {item.portionConfidence != null && (
                      <Text style={{ fontSize: 11, color: c.textMuted }}>
                        {Math.round(item.portionConfidence * 100)}% confidence
                      </Text>
                    )}
                  </View>
                )}

              {/* Macro Summary Row */}
              <View
                style={[styles.macroRow, { borderTopColor: c.borderLight }]}
              >
                <Text
                  style={{ fontSize: 13, fontWeight: "700", color: c.text }}
                >
                  {item.calories != null
                    ? `${item.calories} kcal`
                    : "Unresolved (select a match)"}
                </Text>
                {item.calories != null && (
                  <Text style={{ fontSize: 12, color: c.textMuted }}>
                    P: {item.proteinG ?? 0}g · C: {item.carbsG ?? 0}g · F:{" "}
                    {item.fatG ?? 0}g
                  </Text>
                )}
              </View>

              {/* Alternative Candidates for quick swap */}
              {candidates.length > 1 && (
                <View style={styles.candidatesRow}>
                  <Text
                    style={{
                      fontSize: 11,
                      color: c.textMuted,
                      marginBottom: 4,
                    }}
                  >
                    Tap to select a match:
                  </Text>
                  <ScrollView horizontal showsHorizontalScrollIndicator={false}>
                    {candidates.map((candidate, cIdx) => (
                      <TouchableOpacity
                        key={
                          candidate.food_product_id ??
                          `${candidate.name}-${cIdx}`
                        }
                        onPress={() =>
                          handleSwapCandidate(item.itemId, candidate)
                        }
                        style={[
                          styles.candidateChip,
                          {
                            backgroundColor:
                              item.foodProductId ===
                                candidate.food_product_id ||
                              item.canonicalName === candidate.name
                                ? c.primaryBg
                                : c.bg,
                            borderColor:
                              item.foodProductId ===
                                candidate.food_product_id ||
                              item.canonicalName === candidate.name
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
                              item.foodProductId ===
                                candidate.food_product_id ||
                              item.canonicalName === candidate.name
                                ? c.primaryLight
                                : c.textSecondary,
                          }}
                        >
                          {titleCaseFoodName(candidate.name)}
                        </Text>
                      </TouchableOpacity>
                    ))}
                  </ScrollView>
                </View>
              )}
            </View>
          );
        })}
        </Animated.View>

        {/* Meal Type Picker */}
        <Animated.View
          entering={reduced ? undefined : FadeInDown.delay(3 * 90)}
          style={{ marginTop: 16 }}
        >
          <Text
            style={[styles.sectionHeader, { color: c.text, marginBottom: 8 }]}
          >
            Log As
          </Text>
          <MealTypePicker selected={mealType} onSelect={setMealType} />
        </Animated.View>

        {/* Total Summary Banner */}
        <Animated.View
          entering={reduced ? undefined : FadeInDown.delay(4 * 90)}
          style={[
            styles.totalsCard,
            { backgroundColor: c.card, borderColor: c.border },
          ]}
        >
          <Text style={{ fontSize: 14, fontWeight: "700", color: c.text }}>
            Total Nutrition
          </Text>
          <View style={styles.totalStatsRow}>
            <View style={{ alignItems: "center" }}>
              <CountUpText
                value={totalCalories}
                style={{
                  fontSize: 18,
                  fontWeight: "800",
                  color: c.primaryLight,
                }}
              />
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
        </Animated.View>

        {/* Actions */}
        <Animated.View
          entering={reduced ? undefined : FadeInDown.delay(5 * 90)}
        >
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
              <ActivityIndicator color={c.textOnPrimary} />
            ) : (
              <Text style={{ color: c.textOnPrimary, fontWeight: "700", fontSize: 16 }}>
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
        </Animated.View>
      </ScrollView>
    </BottomSheet>
  );
}

function getSourceBadge(source: string, _c?: ThemeColors) {
  switch (source.toLowerCase()) {
    case "usda":
      return {
        label: "USDA",
        ...sourceChipColors.usda,
      };
    case "openfoodfacts":
    case "off":
      return {
        label: "OFF",
        ...sourceChipColors.off,
      };
    case "au":
      return {
        label: "AU DB",
        ...sourceChipColors.au,
      };
    case "web":
      return {
        label: "Web ↗",
        ...sourceChipColors.web,
      };
    default:
      return {
        label: "AI Estimate",
        ...sourceChipColors.ai,
      };
  }
}

function PortionGramInput({
  grams,
  onCommit,
  c,
}: {
  grams: number;
  onCommit: (grams: number) => void;
  c: ThemeColors;
}) {
  const [text, setText] = useState(String(grams));
  const [isFocused, setIsFocused] = useState(false);

  useEffect(() => {
    if (!isFocused) setText(String(grams));
  }, [grams, isFocused]);

  const commit = (value: string) => {
    const parsed = Number.parseFloat(value.replace(",", "."));
    if (Number.isFinite(parsed)) {
      onCommit(parsed);
    } else {
      setText(String(grams));
    }
  };

  return (
    <View
      style={[
        styles.gramInputBox,
        {
          borderColor: isFocused ? c.primaryBorder : c.border,
          backgroundColor: c.bg,
        },
      ]}
    >
      <TextInput
        value={text}
        onChangeText={setText}
        onFocus={() => setIsFocused(true)}
        onBlur={() => {
          setIsFocused(false);
          commit(text);
        }}
        onSubmitEditing={() => commit(text)}
        keyboardType="decimal-pad"
        returnKeyType="done"
        selectTextOnFocus
        accessibilityLabel="Portion weight in grams"
        style={{
          fontSize: 14,
          fontWeight: "700",
          color: c.text,
          minWidth: 34,
          textAlign: "center",
        }}
      />
      <Text style={{ fontSize: 12, fontWeight: "600", color: c.textMuted }}>
        g
      </Text>
    </View>
  );
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
  quickNudgeBtn: {
    paddingHorizontal: 6,
    paddingVertical: 4,
    borderRadius: 4,
    borderWidth: 1,
  },
  gramInputBox: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
    borderWidth: 1,
    minWidth: 62,
  },
  portionHelperRow: {
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap",
    gap: 6,
    marginTop: 6,
  },
  rangeChip: {
    paddingHorizontal: 7,
    paddingVertical: 3,
    borderRadius: 6,
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
