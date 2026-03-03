namespace GutAI.Infrastructure.Data;

/// <summary>
/// Static term lists used by both index-time quality scoring and post-Lucene re-ranking.
/// </summary>
internal static class FoodScoringTerms
{
    public static readonly string[] HardPenaltyTerms =
    [
        "frozen", "canned", "dehydrated", "powder", "mix",
        "mixture", "substitute", "imitation", "meatless", "baby food", "infant", "formula",
        "alaska native", "industrial", "fast food",
        "ns as to", "usda commodity", "as purchased", "not further specified",
        "nfs", "ready-to-eat", "ready-to-heat", "glucose reduced", "stabilized",
        "nuggets", "nugget", "breaded", "patties", "patty", "stick", "sticks",
        "cereals ready-to-eat", "includes foods for usda", "food distribution program",
        "mechanically deboned", "mechanically separated", "by-products", "manufacturing",
        "glucose", "liquid from"
    ];

    public static readonly string[] SoftPenaltyTerms =
    [
        "navajo", "hopi", "southwest", "shoshone", "apache",
        "pasteurized", "restaurant", "commercial", "institutional",
        "from concentrate", "hohoysi", "laborador", "tundra"
    ];

    public static readonly string[] RawFreshTerms = ["raw", "fresh"];
    public static readonly string[] PlainTerms = ["whole", "plain", "white", "regular"];
    public static readonly string[] ProcessedTerms = ["juice", "concentrate", "dried", "dehydrated", "pickled", "sauce", "paste", "spread", "flavored", "frozen", "canned", "powder"];

    public static readonly string[] ImitationTerms = ["meatless", "imitation", "substitute", "analog"];

    public static readonly string[] OrganMeatTerms = ["liver", "giblets", "heart", "gizzard", "tongue", "kidney", "brain", "tripe", "sweetbreads"];

    public static readonly string[] DerivedFormTerms = ["juice", "oil", "butter", "buns", "frosted", "products", "liquid", "nectar", "concentrate", "roll", "sliced", "deli"];

    public static readonly string[] CuredTerms = ["cured", "salt pork", "corned", "smoked"];

    public static readonly HashSet<string> SpiceTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "cinnamon", "pepper", "nutmeg", "cloves", "cumin", "paprika", "turmeric",
        "oregano", "basil", "thyme", "rosemary", "parsley"
    };
}
