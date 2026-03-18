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
import type { CustomFood } from "../../src/types";

export default function CreateCustomFoodScreen() {
  const t = useThemeColors();
  const router = useRouter();
  const queryClient = useQueryClient();

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

  const parseMutation = useMutation({
    mutationFn: async (imageUri: string) => {
      const resp = await foodApi.parseLabel(imageUri, "image/jpeg");
      return resp.data;
    },
    onSuccess: (data) => {
      setForm((prev) => ({
        ...prev,
        ...data,
      }));
      toast.success("Label parsed successfully!");
    },
    onError: () => {
      toast.error("Failed to parse label or you need a Premium subscription.");
    },
  });

  const saveMutation = useMutation({
    mutationFn: async () => {
      await foodApi.createCustomFood(form);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["custom-foods"] });
      toast.success("Food saved successfully!");
      router.navigate("/(tabs)/scan?tab=custom");
    },
    onError: () => {
      toast.error("Failed to save custom food.");
    },
  });

  const handleScanLabel = async () => {
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
      parseMutation.mutate(result.assets[0].uri);
    }
  };

  const handleImagePicker = async () => {
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
      parseMutation.mutate(result.assets[0].uri);
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

  return (
    <SafeScreen edges={["top"]}>
      <View style={{ flexDirection: "row", alignItems: "center", padding: 16 }}>
        <TouchableOpacity
          onPress={() => router.back()}
          style={{ paddingRight: 16 }}
        >
          <Ionicons name="arrow-back" size={24} color={t.text} />
        </TouchableOpacity>
        <Text style={{ fontSize: 20, fontWeight: "bold", color: t.text }}>
          Create Custom Food
        </Text>
      </View>

      <ScrollView style={{ paddingHorizontal: 16 }}>
        <View
          style={{
            flexDirection: "row",
            gap: 12,
            marginBottom: 24,
            justifyContent: "space-between",
          }}
        >
          <TouchableOpacity
            style={[styles.cameraBtn, { backgroundColor: t.primary, flex: 1 }]}
            onPress={handleScanLabel}
            disabled={parseMutation.isPending}
          >
            <Ionicons name="camera" size={20} color="#fff" />
            <Text style={{ color: "#fff", fontWeight: "bold", marginLeft: 8 }}>
              Scan Label
            </Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={[
              styles.cameraBtn,
              {
                backgroundColor: t.card,
                flex: 1,
                borderWidth: 1,
                borderColor: t.border,
              },
            ]}
            onPress={handleImagePicker}
            disabled={parseMutation.isPending}
          >
            <Ionicons name="image" size={20} color={t.text} />
            <Text style={{ color: t.text, fontWeight: "bold", marginLeft: 8 }}>
              Photo Library
            </Text>
          </TouchableOpacity>
        </View>

        {parseMutation.isPending && (
          <View style={{ marginVertical: 20, alignItems: "center" }}>
            <ActivityIndicator size="large" color={t.primary} />
            <Text style={{ marginTop: 8, color: t.textMuted }}>
              Parsing AI Label (Premium)...
            </Text>
          </View>
        )}

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
              value={form.servingSize ? form.servingSize.toString() : ""}
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
            value={form.calories ? form.calories.toString() : ""}
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
                value={
                  form[macro.key as keyof CustomFood]
                    ? form[macro.key as keyof CustomFood]?.toString()
                    : ""
                }
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
            placeholder="Scanned from label"
            placeholderTextColor={t.textMuted}
            multiline
          />
        </View>

        <TouchableOpacity
          style={[
            styles.saveBtn,
            {
              backgroundColor: t.primary,
              opacity: !form.name || saveMutation.isPending ? 0.5 : 1,
            },
          ]}
          onPress={() => saveMutation.mutate()}
          disabled={!form.name || saveMutation.isPending}
        >
          {saveMutation.isPending ? (
            <ActivityIndicator color="#fff" />
          ) : (
            <Text style={{ color: "#fff", fontWeight: "bold", fontSize: 16 }}>
              Save Custom Food
            </Text>
          )}
        </TouchableOpacity>
        <View style={{ height: 40 }} />
      </ScrollView>
    </SafeScreen>
  );
}

const styles = StyleSheet.create({
  cameraBtn: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    padding: 14,
    borderRadius: 12,
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
  saveBtn: {
    padding: 16,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center",
    marginTop: 8,
  },
});
