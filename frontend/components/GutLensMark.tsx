import { View, Text, type ViewStyle } from "react-native";
import { useThemeColors } from "../src/stores/theme";
import { fontFamilies } from "../src/utils/theme";

type GutLensMarkProps = {
  size?: number;
  style?: ViewStyle;
  accessibilityLabel?: string;
};

export function GutLensMark({
  size = 72,
  style,
  accessibilityLabel = "GutLens",
}: GutLensMarkProps) {
  const colors = useThemeColors();

  return (
    <View
      accessible
      accessibilityRole="image"
      accessibilityLabel={accessibilityLabel}
      style={[
        {
          width: size,
          height: size,
          alignItems: "center",
          justifyContent: "center",
          borderTopLeftRadius: size * 0.35,
          borderTopRightRadius: size * 0.35,
          borderBottomRightRadius: size * 0.35,
          borderBottomLeftRadius: size * 0.1,
          backgroundColor: colors.primary,
        },
        style,
      ]}
    >
      <Text
        style={{
          color: colors.textOnPrimary,
          fontFamily: fontFamilies.displayItalic,
          fontSize: size * 0.52,
          lineHeight: size * 0.78,
        }}
      >
        G
      </Text>
    </View>
  );
}
