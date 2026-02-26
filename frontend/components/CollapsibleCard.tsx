import { useState } from "react";
import { View, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";

export function CollapsibleCard({
  title,
  emoji,
  badge,
  badgeColor,
  defaultOpen = false,
  children,
}: {
  title: string;
  emoji: string;
  badge?: string;
  badgeColor?: string;
  defaultOpen?: boolean;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <View
      style={{
        backgroundColor: "#fff",
        borderRadius: 12,
        padding: 16,
        marginBottom: 12,
      }}
    >
      <TouchableOpacity
        onPress={() => setOpen((v) => !v)}
        activeOpacity={0.7}
        style={{
          flexDirection: "row",
          alignItems: "center",
        }}
      >
        <Text
          style={{
            fontSize: 16,
            fontWeight: "600",
            color: "#334155",
            flex: 1,
          }}
        >
          {emoji} {title}
        </Text>
        {badge && badgeColor && (
          <View
            style={{
              backgroundColor: badgeColor + "18",
              borderRadius: 12,
              paddingHorizontal: 10,
              paddingVertical: 4,
              marginRight: 8,
            }}
          >
            <Text
              style={{ fontSize: 13, fontWeight: "700", color: badgeColor }}
            >
              {badge}
            </Text>
          </View>
        )}
        <Ionicons
          name={open ? "chevron-up" : "chevron-down"}
          size={18}
          color="#94a3b8"
        />
      </TouchableOpacity>
      {open && <View style={{ marginTop: 12 }}>{children}</View>}
    </View>
  );
}
