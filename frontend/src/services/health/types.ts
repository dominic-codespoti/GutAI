export type HealthPlatformId = "health-connect" | "healthkit";

export interface NormalizedMeal {
  externalId: string;
  loggedAt: string; // ISO 8601
  mealType?: "Breakfast" | "Lunch" | "Dinner" | "Snack";
  name?: string;
  servings?: number;
  notes?: string;
  calories?: number;
  proteinG?: number;
  carbsG?: number;
  fatG?: number;
  fiberG?: number;
  sugarG?: number;
  sodiumMg?: number;
}

export interface PlatformMealWrite {
  mealId: string;
  loggedAt: string; // ISO 8601
  mealType?: "Breakfast" | "Lunch" | "Dinner" | "Snack" | string;
  name?: string;
  calories?: number;
  proteinG?: number;
  carbsG?: number;
  fatG?: number;
  fiberG?: number;
  sugarG?: number;
  sodiumMg?: number;
}

export interface HealthBridge {
  readonly platformId: HealthPlatformId;
  isAvailable(): Promise<boolean>;
  requestPermissions(): Promise<boolean>;
  readNutrition(fromISO: Date, toISO: Date): Promise<NormalizedMeal[]>;
  writeMeal(meal: PlatformMealWrite): Promise<void>;
  deleteMeal(mealId: string): Promise<void>;
}
