using System.Text.RegularExpressions;

namespace GutAI.Application.Common.Helpers;

/// <summary>Deterministically classifies food names for portion calibration.</summary>
public static class FoodClassClassifier
{
    public static class Classes
    {
        public const string Starch = "starch";
        public const string Protein = "protein";
        public const string Vegetable = "vegetable";
        public const string Fruit = "fruit";
        public const string Dairy = "dairy";
        public const string FatSauce = "fat_sauce";
        public const string Beverage = "beverage";
        public const string Dessert = "dessert";
        public const string MixedDish = "mixed_dish";
        public const string Other = "other";
    }

    private static readonly (string Class, Regex Pattern)[] Rules =
    [
        (Classes.Beverage, BuildPattern("juice", "coffee", "tea", "soda", "smoothie", "shake", "latte", "beer", "wine", "water", "cola")),
        (Classes.FatSauce, BuildPattern("oil", "butter", "dressing", "mayonnaise", "mayo", "sauce", "gravy", "dip", "aioli", "pesto")),
        (Classes.Dessert, BuildPattern("cake", "cookie", "ice cream", "brownie", "pie", "pastry", "donut", "doughnut", "chocolate", "pudding", "muffin")),
        (Classes.MixedDish, BuildPattern("pizza", "sandwich", "burger", "burrito", "taco", "curry", "stew", "soup", "lasagna", "casserole", "bowl", "stir fry", "wrap", "sushi", "paella", "risotto")),
        (Classes.Starch, BuildPattern("rice", "pasta", "spaghetti", "noodle", "bread", "toast", "potato", "fries", "bagel", "tortilla", "oat", "oatmeal", "porridge", "cereal", "quinoa", "couscous", "hash brown")),
        (Classes.Protein, BuildPattern("chicken", "beef", "pork", "fish", "salmon", "tuna", "egg", "tofu", "steak", "sausage", "bacon", "shrimp", "prawn", "lamb", "turkey", "ham")),
        (Classes.Dairy, BuildPattern("cheese", "yogurt", "yoghurt", "milk", "cream")),
        (Classes.Fruit, BuildPattern("apple", "banana", "berry", "blueberry", "orange", "grape", "melon", "mango", "pineapple", "kiwi", "pear", "peach", "fruit")),
        (Classes.Vegetable, BuildPattern("broccoli", "carrot", "lettuce", "spinach", "salad", "tomato", "cucumber", "pepper", "onion", "vegetable", "veg", "greens", "asparagus", "zucchini", "mushroom", "cabbage", "kale", "bean", "pea", "corn"))
    ];

    /// <summary>
    /// Classifies by ordered whole-word rules. The order intentionally resolves overlaps:
    /// for example, "chicken curry" is a mixed dish and "peanut butter" is a fat/sauce.
    /// </summary>
    public static string Classify(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var (foodClass, pattern) in Rules)
        {
            if (pattern.IsMatch(name))
                return foodClass;
        }

        return Classes.Other;
    }

    public static string ConfidenceTier(decimal portionConfidence) =>
        portionConfidence < 0.5m ? "low" : portionConfidence < 0.75m ? "medium" : "high";

    private static Regex BuildPattern(params string[] keywords)
    {
        var forms = keywords.SelectMany(PluralForms)
            .Select(form => string.Join("(?:\\s+|-)", form.Split(' ').Select(Regex.Escape)));
        var alternatives = string.Join("|", forms);
        return new Regex($"(?<![\\p{{L}}\\p{{N}}])(?:{alternatives})(?![\\p{{L}}\\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static IEnumerable<string> PluralForms(string keyword)
    {
        yield return keyword;
        if (keyword == "stir fry")
            yield return "stir fries";
        if (keyword.EndsWith('y') && keyword.Length > 1 && !IsVowel(keyword[^2]))
        {
            yield return keyword[..^1] + "ies";
            yield break;
        }

        if (keyword.EndsWith('o'))
        {
            yield return keyword + "es";
            yield return keyword + "s";
        }
        else if (keyword.EndsWith('s') || keyword.EndsWith('x') || keyword.EndsWith("ch", StringComparison.Ordinal) || keyword.EndsWith("sh", StringComparison.Ordinal))
            yield return keyword + "es";
        else
            yield return keyword + "s";
    }

    private static bool IsVowel(char character) => "aeiou".Contains(char.ToLowerInvariant(character));
}
