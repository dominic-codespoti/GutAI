using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

/// <summary>
/// The single ranking owner for food search candidates. Callers canonicalize their
/// candidate pool first (see <c>FoodCandidateCanonicalizer</c>), then rank exactly once —
/// never re-rank an already-ranked list with a second, different scorer.
/// </summary>
public interface IFoodRanker
{
    IReadOnlyList<FoodProductDto> Rank(
        IReadOnlyList<FoodProductDto> candidates,
        string query,
        IReadOnlyCollection<Guid> boostIds,
        int maxResults);

    /// <summary>The single resolution decision for auto-selecting a food match (NLP meal
    /// parsing, barcode-driven flows). Reports <see cref="FoodResolutionStatus.Unresolved"/>
    /// instead of returning an unrelated candidate when nothing has meaningful overlap
    /// with the query.</summary>
    FoodResolutionDto Resolve(
        IReadOnlyList<FoodProductDto> candidates,
        string query,
        IReadOnlyCollection<Guid> boostIds,
        int maxResults);
}
