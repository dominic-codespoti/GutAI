import { useState } from "react";
import {
  View,
  Text,
  TextInput,
  ScrollView,
  TouchableOpacity,
  ActivityIndicator,
  Image,
  StyleSheet,
} from "react-native";
import { useRouter } from "expo-router";
import * as ImagePicker from "expo-image-picker";
import { Ionicons } from "@expo/vector-icons";
import { SafeScreen } from "../../components/SafeScreen";
import { useThemeColors } from "../../src/stores/theme";
import { foodApi, mealApi } from "../../src/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "../../src/stores/toast";
import { useSubscriptionStore, presentPaywall } from "../../src/stores/subscription";
import { scaleNutrition } from "../../src/utils/nutrition";
import { maybeRequestReview } from "../../src/utils/review";
import {
  normalizeCustomFood,
  customFoodToMealItem,
  ROUND,
} from "../../src/utils/customFood";
import type { AiGeneratedFood } from "../../src/utils/customFood";
import type { CustomFood } from "../../src/types";

type CreateMethod = "manual" | "description" | "label";
type LabelSource = "camera" | "library";

const MIN_DESCRIBE_LENGTH = 8;
const NUM = (v: number | null | undefined) => (v == null ? "" : v.toString());

function confidenceLevel(score: number | null): "High" | "Medium" | "Low" | null {
  if (score == null || Number.isNaN(score)) return null;
  if (score >= 0.85) return "High";
  if (score >= 0.6) return "Medium";
  return "Low";
}

function confidenceColors(level: string | null, c: ReturnType<typeof useThemeColors>) {
  switch (level) {
    case "High":
      return { bg: c.primaryBg, border: c.primaryBorder, text: c.primaryLight, icon: "checkmark-circle" as const };
    case "Medium":
      return { bg: c.warningBg, border: c.warningBorder, text: c.warning, icon: "alert-circle" as const };
    default:
      return { bg: c.dangerBg, border: c.dangerBorder, text: c.danger, icon: "help-circle" as const };
  }
}

function sourceLabel(source: "description" | "label" | null) {
  switch (source) {
    case "description": return "your description";
    case "label": return "a label image";
    default: return "AI";
  }
}

