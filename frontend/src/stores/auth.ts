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

function isTokenExpired(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split(".")[1]));
    // expired if less than 60s remaining
    return payload.exp * 1000 < Date.now() + 60_000;
  } catch {
    return true;
  }
}

let _retryCount = 0;
const MAX_RETRIES = 8;

function retryHydrate(get: () => AuthState) {
  if (_retryCount >= MAX_RETRIES) return;
  _retryCount++;
  const delay = Math.min(1000 * Math.pow(2, _retryCount), 30_000);
  setTimeout(() => get().connect(), delay);
}

interface AuthState {
  user: UserProfile | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  isReconnecting: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (
    email: string,
    password: string,
    displayName: string,
  ) => Promise<void>;
  logout: () => Promise<void>;
  hydrate: () => Promise<void>;
  connect: () => Promise<void>;
  setUser: (user: UserProfile) => void;
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  isAuthenticated: false,
  isLoading: true,
  isReconnecting: false,

  login: async (email, password) => {
    const { data } = await authApi.login(email, password);
    await setItem("accessToken", data.accessToken);
    await setItem("refreshToken", data.refreshToken);
    _retryCount = 0;
    set({ user: data.user, isAuthenticated: true });
    syncTimezone();
  },

  register: async (email, password, displayName) => {
    const { data } = await authApi.register(email, password, displayName);
    await setItem("accessToken", data.accessToken);
    await setItem("refreshToken", data.refreshToken);
    _retryCount = 0;
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
      let token = await getItem("accessToken");
      const refreshToken = await getItem("refreshToken");

      if (!token && !refreshToken) {
        set({ isLoading: false, isReconnecting: false });
        return;
      }

      // Proactively refresh if access token is missing or expired
      if ((!token || isTokenExpired(token)) && refreshToken) {
        try {
          const { data } = await authApi.refresh(refreshToken);
          await setItem("accessToken", data.accessToken);
          await setItem("refreshToken", data.refreshToken);
          token = data.accessToken;
        } catch {
          // Backend might be cold-starting. Don't drop to login — retry.
          set({ isLoading: false, isReconnecting: true });
          retryHydrate(get);
          return;
        }
      }

      if (!token) {
        set({ isLoading: false, isReconnecting: false });
        return;
      }

      const { userApi } = await import("../api");
      const { data } = await userApi.getProfile();
      _retryCount = 0;
      set({ user: data, isAuthenticated: true, isLoading: false, isReconnecting: false });
      syncTimezone();
    } catch (err: any) {
      const status = err?.response?.status;
      if (status === 401 || status === 403) {
        // Only delete access token; keep refresh token for next attempt
        await deleteItem("accessToken");
        set({ user: null, isAuthenticated: false, isLoading: false, isReconnecting: false });
      } else {
        // Network error / cold start — keep tokens, retry
        set({ isLoading: false, isReconnecting: true });
        retryHydrate(get);
      }
    }
  },

  connect: async () => {
    set({ isReconnecting: true });
    try {
      let token = await getItem("accessToken");
      const refreshToken = await getItem("refreshToken");

      if (!token && !refreshToken) {
        set({ isLoading: false, isReconnecting: false });
        return;
      }

      if ((!token || isTokenExpired(token)) && refreshToken) {
        const { data } = await authApi.refresh(refreshToken);
        await setItem("accessToken", data.accessToken);
        await setItem("refreshToken", data.refreshToken);
        token = data.accessToken;
      }

      if (!token) {
        set({ isLoading: false, isReconnecting: false });
        return;
      }

      const { userApi } = await import("../api");
      const { data } = await userApi.getProfile();
      _retryCount = 0;
      set({ user: data, isAuthenticated: true, isLoading: false, isReconnecting: false });
      syncTimezone();
    } catch (err: any) {
      const status = err?.response?.status;
      if (status === 401 || status === 403) {
        await deleteItem("accessToken");
        set({ user: null, isAuthenticated: false, isLoading: false, isReconnecting: false });
      } else {
        set({ isReconnecting: true });
        retryHydrate(get);
      }
    }
  },

  setUser: (user) => set({ user }),
}));
