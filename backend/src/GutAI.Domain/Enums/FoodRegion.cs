namespace GutAI.Domain.Enums;

/// <summary>
/// Typed region used to bias source trust/priority for whole-food matches
/// (e.g. AUSNUT should outrank USDA for AU users). Replaces ad hoc "AU"/"US" strings.
/// </summary>
public enum FoodRegion
{
    Default,
    Us,
    Au,
}
