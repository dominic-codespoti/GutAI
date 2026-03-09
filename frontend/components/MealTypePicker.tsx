import { View, Text, TouchableOpacity } from "react-native";
import { MEAL_TYPES } from "../src/utils/constants";
import { useThemeColors } from "../src/stores/theme";

interface MealTypePickerProps {
  selected: string;
  onSelect: (type: string) => void;
}

export function MealTypePicker({ selected, onSelect }: MealTypePickerProps) {
  const colors = useThemeColors();
  return (
    <View style={{ flexDirection: "row", marginBottom: 12 }}>
      {MEAL_TYPES.map((type) => (
        <TouchableOpacity
          key={type}
          onPress={() => onSelect(type)}
          style={{
            flex: 1,
            paddingVertical: 6,
            borderRadius: 6,
            marginHorizontal: 2,
            backgroundColor:
              selected === type ? colors.primaryLight : colors.borderLight,
            alignItems: "center",
            borderWidth: selected === type ? 0 : 1,
            borderColor: colors.border,
          }}
        >
          <Text
            style={{
              fontSize: 11,
              fontWeight: "600",
              color:
                selected === type ? colors.textOnPrimary : colors.textMuted,
            }}
          >
            {type}
          </Text>
        </TouchableOpacity>
      ))}
    </View>
  );
}
