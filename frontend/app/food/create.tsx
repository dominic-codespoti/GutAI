import { useState } from "react";
import {
  View,
  Text,
  TextInput,
  ScrollView,
  TouchableOpacity,
  ActivityIndicator,
  StyleSheet,
} from "react-native";
import { useRouter } from "expo-router";
import * as ImagePicker from "expo-image-picker";
import { Ionicons } from "@expo/vector-icons";
import { SafeScreen } from "../../components/SafeScreen";
import { useThemeColors } from "../../src/stores/theme";
import { foodApi } from "../../src/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "../../src/stores/toast";
import { useSubscriptionStore, presentPaywall } from "../../src/stores/subscription";
import type { CustomFood } from "../../src/types";

type AiSource = "describe" | "photo" | "library";
type AiGeneratedCustomFoodResponse = CustomFood & {
  extractionConfidence?: number | null;
};

const MIN_DESCRIBE_LENGTH = 8;

const roundOne = (value: number) => Math.round(value * 10) / 10;

const numberToInput = (value: number | null | undefined) =>
  value == null ? "" : value.toString();

function normalizeParsedFood(data: AiGeneratedCustomFoodResponse): CustomFood {
  return {
    name: data.name ?? "",
    brandName: data.brandName ?? "",
    servingSize: Math.max(1, roundOne(data.servingSize ?? 100)),
    servingSizeUnit: data.servingSizeUnit || "g",
    calories: roundOne(data.calories ?? 0),
    proteinG: roundOne(data.proteinG ?? 0),
    carbG: roundOne(data.carbG ?? 0),
    fatG: roundOne(data.fatG ?? 0),
    fiberG: roundOne(data.fiberG ?? 0),
    sugarG: roundOne(data.sugarG ?? 0),
    sodiumMg: roundOne(data.sodiumMg ?? 0),
    ingredients: data.ingredients ?? "",
  };
}

function getAiConfidenceLevel(
  score: number | null,
): "High" | "Medium" | "Low" | null {
  if (score == null || Number.isNaN(score)) return null;
  if (score >= 0.85) return "High";
  if (score >= 0.6) return "Medium";
  return "Low";
}

function getAiConfidencePalette(
  level: "High" | "Medium" | "Low" | null,
  colors: ReturnType<typeof useThemeColors>,
) {
  switch (level) {
    case "High":
      return {
        backgroundColor: colors.primaryBg,
        borderColor: colors.primaryBorder,
        textColor: colors.primaryLight,
        icon: "checkmark-circle",
      };
    case "Medium":
      return {
        backgroundColor: colors.warningBg,
        borderColor: colors.warningBorder,
        textColor: colors.warning,
        icon: "alert-circle",
      };
    default:
      return {
        backgroundColor: colors.dangerBg,
        borderColor: colors.dangerBorder,
        textColor: colors.danger,
        icon: "help-circle",
      };
  }
}

function formatAiSourceLabel(source: AiSource | null) {
  switch (source) {
    case "describe":
      return "your description";
    case "photo":
      return "a photo";
    case "library":
      return "your library image";
    default:
      return "AI";
  }
}

function pendingAiMessage(source: AiSource | null) {
  switch (source) {
    case "describe":
      return "Generating food details from your description...";
    case "photo":
      return "Analyzing your food photo with AI...";
    case "library":
      return "Analyzing your library image with AI...";
    default:
      return "Generating food details with AI...";
  }
}

