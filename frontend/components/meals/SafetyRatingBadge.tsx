import { View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useThemeColors } from "../../src/stores/theme";
import { ratingColor } from "../../src/utils/colors";

interface Props {
  rating: string | null | undefined;
}

const ratingIcon = (rating: string | null | undefined): keyof typeof Ionicons.glyphMap => {
  switch (rating?.toLowerCase()) {
    case "safe":
      return "shield-checkmark";
    case "caution":
      return "warning";
    case "warning":
      return "alert-circle";
    case "avoid":
      return "close-circle";
    default:
      return "help-circle";
  }
};

export function SafetyRatingBadge({ rating }: Props) {
  const colors = useThemeColors();

  if (!rating) return null;

  return (
    <View
      style={{
        backgroundColor: ratingColor(rating),
        borderRadius: 10,
        width: 20,
        height: 20,
        justifyContent: "center",
        alignItems: "center",
      }}
    >
      <Ionicons
        name={ratingIcon(rating)}
        size={13}
        color={colors.textOnPrimary}
      />
    </View>
  );
}
