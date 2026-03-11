namespace GutAI.Infrastructure.Services;

/// <summary>
/// Canonical severity ratings for ingredients assessed by both FodmapService and GutRiskService.
/// Both services MUST reference these values to prevent contradictory assessments.
/// Based on Monash University FODMAP research and peer-reviewed studies.
/// </summary>
internal static class SharedFodmapSeverities
{
    /// <summary>
    /// Maps canonical ingredient names to their agreed FODMAP severity.
    /// Key = lowercase ingredient name, Value = severity ("High", "Moderate", "Low").
    /// Both FodmapData and GutRiskData MUST derive their severity from these values.
    /// GutRiskData maps "Moderate" → "Medium" via ToRiskLevel().
    /// </summary>
    public static readonly Dictionary<string, string> Severities = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fructans
        ["wheat"] = "High",
        ["barley"] = "High",
        ["rye"] = "High",
        ["onion"] = "High",
        ["garlic"] = "High",
        ["shallot"] = "High",
        ["leek"] = "Moderate",
        ["artichoke"] = "High",
        ["inulin"] = "High",
        ["chicory root"] = "High",
        ["fructooligosaccharide"] = "High",
        ["asparagus"] = "Moderate",
        ["beetroot"] = "Moderate",
        ["brussels sprout"] = "Moderate",
        ["pistachio"] = "High",
        ["cashew"] = "High",
        ["spelt"] = "Moderate",
        ["fennel"] = "Moderate",
        ["oligofructose"] = "High",

        // GOS
        ["chickpea"] = "High",
        ["lentil"] = "High",
        ["kidney bean"] = "High",
        ["black bean"] = "High",
        ["soybean"] = "High",
        ["split pea"] = "High",
        ["navy bean"] = "High",
        ["pinto bean"] = "High",
        ["lima bean"] = "High",
        ["baked bean"] = "High",
        ["broad bean"] = "High",
        ["fava bean"] = "High",
        ["cannellini"] = "High",
        ["edamame"] = "Moderate",
        ["hummus"] = "Moderate",
        ["lupin"] = "High",
        ["soy milk"] = "Moderate",
        ["soy flour"] = "High",
        ["soy protein"] = "Moderate",
        ["pea protein"] = "Moderate",

        // Lactose
        ["milk"] = "High",
        ["milk powder"] = "High",
        ["lactose"] = "High",
        ["yogurt"] = "Moderate",
        ["cream"] = "Moderate",
        ["ice cream"] = "High",
        ["cottage cheese"] = "High",
        ["ricotta"] = "High",
        ["buttermilk"] = "Moderate",
        ["cream cheese"] = "Moderate",
        ["whey powder"] = "Moderate",
        ["whey concentrate"] = "Moderate",
        ["milk solid"] = "High",
        ["condensed milk"] = "High",
        ["mascarpone"] = "High",
        ["paneer"] = "High",

        // Excess Fructose
        ["honey"] = "High",
        ["agave"] = "High",
        ["high fructose corn syrup"] = "High",
        ["apple"] = "High",
        ["pear"] = "High",
        ["mango"] = "High",
        ["watermelon"] = "High",
        ["apple juice"] = "High",
        ["pear juice"] = "High",
        ["fruit juice concentrate"] = "High",
        ["crystalline fructose"] = "High",
        ["date syrup"] = "High",

        // Polyols
        ["sorbitol"] = "High",
        ["mannitol"] = "High",
        ["maltitol"] = "High",
        ["xylitol"] = "High",
        ["mushroom"] = "High",
        ["cauliflower"] = "High",
        ["avocado"] = "Moderate",
        ["sweet potato"] = "Moderate",
        ["erythritol"] = "Low",
        ["isomalt"] = "High",
        ["lactitol"] = "High",
        ["prune"] = "High",
        ["cherry"] = "Moderate",
        ["apricot"] = "Moderate",
        ["peach"] = "Moderate",
        ["plum"] = "Moderate",
        ["nectarine"] = "Moderate",
        ["blackberry"] = "Moderate",
        ["celery"] = "Moderate",
    };

    /// <summary>
    /// Converts a FODMAP severity ("High"/"Moderate"/"Low") to a GutRisk risk level
    /// ("High"/"Medium"/"Low"). The only mapping difference is Moderate → Medium.
    /// </summary>
    public static string ToRiskLevel(string fodmapSeverity)
        => fodmapSeverity == "Moderate" ? "Medium" : fodmapSeverity;

    /// <summary>
    /// Gets the risk level for GutRiskData from the shared severities map.
    /// Returns null if the ingredient is not in the shared map.
    /// </summary>
    public static string? GetRiskLevel(string ingredient)
        => Severities.TryGetValue(ingredient, out var sev) ? ToRiskLevel(sev) : null;
}
