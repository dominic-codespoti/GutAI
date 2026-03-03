import axios from "axios";
import { getItem, setItem, deleteItem } from "../utils/storage";
import { Platform } from "react-native";

import Constants from "expo-constants";

const BASE_URL =
  process.env.EXPO_PUBLIC_API_URL ||
  Constants.expoConfig?.extra?.apiUrl ||
  Platform.select({
    android: "http://10.0.2.2:5000",
    ios: "http://localhost:5000",
    default: "http://localhost:5000",
  });

export const api = axios.create({
  baseURL: BASE_URL,
  headers: { "Content-Type": "application/json" },
  timeout: 15000,
});

api.interceptors.request.use(async (config) => {
  const token = await getItem("accessToken");
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

let isRefreshing = false;
let failedQueue: {
  resolve: (token: string) => void;
  reject: (err: unknown) => void;
}[] = [];

function processQueue(error: unknown, token: string | null) {
  failedQueue.forEach((p) => {
    if (error) p.reject(error);
    else p.resolve(token!);
  });
  failedQueue = [];
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;
    if (error.response?.status === 401 && !originalRequest._retry) {
      if (isRefreshing) {
        return new Promise<string>((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        }).then((token) => {
          originalRequest.headers.Authorization = `Bearer ${token}`;
          return api(originalRequest);
        });
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        const refreshToken = await getItem("refreshToken");
        if (!refreshToken) throw new Error("No refresh token");
        const { data } = await axios.post(`${BASE_URL}/api/auth/refresh`, {
          refreshToken,
        });
        await setItem("accessToken", data.accessToken);
        await setItem("refreshToken", data.refreshToken);
        originalRequest.headers.Authorization = `Bearer ${data.accessToken}`;
        processQueue(null, data.accessToken);
        return api(originalRequest);
      } catch (refreshError) {
        processQueue(refreshError, null);
        // Only delete access token; keep refresh token so the next
        // app launch can retry (failure may be transient).
        await deleteItem("accessToken");
        return Promise.reject(refreshError);
      } finally {
        isRefreshing = false;
      }
    }
    return Promise.reject(error);
  },
);
