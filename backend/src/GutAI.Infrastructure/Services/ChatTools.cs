#pragma warning disable OPENAI001

using OpenAI.Assistants;

namespace GutAI.Infrastructure.Services;

public static class ChatTools
{
    public static readonly FunctionToolDefinition SearchFoods = new("search_foods")
    {
        Description = "Search the food database by name for matching food products. Call this first before any food-related operation to find the right food product ID. Returns up to 10 results with nutrition per 100g, brand, data source, and match confidence. Choose the best match automatically when one result is clearly correct; only ask the user when multiple results are equally plausible.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "query": { "type": "string", "description": "Food name to search for (required). e.g. 'greek yogurt', 'chicken salad', 'coca cola'" }
            },
            "required": ["query"]
        }
        """)
    };

    public static readonly FunctionToolDefinition GetFoodSafety = new("get_food_safety")
    {
        Description = "Get a comprehensive personalized safety report for a food product. Combines FODMAP assessment, gut risk analysis (additives, NOVA, sodium), and a personalized score factoring in the user's allergies, conditions, and meal history. Prefer this over get_fodmap_assessment when you need the full picture.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "food_product_id": { "type": "string", "description": "The food product ID (GUID) from search_foods results (required)" }
            },
            "required": ["food_product_id"]
        }
        """)
    };

    public static readonly FunctionToolDefinition GetFodmapAssessment = new("get_fodmap_assessment")
    {
        Description = "Get the FODMAP assessment for a food product (score, rating, triggers, summary). This is a subset of get_food_safety. Use when you only need FODMAP-specific info, or use get_food_safety for the complete safety picture including gut risk and personalized scoring.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "food_product_id": { "type": "string", "description": "The food product ID (GUID) from search_foods results (required)" }
            },
            "required": ["food_product_id"]
        }
        """)
    };

    public static readonly FunctionToolDefinition LogMeal = new("log_meal")
    {
        Description = "Log a meal with one or more food items. For each item, first call search_foods to find the right database entry, then include its food_product_id here. The system will calculate nutrition from the database record. If the product's default serving weight is missing or wrong, you can pass serving_weight_g (grams per serving) to ensure accurate totals. Example: 4 eggs → search_foods finds 'Egg, whole, raw, fresh', then log with food_product_id + servings=4 + serving_weight_g=50.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "meal_type": { "type": "string", "enum": ["Breakfast", "Lunch", "Dinner", "Snack"], "description": "Type of meal (required)" },
                "items": {
                    "type": "array",
                    "description": "Array of food items to log.",
                    "items": {
                        "type": "object",
                        "properties": {
                            "food_product_id": { "type": "string", "description": "Food product ID (GUID) from search_foods results. Include this to link the log to a known product." },
                            "name": { "type": "string", "description": "Food name. Required as display name." },
                            "servings": { "type": "number", "description": "Number of servings. Defaults to 1." },
                            "serving_weight_g": { "type": "number", "description": "Grams per serving. For example, 1 egg = 50g, so 4 eggs would be servings=4 with serving_weight_g=50. If omitted, the system uses the product's default serving weight (or 100g if unknown)." }
                        },
                        "required": ["name"]
                    }
                },
                "description": { "type": "string", "description": "Fallback: natural language description of the meal. Only use when items array cannot capture the meal." },
                "logged_at": { "type": "string", "description": "Optional ISO 8601 datetime for when the meal was eaten. Defaults to now if omitted. Use when logging past meals, e.g. \"yesterday's lunch\" or \"last night's dinner\"." }
            },
            "required": ["meal_type"]
        }
        """)
    };

    public static readonly FunctionToolDefinition LogSymptom = new("log_symptom")
    {
        Description = "Record a symptom the user is experiencing. Severity must be 1 (mild) to 10 (severe). Common symptom names include: Bloating, Nausea, Gas, Headache, Fatigue, Stomach Pain, Diarrhea, Constipation, Heartburn, Cramps. If the user uses a different name, match it to the closest standard symptom.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "symptom_name": { "type": "string", "description": "Name of the symptom, e.g. 'Bloating', 'Nausea', 'Gas', 'Headache', 'Fatigue', 'Stomach Pain'" },
                "severity": { "type": "integer", "minimum": 1, "maximum": 10, "description": "Severity from 1 (mild) to 10 (severe). Required." },
                "notes": { "type": "string", "description": "Optional notes about the symptom — e.g. timing, triggers, duration." }
            },
            "required": ["symptom_name", "severity"]
        }
        """)
    };

    public static readonly FunctionToolDefinition GetTodaysMeals = new("get_todays_meals")
    {
        Description = "Get all meals the user logged today with per-item and per-meal nutrition info. 'Today' is determined by the user's timezone. Use this to answer questions about what the user has eaten today."
    };

    public static readonly FunctionToolDefinition GetTriggerFoods = new("get_trigger_foods")
    {
        Description = "Get the user's trigger foods — foods most associated with their symptoms based on statistical correlation analysis. Only returns correlations that occurred 2+ times with average severity of 4+. Uses the user's timezone for date range calculation.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "days": { "type": "integer", "description": "Number of days to look back for correlation data. Default 30." }
            }
        }
        """)
    };

    public static readonly FunctionToolDefinition GetSymptomHistory = new("get_symptom_history")
    {
        Description = "Get the user's recent symptom logs. Returns up to 20 of the most recent entries with symptom name, severity, timestamp, and notes. Uses the user's timezone for date range.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "days": { "type": "integer", "description": "Number of days to look back. Default 7." }
            }
        }
        """)
    };

    public static readonly FunctionToolDefinition GetNutritionSummary = new("get_nutrition_summary")
    {
        Description = "Get today's nutrition totals (calories, protein, carbs, fat, fiber) compared against the user's daily goals. 'Today' is determined by the user's timezone. Use this before making dietary recommendations to understand what the user has already consumed."
    };

    public static readonly FunctionToolDefinition GetEliminationDietStatus = new("get_elimination_diet_status")
    {
        Description = "Get the user's current elimination diet phase, foods to eliminate, safe foods, reintroduction results, and recommendations. Use this when the user asks about their elimination diet progress or what foods are safe during their current phase."
    };

    public static readonly FunctionToolDefinition GetUserProfile = new("get_user_profile")
    {
        Description = "Get the authenticated user's profile including allergies, gut conditions, dietary preferences, daily nutrition goals, and timezone. Use this to personalize advice before making recommendations."
    };

    public static IReadOnlyList<FunctionToolDefinition> All => [
        SearchFoods, GetFoodSafety, GetFodmapAssessment,
        LogMeal, LogSymptom, GetTodaysMeals,
        GetTriggerFoods, GetSymptomHistory,
        GetNutritionSummary, GetEliminationDietStatus,
        GetUserProfile
    ];
}