export default function CreateCustomFoodScreen() {
  const c = useThemeColors();
  const router = useRouter();
  const qc = useQueryClient();
  const { isPro } = useSubscriptionStore();

  // ── Form state ──
  const [form, setForm] = useState<CustomFood>({
    name: "", brandName: "", servingSize: 100, servingSizeUnit: "g",
    calories: 0, proteinG: 0, carbG: 0, fatG: 0,
    fiberG: 0, sugarG: 0, sodiumMg: 0, ingredients: "",
  });

  // ── UI state ──
  const [method, setMethod] = useState<CreateMethod>("manual");
  const [generating, setGenerating] = useState(false);
  const [generatedBy, setGeneratedBy] = useState<"description" | "label" | null>(null);
  const [confidenceScore, setConfidenceScore] = useState<number | null>(null);
  const confLevel = confidenceLevel(confidenceScore);
  const confColors = confidenceColors(confLevel, c);

  // ── Description state ──
  const [descText, setDescText] = useState("");

  // ── Label state ──
  const [labelImageUri, setLabelImageUri] = useState<string | null>(null);
  const [labelSource, setLabelSource] = useState<LabelSource | null>(null);

  // ── Mutations ──
  const parseLabel = useMutation({
    mutationFn: (uri: string) => foodApi.parseLabel(uri, "image/jpeg").then((r) => r.data),
    onSuccess: (data: AiGeneratedFood) => {
      setForm(normalizeCustomFood(data));
      setConfidenceScore(data.extractionConfidence ?? null);
      setGeneratedBy("label");
      setGenerating(false);
    },
    onError: () => {
      setGenerating(false);
      toast.error("Could not analyze that image. Try a clearer photo.");
    },
  });

  const describeFood = useMutation({
    mutationFn: (text: string) => foodApi.describeFood(text).then((r) => r.data),
    onSuccess: (data: AiGeneratedFood, sub) => {
      if (sub !== descText.trim()) return;
      setForm(normalizeCustomFood(data));
      setConfidenceScore(data.extractionConfidence ?? null);
      setGeneratedBy("description");
      setGenerating(false);
    },
    onError: () => {
      setGenerating(false);
      toast.error("Could not generate food details from that description.");
    },
  });

  const saveFood = useMutation({
    mutationFn: () => foodApi.createCustomFood(form),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["custom-foods"] });
      toast.success("Food saved!");
      router.navigate("/(tabs)/scan?tab=my-foods");
    },
    onError: () => toast.error("Failed to save custom food."),
  });

  const saveAndLogFood = useMutation({
    mutationFn: async () => {
      const saved = await foodApi.createCustomFood(form);
      const customFood = saved.data as any;
      const scaled = scaleNutrition(
        { calories100g: form.calories, protein100g: form.proteinG, carbs100g: form.carbG, fat100g: form.fatG } as any,
        form.servingSize,
      );
      await mealApi.create({
        mealType: "Snack",
        loggedAt: new Date().toISOString(),
        items: [{
          foodName: form.name,
          foodProductId: customFood.id,
          servings: 1,
          servingUnit: `${form.servingSize}${form.servingSizeUnit}`,
          servingWeightG: form.servingSize,
          ...scaled,
        }],
      });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["meals"] });
      qc.invalidateQueries({ queryKey: ["daily-summary"] });
      qc.invalidateQueries({ queryKey: ["custom-foods"] });
      toast.success("Saved & logged!");
      maybeRequestReview();
      router.back();
    },
    onError: () => toast.error("Failed to save and log."),
  });

  const setField = (field: keyof CustomFood, value: string) => {
    const nums = ["servingSize", "calories", "proteinG", "carbG", "fatG", "fiberG", "sugarG", "sodiumMg"];
    if (nums.includes(field)) {
      const n = parseFloat(value);
      setForm((p) => ({ ...p, [field]: isNaN(n) ? 0 : n }));
    } else {
      setForm((p) => ({ ...p, [field]: value }));
    }
  };

  const isFormValid = form.name.trim().length > 0;

  // ── Description handlers ──
  const handleGenerateDescription = async () => {
    if (!isPro) { const ok = await presentPaywall(); if (!ok) return; }
    const trimmed = descText.trim();
    if (trimmed.length < MIN_DESCRIBE_LENGTH) {
      toast.error(`Type at least ${MIN_DESCRIBE_LENGTH} characters first.`);
      return;
    }
    if (generating) return;
    setGenerating(true);
    describeFood.mutate(trimmed);
  };

  // ── Label handlers ──
  const handleLabelFromCamera = async () => {
    if (!isPro) { const ok = await presentPaywall(); if (!ok) return; }
    const { status } = await ImagePicker.requestCameraPermissionsAsync();
    if (status !== "granted") { toast.error("Camera permission required."); return; }
    const result = await ImagePicker.launchCameraAsync({
      mediaTypes: ImagePicker.MediaTypeOptions.Images,
      allowsEditing: true,
      quality: 0.8,
    });
    if (result.canceled || !result.assets[0]) return;
    const uri = result.assets[0].uri;
    setLabelImageUri(uri);
    setLabelSource("camera");
    setGenerating(true);
    setGeneratedBy(null);
    parseLabel.mutate(uri);
  };

  const handleLabelFromLibrary = async () => {
    if (!isPro) { const ok = await presentPaywall(); if (!ok) return; }
    const { status } = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (status !== "granted") { toast.error("Photo library permission required."); return; }
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ImagePicker.MediaTypeOptions.Images,
      allowsEditing: true,
      quality: 0.8,
    });
    if (result.canceled || !result.assets[0]) return;
    const uri = result.assets[0].uri;
    setLabelImageUri(uri);
    setLabelSource("library");
    setGenerating(true);
    setGeneratedBy(null);
    parseLabel.mutate(uri);
  };

  const resetLabel = () => {
    setLabelImageUri(null);
    setLabelSource(null);
    setConfidenceScore(null);
    setGeneratedBy(null);
  };

  // ── Save handlers ──
  const handleSave = () => saveFood.mutate();
  const handleSaveAndLog = () => saveAndLogFood.mutate();

  const anySaving = saveFood.isPending || saveAndLogFood.isPending;

  // ── Tab config ──
  const tabs: { key: CreateMethod; label: string; pro: boolean }[] = [
    { key: "manual", label: "Manual", pro: false },
    { key: "description", label: "Description ✨", pro: true },
    { key: "label", label: "Label 📷", pro: true },
  ];

  const isGenerating = generating && method !== "manual";

  const showFoodDetails = method === "manual" || (method === "description" && generatedBy === "description") || (method === "label" && generatedBy === "label");

  const handleMethodChange = async (next: CreateMethod) => {
    const isPremium = next === "description" || next === "label";
    if (isPremium && !isPro) {
      const ok = await presentPaywall();
      if (!ok) return;
    }
    setMethod(next);
  };

  return (
    <SafeScreen edges={["top"]}>
      <View style={{ flexDirection: "row", alignItems: "center", padding: 16 }}>
        <TouchableOpacity onPress={() => router.back()} style={{ paddingRight: 16 }}>
          <Ionicons name="arrow-back" size={24} color={c.text} />
        </TouchableOpacity>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 20, fontWeight: "bold", color: c.text }}>Create Custom Food</Text>
          <Text style={{ fontSize: 13, color: c.textMuted, marginTop: 2 }}>
            Manual entry is free. Pro can generate from a description or nutrition label.
          </Text>
        </View>
      </View>

      <ScrollView style={{ paddingHorizontal: 16 }} keyboardShouldPersistTaps="handled">
        {/* ── Method tabs ── */}
        <View
          style={{
            flexDirection: "row",
            backgroundColor: c.bg,
            borderRadius: 10,
            padding: 3,
            marginBottom: 20,
          }}
        >
          {tabs.map(({ key, label, pro }) => (
            <TouchableOpacity
              key={key}
              onPress={() => handleMethodChange(key)}
              accessibilityRole="tab"
              accessibilityState={{ selected: method === key }}
              style={{
                flex: 1,
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "center",
                gap: 4,
                paddingVertical: 10,
                borderRadius: 8,
                backgroundColor: method === key ? c.card : "transparent",
              }}
            >
              <Text
                style={{
                  fontSize: 13,
                  fontWeight: "600",
                  color: method === key ? c.text : c.textMuted,
                }}
              >
                {label}
              </Text>
              {pro && !isPro && (
                <View style={{ backgroundColor: c.warning, borderRadius: 3, paddingHorizontal: 4, paddingVertical: 1 }}>
                  <Text style={{ color: "#fff", fontSize: 8, fontWeight: "800" }}>PRO</Text>
                </View>
              )}
            </TouchableOpacity>
          ))}
        </View>

        {/* ════════════════════════
            MANUAL TAB
            ════════════════════════ */}
        {method === "manual" && (
          <View style={{ marginBottom: 24 }}>
            <Text style={{ fontWeight: "700", fontSize: 15, color: c.text, marginBottom: 4 }}>
              Manual Entry
            </Text>
            <Text style={{ color: c.textSecondary, fontSize: 13, marginBottom: 16 }}>
              Enter the nutrition details yourself.
            </Text>
          </View>
        )}

        {/* ════════════════════════
            DESCRIPTION TAB
            ════════════════════════ */}
        {method === "description" && (
          <View style={{ marginBottom: 24 }}>
            <Text style={{ fontWeight: "700", fontSize: 15, color: c.text, marginBottom: 4 }}>
              Generate from Description
            </Text>
            <Text style={{ color: c.textSecondary, fontSize: 13, marginBottom: 12 }}>
              Describe the food or recipe. We'll estimate nutrition details, then you can review before saving.
            </Text>

            <View style={{ position: "relative" }}>
              <TextInput
                style={{
                  borderWidth: 1,
                  borderColor: c.border,
                  borderRadius: 8,
                  padding: 12,
                  fontSize: 15,
                  color: c.text,
                  backgroundColor: c.card,
                  minHeight: 88,
                  textAlignVertical: "top",
                  paddingRight: 56,
                }}
                value={descText}
                onChangeText={setDescText}
                placeholder="e.g. homemade berry smoothie with banana and yogurt"
                placeholderTextColor={c.textMuted}
                multiline
                autoCapitalize="sentences"
                returnKeyType="send"
                blurOnSubmit={false}
                onSubmitEditing={handleGenerateDescription}
              />
              <TouchableOpacity
                onPress={handleGenerateDescription}
                disabled={isGenerating || descText.trim().length < MIN_DESCRIBE_LENGTH}
                style={{
                  position: "absolute",
                  right: 12,
                  bottom: 12,
                  width: 36,
                  height: 36,
                  borderRadius: 18,
                  backgroundColor:
                    isGenerating || descText.trim().length < MIN_DESCRIBE_LENGTH
                      ? c.borderLight : c.primary,
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                {isGenerating ? (
                  <ActivityIndicator size="small" color={c.textOnPrimary} />
                ) : (
                  <Ionicons
                    name="arrow-forward"
                    size={18}
                    color={descText.trim().length < MIN_DESCRIBE_LENGTH ? c.textMuted : c.textOnPrimary}
                  />
                )}
              </TouchableOpacity>
            </View>
            <Text style={{ color: c.textMuted, fontSize: 12, marginTop: 8 }}>
              {descText.trim().length >= MIN_DESCRIBE_LENGTH
                ? "Tap the arrow or press Enter to generate."
                : "Type at least 8 characters."}
            </Text>
          </View>
        )}

        {/* ════════════════════════
            LABEL TAB
            ════════════════════════ */}
        {method === "label" && (
          <View style={{ marginBottom: 24 }}>
            <Text style={{ fontWeight: "700", fontSize: 15, color: c.text, marginBottom: 4 }}>
              Extract from Nutrition Label
            </Text>
            <Text style={{ color: c.textSecondary, fontSize: 13, marginBottom: 12 }}>
              Take or upload a photo of a nutrition label. We'll extract serving size, calories, macros, and more.
            </Text>

            {!labelImageUri ? (
              <View style={{ flexDirection: "row", gap: 12 }}>
                <TouchableOpacity
                  onPress={handleLabelFromCamera}
                  disabled={isGenerating}
                  style={{
                    flex: 1,
                    alignItems: "center",
                    borderWidth: 1,
                    borderColor: c.border,
                    borderRadius: 12,
                    paddingVertical: 20,
                    backgroundColor: c.card,
                  }}
                >
                  <Ionicons name="camera-outline" size={28} color={c.text} />
                  <Text style={{ color: c.text, fontWeight: "700", fontSize: 14, marginTop: 8 }}>
                    Take Photo
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  onPress={handleLabelFromLibrary}
                  disabled={isGenerating}
                  style={{
                    flex: 1,
                    alignItems: "center",
                    borderWidth: 1,
                    borderColor: c.border,
                    borderRadius: 12,
                    paddingVertical: 20,
                    backgroundColor: c.card,
                  }}
                >
                  <Ionicons name="images-outline" size={28} color={c.text} />
                  <Text style={{ color: c.text, fontWeight: "700", fontSize: 14, marginTop: 8 }}>
                    Choose from Library
                  </Text>
                </TouchableOpacity>
              </View>
            ) : (
              <View
                style={{
                  borderWidth: 1,
                  borderColor: c.border,
                  borderRadius: 12,
                  padding: 12,
                  backgroundColor: c.card,
                }}
              >
                <Image
                  source={{ uri: labelImageUri }}
                  style={{ width: "100%", height: 160, borderRadius: 8, marginBottom: 12 }}
                  resizeMode="contain"
                />
                {isGenerating && (
                  <View style={{ flexDirection: "row", alignItems: "center" }}>
                    <ActivityIndicator size="small" color={c.primary} />
                    <Text style={{ color: c.textSecondary, marginLeft: 8, flex: 1 }}>
                      Analyzing label image...
                    </Text>
                  </View>
                )}
                {generatedBy === "label" && !isGenerating && (
                  <View
                    style={{
                      backgroundColor: confColors.bg,
                      borderColor: confColors.border,
                      borderWidth: 1,
                      borderRadius: 8,
                      padding: 10,
                      marginBottom: 8,
                    }}
                  >
                    <View style={{ flexDirection: "row", alignItems: "center" }}>
                      <Ionicons name={confColors.icon as any} size={16} color={confColors.text} />
                      <Text style={{ color: confColors.text, fontWeight: "700", fontSize: 13, marginLeft: 6 }}>
                        {confLevel ? `${confLevel} confidence` : "Extracted"}
                      </Text>
                      {confidenceScore != null && (
                        <Text style={{ color: confColors.text, fontWeight: "600", fontSize: 12, marginLeft: 6 }}>
                          {Math.round(confidenceScore * 100)}%
                        </Text>
                      )}
                    </View>
                    <Text style={{ color: c.textSecondary, fontSize: 11, marginTop: 4 }}>
                      Extracted from {labelSource === "camera" ? "a photo" : "your library image"}. Review details before saving.
                    </Text>
                  </View>
                )}
                <TouchableOpacity
                  onPress={resetLabel}
                  disabled={isGenerating}
                  style={{ alignSelf: "center", paddingVertical: 6 }}
                >
                  <Text style={{ color: c.primary, fontWeight: "600", fontSize: 13 }}>
                    Replace Photo
                  </Text>
                </TouchableOpacity>
              </View>
            )}
          </View>
        )}

        {/* ════════════════════════
            GENERATING INDICATOR (description-only, label has inline)
            ════════════════════════ */}
        {method === "description" && isGenerating && (
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              borderWidth: 1,
              borderColor: c.borderLight,
              borderRadius: 12,
              padding: 12,
              marginBottom: 16,
            }}
          >
            <ActivityIndicator size="small" color={c.primary} />
            <Text style={{ color: c.textSecondary, marginLeft: 10, flex: 1 }}>
              Generating food details from your description...
            </Text>
          </View>
        )}

        {/* ════════════════════════
            CONFIDENCE CARD (description-only, label has inline)
            ════════════════════════ */}
        {method === "description" && generatedBy === "description" && !isGenerating && (
          <View
            style={{
              borderWidth: 1,
              borderRadius: 12,
              padding: 12,
              marginBottom: 16,
              backgroundColor: confColors.bg,
              borderColor: confColors.border,
            }}
          >
            <View style={{ flexDirection: "row", alignItems: "center" }}>
              <Ionicons name={confColors.icon as any} size={18} color={confColors.text} />
              <Text style={{ color: confColors.text, fontWeight: "700", marginLeft: 8, fontSize: 14 }}>
                {confLevel ? `${confLevel} AI confidence` : "AI draft generated"}
              </Text>
              {confidenceScore != null && (
                <Text style={{ color: confColors.text, fontWeight: "600", marginLeft: 8, fontSize: 13 }}>
                  {Math.round(confidenceScore * 100)}%
                </Text>
              )}
            </View>
            <Text style={{ color: c.textSecondary, fontSize: 12, marginTop: 6 }}>
              Generated from {sourceLabel(generatedBy)}. Review the details before saving.
            </Text>
          </View>
        )}

        {/* ════════════════════════
            FOOD DETAILS FORM
            ════════════════════════ */}
        {showFoodDetails && (
        <View style={{ borderTopWidth: 1, borderTopColor: c.borderLight, paddingTop: 20, marginBottom: 4 }}>
          <Text style={{ color: c.textSecondary, fontWeight: "700", fontSize: 14, marginBottom: 12 }}>
            Food Details
          </Text>
        </View>
        )}

        {showFoodDetails && (
        <>
        <View style={s.field}>
          <Text style={{ color: c.textMuted, marginBottom: 4 }}>Name *</Text>
          <TextInput
            style={inputStyle(c)}
            value={form.name}
            onChangeText={(v) => setField("name", v)}
            placeholder="e.g. My Favorite Bread"
            placeholderTextColor={c.textMuted}
          />
        </View>

        <View style={s.field}>
          <Text style={{ color: c.textMuted, marginBottom: 4 }}>Brand</Text>
          <TextInput
            style={inputStyle(c)}
            value={form.brandName ?? ""}
            onChangeText={(v) => setField("brandName", v)}
            placeholder="Optional"
            placeholderTextColor={c.textMuted}
          />
        </View>

        <View style={{ flexDirection: "row", gap: 12 }}>
          <View style={[s.field, { flex: 1 }]}>
            <Text style={{ color: c.textMuted, marginBottom: 4 }}>Serving Size *</Text>
            <TextInput
              style={inputStyle(c)}
              value={NUM(form.servingSize)}
              onChangeText={(v) => setField("servingSize", v)}
              keyboardType="numeric"
            />
          </View>
          <View style={[s.field, { flex: 1 }]}>
            <Text style={{ color: c.textMuted, marginBottom: 4 }}>Unit *</Text>
            <TextInput
              style={inputStyle(c)}
              value={form.servingSizeUnit}
              onChangeText={(v) => setField("servingSizeUnit", v)}
            />
          </View>
        </View>

        <View style={s.field}>
          <Text style={{ color: c.textMuted, marginBottom: 4 }}>Calories (kcal) *</Text>
          <TextInput
            style={inputStyle(c)}
            value={NUM(form.calories)}
            onChangeText={(v) => setField("calories", v)}
            keyboardType="numeric"
          />
        </View>

        <View style={{ flexDirection: "row", gap: 12, flexWrap: "wrap", marginBottom: 16 }}>
          {[
            { label: "Protein (g) *", key: "proteinG" },
            { label: "Carbs (g) *", key: "carbG" },
            { label: "Fat (g) *", key: "fatG" },
            { label: "Fiber (g)", key: "fiberG" },
            { label: "Sugar (g)", key: "sugarG" },
            { label: "Sodium (mg)", key: "sodiumMg" },
          ].map((m) => (
            <View key={m.key} style={[s.field, { minWidth: "45%", flex: 1 }]}>
              <Text style={{ color: c.textMuted, marginBottom: 4 }}>{m.label}</Text>
              <TextInput
                style={inputStyle(c)}
                value={NUM(form[m.key as keyof CustomFood] as number)}
                onChangeText={(v) => setField(m.key as keyof CustomFood, v)}
                keyboardType="numeric"
              />
            </View>
          ))}
        </View>

        <View style={s.field}>
          <Text style={{ color: c.textMuted, marginBottom: 4 }}>Ingredients</Text>
          <TextInput
            style={[inputStyle(c), { height: 80 }]}
            value={form.ingredients ?? ""}
            onChangeText={(v) => setField("ingredients", v)}
            placeholder="Optional ingredients or notes"
            placeholderTextColor={c.textMuted}
            multiline
          />
        </View>

        {/* ════════════════════════
            SAVE ACTIONS
            ════════════════════════ */}
        <TouchableOpacity
          onPress={handleSave}
          disabled={!isFormValid || anySaving}
          style={[s.saveBtn, { backgroundColor: c.primary, opacity: isFormValid && !anySaving ? 1 : 0.5 }]}
        >
          {anySaving ? (
            <ActivityIndicator color="#fff" />
          ) : (
            <Text style={{ color: "#fff", fontWeight: "bold", fontSize: 16 }}>
              Save to My Foods
            </Text>
          )}
        </TouchableOpacity>

        <TouchableOpacity
          onPress={handleSaveAndLog}
          disabled={!isFormValid || anySaving}
          style={{
            paddingVertical: 14,
            borderRadius: 12,
            alignItems: "center",
            justifyContent: "center",
            marginTop: 8,
            borderWidth: 1,
            borderColor: c.primary,
            opacity: isFormValid && !anySaving ? 1 : 0.5,
          }}
        >
          {anySaving ? (
            <ActivityIndicator color={c.primary} />
          ) : (
            <Text style={{ color: c.primary, fontWeight: "700", fontSize: 15 }}>
              Save & Log
            </Text>
          )}
        </TouchableOpacity>
        </>
        )}

        <View style={{ height: 40 }} />
      </ScrollView>
    </SafeScreen>
  );
}

function inputStyle(c: ReturnType<typeof useThemeColors>) {
  return {
    borderWidth: 1,
    borderColor: c.border,
    borderRadius: 8,
    padding: 12,
    fontSize: 16,
    color: c.text,
    backgroundColor: c.card,
  };
}

const s = StyleSheet.create({
  field: { marginBottom: 16 },
  saveBtn: {
    padding: 16,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center",
    marginTop: 8,
  },
});
