const fs = require('fs');
const path = require('path');
const files = [
  'frontend/components/meals/AddMealSheet.tsx',
  'frontend/components/meals/SwapSearchContent.tsx',
  'frontend/components/meals/DailySummary.tsx',
  'frontend/components/meals/SwipeHint.tsx',
  'frontend/components/meals/CopyMealSheet.tsx',
  'frontend/components/meals/MealGroup.tsx',
  'frontend/components/meals/EditMealSheet.tsx',
  'frontend/components/meals/RecentFoodsRow.tsx',
  'frontend/components/meals/MealItemRow.tsx',
  'frontend/components/meals/MealTypeChips.tsx',
  'frontend/components/meals/MealDateNav.tsx',
  'frontend/components/SwipeableItemRow.tsx',
  'frontend/components/GoalField.tsx',
  'frontend/components/FoodSearchResult.tsx',
  'frontend/components/SourceChip.tsx',
  'frontend/components/AllergyChips.tsx',
  'frontend/components/NutritionBar.tsx',
  'frontend/components/MealTypePicker.tsx',
  'frontend/components/ServingSizeSelector.tsx'
];
for (const rel of files) {
  try {
    const file = path.join(process.cwd(), rel);
    const src = fs.readFileSync(file, 'utf8');
    const importLineRegex = /import\s*\{([\s\S]*?)\}\s*from\s*[](\.{1,2}\/src\/utils\/theme)["]
