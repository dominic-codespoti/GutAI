import { View, Modal, Pressable } from "react-native";
import { colors, radius, spacing } from "../src/utils/theme";

interface BottomSheetProps {
  visible: boolean;
  onClose: () => void;
  children: React.ReactNode;
  maxHeight?: string;
}

export function BottomSheet({
  visible,
  onClose,
  children,
  maxHeight = "85%",
}: BottomSheetProps) {
  return (
    <Modal
      visible={visible}
      animationType="slide"
      transparent
      onRequestClose={onClose}
    >
      <Pressable
        style={{
          flex: 1,
          backgroundColor: "rgba(0,0,0,0.5)",
          justifyContent: "flex-end",
        }}
        onPress={onClose}
      >
        <Pressable
          style={{
            backgroundColor: colors.card,
            borderTopLeftRadius: radius.xl,
            borderTopRightRadius: radius.xl,
            padding: spacing.xxl,
            maxHeight: maxHeight as any,
          }}
          onPress={() => {}}
        >
          <View
            style={{
              width: 36,
              height: 4,
              borderRadius: 2,
              backgroundColor: colors.borderLight,
              alignSelf: "center",
              marginBottom: spacing.lg,
            }}
          />
          {children}
        </Pressable>
      </Pressable>
    </Modal>
  );
}
