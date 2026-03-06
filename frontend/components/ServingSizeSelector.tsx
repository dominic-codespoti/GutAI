import { View, Text, TextInput, TouchableOpacity } from "react-native";
import { useThemeColors } from "../src/stores/theme";
import { buildServingPresets } from "../src/utils/nutrition";
import type { FoodProduct } from "../src/types";

interface ServingSizeSelectorProps {
  servingG: number;
  onServingChange: (grams: number) => void;
  customText: string;
  onCustomTextChange: (text: string) => void;
  multiplier: number;
  onMultiplierChange: (m: number) => void;
  product?: Pick<FoodProduct, "servingQuantity" | "servingSize"> | null;
  summaryText?: string;
}

export function ServingSizeSelector({
  servingG,
  onServingChange,
  customText,
  onCustomTextChange,
  multiplier,
  onMultiplierChange,
  product,
  summaryText,
}: ServingSizeSelectorProps) {
  const colors = useThemeColors();
  const presets = buildServingPresets(product);

  return (
    <View>
      <Text
        style={{
          fontSize: 12,
          fontWeight: "600",
          color: "#334155",
          marginBottom: 6,
        }}
      >
        Serving size:
      </Text>
      <View
        style={{
          flexDirection: "row",
          flexWrap: "wrap",
          gap: 6,
          marginBottom: 8,
        }}
      >
        {presets.map((preset) => (
          <TouchableOpacity
            key={preset.label}
            onPress={() => {
              onServingChange(preset.grams);
              onCustomTextChange("");
            }}
            style={{
              paddingHorizontal: 10,
              paddingVertical: 6,
              borderRadius: 6,
              backgroundColor:
                servingG === preset.grams && customText === ""
                  ? colors.primaryLight
                  : colors.borderLight,
            }}
          >
            <Text
              style={{
                fontSize: 11,
                fontWeight: "600",
                color:
                  servingG === preset.grams && customText === ""
                    ? "#fff"
                    : colors.textMuted,
              }}
            >
              {preset.label}
            </Text>
          </TouchableOpacity>
        ))}
      </View>

      <View
        style={{
          flexDirection: "row",
          alignItems: "center",
          gap: 6,
          marginBottom: 8,
        }}
      >
        <Text style={{ fontSize: 12, fontWeight: "600", color: "#334155" }}>
          Custom (g):
        </Text>
        <TextInput
          value={customText}
          onChangeText={(v) => {
            onCustomTextChange(v);
            const n = Number(v);
            if (n > 0) onServingChange(n);
          }}
          keyboardType="numeric"
          placeholder="e.g. 75"
          placeholderTextColor={colors.textLight}
          style={{
            flex: 1,
            borderWidth: 1,
            borderColor:
              customText !== "" ? colors.primaryLight : colors.border,
            borderRadius: 6,
            paddingHorizontal: 10,
            paddingVertical: 4,
            fontSize: 13,
            color: colors.text,
            backgroundColor: colors.card,
          }}
        />
      </View>

      <Text
        style={{
          fontSize: 12,
          fontWeight: "600",
          color: "#334155",
          marginBottom: 6,
        }}
      >
        Multiplier:
      </Text>
      <View style={{ flexDirection: "row", gap: 6, marginBottom: 8 }}>
        {[1, 2, 3, 4, 5].map((m) => (
          <TouchableOpacity
            key={m}
            onPress={() => onMultiplierChange(m)}
            style={{
              flex: 1,
              paddingVertical: 6,
              borderRadius: 6,
              backgroundColor:
                multiplier === m ? colors.primaryLight : colors.borderLight,
              alignItems: "center",
            }}
          >
            <Text
              style={{
                fontSize: 12,
                fontWeight: "600",
                color: multiplier === m ? "#fff" : colors.textMuted,
              }}
            >
              {m}×
            </Text>
          </TouchableOpacity>
        ))}
      </View>

      {summaryText && (
        <Text style={{ fontSize: 11, color: colors.textMuted }}>
          {summaryText}
        </Text>
      )}
    </View>
  );
}
