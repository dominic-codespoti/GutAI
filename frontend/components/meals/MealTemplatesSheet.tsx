import { useEffect, useState } from "react";
import { ActivityIndicator, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { BottomSheet } from "../BottomSheet";
import { useThemeColors } from "../../src/stores/theme";
import { radius, spacing } from "../../src/utils/theme";
import type { MealTemplate } from "../../src/types";
import { deleteMealTemplate, listMealTemplates } from "../../src/utils/mealTemplates";

interface Props {
  visible: boolean;
  onClose: () => void;
  onUse: (template: MealTemplate) => void;
}

export function MealTemplatesSheet({ visible, onClose, onUse }: Props) {
  const colors = useThemeColors();
  const [templates, setTemplates] = useState<MealTemplate[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!visible) return;
    setLoading(true);
    listMealTemplates().then(setTemplates).finally(() => setLoading(false));
  }, [visible]);

  const remove = async (id: string) => {
    await deleteMealTemplate(id);
    setTemplates((current) => current.filter((template) => template.id !== id));
  };

  return (
    <BottomSheet visible={visible} onClose={onClose} paddingTop={spacing.lg} paddingHorizontal={spacing.lg}>
      <Text style={{ fontSize: 20, fontWeight: "700", color: colors.text, marginBottom: spacing.md }}>
        Meal templates
      </Text>
      {loading ? <ActivityIndicator color={colors.primary} /> : templates.length === 0 ? (
        <Text style={{ color: colors.textMuted, marginBottom: spacing.lg }}>
          Save a logged meal as a template to reuse it later.
        </Text>
      ) : templates.map((template) => (
        <View key={template.id} style={{ flexDirection: "row", alignItems: "center", paddingVertical: spacing.sm, borderBottomWidth: 1, borderBottomColor: colors.borderLight }}>
          <TouchableOpacity onPress={() => onUse(template)} style={{ flex: 1 }} accessibilityRole="button" accessibilityLabel={`Use ${template.name} template`}>
            <Text style={{ color: colors.text, fontWeight: "600" }}>{template.name}</Text>
            <Text style={{ color: colors.textMuted, fontSize: 12, marginTop: 2 }}>{template.items.length} items · {template.mealType}</Text>
          </TouchableOpacity>
          <TouchableOpacity onPress={() => remove(template.id)} accessibilityRole="button" accessibilityLabel={`Delete ${template.name} template`} style={{ padding: spacing.sm }}>
            <Ionicons name="trash-outline" size={18} color={colors.danger} />
          </TouchableOpacity>
        </View>
      ))}
      <TouchableOpacity onPress={onClose} style={{ marginTop: spacing.lg, padding: spacing.md, borderRadius: radius.sm, backgroundColor: colors.bg }}>
        <Text style={{ textAlign: "center", color: colors.textMuted, fontWeight: "600" }}>Close</Text>
      </TouchableOpacity>
    </BottomSheet>
  );
}
