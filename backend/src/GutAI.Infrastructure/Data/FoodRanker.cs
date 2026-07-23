using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// Sole <see cref="IFoodRanker"/> implementation. Builds a throwaway <see cref="FoodMatchIndex"/>
/// over the already-canonicalized candidate set and ranks with <see cref="FoodRelevanceScorer"/>.
/// Stateless and safe as a singleton — no shared/cached index, so no cross-request pollution.
/// </summary>
public sealed class FoodRanker : IFoodRanker
{
    public IReadOnlyList<FoodProductDto> Rank(
        IReadOnlyList<FoodProductDto> candidates, string query, IReadOnlyCollection<Guid> boostIds, int maxResults)
    {
        if (candidates.Count == 0)
            return candidates;

        var index = new FoodMatchIndex(candidates);
        return index.SearchPersonalized(query, boostIds, maxResults);
    }

    public FoodResolutionDto Resolve(
        IReadOnlyList<FoodProductDto> candidates, string query, IReadOnlyCollection<Guid> boostIds, int maxResults)
    {
        if (candidates.Count == 0)
            return new FoodResolutionDto { OriginalQuery = query };

        var index = new FoodMatchIndex(candidates);
        return index.Resolve(query, boostIds, maxResults);
    }
}
