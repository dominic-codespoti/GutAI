import { Platform } from "react-native";
import {
  deleteObjects,
  isHealthDataAvailableAsync,
  queryCorrelationSamples,
  queryQuantitySamples,
  requestAuthorization,
  saveCorrelationSample,
} from "@kingstinct/react-native-healthkit";
import type {
  QuantityTypeIdentifierWriteable,
  SampleTypeIdentifierWriteable,
} from "@kingstinct/react-native-healthkit/types";
import { getItem, setItem } from "../../utils/storage";
import type { HealthBridge, HealthPlatformId, NormalizedMeal, PlatformMealWrite } from "./types";

const OWN_BUNDLE_ID = "gut.ai";
const MEAL_ID_METADATA_KEY = "gutaiMealId";
const FOOD_CORRELATION = "HKCorrelationTypeIdentifierFood" as const;
const UUID_MAP_PREFIX = "healthkit_correlation_uuid_";
const MEAL_WINDOW_MS = 15 * 60 * 1000;

interface NutrientField {
  key: keyof PlatformMealWrite;
  identifier: QuantityTypeIdentifierWriteable;
  unit: string;
  /** Multiplier applied when writing our value in the HK unit. */
  scale: number;
}

const NUTRIENTS: readonly NutrientField[] = [
  { key: "calories", identifier: "HKQuantityTypeIdentifierDietaryEnergyConsumed", unit: "kcal", scale: 1 },
  { key: "proteinG", identifier: "HKQuantityTypeIdentifierDietaryProtein", unit: "g", scale: 1 },
  { key: "carbsG", identifier: "HKQuantityTypeIdentifierDietaryCarbohydrates", unit: "g", scale: 1 },
  { key: "fatG", identifier: "HKQuantityTypeIdentifierDietaryFatTotal", unit: "g", scale: 1 },
  { key: "fiberG", identifier: "HKQuantityTypeIdentifierDietaryFiber", unit: "g", scale: 1 },
  { key: "sugarG", identifier: "HKQuantityTypeIdentifierDietarySugar", unit: "g", scale: 1 },
  { key: "sodiumMg", identifier: "HKQuantityTypeIdentifierDietarySodium", unit: "g", scale: 1 / 1000 },
] as const;

/** Canonicalize any HealthKit quantity: mass → grams, dietary energy → kcal.
 * Callers convert grams → mg where a field is milligram-based (sodium). */
function normalizeQuantity(identifier: string, unit: string, value: number): number | undefined {
  const u = (unit || "").toLowerCase();
  if (identifier === "HKQuantityTypeIdentifierDietaryEnergyConsumed") {
    if (u.startsWith("kj")) return value / 4.184;
    if (u === "cal") return value / 1000;
    if (u.startsWith("j")) return value / 4184;
    return value; // kcal
  }
  if (u.startsWith("kg")) return value * 1000;
  if (u.startsWith("mg")) return value / 1000;
  return value; // g
}

function mealTypeFromMetadata(raw: unknown): NormalizedMeal["mealType"] {
  const v = String(raw ?? "").toLowerCase();
  if (v.startsWith("breakfast")) return "Breakfast";
  if (v.startsWith("lunch")) return "Lunch";
  if (v.startsWith("dinner") || v.startsWith("supper")) return "Dinner";
  if (v.startsWith("snack")) return "Snack";
  return undefined;
}
/** djb2 — stable content hash so repeated reads of one clustered group keep one externalId. */
function clusterHash(...parts: (string | number)[]): string {
  const s = parts.join("|");
  let h = 5381;
  for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) | 0;
  return (h >>> 0).toString(16);
}

export class IOSHealthBridge implements HealthBridge {
  readonly platformId: HealthPlatformId = "healthkit";

  async isAvailable(): Promise<boolean> {
    if (Platform.OS !== "ios") return false;
    try {
      return await isHealthDataAvailableAsync();
    } catch {
      return false;
    }
  }

