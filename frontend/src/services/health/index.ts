import { Linking, Platform } from "react-native";
import { mealApi } from "../../api";
import { toast } from "../../stores/toast";
import type { ImportMealsItem, ImportMealsResult, MealLog } from "../../types";
import { AndroidHealthBridge, getSdkAvailability } from "./android";
import { IOSHealthBridge } from "./ios";
import type {
  HealthBridge,
  HealthPlatformId,
  NormalizedMeal,
  PlatformMealWrite,
} from "./types";
import {
  getLastSyncWatermark,
  getWriteMealsEnabled,
  setLastSyncWatermark,
  setWriteMealsEnabled,
} from "./watermark";

export type {
  HealthBridge,
  HealthPlatformId,
  NormalizedMeal,
  PlatformMealWrite,
};
export {
  getLastSyncWatermark,
  setLastSyncWatermark,
  getWriteMealsEnabled,
  setWriteMealsEnabled,
};
const MEAL_WINDOW_MS = 15 * 60 * 1000;
/** Watermarks lag the sync moment by this much: third-party apps often write to
 * their health store AFTER a record's timestamp, so rescanning the trailing day
 * catches late arrivals. Server dedupe makes the overlap free. */
const WATERMARK_OVERLAP_MS = 24 * 60 * 60 * 1000;
const PLAY_STORE_HEALTH_CONNECT_URL =
  "https://play.google.com/store/apps/details?id=com.google.android.apps.healthdata";

function createPlatformBridge(): HealthBridge | null {
  if (Platform.OS === "android") {
    return new AndroidHealthBridge();
  }
  if (Platform.OS === "ios") {
    return new IOSHealthBridge();
  }
  return null;
}

export const activeHealthBridge = createPlatformBridge();
/** Local-hour meal type heuristic, mirroring the backend's ranges but in device time. */
function deriveLocalMealType(iso: string): ImportMealsItem["mealType"] {
  const h = new Date(iso).getHours();
  if (h >= 6 && h < 11) return "Breakfast";
  if (h >= 11 && h < 15) return "Lunch";
  if (h >= 18 && h < 22) return "Dinner";
  return "Snack";
}

/** Build a platform write payload from a created/updated MealLog response.
 * Single source of truth so every logging surface exports identical data. */
export function mealWriteFromResponse(m: MealLog): PlatformMealWrite {
  const names = m.items
    ?.map((i) => i.foodName)
    .filter(Boolean)
    .join(", ");
  const fiberG = m.items?.reduce((sum, it) => sum + (it.fiberG || 0), 0);
  const sugarG = m.items?.reduce((sum, it) => sum + (it.sugarG || 0), 0);
  const sodiumMg = m.items?.reduce((sum, it) => sum + (it.sodiumMg || 0), 0);
  return {
    mealId: m.id,
    loggedAt: m.loggedAt,
    mealType: m.mealType,
    name: names || m.mealType,
    calories: m.totalCalories,
    proteinG: m.totalProteinG,
    carbsG: m.totalCarbsG,
    fatG: m.totalFatG,
    fiberG: fiberG !== undefined ? fiberG : undefined,
    sugarG: sugarG !== undefined ? sugarG : undefined,
    sodiumMg: sodiumMg !== undefined ? sodiumMg : undefined,
  };
}

