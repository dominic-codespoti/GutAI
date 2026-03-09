import { View, Text, TextInput } from "react-native";
import { radius, spacing } from "../src/utils/theme";
import { useThemeColors, useThemeFonts } from "../src/stores/theme";

interface GoalFieldProps {
  label: string;
  value: string;
  onChangeText: (text: string) => void;
  max?: number;
  maxLength?: number;
}

export function GoalField({
  label,
  value,
  onChangeText,
  max,
  maxLength,
}: GoalFieldProps) {
  const colors = useThemeColors();
  const fonts = useThemeFonts();

  const handleChange = (text: string) => {
    // Strip non-numeric characters
    const numeric = text.replace(/[^0-9]/g, "");
    // Enforce max if provided
    if (max && numeric.length > 0 && Number(numeric) > max) return;
    onChangeText(numeric);
  };

  return (
    <View style={{ marginBottom: spacing.md }}>
      <Text style={{ ...fonts.caption, marginBottom: spacing.xs }}>
        {label}
      </Text>
      <TextInput
        value={value}
        onChangeText={handleChange}
        keyboardType="numeric"
        maxLength={maxLength}
        style={{
          borderWidth: 1,
          borderColor: colors.border,
          borderRadius: radius.sm,
          padding: spacing.md,
          fontSize: 16,
          color: colors.text,
          backgroundColor: colors.card,
        }}
      />
    </View>
  );
}
