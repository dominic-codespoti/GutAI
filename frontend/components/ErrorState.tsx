import { View, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useThemeColors } from "../src/stores/theme";

export function ErrorState({
  message = "Something went wrong",
  onRetry,
}: {
  message?: string;
  onRetry?: () => void;
}) {
  const colors = useThemeColors();
  return (
    <View style={{ alignItems: "center", paddingVertical: 40 }}>
      <Ionicons name="cloud-offline-outline" size={48} color={colors.danger} />
      <Text
        style={{
          color: colors.danger,
          marginTop: 12,
          fontSize: 16,
          fontWeight: "600",
        }}
      >
        {message}
      </Text>
      {onRetry && (
        <TouchableOpacity
          onPress={onRetry}
          style={{
            marginTop: 12,
            backgroundColor: colors.primary,
            paddingHorizontal: 24,
            paddingVertical: 10,
            borderRadius: 8,
          }}
        >
          <Text style={{ color: colors.textOnPrimary, fontWeight: "600" }}>
            Retry
          </Text>
        </TouchableOpacity>
      )}
    </View>
  );
}
