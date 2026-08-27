import { Platform } from "react-native";
import type { HealthConnectRecord, NutritionRecord } from "react-native-health-connect";
import type { HealthBridge, NormalizedMeal, PlatformMealWrite } from "./types";

const OWN_PACKAGE_NAME = "com.djapplications.gutai";

type HealthConnectModule = typeof import("react-native-health-connect");

let HealthConnect: HealthConnectModule | null = null;
try {
  if (Platform.OS === "android") {
    HealthConnect = require("react-native-health-connect");
  }
} catch {
  HealthConnect = null;
}

function mapMealTypeToHealthConnect(mealType?: string): number {
  if (!mealType) return 0; // UNKNOWN
  const lower = mealType.toLowerCase();
  if (lower.includes("breakfast")) return 1; // BREAKFAST
  if (lower.includes("lunch")) return 2; // LUNCH
  if (lower.includes("dinner")) return 3; // DINNER
  if (lower.includes("snack")) return 4; // SNACK
  return 0; // UNKNOWN
}

function mapMealTypeFromHealthConnect(
  mealTypeInt?: number,
): "Breakfast" | "Lunch" | "Dinner" | "Snack" | undefined {
  switch (mealTypeInt) {
    case 1:
      return "Breakfast";
    case 2:
      return "Lunch";
    case 3:
      return "Dinner";
    case 4:
      return "Snack";
    default:
      return undefined;
  }
}

export type HealthConnectSdkAvailability =
  | "available"
  | "provider-update-required"
  | "unavailable";

/** Classify SDK status so callers can tell "not installed" apart from
 * "installed but the provider app needs an update" (routes to Play Store). */
export async function getSdkAvailability(): Promise<HealthConnectSdkAvailability> {
  if (Platform.OS !== "android" || !HealthConnect) return "unavailable";
  try {
    const status = await HealthConnect.getSdkStatus();
    if (status === HealthConnect.SdkAvailabilityStatus.SDK_AVAILABLE) return "available";
    if (status === HealthConnect.SdkAvailabilityStatus.SDK_UNAVAILABLE_PROVIDER_UPDATE_REQUIRED) {
      return "provider-update-required";
    }
    return "unavailable";
  } catch {
    return "unavailable";
  }
}

export class AndroidHealthBridge implements HealthBridge {
  readonly platformId = "health-connect" as const;

  async isAvailable(): Promise<boolean> {
    if (Platform.OS !== "android" || !HealthConnect) return false;
    if ((await getSdkAvailability()) !== "available") return false;
    try {
      return await HealthConnect.initialize();
    } catch {
      return false;
    }
  }

  async requestPermissions(): Promise<boolean> {
    if (Platform.OS !== "android" || !HealthConnect) return false;
    try {
      const initialized = await HealthConnect.initialize();
      if (!initialized) return false;

      const granted = await HealthConnect.requestPermission([
        { accessType: "read", recordType: "Nutrition" },
        { accessType: "write", recordType: "Nutrition" },
      ]);

      // This feature is dual-direction: connect means both directions work.
      // Granting read but denying write would silently no-op every export.
      const hasRead = granted.some(
        (p) =>
          "recordType" in p &&
          p.recordType === "Nutrition" &&
          p.accessType === "read",
      );
      const hasWrite = granted.some(
        (p) =>
          "recordType" in p &&
          p.recordType === "Nutrition" &&
          p.accessType === "write",
      );
      return hasRead && hasWrite;
    } catch (err) {
      console.warn("Health Connect permission request failed:", err);
      return false;
    }
  }