export async function syncHealthImport(
  platform?: HealthPlatformId,
): Promise<ImportMealsResult | null> {
  const bridge = activeHealthBridge;
  if (!bridge) {
    toast.error("Health sync is not supported on this platform");
    return null;
  }

  const targetPlatform = platform ?? bridge.platformId;

  try {
    const isAvailable = await bridge.isAvailable();
    if (!isAvailable) {
      // Distinguish a missing provider from one that only needs an update:
      // the Play Store listing is the remediation path for the latter.
      if (
        targetPlatform === "health-connect" &&
        (await getSdkAvailability()) === "provider-update-required"
      ) {
        toast.error("Google Health Connect needs an update before GutLens can sync");
        Linking.openURL(PLAY_STORE_HEALTH_CONNECT_URL).catch(() => {});
        return null;
      }
      toast.error(
        targetPlatform === "health-connect"
          ? "Google Health Connect is not available or not installed"
          : "Apple HealthKit is not available on this device",
      );
      return null;
    }

    const hasPermission = await bridge.requestPermissions();
    if (!hasPermission) {
      toast.error("Permission to access health data was denied");
      return null;
    }

    const now = new Date();
    const lastSync = await getLastSyncWatermark(targetPlatform);
    // Default to last 30 days if no watermark exists
    let from = lastSync ?? new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000);

    const records = await bridge.readNutrition(from, now);

    if (records.length === 0) {
      await setLastSyncWatermark(
        targetPlatform,
        new Date(
          Math.max(from.getTime(), now.getTime() - WATERMARK_OVERLAP_MS),
        ),
      );
      toast.info("No new meals found to import");
      return {
        imported: 0,
        skippedDuplicates: 0,
        failed: 0,
        errors: [],
      };
    }

    const items: ImportMealsItem[] = records.map((r) => ({
      loggedAt: r.loggedAt,
      // Derive locally from device time when the source record has no explicit
      // meal type — the server's fallback uses UTC hours, which mislabels
      // meals for anyone outside UTC.
      mealType: r.mealType ?? deriveLocalMealType(r.loggedAt),
      externalId: r.externalId,
      name: r.name,
      servings: r.servings,
      notes: r.notes,
      calories: r.calories,
      proteinG: r.proteinG,
      carbsG: r.carbsG,
      fatG: r.fatG,
      fiberG: r.fiberG,
      sugarG: r.sugarG,
      sodiumMg: r.sodiumMg,
    }));

    // Send items in chunks of up to 1000 items (backend limit is 2000)
    const CHUNK_SIZE = 1000;
    let totalImported = 0;
    let totalSkipped = 0;
    let totalFailed = 0;
    const allErrors: string[] = [];

    for (let i = 0; i < items.length; i += CHUNK_SIZE) {
      const chunk = items.slice(i, i + CHUNK_SIZE);
      const res = await mealApi.import({
        source: targetPlatform,
        items: chunk,
      });

      totalImported += res.data.imported;
      totalSkipped += res.data.skippedDuplicates;
      totalFailed += res.data.failed;
      if (res.data.errors?.length) {
        allErrors.push(...res.data.errors);
      }
    }

    // Advance the watermark only on a fully clean pass. A pass with failures
    // rescans its window next time; server-side (source, externalId) dedupe
    // makes those retries free, so nothing is silently lost to a transient error.
    if (totalFailed === 0) {
      await setLastSyncWatermark(
        targetPlatform,
        new Date(
          Math.max(from.getTime(), now.getTime() - WATERMARK_OVERLAP_MS),
        ),
      );
    }

    const result: ImportMealsResult = {
      imported: totalImported,
      skippedDuplicates: totalSkipped,
      failed: totalFailed,
      errors: allErrors,
    };

    if (totalImported > 0 || totalSkipped > 0) {
      toast.success(
        `Imported ${totalImported} meal${totalImported === 1 ? "" : "s"}${
          totalSkipped > 0 ? ` (${totalSkipped} skipped)` : ""
        }`,
      );
    } else if (totalFailed > 0) {
      toast.error(`Failed to import meals: ${allErrors[0] ?? "Unknown error"}`);
    } else {
      toast.info("No new meals to import");
    }

    return result;
  } catch (err) {
    console.warn("Health sync import failed:", err);
    toast.error("Failed to sync health meals");
    return null;
  }
}

export async function maybeWriteMealToPlatform(
  meal: PlatformMealWrite,
): Promise<void> {
  const bridge = activeHealthBridge;
  if (!bridge) return;

  try {
    const enabled = await getWriteMealsEnabled(bridge.platformId);
    if (!enabled) return;

    await bridge.writeMeal(meal);
  } catch (err) {
    console.warn("Failed background health writeMeal:", err);
  }
}

export async function maybeDeleteMealFromPlatform(
  mealId: string,
): Promise<void> {
  const bridge = activeHealthBridge;
  if (!bridge) return;

  try {
    // Deletion is NOT gated on the export toggle: if the user exported a meal
    // and later turned exporting off, deleting that meal must still remove the
    // orphaned copy from the health store. This only ever removes data we wrote.
    await bridge.deleteMeal(mealId);
  } catch (err) {
    console.warn("Failed background health deleteMeal:", err);
  }
}
