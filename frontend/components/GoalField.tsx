import { View, Text, TextInput } from "react-native";
import { radius, spacing } from "../src/utils/theme";
import { useThemeColors, useThemeFonts } from "../src/stores/theme";

interface GoalFieldProps {
  label: string;
  value: string;
  onChangeText: (text: string) => void;
}

export function GoalField({ label, value, onChangeText }: GoalFieldProps) {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  return (
    <View style={{ marginBottom: spacing.md }}>
      <Text style={{ ...fonts.caption, marginBottom: spacing.xs }}>
        {label}
      </Text>
      <TextInput
        value={value}
        onChangeText={onChangeText}
        keyboardType="numeric"
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
