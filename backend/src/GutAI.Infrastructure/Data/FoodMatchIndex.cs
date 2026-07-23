using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;

namespace GutAI.Infrastructure.Data;

/// <summary>A candidate with its text-matching fields and query-independent quality
/// precomputed once at <see cref="FoodMatchIndex.AddRange"/> time, so search only ever
/// recomputes the query-dependent relevance signal per call.</summary>
internal sealed record FoodCandidate(
    FoodProductDto Dto, string NameLower, string PrimaryNounLower,
    string[] NameTokens, string[] PrimaryTokens, float Quality);

/// <summary>
/// In-memory food candidate store and ranker. Replaces the previous Lucene-backed
/// <c>FoodSearchIndex</c>: for catalogs of this size (thousands, not millions, of short
/// name strings) a linear scan over precomputed tokens is simpler, fully unit-testable
/// without IR test infrastructure, and no slower in practice than Lucene's inverted-index
/// retrieval followed by a full re-score of every returned candidate (which the old design
/// already did for every query — Lucene's own relevance score contributed only a small,
/// diluted additive term once <c>FinalScore</c> ran on top of it).
/// </summary>
public sealed class FoodMatchIndex
{
    private readonly List<FoodCandidate> _candidates = [];
    private readonly HashSet<string> _seenIdentities = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _brandTokens = new(StringComparer.OrdinalIgnoreCase);

    public FoodMatchIndex() { }

    public FoodMatchIndex(IEnumerable<FoodProductDto> foods) => AddRange(foods);

    public int Count => _candidates.Count;

    public void Add(FoodProductDto food) => AddRange([food]);

    public void AddRange(IEnumerable<FoodProductDto> foods)
    {
        foreach (var food in foods)
        {
            if (!_seenIdentities.Add(FoodCandidateIdentity.Of(food)))
                continue;

            var primaryNoun = FoodTextNormalizer.ExtractPrimaryNoun(food.Name);
            var nameLower = food.Name.ToLowerInvariant();
            var primaryNounLower = primaryNoun.ToLowerInvariant();

            _candidates.Add(new FoodCandidate(
                Dto: food,
                NameLower: nameLower,
                PrimaryNounLower: primaryNounLower,
                NameTokens: FoodTextNormalizer.Tokenize(nameLower),
                PrimaryTokens: primaryNounLower.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Quality: FoodQualityScorer.Score(food)));

            if (!string.IsNullOrEmpty(food.Brand))
                foreach (var t in food.Brand.Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries))
                    if (t.Length > 2) _brandTokens.Add(t);
        }
    }

    public List<FoodProductDto> Search(string query, int maxResults = 15) =>
        SearchPersonalized(query, [], maxResults);

    public List<FoodProductDto> SearchPersonalized(string query, IEnumerable<Guid> boostIds, int maxResults = 15)
    {
        var resolution = Resolve(query, boostIds, maxResults);
        return resolution.Selected is null ? [] : [resolution.Selected, .. resolution.Alternatives];
    }

    /// <summary>Minimum score margin the top candidate needs over the runner-up to be
    /// auto-selected with confidence instead of flagged <see cref="FoodResolutionStatus.Ambiguous"/>.
    /// Calibrated against typical bonus magnitudes (full coverage ~15, brand match ~20-40) —
    /// a smaller gap than this means the top two candidates are effectively tied.</summary>
    private const float AmbiguityMargin = 15f;

    /// <summary>
    /// The single resolution decision for auto-selecting a food match. Unlike <see cref="Search"/>,
    /// which just returns a ranked list, this reports whether the top candidate is a safe
    /// auto-selection (<see cref="FoodResolutionStatus.Exact"/>/<see cref="FoodResolutionStatus.Probable"/>),
    /// too close to call (<see cref="FoodResolutionStatus.Ambiguous"/>), or whether nothing in the
    /// candidate set had meaningful overlap with the query at all
    /// (<see cref="FoodResolutionStatus.Unresolved"/>) — the case a plain ranked list can't express,
    /// since it would otherwise return the highest-quality candidates regardless of relevance.
    /// </summary>
    public FoodResolutionDto Resolve(string query, IEnumerable<Guid> boostIds, int maxResults = 15)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new FoodResolutionDto { OriginalQuery = query };

        var ctx = FoodQueryContext.Build(query, _brandTokens);
        if (ctx.RawTokens.Length == 0)
            return new FoodResolutionDto { OriginalQuery = query };

        var boostSet = boostIds as ISet<Guid> ?? new HashSet<Guid>(boostIds);

        var scored = _candidates
            .Select(c =>
            {
                var relevance = FoodRelevanceScorer.Score(c, ctx, out var eligible, out var exact, out var coverage);
                var personalized = relevance + c.Quality * FoodRelevanceScorer.QualityWeight
                    + (boostSet.Count > 0 && boostSet.Contains(c.Dto.Id) ? FoodRelevanceScorer.PersonalizationBoost : 0f);
                return (Candidate: c, Score: personalized, Eligible: eligible, Exact: exact, Coverage: coverage);
            })
            .Where(x => x.Eligible)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .ToList();

        if (scored.Count == 0)
            return new FoodResolutionDto { OriginalQuery = query };

        var top = scored[0];
        var confidence = FoodRelevanceScorer.ComputeConfidence(top.Exact, top.Coverage);
        var margin = scored.Count > 1 ? top.Score - scored[1].Score : float.MaxValue;
        var status = top.Exact
            ? FoodResolutionStatus.Exact
            : margin >= AmbiguityMargin
                ? FoodResolutionStatus.Probable
                : FoodResolutionStatus.Ambiguous;

        return new FoodResolutionDto
        {
            OriginalQuery = query,
            Status = status,
            Selected = top.Candidate.Dto with { MatchConfidence = confidence },
            MatchConfidence = confidence,
            Alternatives = scored.Skip(1)
                .Select(x => x.Candidate.Dto with
                {
                    MatchConfidence = FoodRelevanceScorer.ComputeConfidence(x.Exact, x.Coverage)
                })
                .ToList(),
        };
    }
}