  async requestPermissions(): Promise<boolean> {
    if (Platform.OS !== "ios") return false;
    try {
      return await requestAuthorization({
        toRead: [...NUTRIENTS.map((n) => n.identifier), FOOD_CORRELATION],
        toShare: [...NUTRIENTS.map((n) => n.identifier), FOOD_CORRELATION],
      });
    } catch (err) {
      console.warn("HealthKit authorization failed:", err);
      return false;
    }
  }

  async readNutrition(from: Date, to: Date): Promise<NormalizedMeal[]> {
    if (Platform.OS !== "ios") return [];
    try {
      const dateFilter = { date: { from, to } };
      const consumedUuids = new Set<string>();
      const meals: NormalizedMeal[] = [];

      const correlations = await queryCorrelationSamples(FOOD_CORRELATION, {
        limit: 2000,
        ascending: true,
        filter: { date: { startDate: from, endDate: to } },
      });
      for (const c of correlations) {
        if (c.sourceRevision?.source?.bundleIdentifier === OWN_BUNDLE_ID) continue;
        if (c.metadata?.[MEAL_ID_METADATA_KEY]) continue;

        let calories: number | undefined;
        const grams = new Map<string, number>();
        for (const obj of c.objects ?? []) {
          consumedUuids.add(obj.uuid);
          if (!("quantityType" in obj)) continue;
          const v = normalizeQuantity(obj.quantityType, String(obj.unit ?? ""), Number(obj.quantity));
          if (v === undefined || Number.isNaN(v)) continue;
          if (obj.quantityType === "HKQuantityTypeIdentifierDietaryEnergyConsumed") calories = v;
          else grams.set(obj.quantityType, v);
        }

        meals.push({
          externalId: `hk-corr-${c.uuid}`,
          loggedAt: new Date(c.startDate).toISOString(),
          mealType: mealTypeFromMetadata(c.metadata?.HKFoodMeal),
          name:
            typeof c.metadata?.HKMetadataKeyFoodType === "string"
              ? c.metadata.HKMetadataKeyFoodType
              : undefined,
          calories: calories !== undefined ? Math.round(calories) : undefined,
          proteinG: grams.get("HKQuantityTypeIdentifierDietaryProtein"),
          carbsG: grams.get("HKQuantityTypeIdentifierDietaryCarbohydrates"),
          fatG: grams.get("HKQuantityTypeIdentifierDietaryFatTotal"),
          fiberG: grams.get("HKQuantityTypeIdentifierDietaryFiber"),
          sugarG: grams.get("HKQuantityTypeIdentifierDietarySugar"),
          sodiumMg: grams.has("HKQuantityTypeIdentifierDietarySodium")
            ? Math.round((grams.get("HKQuantityTypeIdentifierDietarySodium") ?? 0) * 1000)
            : undefined,
        });
      }

      // ── Raw dietary samples: apps that write bare quantities ─────────────────
      const raw: Array<{ uuid: string; at: Date; kind: string; value: number }> = [];
      for (const n of NUTRIENTS) {
        const samples = await queryQuantitySamples(n.identifier, {
          limit: 5000,
          ascending: true,
          filter: { date: { startDate: from, endDate: to } },
        });
        for (const s of samples) {
          if (s.sourceRevision?.source?.bundleIdentifier === OWN_BUNDLE_ID) continue;
          if (s.metadata?.[MEAL_ID_METADATA_KEY]) continue;
          if (consumedUuids.has(s.uuid)) continue;
          const v = normalizeQuantity(s.quantityType, String(s.unit ?? ""), Number(s.quantity));
          if (v === undefined || Number.isNaN(v) || v <= 0) continue;
          raw.push({
            uuid: s.uuid,
            at: new Date(s.startDate),
            kind: n.identifier.replace("HKQuantityTypeIdentifierDietary", ""),
            value: v,
          });
        }
      }

      // Cluster same-day samples separated by ≤15-minute gaps into pseudo-meals.
      raw.sort((a, b) => a.at.getTime() - b.at.getTime());
      const kindValues = (group: typeof raw, suffix: string) => {
        const vals = group.filter((s) => s.kind === suffix).map((s) => s.value);
        return vals.length ? vals.reduce((sum, v) => sum + v, 0) : undefined;
      };
      const flushCluster = (group: typeof raw): NormalizedMeal => {
        const calories = kindValues(group, "EnergyConsumed");
        return {
          externalId: `hk-raw-${clusterHash(
            group[0].at.toISOString(),
            Math.round(calories ?? 0),
            group.length,
          )}`,
          loggedAt: group[0].at.toISOString(),
          calories: calories !== undefined ? Math.round(calories) : undefined,
          proteinG: kindValues(group, "Protein"),
          carbsG: kindValues(group, "Carbohydrates"),
          fatG: kindValues(group, "FatTotal"),
          fiberG: kindValues(group, "Fiber"),
          sugarG: kindValues(group, "Sugar"),
          sodiumMg: kindValues(group, "Sodium") !== undefined
            ? Math.round((kindValues(group, "Sodium") ?? 0) * 1000)
            : undefined,
        };
      };

      let group: typeof raw = [];
      for (const s of raw) {
        if (
          group.length > 0 &&
          s.at.getTime() - group[group.length - 1].at.getTime() > MEAL_WINDOW_MS
        ) {
          meals.push(flushCluster(group));
          group = [];
        }
        group.push(s);
      }
      if (group.length > 0) meals.push(flushCluster(group));

      return meals;
    } catch (err) {
      console.warn("Failed to read nutrition from Apple HealthKit:", err);
      return [];
    }
  }

