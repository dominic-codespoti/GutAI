import { create } from "zustand";
import { getItem, setItem, deleteItem } from "../utils/storage";
import type { UserProfile } from "../types";
import { authApi } from "../api";
import { getCalendars } from "expo-localization";
import { queryClient } from "../queryClient";
import Purchases from "react-native-purchases";

async function syncTimezone() {
  try {
    const calendars = getCalendars();
    const tz = calendars[0]?.timeZone;
    if (!tz) return;
    const { userApi } = await import("../api");
    const { data: profile } = await userApi.getProfile();
    if (profile.timezoneId !== tz) {
      await userApi.updateProfile({ timezoneId: tz });
    }
  } catch {}
}

interface AuthState {
  user: UserProfile | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (
    email: string,
    password: string,
    displayName: string,
  ) => Promise<void>;
  logout: () => Promise<void>;
  hydrate: () => Promise<void>;
  setUser: (user: UserProfile) => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  isAuthenticated: false,
  isLoading: true,

  login: async (email, password) => {
    const { data } = await authApi.login(email, password);
    await setItem("accessToken", data.accessToken);
    await setItem("refreshToken", data.refreshToken);
    set({ user: data.user, isAuthenticated: true });
    syncTimezone();
  },

  register: async (email, password, displayName) => {
    const { data } = await authApi.register(email, password, displayName);
    await setItem("accessToken", data.accessToken);
    await setItem("refreshToken", data.refreshToken);
    set({ user: data.user, isAuthenticated: true });
    syncTimezone();
  },

  logout: async () => {
    try {
      await authApi.logout();
    } catch {}
    try {
      await Purchases.logOut();
    } catch {}
    const { useSubscriptionStore } = await import("./subscription");
    useSubscriptionStore.getState().reset();
    await deleteItem("accessToken");
    await deleteItem("refreshToken");
    queryClient.clear();
    set({ user: null, isAuthenticated: false });
  },

  hydrate: async () => {
    try {
      const token = await getItem("accessToken");
      if (!token) {
        set({ isLoading: false });
        return;
      }
      const { userApi } = await import("../api");
      const { data } = await userApi.getProfile();
      set({ user: data, isAuthenticated: true, isLoading: false });
      syncTimezone();
    } catch (err: any) {
      const status = err?.response?.status;
      if (status === 401 || status === 403) {
        await deleteItem("accessToken");
        await deleteItem("refreshToken");
        set({ user: null, isAuthenticated: false, isLoading: false });
      } else {
        // Network error / timeout — keep tokens, let user retry
        set({ isLoading: false });
      }
    }
  },

  setUser: (user) => set({ user }),
}));
