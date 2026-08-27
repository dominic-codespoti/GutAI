import { getCalendars } from "expo-localization";

/**
 * Returns the device's IANA timezone identifier (e.g. "America/New_York", "Europe/London").
 * Uses Intl.DateTimeFormat().resolvedOptions().timeZone with fallback to expo-localization getCalendars(),
 * and returns undefined if unable to resolve.
 */
export function getDeviceTimezoneId(): string | undefined {
  try {
    if (typeof Intl !== "undefined" && typeof Intl.DateTimeFormat === "function") {
      const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
      if (tz && typeof tz === "string" && tz.trim().length > 0) {
        return tz.trim();
      }
    }
  } catch {
    // Fallback to expo-localization if Intl throws or is unavailable
  }

  try {
    const calendars = getCalendars();
    const tz = calendars?.[0]?.timeZone;
    if (tz && typeof tz === "string" && tz.trim().length > 0) {
      return tz.trim();
    }
  } catch {
    // Ignore error
  }

  return undefined;
}
