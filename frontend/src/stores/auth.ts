import { create } from 'zustand';
import { getItem, setItem, deleteItem } from '../utils/storage';
import type { UserProfile } from '../types';
import { authApi } from '../api';

interface AuthState {
  user: UserProfile | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
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
    await setItem('accessToken', data.accessToken);
    await setItem('refreshToken', data.refreshToken);
    set({ user: data.user, isAuthenticated: true });
  },

  register: async (email, password, displayName) => {
    const { data } = await authApi.register(email, password, displayName);
    await setItem('accessToken', data.accessToken);
    await setItem('refreshToken', data.refreshToken);
    set({ user: data.user, isAuthenticated: true });
  },

  logout: async () => {
    try { await authApi.logout(); } catch {}
    await deleteItem('accessToken');
    await deleteItem('refreshToken');
    set({ user: null, isAuthenticated: false });
  },

  hydrate: async () => {
    try {
      const token = await getItem('accessToken');
      if (!token) {
        set({ isLoading: false });
        return;
      }
      const { userApi } = await import('../api');
      const { data } = await userApi.getProfile();
      set({ user: data, isAuthenticated: true, isLoading: false });
    } catch {
      await deleteItem('accessToken');
      await deleteItem('refreshToken');
      set({ user: null, isAuthenticated: false, isLoading: false });
    }
  },

  setUser: (user) => set({ user }),
}));
