import { Platform } from "react-native";
import * as Notifications from "expo-notifications";
import { getItem, setItem } from "./storage";

export type ReminderPrefs = {
  dailyEnabled: boolean;
  dailyHour: number; // 0-23
  dailyMinute: number;
  nudgeEnabled: boolean;
  nudgeHour: number; // 0-23
};

const PREFS_KEY = "gutai_reminder_prefs";
const DAILY_ID = "gutai-daily-logging-reminder";
const NUDGE_ID = "gutai-streak-nudge";

export const DEFAULT_REMINDER_PREFS: ReminderPrefs = {
  dailyEnabled: false,
  dailyHour: 9,
  dailyMinute: 0,
  nudgeEnabled: false,
  nudgeHour: 20,
};

export const REMINDER_TIME_CHOICES = [
  { label: "7:00", hour: 7, minute: 0 },
  { label: "9:00", hour: 9, minute: 0 },
  { label: "12:30", hour: 12, minute: 30 },
  { label: "18:00", hour: 18, minute: 0 },
  { label: "20:00", hour: 20, minute: 0 },
  { label: "21:30", hour: 21, minute: 30 },
];

/** Local notifications only — no tokens are collected or sent anywhere. */
export async function loadReminderPrefs(): Promise<ReminderPrefs> {
  try {
    const raw = await getItem(PREFS_KEY);
    if (!raw) return DEFAULT_REMINDER_PREFS;
    return { ...DEFAULT_REMINDER_PREFS, ...JSON.parse(raw) };
  } catch {
    return DEFAULT_REMINDER_PREFS;
  }
}

export async function saveReminderPrefs(prefs: ReminderPrefs): Promise<void> {
  await setItem(PREFS_KEY, JSON.stringify(prefs));
}

/**
 * Permission gate for opt-in flows. Returns true when notifications are
 * authorized. Idempotent: no-op prompt when already granted. Callers must
 * NOT invoke this on app launch — only from a user-initiated toggle after
 * the in-app explainer.
 */
export async function ensureNotificationPermissionAsync(): Promise<boolean> {
  const req = await Notifications.requestPermissionsAsync();
  // Some hoisted expo-modules-core type copies omit `granted`; trust runtime.
  return Boolean((req as unknown as { granted?: boolean }).granted);
}

async function ensureAndroidChannel(): Promise<void> {
  if (Platform.OS !== "android") return;
  await Notifications.setNotificationChannelAsync("reminders", {
    name: "Reminders",
    importance: Notifications.AndroidImportance.HIGH,
    lightColor: "#16a34a",
  });
}

function calendarTrigger(hour: number, minute: number, repeats: boolean) {
  return {
    type: Notifications.SchedulableTriggerInputTypes.DAILY,
    hour,
    minute,
    repeats,
    channelId: "reminders",
  } as const;
}

/** (Re)schedule both reminders from prefs. Safe no-op when disabled/denied. */
export async function applyReminderSchedule(
  prefs: ReminderPrefs,
): Promise<{ ok: boolean; denied?: boolean }> {
  const granted = prefs.dailyEnabled || prefs.nudgeEnabled
    ? await ensureNotificationPermissionAsync()
    : true;
  if (!granted) {
    return { ok: false, denied: true };
  }

  await ensureAndroidChannel();
  await Notifications.cancelScheduledNotificationAsync(DAILY_ID).catch(() => {});
  await Notifications.cancelScheduledNotificationAsync(NUDGE_ID).catch(() => {});

  if (prefs.dailyEnabled) {
    await Notifications.scheduleNotificationAsync({
      identifier: DAILY_ID,
      content: {
        title: "How's today going?",
        body: "Take a second to log your meals while it's fresh.",
        sound: true,
      },
      trigger: calendarTrigger(prefs.dailyHour, prefs.dailyMinute, true),
    });
  }
  return { ok: true };
}

/**
 * One-shot evening nudge, rescheduled by app foreground + meal logging:
 * shown tonight only when nothing is logged yet today.
 */
export async function syncStreakNudge(
  prefs: ReminderPrefs,
  hasLoggedToday: boolean,
): Promise<void> {
  await Notifications.cancelScheduledNotificationAsync(NUDGE_ID).catch(() => {});
  if (!prefs.nudgeEnabled || hasLoggedToday) return;

  const now = new Date();
  const target = new Date();
  target.setHours(prefs.nudgeHour, 0, 0, 0);
  if (target.getTime() <= now.getTime()) return; // window passed; next sync retries tomorrow

  const seconds = Math.max((target.getTime() - now.getTime()) / 1000, 1);
  await Notifications.scheduleNotificationAsync({
    identifier: NUDGE_ID,
    content: {
      title: "Keep your streak alive 🔥",
      body: "Nothing logged yet today — a quick log keeps your streak going.",
      sound: false,
    },
    trigger: {
      type: Notifications.SchedulableTriggerInputTypes.TIME_INTERVAL,
      seconds,
      channelId: "reminders",
    },
  });
}
