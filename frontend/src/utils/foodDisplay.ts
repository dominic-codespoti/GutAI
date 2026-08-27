const MINOR_WORD_BY_NAME: Record<string, true> = {
  a: true,
  an: true,
  and: true,
  as: true,
  at: true,
  but: true,
  by: true,
  for: true,
  in: true,
  of: true,
  on: true,
  or: true,
  the: true,
  to: true,
  with: true,
};

/** Title-case a food name while preserving short all-caps tokens such as USDA. */
export function titleCaseFoodName(value: string): string {
  const trimmed = value.trim();
  if (!trimmed) return "";

  let wordIndex = 0;
  return trimmed
    .split(/(\s+|-)/)
    .map((part) => {
      if (!part || /^\s+$|^-$/.test(part)) return part;

      const lower = part.toLocaleLowerCase();
      const isFirst = wordIndex++ === 0;
      if (!isFirst && MINOR_WORD_BY_NAME[lower]) return lower;
      if (part === part.toUpperCase() && part.length <= 4) return part;

      return part.charAt(0).toLocaleUpperCase() + part.slice(1);
    })
    .join("");
}

/** Display the persisted total weight for a meal item, with a safe legacy fallback. */
export function formatMealItemPortion(item: {
  servings: number;
  servingUnit: string;
  servingWeightG?: number;
}): string {
  if (item.servingWeightG && item.servingWeightG > 0) {
    return `${Math.round(item.servingWeightG)} g`;
  }
  return `${item.servings} ${item.servingUnit}`.trim();
}

function formatQuantity(quantity: number): string {
  if (quantity >= 10) return String(Math.round(quantity));

  const quarters = Math.round(quantity * 4);
  const whole = Math.floor(quarters / 4);
  const remainder = quarters % 4;
  const fraction = ["", "¼", "½", "¾"][remainder];
  if (remainder === 0) return String(whole);
  if (whole === 0) return fraction;
  return `${whole}${fraction}`;
}

/**
 * Render the model-supplied household unit against the current editable weight.
 * The hint is display-only and never contributes nutrition calculations.
 */
export function formatServingHint(item: {
  grams?: number;
  servingWeightG?: number;
  servingHintUnit?: string | null;
  servingHintUnitPlural?: string | null;
  servingHintUnitGrams?: number | null;
}): string | null {
  const singular = item.servingHintUnit?.trim();
  const plural = item.servingHintUnitPlural?.trim() || singular;
  const unitGrams = item.servingHintUnitGrams ?? 0;
  if (!singular || !Number.isFinite(unitGrams) || unitGrams <= 0) return null;
  const weightG = item.grams ?? item.servingWeightG;
  if (weightG == null || !Number.isFinite(weightG) || weightG <= 0) return null;
  const count = weightG / unitGrams;
  const quantity = formatQuantity(count);
  const isSingular = Math.round(count * 4) / 4 === 1;
  return `≈ ${quantity} ${isSingular ? singular : plural}`;
}
