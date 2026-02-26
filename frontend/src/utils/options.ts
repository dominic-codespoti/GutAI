export const ALLERGY_OPTIONS = [
  "Peanuts",
  "Tree Nuts",
  "Milk",
  "Eggs",
  "Wheat",
  "Soy",
  "Fish",
  "Shellfish",
  "Sesame",
] as const;

export const DIET_OPTIONS = [
  "None",
  "Vegetarian",
  "Vegan",
  "Keto",
  "Paleo",
  "Gluten-Free",
  "Low-FODMAP",
  "Mediterranean",
] as const;

export const GUT_CONDITION_OPTIONS = [
  {
    id: "IBS",
    label: "IBS",
    emoji: "🫃",
    description: "Irritable Bowel Syndrome",
  },
  {
    id: "FODMAP Sensitive",
    label: "FODMAP Sensitive",
    emoji: "🧪",
    description: "Sensitive to fermentable carbs",
  },
  {
    id: "SIBO",
    label: "SIBO",
    emoji: "🦠",
    description: "Small intestinal bacterial overgrowth",
  },
  {
    id: "Crohn's Disease",
    label: "Crohn's Disease",
    emoji: "🔥",
    description: "Inflammatory bowel disease",
  },
  {
    id: "Ulcerative Colitis",
    label: "Ulcerative Colitis",
    emoji: "🩸",
    description: "Colon inflammation",
  },
  {
    id: "GERD",
    label: "GERD",
    emoji: "🔥",
    description: "Acid reflux / heartburn",
  },
  {
    id: "Celiac Disease",
    label: "Celiac Disease",
    emoji: "🌾",
    description: "Gluten intolerance",
  },
  {
    id: "General Wellness",
    label: "General Wellness",
    emoji: "💚",
    description: "No specific condition",
  },
] as const;
