#pragma warning disable OPENAI001

using OpenAI.Assistants;

namespace GutAI.Infrastructure.Services;

public static class ChatTools
{
    public static readonly FunctionToolDefinition SearchFoods = new("search_foods")
    {
        Description = "Search the food database by name. Returns up to 10 matching food products with IDs, nutrition, brand, data source, and match confidence. Use these results to pick the best match for the user's request. If the match is obvious (e.g. generic 'water' or 'chicken salad'), select it automatically. Only ask the user to choose when multiple results are plausible matches.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "query": { "type": "string", "description": "Food name to search for, e.g. 'greek yogurt' or 'doritos'" }
            },
            "required": ["query"]
        }
        """)
    };

    public static readonly FunctionToolDefinition GetFoodSafety = new("get_food_safety")
    {
        Description = "Get a safety report for a food product including FODMAP assessment, gut risk, additive analysis, and personalized score.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "food_product_id": { "type": "string", "description": "The food product ID (GUID) to get safety info for" }
            },
            "required": ["food_product_id"]
        }
        """)
    };

    public static readonly FunctionToolDefinition GetFodmapAssessment = new("get_fodmap_assessment")
    {
        Description = "Check the FODMAP status of a food product. Useful for users with IBS or FODMAP sensitivities.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "food_product_id": { "type": "string", "description": "The food product ID (GUID)" }
            },
            "required": ["food_product_id"]
        }
        """)
    };

    public static readonly FunctionToolDefinition LogMeal = new("log_meal")
    {
        Description = "Log a meal for the user. You MUST include food_product_id (the 'id' GUID from search_foods results) for EVERY item. Without food_product_id, nutrition data will be inaccurate. Workflow: 1) call search_foods, 2) select the best result, 3) call log_meal with that result's 'id' as food_product_id. NEVER call log_meal with only a name and no food_product_id.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "meal_type": { "type": "string", "enum": ["Breakfast", "Lunch", "Dinner", "Snack"], "description": "Type of meal" },
                "items": {
                    "type": "array",
                    "description": "Array of food items to log. Use food_product_id from search_foods results when available.",
                    "items": {
                        "type": "object",
                        "properties": {
                            "food_product_id": { "type": "string", "description": "Food product ID (GUID) from search_foods results. Preferred for accurate nutrition." },
                            "name": { "type": "string", "description": "Food name. Used as display name and as fallback for nutrition lookup when food_product_id is not available." },
                            "servings": { "type": "number", "description": "Number of servings. Defaults to 1." }
                        }
                    }
                },
                "description": { "type": "string", "description": "Fallback: Natural language description. Only use when items array is not feasible." }
            },
            "required": ["meal_type"]
        }
        """)
    };

    public static readonly FunctionToolDefinition LogSymptom = new("log_symptom")
    {
        Description = "Record a symptom for the user. Use the symptom type name and severity.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "symptom_name": { "type": "string", "description": "Name of the symptom, e.g. 'Bloating', 'Nausea', 'Gas', 'Headache', 'Fatigue'" },
                "severity": { "type": "integer", "minimum": 1, "maximum": 10, "description": "Severity from 1 (mild) to 10 (severe)" },
                "notes": { "type": "string", "description": "Optional notes about the symptom" }
            },
            "required": ["symptom_name", "severity"]
        }
        """)
    };

    public static readonly FunctionToolDefinition GetTodaysMeals = new("get_todays_meals")
    {
        Description = "Get all meals the user logged today with items and nutrition info."
    };

    public static readonly FunctionToolDefinition GetTriggerFoods = new("get_trigger_foods")
    {
        Description = "Get the user's trigger foods — foods most associated with their symptoms based on correlation analysis.",
        Parameters = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "days": { "type": "integer", "description": "Number of days to look back. Default 30." }
            }
        }
        """)
    };

    public static readonly FunctionToolDefinition GetSymptomHistory = new("get_symptom_history")
    {
        Description = "Get the user's recent symptom logs.",
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
        Description = "Get today's nutrition summary: total calories, protein, carbs, fat vs the user's goals."
    };

    public static readonly FunctionToolDefinition GetEliminationDietStatus = new("get_elimination_diet_status")
    {
        Description = "Get the user's current elimination diet phase, foods to eliminate, safe foods, and reintroduction results."
    };

    public static IReadOnlyList<FunctionToolDefinition> All => [
        SearchFoods, GetFoodSafety, GetFodmapAssessment,
        LogMeal, LogSymptom, GetTodaysMeals,
        GetTriggerFoods, GetSymptomHistory,
        GetNutritionSummary, GetEliminationDietStatus
    ];
}