  async writeMeal(meal: PlatformMealWrite): Promise<void> {
    if (Platform.OS !== "ios") return;
    try {
      // Update semantics: HealthKit has no upsert — drop the previous correlation
      // before saving the refreshed one, or every edit stacks a duplicate meal.
      const previousUuid = await getItem(`${UUID_MAP_PREFIX}${meal.mealId}`);
      if (previousUuid) {
        try {
          await deleteObjects(FOOD_CORRELATION, { uuid: previousUuid });
        } catch {
          // Previous record may already be gone; saving below still proceeds.
        }
      }

      const start = new Date(meal.loggedAt);
      const end = new Date(start.getTime() + MEAL_WINDOW_MS);

      const samples: Array<{
        startDate: Date;
        endDate: Date;
        quantityType: QuantityTypeIdentifierWriteable;
        quantity: number;
        unit: string;
        metadata: { gutaiMealId: string };
      }> = [];
      for (const n of NUTRIENTS) {
        const rawValue = meal[n.key];
        if (typeof rawValue !== "number" || !Number.isFinite(rawValue) || rawValue <= 0) continue;
        samples.push({
          startDate: start,
          endDate: end,
          quantityType: n.identifier,
          quantity: rawValue * n.scale,
          unit: n.unit,
          metadata: { [MEAL_ID_METADATA_KEY]: meal.mealId },
        });
      }
      if (samples.length === 0) return;

      const saved = await saveCorrelationSample(FOOD_CORRELATION, samples, start, end, {
        gutaiMealId: meal.mealId,
      });

      if (saved?.uuid) {
        await setItem(`${UUID_MAP_PREFIX}${meal.mealId}`, saved.uuid);
      }
    } catch (err) {
      console.warn("Failed to write meal to Apple HealthKit:", err);
    }
  }

  async deleteMeal(mealId: string): Promise<void> {
    if (Platform.OS !== "ios") return;
    try {
      const uuid = await getItem(`${UUID_MAP_PREFIX}${mealId}`);
      if (!uuid) return; // Nothing we wrote (or already removed).
      await deleteObjects(FOOD_CORRELATION, { uuid });
      await setItem(`${UUID_MAP_PREFIX}${mealId}`, "");
    } catch (err) {
      console.warn("Failed to delete meal from Apple HealthKit:", err);
    }
  }
}
