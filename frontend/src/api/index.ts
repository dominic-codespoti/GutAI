import { api } from "./client";
import { Platform } from "react-native";
import { shiftDate, toLocalDateStr } from "../utils/date";
import { getDeviceTimezoneId } from "../utils/timezone";
import { getItem } from "../utils/storage";
import type {
  AuthResponse,
  MealLog,
  CreateMealRequest,
  NaturalLanguageMealRequest,
  NaturalLanguageResponse,
  FoodProduct,
  FoodAdditive,
  SafetyReport,
  GutRiskAssessment,
  FodmapAssessment,
  SubstitutionResult,
  GlycemicAssessment,
  PersonalizedScore,
  SymptomLog,
  SymptomType,
  CreateSymptomRequest,
  DailyNutritionSummary,
  Correlation,
  UserProfile,
  NutritionTrend,
  AdditiveExposure,
  TriggerFood,
  UserFoodAlert,
  ChangePasswordRequest,
  DataExport,
  FoodDiaryAnalysis,
  EliminationDietStatus,
  ChatMessage,
  RecentFood,
  Streak,
  MealTypeNutrition,
  FavoriteFood,
  CustomFood,
  MealScanDraft,
  MealScanConfirmRequest,
  PairingCodeResponse,
  LinkedAccessToken,
  ImportMealsRequest,
  ImportMealsResult,
} from "../types";

interface UpdateProfileRequest {
  displayName?: string;
  allergies?: string[];
  dietaryPreferences?: string[];
  gutConditions?: string[];
  timezoneId?: string;
  onboardingCompleted?: boolean;
}
interface UpdateGoalsRequest {
  dailyCalorieGoal: number;
  dailyProteinGoalG: number;
  dailyCarbGoalG: number;
  dailyFatGoalG: number;
  dailyFiberGoalG: number;
}

/** JS getTimezoneOffset() returns minutes behind UTC. Passed to backend for legacy local-day filtering. */
const tz = () => new Date().getTimezoneOffset();
const tzId = () => getDeviceTimezoneId();
export const authApi = {
  register: (email: string, password: string, displayName: string) =>
    api.post<AuthResponse>("/api/auth/register", {
      email,
      password,
      displayName,
    }),
  login: (email: string, password: string) =>
    api.post<AuthResponse>("/api/auth/login", { email, password }),
  refresh: (refreshToken: string) =>
    api.post<AuthResponse>("/api/auth/refresh", { refreshToken }),
  logout: () => api.post("/api/auth/logout"),
  changePassword: (data: ChangePasswordRequest) =>
    api.post("/api/auth/change-password", data),
};

export const mealApi = {
  list: (date?: string) =>
    api.get<MealLog[]>("/api/meals", {
      params: { date, tzOffsetMinutes: tz(), timezoneId: tzId() },
    }),
  get: (id: string) => api.get<MealLog>(`/api/meals/${id}`),
  create: (data: CreateMealRequest) => api.post<MealLog>("/api/meals", data),
  update: (id: string, data: CreateMealRequest) =>
    api.put<MealLog>(`/api/meals/${id}`, data),
  delete: (id: string) => api.delete(`/api/meals/${id}`),
  parseNatural: (data: NaturalLanguageMealRequest) =>
    api.post<NaturalLanguageResponse>("/api/meals/log-natural", data),
  dailySummary: (date: string) =>
    api.get<DailyNutritionSummary>(`/api/meals/daily-summary/${date}`, {
      params: { tzOffsetMinutes: tz(), timezoneId: tzId() },
    }),
  export: (from?: string, to?: string) =>
    api.get<DataExport>("/api/meals/export", {
      params: { from, to, timezoneId: tzId() },
    }),
  recentFoods: (limit?: number) =>
    api.get<RecentFood[]>("/api/meals/recent-foods", { params: { limit } }),
  streak: () =>
    api.get<Streak>("/api/meals/streak", { params: { timezoneId: tzId() } }),
  import: (data: ImportMealsRequest) =>
    api.post<ImportMealsResult>("/api/meals/import", data, {
      params: { timezoneId: tzId() },
    }),
};

