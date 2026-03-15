import { useState } from "react";
import { TouchableOpacity, Modal, View, Text, Pressable } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useThemeColors } from "../src/stores/theme";
import * as haptics from "../src/utils/haptics";

export function InfoTooltip({ title, body }: { title: string; body: string }) {
  const [visible, setVisible] = useState(false);
  const colors = useThemeColors();
  return (
    <>
      <TouchableOpacity
        onPress={() => {
          haptics.light();
          setVisible(true);
        }}
        hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
        style={{ marginLeft: 4, padding: 8 }}
        accessibilityRole="button"
        accessibilityLabel="More information"
      >
        <Ionicons
          name="information-circle-outline"
          size={16}
          color={colors.textMuted}
        />
      </TouchableOpacity>
      <Modal
        visible={visible}
        transparent
        animationType="fade"
        onRequestClose={() => setVisible(false)}
      >
        <Pressable
          onPress={() => setVisible(false)}
          style={{
            flex: 1,
            backgroundColor: colors.overlay,
            justifyContent: "center",
            alignItems: "center",
            padding: 32,
          }}
        >
          <Pressable
            onPress={(e) => e.stopPropagation()}
            style={{
              backgroundColor: colors.card,
              borderRadius: 16,
              padding: 20,
              width: "100%",
              maxWidth: 320,
            }}
          >
            <Text
              style={{
                fontSize: 16,
                fontWeight: "700",
                color: colors.text,
                marginBottom: 8,
              }}
            >
              {title}
            </Text>
            <Text
              style={{
                fontSize: 14,
                color: colors.textSecondary,
                lineHeight: 20,
              }}
            >
              {body}
            </Text>
            <TouchableOpacity
              onPress={() => setVisible(false)}
              style={{
                marginTop: 16,
                alignSelf: "flex-end",
                paddingHorizontal: 16,
                paddingVertical: 8,
                backgroundColor: colors.borderLight,
                borderRadius: 8,
              }}
            >
              <Text style={{ fontWeight: "600", color: colors.text }}>
                Got it
              </Text>
            </TouchableOpacity>
          </Pressable>
        </Pressable>
      </Modal>
    </>
  );
}
