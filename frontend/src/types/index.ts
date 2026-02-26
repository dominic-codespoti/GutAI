// Types matching the backend DTOs

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  dailyCalorieGoal: number;
  dailyProteinGoalG: number;
  dailyCarbGoalG: number;
  dailyFatGoalG: number;
  dailyFiberGoalG: number;
  allergies: string[];
  dietaryPreferences: string[];
  gutConditions: string[];
  onboardingCompleted: boolean;
  timezoneId?: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: UserProfile;
}

export interface MealLog {
  id: string;
  userId: string;
  mealType: "Breakfast" | "Lunch" | "Dinner" | "Snack";
  loggedAt: string;
  notes: string | null;
  originalText: string | null;
  items: MealItem[];
  totalCalories: number;
  totalProteinG: number;
  totalCarbsG: number;
  totalFatG: number;
}

export interface MealItem {
  id: string;
  foodName: string;
  barcode: string | null;
  servings: number;
  servingUnit: string;
  servingWeightG?: number;
  foodProductId?: string;
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  fiberG: number;
  sugarG: number;
  sodiumMg: number;
  cholesterolMg?: number;
  saturatedFatG?: number;
  potassiumMg?: number;
}

export interface CreateMealRequest {
  mealType: string;
  loggedAt: string;
  notes?: string;
  originalText?: string;
  items: CreateMealItemRequest[];
}

export interface CreateMealItemRequest {
  foodName: string;
  barcode?: string;
  foodProductId?: string;
  servings: number;
  servingUnit: string;
  servingWeightG?: number;
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  fiberG?: number;
  sugarG?: number;
  sodiumMg?: number;
  cholesterolMg?: number;
  saturatedFatG?: number;
  potassiumMg?: number;
}

export interface NaturalLanguageMealRequest {
  text: string;
  mealType: string;
}

export interface ParsedFoodItem {
  name: string;
  foodProductId?: string;
  servingWeightG: number;
  servingSize: string;
  servingQuantity: number;
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  fiberG: number;
  sugarG: number;
  sodiumMg: number;
  cholesterolMg: number;
  saturatedFatG: number;
  potassiumMg: number;
}

export interface NaturalLanguageResponse {
  originalText: string;
  mealType: string;
  parsedItems: ParsedFoodItem[];
}

export interface FoodProduct {
  id: string;
  barcode: string | null;
  name: string;
  brand: string | null;
  ingredients: string | null;
  imageUrl: string | null;
  novaGroup: number | null;
  nutriScore: string | null;
  allergensTags: string[];
  calories100g: number | null;
  protein100g: number | null;
  carbs100g: number | null;
  fat100g: number | null;
  fiber100g: number | null;
  sugar100g: number | null;
  sodium100g: number | null;
  servingSize: string | null;
  servingQuantity: number | null;
  safetyScore: number | null;
  safetyRating: string | null;
  dataSource: string | null;
  externalId: string | null;
  additives: FoodAdditive[];
}

export interface FoodAdditive {
  id: number;
  eNumber: string | null;
  name: string;
  category: string;
  cspiRating: string;
  usStatus: string;
  euStatus: string;
  safetyRating: string;
  healthConcerns: string;
  bannedInCountries: string[];
  description?: string;
  alternateNames?: string[];
  efsaAdiMgPerKgBw?: number;
}

export interface SymptomLog {
  id: string;
  symptomTypeId: number;
  symptomName: string;
  category: string;
  icon: string;
  severity: number;
  occurredAt: string;
  relatedMealLogId: string | null;
  notes: string | null;
  duration: string | null;
}

export interface SymptomType {
  id: number;
  name: string;
  category: string;
  icon: string;
}

export interface CreateSymptomRequest {
  symptomTypeId: number;
  severity: number;
  occurredAt: string;
  notes?: string;
  relatedMealLogId?: string;
  duration?: string;
}

export interface DailyNutritionSummary {
  date: string;
  totalCalories: number;
  totalProteinG: number;
  totalCarbsG: number;
  totalFatG: number;
  totalFiberG: number;
  totalSugarG: number;
  totalSodiumMg: number;
  mealCount: number;
  calorieGoal: number;
}

export interface Correlation {
  foodOrAdditive: string;
  symptomName: string;
  occurrences: number;
  totalMeals: number;
  frequencyPercent: number;
  averageSeverity: number;
  confidence: string;
}

