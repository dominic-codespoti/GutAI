import { View, Text, TouchableOpacity } from "react-native";
import { ALLERGY_OPTIONS } from "../src/utils/options";
import { radius, spacing } from "../src/utils/theme";
import { useThemeColors } from "../src/stores/theme";
import * as haptics from "../src/utils/haptics";

interface AllergyChipsProps {
  selected: string[];
  onToggle: (allergy: string) => void;
}

export function AllergyChips({ selected, onToggle }: AllergyChipsProps) {
  const colors = useThemeColors();
  return (
    <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 8 }}>
      {ALLERGY_OPTIONS.map((a) => {
        const active = selected.includes(a);
        return (
          <TouchableOpacity
            key={a}
            onPress={() => {
              haptics.selection();
              onToggle(a);
            }}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: active }}
            accessibilityLabel={a}
            style={{
              backgroundColor: active ? colors.primaryBg : colors.borderLight,
              borderWidth: 1,
              borderColor: active ? colors.primaryLight : colors.border,
              borderRadius: radius.full,
              paddingHorizontal: spacing.lg,
              paddingVertical: spacing.sm,
            }}
          >
            <Text
              style={{
                fontWeight: "600",
                color: active ? colors.primary : colors.textMuted,
              }}
            >
              {a}
            </Text>
          </TouchableOpacity>
        );
      })}
    </View>
  );
}
