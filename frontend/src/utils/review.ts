import { Platform } from "react-native";
import * as StoreReview from "expo-store-review";
import { getItem, setItem } from "./storage";

const REVIEW_KEY = "hasPromptedReview";

export async function maybeRequestReview() {
  if (Platform.OS === "web") return;
  try {
    const prompted = await getItem(REVIEW_KEY);
    if (prompted) return;
    const available = await StoreReview.isAvailableAsync();
    if (!available) return;
    await StoreReview.requestReview();
    await setItem(REVIEW_KEY, "true");
  } catch {}
}