export interface NutritionTrend {
  date: string;
  calories: number;
  protein: number;
  carbs: number;
  fat: number;
  fiber: number;
  sugar: number;
  sodium: number;
  mealCount: number;
}

export interface AdditiveExposure {
  additive: string;
  cspiRating: string;
  count: number;
}

export interface UserFoodAlert {
  additiveId: number;
  name: string;
  cspiRating: string;
  alertEnabled: boolean;
}

export interface SafetyReport {
  product: FoodProduct;
  additives: FoodAdditive[];
  safetyScore: number | null;
  safetyRating: string | null;
  novaGroup: number | null;
  nutriScore: string | null;
  gutRisk: GutRiskAssessment | null;
  fodmap: FodmapAssessment | null;
  substitutions: SubstitutionResult | null;
  glycemic: GlycemicAssessment | null;
}

export interface GutRiskAssessment {
  gutScore: number;
  gutRating: string;
  flagCount: number;
  highRiskCount: number;
  mediumRiskCount: number;
  lowRiskCount: number;
  flags: GutRiskFlag[];
  summary: string;
}

export interface GutRiskFlag {
  source: string;
  code: string;
  name: string;
  category: string;
  riskLevel: string;
  explanation: string;
}

export interface FodmapAssessment {
  fodmapScore: number;
  fodmapRating: string;
  triggerCount: number;
  highCount: number;
  moderateCount: number;
  lowCount: number;
  categories: string[];
  triggers: FodmapTrigger[];
  summary: string;
}

export interface FodmapTrigger {
  name: string;
  category: string;
  subCategory: string;
  severity: string;
  explanation: string;
}

export interface SubstitutionResult {
  productName: string;
  suggestionCount: number;
  suggestions: Substitution[];
  summary: string;
}

export interface Substitution {
  original: string;
  substitute: string;
  reason: string;
  category: string;
  gutBenefit: string;
  confidence: string;
}

export interface GlycemicAssessment {
  estimatedGI: number | null;
  giCategory: string;
  estimatedGL: number | null;
  glCategory: string;
  matchCount: number;
  matches: GlycemicMatch[];
  gutImpactSummary: string;
  recommendations: string[];
}

export interface GlycemicMatch {
  food: string;
  gi: number;
  giCategory: string;
  source: string;
  notes: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface DataExport {
  exportedAt: string;
  from: string;
  to: string;
  meals: MealLog[];
  symptoms: {
    id: string;
    symptomName: string;
    category: string;
    severity: number;
    occurredAt: string;
    notes: string | null;
  }[];
}

export interface TriggerFood {
  food: string;
  symptoms: string[];
  totalOccurrences: number;
  avgSeverity: number;
  worstConfidence: string;
}

export interface PersonalizedScore {
  compositeScore: number;
  rating: string;
  fodmapComponent: number;
  additiveRiskComponent: number;
  novaComponent: number;
  fiberComponent: number;
  allergenComponent: number;
  sugarAlcoholComponent: number;
  personalTriggerPenalty: number;
  explanations: ScoreExplanation[];
  personalWarnings: string[];
  summary: string;
}

export interface ScoreExplanation {
  component: string;
  weight: number;
  rawScore: number;
  weightedContribution: number;
  explanation: string;
}

export interface FoodDiaryAnalysis {
  totalMealsAnalyzed: number;
  totalSymptomsAnalyzed: number;
  patternsFound: number;
  fromDate: string;
  toDate: string;
  patterns: FoodSymptomPattern[];
  timingInsights: TimingInsight[];
  recommendations: string[];
  summary: string;
}

export interface FoodSymptomPattern {
  foodName: string;
  symptomName: string;
  occurrences: number;
  averageSeverity: number;
  averageOnsetHours: number;
  confidence: string;
  explanation: string;
}

export interface TimingInsight {
  insight: string;
  category: string;
  supportingDataPoints: number;
}

export interface EliminationDietStatus {
  phase: string;
  foodsToEliminate: string[];
  foodsToReintroduce: string[];
  safeFoods: string[];
  reintroductionResults: ReintroductionResult[];
  recommendations: string[];
  summary: string;
}

export interface ReintroductionResult {
  foodName: string;
  result: string;
  averageSeverity: number;
  testCount: number;
}
