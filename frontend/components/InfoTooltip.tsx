import { useState } from "react";
import { TouchableOpacity, Modal, View, Text, Pressable } from "react-native";
import { Ionicons } from "@expo/vector-icons";

export function InfoTooltip({ title, body }: { title: string; body: string }) {
  const [visible, setVisible] = useState(false);
  return (
    <>
      <TouchableOpacity
        onPress={() => setVisible(true)}
        hitSlop={8}
        style={{ marginLeft: 4 }}
      >
        <Ionicons name="information-circle-outline" size={16} color="#94a3b8" />
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
            backgroundColor: "rgba(0,0,0,0.4)",
            justifyContent: "center",
            alignItems: "center",
            padding: 32,
          }}
        >
          <Pressable
            onPress={(e) => e.stopPropagation()}
            style={{
              backgroundColor: "#fff",
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
                color: "#0f172a",
                marginBottom: 8,
              }}
            >
              {title}
            </Text>
            <Text style={{ fontSize: 14, color: "#475569", lineHeight: 20 }}>
              {body}
            </Text>
            <TouchableOpacity
              onPress={() => setVisible(false)}
              style={{
                marginTop: 16,
                alignSelf: "flex-end",
                paddingHorizontal: 16,
                paddingVertical: 8,
                backgroundColor: "#f1f5f9",
                borderRadius: 8,
              }}
            >
              <Text style={{ fontWeight: "600", color: "#334155" }}>
                Got it
              </Text>
            </TouchableOpacity>
          </Pressable>
        </Pressable>
      </Modal>
    </>
  );
}