export const foodApi = {
  search: (q: string, signal?: AbortSignal) =>
    api.get<FoodProduct[]>("/api/food/search", {
      params: { q },
      signal,
    }),
  lookupBarcode: (code: string) =>
    api.get<FoodProduct>(`/api/food/barcode/${code}`),
  get: (id: string) => api.get<FoodProduct>(`/api/food/${id}`),
  safetyReport: (id: string) =>
    api.get<SafetyReport>(`/api/food/${id}/safety-report`),
  gutRisk: (id: string) =>
    api.get<GutRiskAssessment>(`/api/food/${id}/gut-risk`),
  fodmap: (id: string) => api.get<FodmapAssessment>(`/api/food/${id}/fodmap`),
  substitutions: (id: string) =>
    api.get<SubstitutionResult>(`/api/food/${id}/substitutions`),
  glycemic: (id: string) =>
    api.get<GlycemicAssessment>(`/api/food/${id}/glycemic`),
  personalizedScore: (id: string) =>
    api.get<PersonalizedScore>(`/api/food/${id}/personalized-score`, {
      params: { timezoneId: tzId() },
    }),
  listAdditives: () => api.get<FoodAdditive[]>("/api/food/additives"),
  getAdditive: (id: number) =>
    api.get<FoodAdditive>(`/api/food/additives/${id}`),
  favorites: () => api.get<FavoriteFood[]>("/api/food/favorites"),
  addFavorite: (id: string) => api.post(`/api/food/${id}/favorite`),
  removeFavorite: (id: string) => api.delete(`/api/food/${id}/favorite`),
  customFoods: () => api.get<FoodProduct[]>("/api/food/custom"),
  describeFood: (text: string) =>
    api.post<CustomFood & { extractionConfidence?: number | null }>(
      "/api/food/describe",
      { text },
    ),
  createCustomFood: (data: CustomFood) =>
    api.post<CustomFood>("/api/food/custom", data),
  updateCustomFood: (id: string, data: CustomFood) =>
    api.put<CustomFood>(`/api/food/custom/${id}`, data),
  deleteCustomFood: (id: string) => api.delete(`/api/food/custom/${id}`),
  parseLabel: async (imageUri: string, mimeType: string = "image/jpeg") => {
    const formData = new FormData();

    if (Platform.OS === "web") {
      const response = await fetch(imageUri);
      const blob = await response.blob();
      formData.append("file", blob, "label.jpg");
    } else {
      formData.append("file", {
        uri: imageUri,
        name: "label.jpg",
        type: mimeType,
      } as any);
    }

    // Bypass axios since it struggles with mixed native/web FormData boundary rendering
    // when a global Content-Type interceptor is set
    const token = await getItem("accessToken");
    const baseURL = api.defaults.baseURL || "";

    const response = await fetch(`${baseURL}/api/food/parse-label`, {
      method: "POST",
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: formData,
    });

    if (!response.ok) {
      throw new Error("Label parsing failed");
    }
    const data: CustomFood = await response.json();
    return { data };
  },
};

