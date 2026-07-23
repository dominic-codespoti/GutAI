namespace GutAI.Domain.Enums;

/// <summary>
/// Declares what a food data provider can actually do, so orchestration code can
/// route requests (e.g. barcode lookups) only to providers capable of answering them
/// instead of calling every registered provider regardless of fit.
/// </summary>
[Flags]
public enum FoodProviderCapabilities
{
    None = 0,
    Search = 1 << 0,
    Barcode = 1 << 1,
}
