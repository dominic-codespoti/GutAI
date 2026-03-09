import React from "react";
import { View, Text, TouchableOpacity, StyleSheet } from "react-native";
import { Image } from "expo-image";
import { Ionicons } from "@expo/vector-icons";
import { FoodProduct } from "../src/types";
import { spacing, radius } from "../src/utils/theme";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../src/stores/theme";
import { SourceChip } from "./SourceChip";
import { ratingColor } from "../src/utils/colors";

interface FoodSearchResultProps {
  product: FoodProduct;
  onPress: (product: FoodProduct) => void;
  onDetailPress?: (product: FoodProduct) => void;
  style?: any;
}

export const FoodSearchResult: React.FC<FoodSearchResultProps> = ({
  product,
  onPress,
  onDetailPress,
  style,
}) => {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow } = useThemeShadow();
  const calories = product.calories100g ?? 0;
  const rating = product.safetyRating;

  const styles = React.useMemo(
    () =>
      StyleSheet.create({
        container: {
          flexDirection: "row",
          backgroundColor: colors.card,
          borderRadius: radius.md,
          padding: spacing.sm,
          marginBottom: spacing.sm,
          borderWidth: 1,
          borderColor: colors.borderLight,
        },
        imageContainer: {
          width: 60,
          height: 60,
          borderRadius: radius.sm,
          backgroundColor: colors.bg,
          overflow: "hidden",
          marginRight: spacing.md,
          justifyContent: "center",
          alignItems: "center",
        },
        image: {
          width: "100%",
          height: "100%",
        },
        fallbackImage: {
          width: "100%",
          height: "100%",
          justifyContent: "center",
          alignItems: "center",
        },
        content: {
          flex: 1,
          justifyContent: "center",
        },
        header: {
          flexDirection: "row",
          alignItems: "flex-start",
          justifyContent: "space-between",
        },
        name: {
          ...fonts.h4,
          color: colors.text,
          fontSize: 15,
        },
        brand: {
          ...fonts.caption,
          marginTop: 1,
        },
        infoButton: {
          padding: 4,
          marginLeft: spacing.xs,
        },
        footer: {
          flexDirection: "row",
          alignItems: "center",
          justifyContent: "space-between",
          marginTop: spacing.xs,
        },
        stats: {
          flexDirection: "row",
          alignItems: "center",
          flex: 1,
        },
        statText: {
          ...fonts.small,
          marginRight: spacing.sm,
        },
        ratingContainer: {
          flexDirection: "row",
          alignItems: "center",
        },
        ratingDot: {
          width: 8,
          height: 8,
          borderRadius: 4,
          marginRight: 4,
        },
        sourceChip: {
          paddingVertical: 2,
          paddingHorizontal: 6,
        },
      }),
    [colors, fonts],
  );

  return (
    <TouchableOpacity
      onPress={() => onPress(product)}
      style={[styles.container, shadow, style]}
      activeOpacity={0.7}
    >
      <View style={styles.imageContainer}>
        {product.imageUrl ? (
          <Image
            source={{ uri: product.imageUrl }}
            style={styles.image}
            contentFit="contain"
            transition={200}
            placeholder={{ blurhash: "L6PZfSi_.AyE_3t7t7R**0o#DgR4" }}
            cachePolicy="memory-disk"
            recyclingKey={product.imageUrl}
          />
        ) : (
          <View style={styles.fallbackImage}>
            <Ionicons
              name="fast-food-outline"
              size={24}
              color={colors.textLight}
            />
          </View>
        )}
      </View>

      <View style={styles.content}>
        <View style={styles.header}>
          <View style={{ flex: 1 }}>
            <Text style={styles.name} numberOfLines={1}>
              {product.name}
            </Text>
            {product.brand && (
              <Text style={styles.brand} numberOfLines={1}>
                {product.brand}
              </Text>
            )}
          </View>
          {onDetailPress && (
            <TouchableOpacity
              onPress={(e) => {
                e.stopPropagation();
                onDetailPress(product);
              }}
              style={styles.infoButton}
            >
              <Ionicons
                name="information-circle-outline"
                size={20}
                color={colors.secondary}
              />
            </TouchableOpacity>
          )}
        </View>

        <View style={styles.footer}>
          <View style={styles.stats}>
            {calories != null && (
              <Text style={styles.statText}>
                {Math.round(calories)} kcal/100g
              </Text>
            )}
            {rating != null && (
              <View style={styles.ratingContainer}>
                <View
                  style={[
                    styles.ratingDot,
                    { backgroundColor: ratingColor(rating) },
                  ]}
                />
                <Text style={styles.statText}>Safety: {rating}</Text>
              </View>
            )}
          </View>
          {product.dataSource && (
            <SourceChip
              source={product.dataSource}
              url={product.sourceUrl}
              style={styles.sourceChip}
            />
          )}
        </View>
      </View>
    </TouchableOpacity>
  );
};