export const mealScanApi = {
  scanImage: async (imageUri: string, mimeType: string = "image/jpeg") => {
    const formData = new FormData();
    if (Platform.OS === "web") {
      const response = await fetch(imageUri);
      const blob = await response.blob();
      formData.append("file", blob, "meal.jpg");
    } else {
      formData.append("file", {
        uri: imageUri,
        name: "meal.jpg",
        type: mimeType,
      } as any);
    }

    const token = await getItem("accessToken");
    const baseURL = api.defaults.baseURL || "";

    const response = await fetch(`${baseURL}/api/meals/scan/image`, {
      method: "POST",
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: formData,
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      throw new Error(err.error || "Meal photo scan failed");
    }
    const data: MealScanDraft = await response.json();
    return { data };
  },

  getDraft: (id: string) => api.get<MealScanDraft>(`/api/meals/scan/${id}`),

  discardDraft: (id: string) => api.delete(`/api/meals/scan/${id}`),

  confirmDraft: (id: string, request: MealScanConfirmRequest) =>
    api.put<{ mealId: string }>(`/api/meals/scan/${id}/confirm`, request),
};

export const symptomApi = {
  list: (params?: { date?: string }) =>
    api.get<SymptomLog[]>("/api/symptoms", {
      params: { ...params, tzOffsetMinutes: tz(), timezoneId: tzId() },
    }),
  history: (params?: { from?: string; to?: string; typeId?: number }) =>
    api.get<SymptomLog[]>("/api/symptoms/history", {
      params: { ...params, timezoneId: tzId() },
    }),
  create: (data: CreateSymptomRequest) =>
    api.post<SymptomLog>("/api/symptoms", data),
  update: (id: string, data: CreateSymptomRequest) =>
    api.put<SymptomLog>(`/api/symptoms/${id}`, data),
  delete: (id: string) => api.delete(`/api/symptoms/${id}`),
  types: () => api.get<SymptomType[]>("/api/symptoms/types"),
  get: (id: string) => api.get<SymptomLog>(`/api/symptoms/${id}`),
};

export const insightApi = {
  correlations: (days?: number) => {
    const to = toLocalDateStr();
    const from = shiftDate(to, -(days ?? 30));
    return api.get<Correlation[]>("/api/insights/correlations", {
      params: { from, to, timezoneId: tzId() },
    });
  },
  nutritionTrends: (days?: number) => {
    const to = toLocalDateStr();
    const from = shiftDate(to, -(days ?? 14));
    return api.get<NutritionTrend[]>("/api/insights/nutrition-trends", {
      params: { from, to, tzOffsetMinutes: tz(), timezoneId: tzId() },
    });
  },
  additiveExposure: (days?: number) => {
    const to = toLocalDateStr();
    const from = shiftDate(to, -(days ?? 30));
    return api.get<AdditiveExposure[]>("/api/insights/additive-exposure", {
      params: { from, to, timezoneId: tzId() },
    });
  },
  triggerFoods: (days?: number) => {
    const to = toLocalDateStr();
    const from = shiftDate(to, -(days ?? 30));
    return api.get<TriggerFood[]>("/api/insights/trigger-foods", {
      params: { from, to, timezoneId: tzId() },
    });
  },
  foodDiaryAnalysis: (days?: number) => {
    const to = toLocalDateStr();
    const from = shiftDate(to, -(days ?? 30));
    return api.get<FoodDiaryAnalysis>("/api/insights/food-diary-analysis", {
      params: { from, to, timezoneId: tzId() },
    });
  },
  nutritionByMealType: (days?: number) => {
    const to = toLocalDateStr();
    const from = shiftDate(to, -(days ?? 30));
    return api.get<MealTypeNutrition[]>(
      "/api/insights/nutrition-by-meal-type",
      {
        params: { from, to, timezoneId: tzId() },
      },
    );
  },
  eliminationDietStatus: () =>
    api.get<EliminationDietStatus>("/api/insights/elimination-diet/status", {
      params: { timezoneId: tzId() },
    }),
};
export const userApi = {
  getProfile: () => api.get<UserProfile>("/api/user/profile"),
  updateProfile: (data: UpdateProfileRequest) =>
    api.put<UserProfile>("/api/user/profile", data),
  updateGoals: (data: UpdateGoalsRequest) => api.put("/api/user/goals", data),
  getAlerts: () => api.get<UserFoodAlert[]>("/api/user/alerts"),
  addAlert: (additiveId: number) =>
    api.post("/api/user/alerts", { additiveId }),
  removeAlert: (additiveId: number) =>
    api.delete(`/api/user/alerts/${additiveId}`),
  deleteAccount: () => api.delete("/api/user/account"),
  createPairingCode: () =>
    api.post<PairingCodeResponse>("/api/user/pairing-codes"),
  listTokens: () => api.get<LinkedAccessToken[]>("/api/user/tokens"),
  revokeToken: (id: string) => api.delete(`/api/user/tokens/${id}`),
};

export const chatApi = {
  getHistory: (limit?: number) =>
    api.get<ChatMessage[]>("/api/chat/history", { params: { limit } }),
  clearHistory: () => api.delete("/api/chat/history"),
};