  async readNutrition(from: Date, to: Date): Promise<NormalizedMeal[]> {
    if (Platform.OS !== "android" || !HealthConnect) return [];
    try {
      await HealthConnect.initialize();
      const meals: NormalizedMeal[] = [];
      // Follow page tokens: native default pageSize is 1000, so a busy window
      // can span multiple pages. Truncating would silently skip older records
      // while the watermark still advanced past them.
      let pageToken: string | undefined;
      do {
        const result = await HealthConnect.readRecords("Nutrition", {
          timeRangeFilter: {
            operator: "between",
            startTime: from.toISOString(),
            endTime: to.toISOString(),
          },
          ...(pageToken ? { pageToken } : {}),
        });
        pageToken = result.pageToken;

        for (const rec of result.records || []) {
          // Skip self-origin records to avoid duplicate import loop
          if (rec.metadata?.dataOrigin === OWN_PACKAGE_NAME) {
            continue;
          }

          const externalId =
            rec.metadata?.id ||
            rec.metadata?.clientRecordId ||
            `${rec.startTime}_${rec.name ?? "meal"}`;

          const calories = rec.energy?.inKilocalories;
          const proteinG = rec.protein?.inGrams;
          const carbsG = rec.totalCarbohydrate?.inGrams;
          const fatG = rec.totalFat?.inGrams;
          const fiberG = rec.dietaryFiber?.inGrams;
          const sugarG = rec.sugar?.inGrams;
          const sodiumMg = rec.sodium?.inGrams ? rec.sodium.inGrams * 1000 : undefined;

          meals.push({
            externalId,
            loggedAt: rec.startTime,
            mealType: mapMealTypeFromHealthConnect(rec.mealType),
            name: rec.name ?? undefined,
            calories: calories !== undefined ? Math.round(calories) : undefined,
            proteinG: proteinG !== undefined ? Math.round(proteinG * 10) / 10 : undefined,
            carbsG: carbsG !== undefined ? Math.round(carbsG * 10) / 10 : undefined,
            fatG: fatG !== undefined ? Math.round(fatG * 10) / 10 : undefined,
            fiberG: fiberG !== undefined ? Math.round(fiberG * 10) / 10 : undefined,
            sugarG: sugarG !== undefined ? Math.round(sugarG * 10) / 10 : undefined,
            sodiumMg: sodiumMg !== undefined ? Math.round(sodiumMg) : undefined,
          });
        }
      } while (pageToken);

      return meals;
    } catch (err) {
      console.warn("Failed to read nutrition from Health Connect:", err);
      return [];
    }
  }

  async writeMeal(meal: PlatformMealWrite): Promise<void> {
    if (Platform.OS !== "android" || !HealthConnect) return;
    try {
      await HealthConnect.initialize();

      const startTime = new Date(meal.loggedAt).toISOString();
      // Health Connect treats Nutrition as an interval record; a zero-length span
      // can be dropped by readers and renders poorly in other apps' timelines.
      const endTime = new Date(new Date(meal.loggedAt).getTime() + 15 * 60 * 1000).toISOString();

      const record: NutritionRecord = {
        recordType: "Nutrition",
        startTime,
        endTime,
        mealType: mapMealTypeToHealthConnect(meal.mealType),
        name: meal.name || "Meal",
        metadata: {
          clientRecordId: meal.mealId,
          dataOrigin: OWN_PACKAGE_NAME,
        },
      };

      if (meal.calories !== undefined) {
        record.energy = { value: meal.calories, unit: "kilocalories" };
      }
      if (meal.proteinG !== undefined) {
        record.protein = { value: meal.proteinG, unit: "grams" };
      }
      if (meal.carbsG !== undefined) {
        record.totalCarbohydrate = { value: meal.carbsG, unit: "grams" };
      }
      if (meal.fatG !== undefined) {
        record.totalFat = { value: meal.fatG, unit: "grams" };
      }
      if (meal.fiberG !== undefined) {
        record.dietaryFiber = { value: meal.fiberG, unit: "grams" };
      }
      if (meal.sugarG !== undefined) {
        record.sugar = { value: meal.sugarG, unit: "grams" };
      }
      if (meal.sodiumMg !== undefined) {
        record.sodium = { value: meal.sodiumMg / 1000, unit: "grams" };
      }

      await HealthConnect.insertRecords([record as HealthConnectRecord]);
    } catch (err) {
      console.warn("Failed to write meal to Health Connect:", err);
    }
  }

  async deleteMeal(mealId: string): Promise<void> {
    if (Platform.OS !== "android" || !HealthConnect) return;
    try {
      await HealthConnect.initialize();
      await HealthConnect.deleteRecordsByUuids("Nutrition", [], [mealId]);
    } catch (err) {
      console.warn("Failed to delete meal from Health Connect:", err);
    }
  }
}
