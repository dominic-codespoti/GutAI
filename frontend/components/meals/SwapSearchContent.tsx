import { useState, useEffect } from "react";
import {
  View,
  Text,
  TextInput,
  ScrollView,
  TouchableOpacity,
  ActivityIndicator,
} from "react-native";
import { useQuery } from "@tanstack/react-query";
import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { foodApi } from "../../src/api";
import { FoodSearchResult } from "../FoodSearchResult";
import { radius, spacing } from "../../src/utils/theme";
import { useThemeColors } from "../../src/stores/theme";
import type { FoodProduct } from "../../src/types";

interface Props {
  initialSearch?: string;
  onSelect: (food: FoodProduct) => void;
  onBack: () => void;
}
export function SwapSearchContent({
  initialSearch = "",
  onSelect,
  onBack,
}: Props) {
  const colors = useThemeColors();
  const router = useRouter();
  const [search, setSearch] = useState(initialSearch);
  const [debounced, setDebounced] = useState(initialSearch);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(search), 350);
    return () => clearTimeout(timer);
  }, [search]);

  const { data, isLoading } = useQuery({
    queryKey: ["swap-food-search", debounced],
    queryFn: ({ signal }) =>
      foodApi.search(debounced, signal).then((r) => r.data),
    enabled: debounced.length >= 2,
    staleTime: 5 * 60 * 1000,
    placeholderData: (prev) => prev,
  });

  return (
    <View style={{ flex: 1 }}>
      <View
        style={{
          flexDirection: "row",
          alignItems: "center",
          marginBottom: spacing.md,
          gap: 8,
        }}
      >
        <TouchableOpacity onPress={onBack} style={{ padding: 4 }}>
          <Ionicons name="arrow-back" size={22} color={colors.textSecondary} />
        </TouchableOpacity>
        <Text
          style={{
            fontSize: 16,
            fontWeight: "700",
            color: colors.text,
            flex: 1,
          }}
        >
          Swap food item
        </Text>
      </View>

      <TextInput
        placeholder="Search foods..."
        placeholderTextColor={colors.textLight}
        value={search}
        onChangeText={setSearch}
        autoFocus
        autoCapitalize="none"
        autoCorrect={false}
        returnKeyType="search"
        maxLength={200}
        style={{
          backgroundColor: colors.bg,
          borderRadius: radius.sm,
          padding: spacing.md,
          fontSize: 15,
          color: colors.text,
          borderWidth: 1,
          borderColor: colors.border,
          marginBottom: spacing.md,
        }}
      />

      <ScrollView
        style={{ maxHeight: 400 }}
        keyboardShouldPersistTaps="handled"
      >
        {isLoading && (
          <ActivityIndicator
            color={colors.primary}
            style={{ marginVertical: spacing.lg }}
          />
        )}
        {data?.map((food) => (
          <FoodSearchResult
            key={food.id || food.name}
            product={food}
            onPress={() => onSelect(food)}
            onDetailPress={() => {
              if (
                food.id &&
                food.id !== "00000000-0000-0000-0000-000000000000"
              ) {
                router.push(`/food/${food.id}`);
              }
            }}
            style={{ marginHorizontal: spacing.md, marginTop: spacing.sm }}
          />
        ))}
        {debounced.length >= 2 && !isLoading && data?.length === 0 && (
          <Text
            style={{
              textAlign: "center",
              color: colors.textMuted,
              padding: spacing.lg,
            }}
          >
            No results found
          </Text>
        )}
        {debounced.length < 2 && (
          <Text
            style={{
              textAlign: "center",
              color: colors.textMuted,
              padding: spacing.lg,
            }}
          >
            Type at least 2 characters to search
          </Text>
        )}
      </ScrollView>
    </View>
  );
}