export default function CreateCustomFoodScreen() {
  const t = useThemeColors();
  const router = useRouter();
  const queryClient = useQueryClient();
  const { isPro, isLoaded: subLoaded } = useSubscriptionStore();

  const [form, setForm] = useState<CustomFood>({
    name: "",
    brandName: "",
    servingSize: 100,
    servingSizeUnit: "g",
    calories: 0,
    proteinG: 0,
    carbG: 0,
    fatG: 0,
    fiberG: 0,
    sugarG: 0,
    sodiumMg: 0,
    ingredients: "",
  });
  const [aiInputMode, setAiInputMode] = useState<AiSource>("describe");
  const [describeText, setDescribeText] = useState("");
  const [pendingAiSource, setPendingAiSource] = useState<AiSource | null>(null);
  const [generatedBy, setGeneratedBy] = useState<AiSource | null>(null);
  const [aiConfidenceScore, setAiConfidenceScore] = useState<number | null>(
    null,
  );

  const isGenerating = pendingAiSource !== null;
  const aiConfidenceLevel = getAiConfidenceLevel(aiConfidenceScore);
  const aiConfidencePalette = getAiConfidencePalette(aiConfidenceLevel, t);

  const applyGeneratedFood = (
    nextFood: CustomFood,
    source: AiSource,
    confidence: number | null,
  ) => {
    setForm((prev) => ({
      ...prev,
      ...nextFood,
    }));
    setGeneratedBy(source);
    setAiConfidenceScore(confidence);
  };

  const imageParseMutation = useMutation({
    mutationFn: async ({
      imageUri,
      source,
    }: {
      imageUri: string;
      source: Extract<AiSource, "photo" | "library">;
    }) => {
      const resp = await foodApi.parseLabel(imageUri, "image/jpeg");
      return {
        data: resp.data as AiGeneratedCustomFoodResponse,
        source,
      };
    },
    onSuccess: ({ data, source }) => {
      applyGeneratedFood(
        normalizeParsedFood(data),
        source,
        data.extractionConfidence ?? null,
      );
      setPendingAiSource(null);
    },
    onError: () => {
      setPendingAiSource(null);
      toast.error("Could not analyze that image. Try a clearer photo.");
    },
  });

  const describeMutation = useMutation({
    mutationFn: (text: string) =>
      foodApi.describeFood(text).then((r) => r.data),
    onSuccess: (response, submittedText) => {
      setPendingAiSource(null);

      if (submittedText !== describeText.trim()) {
        return;
      }

      applyGeneratedFood(
        normalizeParsedFood(response),
        "describe",
        response.extractionConfidence ?? null,
      );
    },
    onError: () => {
      setPendingAiSource(null);
      toast.error("Could not generate food details from that description.");
    },
  });

  const saveMutation = useMutation({
    mutationFn: async () => {
      await foodApi.createCustomFood(form);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["custom-foods"] });
      toast.success("Food saved successfully!");
      router.navigate("/(tabs)/scan?tab=my-foods");
    },
    onError: () => {
      toast.error("Failed to save custom food.");
    },
  });

  const submitDescribePrompt = () => {
    const trimmed = describeText.trim();

    if (aiInputMode !== "describe") return;
    if (trimmed.length < MIN_DESCRIBE_LENGTH) {
      toast.error(`Type at least ${MIN_DESCRIBE_LENGTH} characters first.`);
      return;
    }
    if (isGenerating) return;

    setPendingAiSource("describe");
    describeMutation.mutate(trimmed);
  };

  const handleDescribePress = () => {
    setAiInputMode("describe");
  };

  const handlePhotoOfFood = async () => {
    setAiInputMode("photo");

    const { status } = await ImagePicker.requestCameraPermissionsAsync();
    if (status !== "granted") {
      toast.error("Camera permission is required.");
      return;
    }

    const result = await ImagePicker.launchCameraAsync({
      mediaTypes: ImagePicker.MediaTypeOptions.Images,
      allowsEditing: true,
      quality: 0.8,
    });

    if (!result.canceled && result.assets[0]) {
      setPendingAiSource("photo");
      imageParseMutation.mutate({
        imageUri: result.assets[0].uri,
        source: "photo",
      });
    }
  };

  const handleImagePicker = async () => {
    setAiInputMode("library");

    const { status } = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (status !== "granted") {
      toast.error("Photo library permission is required.");
      return;
    }

    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ImagePicker.MediaTypeOptions.Images,
      allowsEditing: true,
      quality: 0.8,
    });

    if (!result.canceled && result.assets[0]) {
      setPendingAiSource("library");
      imageParseMutation.mutate({
        imageUri: result.assets[0].uri,
        source: "library",
      });
    }
  };

  const setField = (field: keyof CustomFood, value: string) => {
    const numericFields = [
      "servingSize",
      "calories",
      "proteinG",
      "carbG",
      "fatG",
      "fiberG",
      "sugarG",
      "sodiumMg",
    ];
    if (numericFields.includes(field)) {
      const num = parseFloat(value);
      setForm((prev) => ({ ...prev, [field]: isNaN(num) ? 0 : num }));
    } else {
      setForm((prev) => ({ ...prev, [field]: value }));
    }
  };

  if (!subLoaded) {
    return (
      <SafeScreen edges={["top"]}>
        <View style={{ flex: 1, alignItems: "center", justifyContent: "center" }}>
          <ActivityIndicator size="large" color={t.primary} />
        </View>
      </SafeScreen>
    );
  }

  if (!isPro) {
    return (
      <SafeScreen edges={["top"]}>
        <View style={{ flexDirection: "row", alignItems: "center", padding: 16 }}>
          <TouchableOpacity
            onPress={() => router.back()}
            style={{ paddingRight: 16 }}
          >
            <Ionicons name="arrow-back" size={24} color={t.text} />
          </TouchableOpacity>
          <View style={{ flex: 1 }}>
            <Text style={{ fontSize: 20, fontWeight: "bold", color: t.text }}>
              AI Food Creation
            </Text>
          </View>
        </View>
        <View style={{ flex: 1, alignItems: "center", justifyContent: "center", paddingHorizontal: 32 }}>
          <View style={{ width: 72, height: 72, borderRadius: 36, backgroundColor: t.primaryBg, alignItems: "center", justifyContent: "center", marginBottom: 20 }}>
            <Ionicons name="sparkles" size={36} color={t.primary} />
          </View>
          <Text style={{ fontSize: 22, fontWeight: "bold", color: t.text, textAlign: "center", marginBottom: 12 }}>
            Unlock AI Food Creation
          </Text>
          <Text style={{ fontSize: 15, color: t.textSecondary, textAlign: "center", lineHeight: 22, marginBottom: 28 }}>
            Describe a food or snap a photo and let AI instantly generate complete nutrition details — no manual entry needed.
          </Text>
          <TouchableOpacity
            onPress={() => presentPaywall()}
            accessibilityRole="button"
            accessibilityLabel="Subscribe to Gut Lens Pro"
            style={{
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "center",
              backgroundColor: t.primary,
              paddingVertical: 16,
              paddingHorizontal: 32,
              borderRadius: 12,
            }}
          >
            <Ionicons name="diamond-outline" size={18} color="#fff" style={{ marginRight: 8 }} />
            <Text style={{ color: "#fff", fontWeight: "bold", fontSize: 16 }}>
              Subscribe to Gut Lens Pro
            </Text>
          </TouchableOpacity>
        </View>
      </SafeScreen>
    );
  }

  return (
    <SafeScreen edges={["top"]}>
      <View style={{ flexDirection: "row", alignItems: "center", padding: 16 }}>
        <TouchableOpacity
          onPress={() => router.back()}
          style={{ paddingRight: 16 }}
        >
          <Ionicons name="arrow-back" size={24} color={t.text} />
        </TouchableOpacity>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 20, fontWeight: "bold", color: t.text }}>
            Create Food
          </Text>
          <Text style={{ fontSize: 13, color: t.textMuted, marginTop: 2 }}>
            Use AI or fill in the nutrition details, then save it to My Foods.
          </Text>
        </View>
      </View>

      <ScrollView
        style={{ paddingHorizontal: 16 }}
        keyboardShouldPersistTaps="handled"
      >
        <View
          style={{
            marginBottom: 24,
          }}
        >
          <TouchableOpacity
            style={[
              styles.aiPrimaryButton,
              {
                backgroundColor:
                  aiInputMode === "describe" ? t.primaryBg : t.card,
                borderColor:
                  aiInputMode === "describe" ? t.primaryLight : t.border,
              },
            ]}
            onPress={handleDescribePress}
            disabled={isGenerating}
          >
            <Ionicons
              name="sparkles-outline"
              size={20}
              color={aiInputMode === "describe" ? t.primaryLight : t.text}
            />
            <View style={{ flex: 1, marginLeft: 10 }}>
              <Text
                style={{
                  color: aiInputMode === "describe" ? t.primaryLight : t.text,
                  fontWeight: "700",
                  fontSize: 15,
                }}
              >
                Describe with AI
              </Text>
              <Text
                style={{
                  color: t.textSecondary,
                  fontSize: 12,
                  marginTop: 2,
                }}
              >
                Type a description, then explicitly generate food details.
              </Text>
            </View>
          </TouchableOpacity>

          <View style={{ flexDirection: "row", gap: 12, marginTop: 12 }}>
            <TouchableOpacity
              style={[
                styles.aiSecondaryButton,
                {
                  backgroundColor:
                    aiInputMode === "photo" ? t.primaryBg : t.card,
                  borderColor:
                    aiInputMode === "photo" ? t.primaryLight : t.border,
                },
              ]}
              onPress={handlePhotoOfFood}
              disabled={isGenerating}
            >
              <Ionicons
                name="camera-outline"
                size={20}
                color={aiInputMode === "photo" ? t.primaryLight : t.text}
              />
              <Text
                style={{
                  color: aiInputMode === "photo" ? t.primaryLight : t.text,
                  fontWeight: "700",
                  marginTop: 8,
                }}
              >
                Photo of Food
              </Text>
            </TouchableOpacity>

            <TouchableOpacity
              style={[
                styles.aiSecondaryButton,
                {
                  backgroundColor:
                    aiInputMode === "library" ? t.primaryBg : t.card,
                  borderColor:
                    aiInputMode === "library" ? t.primaryLight : t.border,
                },
              ]}
              onPress={handleImagePicker}
              disabled={isGenerating}
            >
              <Ionicons
                name="images-outline"
                size={20}
                color={aiInputMode === "library" ? t.primaryLight : t.text}
              />
              <Text
                style={{
                  color: aiInputMode === "library" ? t.primaryLight : t.text,
                  fontWeight: "700",
                  marginTop: 8,
                }}
              >
                From Library
              </Text>
            </TouchableOpacity>
          </View>

          {aiInputMode === "describe" && (
            <View style={{ marginTop: 12 }}>
              <View style={{ position: "relative" }}>
                <TextInput
                  style={[
                    styles.input,
                    styles.describeInput,
                    {
                      color: t.text,
                      backgroundColor: t.card,
                      borderColor: t.border,
                      paddingRight: 56,
                    },
                  ]}
                  value={describeText}
                  onChangeText={setDescribeText}
                  placeholder="Describe the food or recipe, e.g. homemade berry smoothie with banana and yogurt"
                  placeholderTextColor={t.textMuted}
                  multiline
                  autoCapitalize="sentences"
                  returnKeyType="send"
                  blurOnSubmit={false}
                  onSubmitEditing={submitDescribePrompt}
                />
                <TouchableOpacity
                  onPress={submitDescribePrompt}
                  disabled={
                    isGenerating ||
                    describeText.trim().length < MIN_DESCRIBE_LENGTH
                  }
                  accessibilityRole="button"
                  accessibilityLabel="Generate food details"
                  style={{
                    position: "absolute",
                    right: 12,
                    bottom: 12,
                    width: 36,
                    height: 36,
                    borderRadius: 18,
                    backgroundColor:
                      isGenerating ||
                      describeText.trim().length < MIN_DESCRIBE_LENGTH
                        ? t.borderLight
                        : t.primaryLight,
                    alignItems: "center",
                    justifyContent: "center",
                  }}
                >
                  {isGenerating && pendingAiSource === "describe" ? (
                    <ActivityIndicator size="small" color={t.textOnPrimary} />
                  ) : (
                    <Ionicons
                      name="arrow-forward"
                      size={18}
                      color={
                        describeText.trim().length < MIN_DESCRIBE_LENGTH
                          ? t.textMuted
                          : t.textOnPrimary
                      }
                    />
                  )}
                </TouchableOpacity>
              </View>
              <Text style={{ color: t.textMuted, fontSize: 12, marginTop: 8 }}>
                {describeText.trim().length >= MIN_DESCRIBE_LENGTH
                  ? "Tap the send button or press Enter to generate food details."
                  : "Type at least 8 characters before generating food details."}
              </Text>
            </View>
          )}

          {isGenerating && (
            <View
              style={[
                styles.aiStatusCard,
                {
                  backgroundColor: t.card,
                  borderColor: t.borderLight,
                },
              ]}
            >
              <ActivityIndicator size="small" color={t.primary} />
              <Text style={{ color: t.textSecondary, marginLeft: 10, flex: 1 }}>
                {pendingAiMessage(pendingAiSource)}
              </Text>
            </View>
          )}

          {generatedBy && !isGenerating && (
            <View
              style={[
                styles.aiStatusCard,
                {
                  backgroundColor: aiConfidencePalette.backgroundColor,
                  borderColor: aiConfidencePalette.borderColor,
                  marginTop: 12,
                },
              ]}
            >
              <View style={{ flexDirection: "row", alignItems: "center" }}>
                <Ionicons
                  name={aiConfidencePalette.icon as any}
                  size={18}
                  color={aiConfidencePalette.textColor}
                />
                <Text
                  style={{
                    color: aiConfidencePalette.textColor,
                    fontWeight: "700",
                    marginLeft: 8,
                    fontSize: 14,
                  }}
                >
                  {aiConfidenceLevel
                    ? `${aiConfidenceLevel} AI confidence`
                    : "AI draft generated"}
                </Text>
                {aiConfidenceScore != null && (
                  <Text
                    style={{
                      color: aiConfidencePalette.textColor,
                      fontWeight: "600",
                      marginLeft: 8,
                      fontSize: 13,
                    }}
                  >
                    {Math.round(aiConfidenceScore * 100)}%
                  </Text>
                )}
              </View>
              <Text
                style={{ color: t.textSecondary, fontSize: 12, marginTop: 6 }}
              >
                Generated from {formatAiSourceLabel(generatedBy)}. Review the
                details before saving.
              </Text>
            </View>
          )}
        </View>

        <View
          style={{
            borderTopWidth: 1,
            borderTopColor: t.borderLight,
            paddingTop: 20,
            marginBottom: 4,
          }}
        >
          <Text
            style={{
              color: t.textSecondary,
              fontWeight: "700",
              fontSize: 14,
              marginBottom: 12,
            }}
          >
            Food Details
          </Text>
        </View>

        <View style={styles.formGroup}>
          <Text style={{ color: t.textMuted, marginBottom: 4 }}>Name *</Text>
          <TextInput
            style={[
              styles.input,
              { color: t.text, backgroundColor: t.card, borderColor: t.border },
            ]}
            value={form.name}
            onChangeText={(v) => setField("name", v)}
            placeholder="e.g. My Favorite Bread"
            placeholderTextColor={t.textMuted}
          />
        </View>

        <View style={styles.formGroup}>
          <Text style={{ color: t.textMuted, marginBottom: 4 }}>Brand</Text>
          <TextInput
            style={[
              styles.input,
              { color: t.text, backgroundColor: t.card, borderColor: t.border },
            ]}
            value={form.brandName ?? ""}
            onChangeText={(v) => setField("brandName", v)}
            placeholder="Optional"
            placeholderTextColor={t.textMuted}
          />
        </View>

        <View style={{ flexDirection: "row", gap: 12 }}>
          <View style={[styles.formGroup, { flex: 1 }]}>
            <Text style={{ color: t.textMuted, marginBottom: 4 }}>
              Serving Size *
            </Text>
            <TextInput
              style={[
                styles.input,
                {
                  color: t.text,
                  backgroundColor: t.card,
                  borderColor: t.border,
                },
              ]}
              value={numberToInput(form.servingSize)}
              onChangeText={(v) => setField("servingSize", v)}
              keyboardType="numeric"
            />
          </View>
          <View style={[styles.formGroup, { flex: 1 }]}>
            <Text style={{ color: t.textMuted, marginBottom: 4 }}>Unit *</Text>
            <TextInput
              style={[
                styles.input,
                {
                  color: t.text,
                  backgroundColor: t.card,
                  borderColor: t.border,
                },
              ]}
              value={form.servingSizeUnit}
              onChangeText={(v) => setField("servingSizeUnit", v)}
            />
          </View>
        </View>

        <View style={styles.formGroup}>
          <Text style={{ color: t.textMuted, marginBottom: 4 }}>
            Calories (kcal) *
          </Text>
          <TextInput
            style={[
              styles.input,
              { color: t.text, backgroundColor: t.card, borderColor: t.border },
            ]}
            value={numberToInput(form.calories)}
            onChangeText={(v) => setField("calories", v)}
            keyboardType="numeric"
          />
        </View>

        <View
          style={{
            flexDirection: "row",
            gap: 12,
            flexWrap: "wrap",
            marginBottom: 16,
          }}
        >
          {[
            { label: "Protein (g) *", key: "proteinG" },
            { label: "Carbs (g) *", key: "carbG" },
            { label: "Fat (g) *", key: "fatG" },
            { label: "Fiber (g)", key: "fiberG" },
            { label: "Sugar (g)", key: "sugarG" },
            { label: "Sodium (mg)", key: "sodiumMg" },
          ].map((macro) => (
            <View
              key={macro.key}
              style={[styles.formGroup, { minWidth: "45%", flex: 1 }]}
            >
              <Text style={{ color: t.textMuted, marginBottom: 4 }}>
                {macro.label}
              </Text>
              <TextInput
                style={[
                  styles.input,
                  {
                    color: t.text,
                    backgroundColor: t.card,
                    borderColor: t.border,
                  },
                ]}
                value={numberToInput(
                  form[macro.key as keyof CustomFood] as number,
                )}
                onChangeText={(v) => setField(macro.key as keyof CustomFood, v)}
                keyboardType="numeric"
              />
            </View>
          ))}
        </View>

        <View style={styles.formGroup}>
          <Text style={{ color: t.textMuted, marginBottom: 4 }}>
            Ingredients
          </Text>
          <TextInput
            style={[
              styles.input,
              {
                height: 80,
                color: t.text,
                backgroundColor: t.card,
                borderColor: t.border,
              },
            ]}
            value={form.ingredients ?? ""}
            onChangeText={(v) => setField("ingredients", v)}
            placeholder="Optional ingredients or notes"
            placeholderTextColor={t.textMuted}
            multiline
          />
        </View>

        <TouchableOpacity
          style={[
            styles.saveBtn,
            {
              backgroundColor: t.primary,
              opacity: !form.name.trim() || saveMutation.isPending ? 0.5 : 1,
            },
          ]}
          onPress={() => saveMutation.mutate()}
          disabled={!form.name.trim() || saveMutation.isPending}
        >
          {saveMutation.isPending ? (
            <ActivityIndicator color="#fff" />
          ) : (
            <Text style={{ color: "#fff", fontWeight: "bold", fontSize: 16 }}>
              Save to My Foods
            </Text>
          )}
        </TouchableOpacity>
        <View style={{ height: 40 }} />
      </ScrollView>
    </SafeScreen>
  );
}

const styles = StyleSheet.create({
  aiPrimaryButton: {
    flexDirection: "row",
    alignItems: "center",
    borderWidth: 1,
    padding: 14,
    borderRadius: 12,
  },
  aiSecondaryButton: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1,
    paddingVertical: 16,
    paddingHorizontal: 12,
    borderRadius: 12,
  },
  aiStatusCard: {
    flexDirection: "column",
    borderWidth: 1,
    borderRadius: 12,
    padding: 12,
  },
  formGroup: {
    marginBottom: 16,
  },
  input: {
    borderWidth: 1,
    borderRadius: 8,
    padding: 12,
    fontSize: 16,
  },
  describeInput: {
    minHeight: 88,
    textAlignVertical: "top",
  },
  saveBtn: {
    padding: 16,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center",
    marginTop: 8,
  },
});
