/** Return local YYYY-MM-DD string for a Date (defaults to now). */
export function toLocalDateStr(d: Date = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

export function shiftDate(dateStr: string, days: number) {
  const d = new Date(dateStr + "T12:00:00");
  d.setDate(d.getDate() + days);
  return toLocalDateStr(d);
}

export function formatDateLabel(dateStr: string) {
  const todayStr = toLocalDateStr();
  const yesterday = shiftDate(todayStr, -1);
  if (dateStr === todayStr) return "Today";
  if (dateStr === yesterday) return "Yesterday";
  return new Date(dateStr + "T12:00:00").toLocaleDateString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
  });
}

export const today = () => toLocalDateStr();

/**
 * Build a correct UTC ISO-8601 loggedAt timestamp for a given local date.
 * Uses the current local time of day combined with the supplied YYYY-MM-DD,
 * then converts to UTC via toISOString().
 */
export function buildLoggedAt(dateStr: string): string {
  const now = new Date();
  const [y, m, d] = dateStr.split("-").map(Number);
  const local = new Date(
    y,
    m - 1,
    d,
    now.getHours(),
    now.getMinutes(),
    now.getSeconds(),
    now.getMilliseconds(),
  );
  return local.toISOString();
}

/**
 * Re-date an existing UTC ISO-8601 timestamp to a different local date,
 * preserving the original local time-of-day.
 */
export function redateLoggedAt(
  originalIso: string,
  newDateStr: string,
): string {
  const orig = new Date(originalIso);
  const [y, m, d] = newDateStr.split("-").map(Number);
  const local = new Date(
    y,
    m - 1,
    d,
    orig.getHours(),
    orig.getMinutes(),
    orig.getSeconds(),
    orig.getMilliseconds(),
  );
  return local.toISOString();
}
