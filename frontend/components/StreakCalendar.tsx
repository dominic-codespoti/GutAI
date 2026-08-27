import { View, Text } from "react-native";
import { useThemeColors } from "../src/stores/theme";
import { radius, spacing } from "../src/utils/theme";
import { toLocalDateStr } from "../src/utils/date";

export interface StreakDay {
  date: string;
  logged: boolean;
  mealCount?: number;
}

export interface StreakCalendarProps {
  data?: StreakDay[];
}

const WEEKDAYS = ["M", "T", "W", "T", "F", "S", "S"] as const;

/**
 * Compute the 28 dates ending on the current week's Sunday.
 * Grid layout: 4 rows (weeks) × 7 columns (Mon-Sun).
 */
function get28DayGrid(): string[][] {
  const today = new Date();
  // In JS: Sunday = 0, Monday = 1, ..., Saturday = 6.
  // Normalize so Monday = 0, ..., Sunday = 6.
  const dayOfWeek = (today.getDay() + 6) % 7;
  const daysUntilSunday = 6 - dayOfWeek;

  // End date of the 4-week window (Sunday of current week)
  const endDate = new Date(today.getFullYear(), today.getMonth(), today.getDate() + daysUntilSunday);

  const weeks: string[][] = [];
  for (let w = 0; w < 4; w++) {
    const weekDays: string[] = [];
    for (let d = 0; d < 7; d++) {
      const offset = (3 - w) * 7 + (6 - d);
      const cellDate = new Date(endDate.getFullYear(), endDate.getMonth(), endDate.getDate() - offset);
      weekDays.push(toLocalDateStr(cellDate));
    }
    weeks.push(weekDays);
  }
  return weeks;
}

export function StreakCalendar({ data }: StreakCalendarProps) {
  const c = useThemeColors();

  if (!data || data.length === 0) {
    return null;
  }

  const todayStr = toLocalDateStr();
  const weeks = get28DayGrid();

  const dataByDate: Record<string, StreakDay> = {};
  for (const item of data) {
    dataByDate[item.date] = item;
  }

  let loggedCount = 0;
  for (const week of weeks) {
    for (const dateStr of week) {
      if (dataByDate[dateStr]?.logged) {
        loggedCount++;
      }
    }
  }

  const a11ySummary = `Logged ${loggedCount} of last 28 days`;

  return (
    <View
      accessibilityRole="summary"
      accessibilityLabel={a11ySummary}
      style={{ alignItems: "center", width: "100%" }}
    >
      {/* Weekday Header Row */}
      <View
        style={{
          flexDirection: "row",
          justifyContent: "space-between",
          width: "100%",
          maxWidth: 240,
          marginBottom: spacing.xs,
        }}
      >
        {WEEKDAYS.map((initial, idx) => (
          <View
            key={`header-${idx}`}
            style={{ width: 18, alignItems: "center", justifyContent: "center" }}
          >
            <Text
              style={{
                fontSize: 10,
                fontWeight: "600",
                color: c.textMuted,
                textAlign: "center",
              }}
            >
              {initial}
            </Text>
          </View>
        ))}
      </View>

      {/* 4 Week Rows */}
      <View style={{ width: "100%", maxWidth: 240, gap: spacing.xs }}>
        {weeks.map((week, weekIdx) => (
          <View
            key={`week-${weekIdx}`}
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
            }}
          >
            {week.map((dateStr) => {
              const entry = dataByDate[dateStr];
              const isLogged = entry?.logged ?? false;
              const isToday = dateStr === todayStr;
              const mealCount = entry?.mealCount;

              let cellBg = c.borderLight;
              let opacity = 1;

              if (isLogged) {
                cellBg = c.primary;
                if (typeof mealCount === "number" && mealCount > 0) {
                  if (mealCount === 1) {
                    opacity = 0.45;
                  } else if (mealCount === 2) {
                    opacity = 0.75;
                  } else {
                    opacity = 1;
                  }
                }
              }

              return (
                <View
                  key={dateStr}
                  style={{
                    width: 18,
                    height: 18,
                    borderRadius: 4,
                    backgroundColor: cellBg,
                    opacity,
                    borderWidth: isToday ? 1.5 : 0,
                    borderColor: isToday ? c.text : "transparent",
                  }}
                />
              );
            })}
          </View>
        ))}
      </View>
    </View>
  );
}
