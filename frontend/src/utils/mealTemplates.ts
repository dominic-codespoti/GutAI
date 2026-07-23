import type { CreateMealItemRequest, MealLog, MealTemplate } from "../types";
import { deleteItem, getItem, setItem } from "./storage";

const KEY = "meal-templates";

export async function listMealTemplates(): Promise<MealTemplate[]> {
  const raw = await getItem(KEY);
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export async function saveMealTemplate(meal: MealLog): Promise<MealTemplate> {
  const items: CreateMealItemRequest[] = meal.items.map((item) => ({
    foodName: item.foodName,
    barcode: item.barcode ?? undefined,
    foodProductId: item.foodProductId ?? undefined,
    servings: item.servings,
    servingUnit: item.servingUnit,
    servingWeightG: item.servingWeightG,
    calories: item.calories,
    proteinG: item.proteinG,
    carbsG: item.carbsG,
    fatG: item.fatG,
    fiberG: item.fiberG,
    sugarG: item.sugarG,
    sodiumMg: item.sodiumMg,
    cholesterolMg: item.cholesterolMg,
    saturatedFatG: item.saturatedFatG,
    potassiumMg: item.potassiumMg,
  }));
  const template: MealTemplate = {
    id: `${Date.now()}-${Math.random().toString(36).slice(2)}`,
    name: `${meal.mealType} · ${meal.items.map((item) => item.foodName).slice(0, 2).join(", ")}`,
    mealType: meal.mealType,
    notes: meal.notes ?? undefined,
    items,
  };
  const templates = await listMealTemplates();
  await setItem(KEY, JSON.stringify([template, ...templates].slice(0, 20)));
  return template;
}

export async function deleteMealTemplate(id: string): Promise<void> {
  const templates = await listMealTemplates();
  const next = templates.filter((template) => template.id !== id);
  if (next.length === 0) await deleteItem(KEY);
  else await setItem(KEY, JSON.stringify(next));
}
