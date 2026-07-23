namespace GutAI.Infrastructure.Data;

/// <summary>
/// Single consolidated synonym table (regional/colloquial single words + multi-word
/// phrases) used symmetrically for query expansion. Previously split across two
/// independently-maintained dictionaries — the Lucene analyzer's <c>SynonymFilter</c> map
/// and <c>FoodQueryBuilder.MultiWordSynonyms</c> — with no shared vocabulary between them.
/// </summary>
internal static class FoodSynonyms
{
    /// <summary>Multi-word phrase → expansion tokens. Checked first against the whole query.</summary>
    private static readonly Dictionary<string, string[]> PhraseSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orange juice"] = ["orange", "juice", "raw"],
        ["chicken breast"] = ["chicken", "broilers", "breast", "meat"],
        ["white rice"] = ["rice", "white", "long", "grain"],
        ["brown rice"] = ["rice", "brown", "long", "grain"],
        ["sweet potato"] = ["sweet", "potato", "raw"],
        ["olive oil"] = ["oil", "olive", "salad", "cooking"],
        ["white bread"] = ["bread", "white"],
        ["ground beef"] = ["beef", "ground"],
        ["whole milk"] = ["milk", "whole"],
        ["corn tortilla"] = ["tortilla", "corn"],
        ["rice cake"] = ["rice", "cake", "puffed"],
        ["rice cakes"] = ["rice", "cake", "puffed"],
        ["tomato sauce"] = ["tomato", "sauce", "marinara", "pasta"],
        ["protein shake"] = ["protein", "shake", "beverage", "supplement", "whey"],
        ["protein bar"] = ["protein", "bar", "energy"],
        ["hot dog"] = ["frankfurter", "sausage", "beef"],
        ["mac and cheese"] = ["macaroni", "cheese"],
        ["peanut butter"] = ["peanut", "butter", "spread"],
        ["almond milk"] = ["almond", "milk", "beverage"],
        ["oat milk"] = ["oat", "milk", "beverage"],
        ["coconut milk"] = ["coconut", "milk"],
        ["soy milk"] = ["soy", "milk", "soymilk", "beverage"],
        ["cream cheese"] = ["cream", "cheese"],
        ["sour cream"] = ["sour", "cream"],
        ["ice cream"] = ["ice", "cream", "frozen", "dessert"],
        ["green tea"] = ["tea", "green"],
        ["black tea"] = ["tea", "black"],
        ["fried rice"] = ["rice", "fried"],
        ["fish fingers"] = ["fish", "sticks", "breaded"],
        ["fish sticks"] = ["fish", "sticks", "breaded"],
        ["chicken thigh"] = ["chicken", "thigh", "meat"],
        ["chicken wing"] = ["chicken", "wing"],
        ["lamb chop"] = ["lamb", "chop", "loin"],
        ["bell pepper"] = ["peppers", "sweet", "bell"],
        ["spring onion"] = ["onions", "spring", "scallion"],
        ["green onion"] = ["onions", "spring", "scallion"],
    };

    /// <summary>Single-word regional/colloquial → canonical token(s). Applied per query token.</summary>
    private static readonly Dictionary<string, string[]> WordSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Cooking forms
        ["toast"] = ["bread", "toasted"],
        ["steak"] = ["beef", "loin"],
        ["oatmeal"] = ["oats", "cereal"],
        ["porridge"] = ["oats", "cereal"],
        ["fries"] = ["potatoes", "french", "fried"],
        ["chips"] = ["potato", "chips"],
        ["soda"] = ["carbonated", "beverage"],
        ["pop"] = ["carbonated", "beverage"],

        // Regional AU/UK → US equivalents
        ["capsicum"] = ["peppers", "sweet"],
        ["prawns"] = ["shrimp"],
        ["prawn"] = ["shrimp"],
        ["mince"] = ["ground", "beef"],
        ["rocket"] = ["arugula"],
        ["coriander"] = ["cilantro"],
        ["aubergine"] = ["eggplant"],
        ["courgette"] = ["zucchini"],
        ["beetroot"] = ["beets"],
        ["sultana"] = ["raisins", "golden"],
        ["sultanas"] = ["raisins", "golden"],
        ["crisps"] = ["potato", "chips"],
        ["biscuit"] = ["cookie"],
        ["biscuits"] = ["cookies"],
        ["lolly"] = ["candy"],
        ["lollies"] = ["candy"],
        ["muesli"] = ["granola", "cereal"],
        ["skim"] = ["nonfat"],
        ["skimmed"] = ["nonfat"],
        ["wholemeal"] = ["whole", "wheat"],
        ["minced"] = ["ground"],
        ["tinned"] = ["canned"],

        // Common colloquial terms
        ["hotdog"] = ["frankfurter", "sausage"],
        ["jam"] = ["preserves", "jelly"],
        ["ketchup"] = ["catsup", "tomato", "sauce"],
        ["mayo"] = ["mayonnaise"],
        ["vegemite"] = ["yeast", "extract", "spread"],
        ["marmite"] = ["yeast", "extract", "spread"],
        ["yoghurt"] = ["yogurt"],
    };

    /// <summary>
    /// Expands query tokens with synonyms: whole-phrase match first (higher-fidelity — a
    /// multi-word phrase carries more context than any single token), then per-token
    /// single-word lookups. Always includes the original tokens.
    /// </summary>
    public static string[] Expand(string queryLower, string[] tokens)
    {
        var expanded = new List<string>(tokens);

        foreach (var (phrase, expansion) in PhraseSynonyms)
        {
            if (queryLower.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                expanded.AddRange(expansion);
                break;
            }
        }

        foreach (var token in tokens)
        {
            if (WordSynonyms.TryGetValue(token, out var expansion))
                expanded.AddRange(expansion);
        }

        return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
