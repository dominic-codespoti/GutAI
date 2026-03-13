import { Platform } from "react-native";
import * as Haptics from "expo-haptics";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { foodApi } from "../api";
import { toast } from "../stores/toast";
import type { FavoriteFood } from "../types";

export function useFavorites() {
  const queryClient = useQueryClient();

  const {
    data: favorites = [],
    isLoading,
    refetch,
  } = useQuery({
    queryKey: ["favorite-foods"],
    queryFn: () => foodApi.favorites().then((r) => r.data),
    staleTime: 5 * 60 * 1000,
  });

  const favoriteIds = new Set(favorites.map((f) => f.foodProductId));

  const addFavorite = useMutation({
    mutationFn: (foodProductId: string) => foodApi.addFavorite(foodProductId),
    onMutate: async (foodProductId) => {
      await queryClient.cancelQueries({ queryKey: ["favorite-foods"] });
      const prev = queryClient.getQueryData<FavoriteFood[]>(["favorite-foods"]);
      queryClient.setQueryData<FavoriteFood[]>(["favorite-foods"], (old) => [
        ...(old ?? []),
        {
          foodProductId,
          foodName: "",
          brand: null,
          calories100g: null,
          protein100g: null,
          carbs100g: null,
          fat100g: null,
          servingSize: null,
          servingQuantity: null,
          servingWeightG: null,
          imageUrl: null,
          createdAt: new Date().toISOString(),
        },
      ]);
      return { prev };
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["favorite-foods"] });
      if (Platform.OS !== "web") {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
      }
    },
    onError: (_err, _id, ctx) => {
      if (ctx?.prev) queryClient.setQueryData(["favorite-foods"], ctx.prev);
      toast.error("Failed to favorite food");
    },
  });

  const removeFavorite = useMutation({
    mutationFn: (foodProductId: string) =>
      foodApi.removeFavorite(foodProductId),
    onMutate: async (foodProductId) => {
      await queryClient.cancelQueries({ queryKey: ["favorite-foods"] });
      const prev = queryClient.getQueryData<FavoriteFood[]>(["favorite-foods"]);
      queryClient.setQueryData<FavoriteFood[]>(["favorite-foods"], (old) =>
        (old ?? []).filter((f) => f.foodProductId !== foodProductId),
      );
      return { prev };
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["favorite-foods"] });
      if (Platform.OS !== "web") {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
      }
    },
    onError: (_err, _id, ctx) => {
      if (ctx?.prev) queryClient.setQueryData(["favorite-foods"], ctx.prev);
      toast.error("Failed to unfavorite food");
    },
  });

  const toggleFavorite = (foodProductId: string) => {
    if (favoriteIds.has(foodProductId)) {
      removeFavorite.mutate(foodProductId);
    } else {
      addFavorite.mutate(foodProductId);
    }
  };

  const isFavorite = (foodProductId: string | undefined) =>
    !!foodProductId && favoriteIds.has(foodProductId);

  return {
    favorites,
    isLoading,
    refetch,
    addFavorite,
    removeFavorite,
    toggleFavorite,
    isFavorite,
  };
}
