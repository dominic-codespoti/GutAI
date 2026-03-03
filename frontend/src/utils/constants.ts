export const MEAL_TYPES = ['Breakfast', 'Lunch', 'Dinner', 'Snack'] as const;

export function getMealTypeForTime(hour: number): typeof MEAL_TYPES[number] {
  if (hour >= 5 && hour < 11) return 'Breakfast';
  if (hour >= 11 && hour < 15) return 'Lunch';
  if (hour >= 15 && hour < 21) return 'Dinner';
  return 'Snack';
}
