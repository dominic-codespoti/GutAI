import { useState, useEffect } from "react";
import { View, Text, TouchableOpacity, ActivityIndicator } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { BottomSheet } from "../BottomSheet";
import { useMealSheetStore } from "../../src/stores/mealSheet";
import { useMealMutations } from "../../src/hooks/useMealMutations";
import { shiftDate, formatDateLabel, today } from "../../src/utils/date";
import {
  colors,
  fonts,
  radius,
  spacing,
  mealTypeEmoji,
} from "../../src/utils/theme";

export function CopyMealSheet() {
  const mode = useMealSheetStore((s) => s.mode);
  const copyingMeal = useMealSheetStore((s) => s.copyingMeal);
  const close = useMealSheetStore((s) => s.close);
  const visible = mode === "copy-meal" && !!copyingMeal;

  const [targetDate, setTargetDate] = useState(today());
  const { copyMeal } = useMealMutations();

  // Reset target date when sheet opens
  useEffect(() => {
    if (visible) setTargetDate(today());
  }, [visible]);

  const handleCopy = () => {
    if (!copyingMeal) return;
    copyMeal.mutate({ meal: copyingMeal, targetDate });
  };

  if (!copyingMeal) return null;

  const emoji = mealTypeEmoji[copyingMeal.mealType] ?? "🍽️";

  return (
    <BottomSheet visible={visible} onClose={close}>
      <View>
        <Text style={{ ...fonts.h3, marginBottom: spacing.md }}>Copy Meal</Text>

        {/* Source summary */}
        <View
          style={{
            backgroundColor: colors.bg,
            borderRadius: radius.sm,
            padding: spacing.md,
            marginBottom: spacing.lg,
          }}
        >
          <Text
            style={{
              fontWeight: "700",
              fontSize: 15,
              color: colors.text,
              marginBottom: 4,
            }}
          >
            {emoji} {copyingMeal.mealType}
          </Text>
          <Text
            style={{
              fontSize: 13,
              color: colors.textMuted,
              marginBottom: 2,
            }}
          >
            {formatDateLabel(copyingMeal.loggedAt.split("T")[0])}
          </Text>
          {copyingMeal.items.map((item, idx) => (
            <Text
              key={item.id || idx}
              style={{
                fontSize: 13,
                color: colors.textSecondary,
                marginTop: 2,
              }}
            >
              • {item.foodName}{" "}
              <Text style={{ color: colors.textMuted }}>
                ({item.calories} cal)
              </Text>
            </Text>
          ))}
          <Text
            style={{
              fontSize: 13,
              fontWeight: "600",
              color: colors.text,
              marginTop: 6,
            }}
          >
            Total: {copyingMeal.totalCalories} cal · {copyingMeal.totalProteinG}
            g P · {copyingMeal.totalCarbsG}g C · {copyingMeal.totalFatG}g F
          </Text>
        </View>

        {/* Target date picker */}
        <Text
          style={{
            fontSize: 13,
            fontWeight: "600",
            color: colors.textMuted,
            marginBottom: 8,
            textTransform: "uppercase",
            letterSpacing: 0.5,
          }}
        >
          Copy to date
        </Text>
        <View
          style={{
            flexDirection: "row",
            alignItems: "center",
            justifyContent: "center",
            marginBottom: spacing.xl,
            gap: 16,
            backgroundColor: colors.bg,
            borderRadius: radius.sm,
            paddingVertical: 12,
            paddingHorizontal: 16,
          }}
        >
          <TouchableOpacity
            onPress={() => setTargetDate((d) => shiftDate(d, -1))}
            hitSlop={8}
          >
            <Ionicons name="chevron-back" size={22} color={colors.textMuted} />
          </TouchableOpacity>
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              gap: 6,
              minWidth: 140,
              justifyContent: "center",
            }}
          >
            <Ionicons
              name="calendar-outline"
              size={18}
              color={colors.primary}
            />
            <Text
              style={{
                fontSize: 16,
                fontWeight: "600",
                color: colors.text,
              }}
            >
              {formatDateLabel(targetDate)}
            </Text>
          </View>
          <TouchableOpacity
            onPress={() => setTargetDate((d) => shiftDate(d, 1))}
            hitSlop={8}
          >
            <Ionicons
              name="chevron-forward"
              size={22}
              color={colors.textMuted}
            />
          </TouchableOpacity>
        </View>

        {/* Actions */}
        <View
          style={{
            flexDirection: "row",
            justifyContent: "flex-end",
            gap: 12,
          }}
        >
          <TouchableOpacity
            onPress={close}
            style={{ paddingHorizontal: 20, paddingVertical: 12 }}
          >
            <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
              Cancel
            </Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={handleCopy}
            disabled={copyMeal.isPending}
            style={{
              flexDirection: "row",
              alignItems: "center",
              gap: 6,
              backgroundColor: colors.primary,
              paddingHorizontal: 24,
              paddingVertical: 12,
              borderRadius: radius.sm,
            }}
          >
            {copyMeal.isPending ? (
              <ActivityIndicator color="#fff" size="small" />
            ) : (
              <>
                <Ionicons name="copy-outline" size={16} color="#fff" />
                <Text style={{ color: "#fff", fontWeight: "700" }}>Copy</Text>
              </>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </BottomSheet>
  );
}
