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
  correctionCount: number;
  lastCorrectedAt: string | null;
  items: MealItem[];
  totalCalories: number;
  totalProteinG: number;
  totalCarbsG: number;
  totalFatG: number;
}
export interface MealTemplate {
  id: string;
  name: string;
  mealType: string;
  notes?: string;
  items: CreateMealItemRequest[];
}

export interface MealItem {
  id: string;
  foodName: string;
  barcode: string | null;
  servings: number;
  servingUnit: string;
  servingWeightG?: number;
  foodProductId?: string;
  safetyRating?: string | null;
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
  matchConfidence?: number;
  nutritionProvenance?: string;
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
  matchConfidence?: number;
  nutritionProvenance?: string;
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
  matchConfidence: number;
  portionConfidence: number;
  nutritionProvenance: string;
  resolutionStatus: string;
}

export interface NaturalLanguageResponse {
  originalText: string;
  mealType: string;
  parsedItems: ParsedFoodItem[];
}

export type FoodKind = "WholeFood" | "Branded" | "Unknown";

export interface FoodProduct {
  id: string;
  barcode: string | null;
  name: string;
  brand: string | null;
  ingredients: string | null;
  imageUrl: string | null;
  imageFrontUrl: string | null;
  imageIngredientsUrl: string | null;
  imageNutritionUrl: string | null;
  novaGroup: number | null;
  nutriScore: string | null;
  allergensTags: string[];
  calories100g: number | null;
  protein100g: number | null;
  carbs100g: number | null;
  fat100g: number | null;
  fiber100g: number | null;
  sugar100g: number | null;
  sodiumMg100g: number | null;
  servingSize: string | null;
  servingQuantity: number | null;
  safetyScore: number | null;
  safetyRating: string | null;
  foodKind: FoodKind;
  dataSource: string | null;
  sourceUrl: string | null;
  sourceVersion?: string | null;
  licenseType?: string | null;
  attribution?: string | null;
  retrievedAt?: string | null;
  externalId: string | null;
  additives: FoodAdditive[];
  matchConfidence: number;
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
  evidenceSources?: string[];
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
  baselineFrequencyPercent: number;
  averageSeverity: number;
  confidence: string;
  attributionMethod: string;
  limitations: string[];
}

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  createdAt: string;
}

export interface ChatStreamEvent {
  content?: string;
  tool_call?: string;
  status?: string;
  error?: string;
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
  confidence: string;
  flagCount: number;
  highRiskCount: number;
  mediumRiskCount: number;
  lowRiskCount: number;
  doseSensitiveFlagsCount: number;
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
  triggerType: string;
  fodmapClass: string | null;
  doseSensitivity: string | null;
}

export interface FodmapAssessment {
  status: string;
  ingredientScreeningScore: number;
  confidence: string;
  triggerCount: number;
  highCount: number;
  moderateCount: number;
  lowCount: number;
  categories: string[];
  triggers: FodmapTrigger[];
  missingEvidence: string[];
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
  confidence: string;
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
  exposureMeals: number;
  associationRatePercent: number;
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

export interface RecentFood {
  foodName: string;
  foodProductId?: string;
  calories: number;
  proteinG: number;
  carbsG: number;
  fatG: number;
  fiberG: number;
  sugarG: number;
  sodiumMg: number;
  servingWeightG?: number;
  servingUnit: string;
  lastLoggedAt: string;
  logCount: number;
}

export interface Streak {
  currentStreak: number;
  longestStreak: number;
  totalDaysLogged: number;
}

export interface MealTypeNutrition {
  mealType: string;
  totalCalories: number;
  totalProteinG: number;
  totalCarbsG: number;
  totalFatG: number;
  mealCount: number;
}

export interface FavoriteFood {
  foodProductId: string;
  foodName: string;
  brand: string | null;
  calories100g: number | null;
  protein100g: number | null;
  carbs100g: number | null;
  fat100g: number | null;
  servingSize: string | null;
  servingQuantity: number | null;
  servingWeightG: number | null;
  imageUrl: string | null;
  createdAt: string;
}

export interface MealFood {
  id: string;
  name: string;
  brand: string | null;
  imageUrl: string | null;
  calories100g: number | null;
  protein100g: number | null;
  carbs100g: number | null;
  fat100g: number | null;
  fiber100g: number | null;
  sugar100g: number | null;
  sodiumMg100g: number | null;
  servingSize: string | null;
  servingQuantity: number | null;
  dataSource: string | null;
  sourceUrl: string | null;
  externalId: string | null;
}

export interface CustomFood {
  id?: string;
  name: string;
  brandName?: string | null;
  servingSize: number;
  servingSizeUnit: string;
  calories: number;
  proteinG: number;
  carbG: number;
  fatG: number;
  fiberG?: number | null;
  sugarG?: number | null;
  sodiumMg?: number | null;
  ingredients?: string | null;
}
