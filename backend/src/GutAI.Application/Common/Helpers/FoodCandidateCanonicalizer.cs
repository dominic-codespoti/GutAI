using GutAI.Application.Common.DTOs;
using GutAI.Domain.Enums;

namespace GutAI.Application.Common.Helpers;

/// <summary>
/// Merges candidates from multiple sources (local store, external providers, historical
/// cache) into one canonical list: same-identity duplicates collapse to the highest
/// source-priority instance, but distinct products that merely share a display name
/// (different brand/barcode/source) are preserved. This is the only dedup step in the
/// pipeline — rank exactly once, after canonicalizing, never before.
/// </summary>
public static class FoodCandidateCanonicalizer
{
    public static IReadOnlyList<FoodProductDto> Canonicalize(
        IEnumerable<FoodProductDto> candidates, FoodRegion region = FoodRegion.Default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FoodProductDto>();

        foreach (var dto in candidates.OrderByDescending(d => FoodSourcePolicy.Priority(d, region)))
        {
            if (seen.Add(FoodCandidateIdentity.Of(dto)))
                result.Add(dto);
        }

        return result;
    }
}
