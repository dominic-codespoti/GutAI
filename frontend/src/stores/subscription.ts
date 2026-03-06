import { create } from "zustand";
import { Platform } from "react-native";
import Purchases, { LOG_LEVEL } from "react-native-purchases";
import RevenueCatUI, { PAYWALL_RESULT } from "react-native-purchases-ui";

const ENTITLEMENT_ID = "Gut Lens Pro";
const APPLE_API_KEY = "appl_CMDYhmUXPKIYvLBPOqaSrGbPtyL";
const GOOGLE_API_KEY = "goog_VWxjwVUrPyHZbITZozKEhgAoKvo";
const TEST_API_KEY = "test_UleVTskToJYezcnQbuEpfleLOYX";

interface SubscriptionState {
  isPro: boolean;
  isLoaded: boolean;
  checkEntitlement: () => Promise<void>;
  restore: () => Promise<boolean>;
  reset: () => void;
}

export const useSubscriptionStore = create<SubscriptionState>((set) => ({
  isPro: false,
  isLoaded: false,

  checkEntitlement: async () => {
    try {
      const customerInfo = await Purchases.getCustomerInfo();
      const active =
        typeof customerInfo.entitlements.active[ENTITLEMENT_ID] !== "undefined";
      set({ isPro: active, isLoaded: true });
    } catch {
      set({ isLoaded: true });
    }
  },

  restore: async () => {
    try {
      const customerInfo = await Purchases.restorePurchases();
      const active =
        typeof customerInfo.entitlements.active[ENTITLEMENT_ID] !== "undefined";
      set({ isPro: active });
      return active;
    } catch {
      return false;
    }
  },

  reset: () => set({ isPro: false, isLoaded: false }),
}));

export async function configurePurchases(userId: string) {
  if (__DEV__) {
    Purchases.setLogLevel(LOG_LEVEL.VERBOSE);
    await Purchases.configure({ apiKey: TEST_API_KEY });
  } else if (Platform.OS === "ios") {
    await Purchases.configure({ apiKey: APPLE_API_KEY });
  } else if (Platform.OS === "android") {
    await Purchases.configure({ apiKey: GOOGLE_API_KEY });
  }

  await Purchases.logIn(userId);
}

export async function presentPaywall(): Promise<boolean> {
  const paywallResult: PAYWALL_RESULT = await RevenueCatUI.presentPaywall();

  switch (paywallResult) {
    case PAYWALL_RESULT.PURCHASED:
    case PAYWALL_RESULT.RESTORED:
      await useSubscriptionStore.getState().checkEntitlement();
      return true;
    default:
      return false;
  }
}
