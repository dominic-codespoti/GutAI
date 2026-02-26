import { api } from "./client";
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
    api.get<MealLog[]>("/api/meals", { params: { date } }),
  get: (id: string) => api.get<MealLog>(`/api/meals/${id}`),
  create: (data: CreateMealRequest) => api.post<MealLog>("/api/meals", data),
  update: (id: string, data: CreateMealRequest) =>
    api.put<MealLog>(`/api/meals/${id}`, data),
  delete: (id: string) => api.delete(`/api/meals/${id}`),
  parseNatural: (data: NaturalLanguageMealRequest) =>
    api.post<NaturalLanguageResponse>("/api/meals/log-natural", data),
  dailySummary: (date: string) =>
    api.get<DailyNutritionSummary>(`/api/meals/daily-summary/${date}`),
  export: (from?: string, to?: string) =>
    api.get<DataExport>("/api/meals/export", { params: { from, to } }),
};

export const foodApi = {
  search: (q: string) =>
    api.get<FoodProduct[]>("/api/food/search", { params: { q } }),
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
    api.get<PersonalizedScore>(`/api/food/${id}/personalized-score`),
  listAdditives: () => api.get<FoodAdditive[]>("/api/food/additives"),
  getAdditive: (id: number) =>
    api.get<FoodAdditive>(`/api/food/additives/${id}`),
};

export const symptomApi = {
  list: (params?: { date?: string }) =>
    api.get<SymptomLog[]>("/api/symptoms", { params }),
  history: (params?: { from?: string; to?: string; typeId?: number }) =>
    api.get<SymptomLog[]>("/api/symptoms/history", { params }),
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
    const to = new Date().toISOString().split("T")[0];
    const from = new Date(Date.now() - (days ?? 30) * 86400000)
      .toISOString()
      .split("T")[0];
    return api.get<Correlation[]>("/api/insights/correlations", {
      params: { from, to },
    });
  },
  nutritionTrends: (days?: number) => {
    const to = new Date().toISOString().split("T")[0];
    const from = new Date(Date.now() - (days ?? 14) * 86400000)
      .toISOString()
      .split("T")[0];
    return api.get<NutritionTrend[]>("/api/insights/nutrition-trends", {
      params: { from, to },
    });
  },
  additiveExposure: (days?: number) => {
    const to = new Date().toISOString().split("T")[0];
    const from = new Date(Date.now() - (days ?? 30) * 86400000)
      .toISOString()
      .split("T")[0];
    return api.get<AdditiveExposure[]>("/api/insights/additive-exposure", {
      params: { from, to },
    });
  },
  triggerFoods: (days?: number) => {
    const to = new Date().toISOString().split("T")[0];
    const from = new Date(Date.now() - (days ?? 30) * 86400000)
      .toISOString()
      .split("T")[0];
    return api.get<TriggerFood[]>("/api/insights/trigger-foods", {
      params: { from, to },
    });
  },
  foodDiaryAnalysis: (days?: number) => {
    const to = new Date().toISOString().split("T")[0];
    const from = new Date(Date.now() - (days ?? 30) * 86400000)
      .toISOString()
      .split("T")[0];
    return api.get<FoodDiaryAnalysis>("/api/insights/food-diary-analysis", {
      params: { from, to },
    });
  },
  eliminationDietStatus: () =>
    api.get<EliminationDietStatus>("/api/insights/elimination-diet/status"),
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
};
