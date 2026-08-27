import { getItem, setItem } from "../../utils/storage";
import type { HealthPlatformId } from "./types";

const WATERMARK_PREFIX = "health_sync_watermark_";
const SYNC_ENABLED_PREFIX = "health_sync_write_enabled_";

export async function getLastSyncWatermark(
  platform: HealthPlatformId,
): Promise<Date | null> {
  try {
    const raw = await getItem(`${WATERMARK_PREFIX}${platform}`);
    if (!raw) return null;
    const date = new Date(raw);
    return isNaN(date.getTime()) ? null : date;
  } catch {
    return null;
  }
}

export async function setLastSyncWatermark(
  platform: HealthPlatformId,
  watermark: Date,
): Promise<void> {
  try {
    await setItem(`${WATERMARK_PREFIX}${platform}`, watermark.toISOString());
  } catch (err) {
    console.warn("Failed to set health sync watermark", err);
  }
}

export async function getWriteMealsEnabled(
  platform: HealthPlatformId,
): Promise<boolean> {
  try {
    const raw = await getItem(`${SYNC_ENABLED_PREFIX}${platform}`);
    return raw === "true";
  } catch {
    return false;
  }
}

export async function setWriteMealsEnabled(
  platform: HealthPlatformId,
  enabled: boolean,
): Promise<void> {
  try {
    await setItem(`${SYNC_ENABLED_PREFIX}${platform}`, enabled ? "true" : "false");
  } catch (err) {
    console.warn("Failed to set write meals enabled flag", err);
  }
}
