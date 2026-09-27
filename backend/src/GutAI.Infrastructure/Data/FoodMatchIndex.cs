using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;

namespace GutAI.Infrastructure.Data;

/// <summary>A candidate with its text-matching fields and query-independent quality
/// precomputed once at <see cref="FoodMatchIndex.AddRange"/> time, so search only ever
/// recomputes the query-dependent relevance signal per call.</summary>
internal sealed record FoodCandidate(
    FoodProductDto Dto,
    string NameLower,
    string NameStem,
    string NormalizedName,
    string PrimaryNounLower,
    string[] NameTokens,
    string[] NameTokenStems,
    string[] PrimaryTokens,
    string[] PrimaryTokenStems,
    string[] DescriptorTokens,
    string[] DescriptorTokenStems,
    string[] BrandTokenStems,
    FoodMacros? Macros,
    int NameTokenCount,
    float Quality);

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

            var name = food.Name;
            var primaryNoun = FoodTextNormalizer.ExtractPrimaryNoun(name);
            var nameLower = name.ToLowerInvariant();
            var primaryNounLower = primaryNoun.ToLowerInvariant();
            var nameTokens = FoodTextNormalizer.TokenizeForMatching(nameLower);
            var primaryTokens = FoodTextNormalizer.TokenizeForMatching(primaryNounLower);
            var nameTokenStems = FoodTextNormalizer.DepluralizeAll(nameTokens);
            var primaryTokenStems = FoodTextNormalizer.DepluralizeAll(primaryTokens);
            var commaIdx = nameLower.IndexOf(',');
            var descriptorTokens = commaIdx >= 0
                ? nameLower[(commaIdx + 1)..].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
            var brandTokenStems = string.IsNullOrWhiteSpace(food.Brand)
                ? Array.Empty<string>()
                : FoodTextNormalizer.DepluralizeAll(FoodTextNormalizer.Tokenize(food.Brand));

            _candidates.Add(new FoodCandidate(
                Dto: food,
                NameLower: nameLower,
                NameStem: FoodTextNormalizer.Depluralize(nameLower),
                NormalizedName: FoodTextNormalizer.NormalizeFoodName(name),
                PrimaryNounLower: primaryNounLower,
                NameTokens: nameTokens,
                NameTokenStems: nameTokenStems,
                PrimaryTokens: primaryTokens,
                PrimaryTokenStems: primaryTokenStems,
                DescriptorTokens: descriptorTokens,
                DescriptorTokenStems: FoodTextNormalizer.DepluralizeAll(descriptorTokens),
                BrandTokenStems: brandTokenStems,
                Macros: food.Calories100g.HasValue ? FoodMacros.From(food) : null,
                NameTokenCount: name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
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

    /// <summary>Minimum score margin, after removing the shared lexical coverage signal,
    /// required for a non-exact match to be auto-selected as probable.</summary>
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

        var resultLimit = Math.Min(Math.Max(maxResults, 0), _candidates.Count);
        if (resultLimit == 0)
            return new FoodResolutionDto { OriginalQuery = query };

        var boostSet = boostIds as ISet<Guid>;
        if (boostSet is null && boostIds is not IReadOnlyCollection<Guid> { Count: 0 })
            boostSet = new HashSet<Guid>(boostIds);

        var scored = new List<ScoredFoodCandidate>(resultLimit);
        foreach (var candidate in _candidates)
        {
            var relevance = FoodRelevanceScorer.Score(
                candidate, ctx, out var eligible, out var exact, out var coverage, out var tokenMatchScore);
            if (!eligible)
                continue;

            var personalized = relevance + candidate.Quality * FoodRelevanceScorer.QualityWeight
                + (boostSet is { Count: > 0 } && boostSet.Contains(candidate.Dto.Id)
                    ? FoodRelevanceScorer.PersonalizationBoost
                    : 0f);
            var item = new ScoredFoodCandidate(
                candidate, personalized, personalized - tokenMatchScore, exact, coverage);

            var insertionIndex = 0;
            while (insertionIndex < scored.Count && !ComesBefore(item, scored[insertionIndex]))
                insertionIndex++;
            if (insertionIndex >= resultLimit)
                continue;

            if (scored.Count == resultLimit)
                scored.RemoveAt(scored.Count - 1);
            scored.Insert(insertionIndex, item);
        }

        if (scored.Count == 0)
            return new FoodResolutionDto { OriginalQuery = query };

        var top = scored[0];
        var confidence = FoodRelevanceScorer.ComputeConfidence(top.Exact, top.Coverage);
        var margin = scored.Count > 1 ? top.DecisionScore - scored[1].DecisionScore : float.MaxValue;
        var hasEquivalentAlternative = false;
        if (!top.Exact && ctx.RawTokens.Length == 1)
        {
            for (var i = 1; i < scored.Count; i++)
            {
                var alternative = scored[i].Candidate.Dto;
                var selected = top.Candidate.Dto;
                if (scored[i].Coverage == top.Coverage
                    && alternative.FoodKind == selected.FoodKind
                    && alternative.DataSource == selected.DataSource
                    && alternative.Calories100g == selected.Calories100g
                    && alternative.Protein100g == selected.Protein100g
                    && alternative.Carbs100g == selected.Carbs100g
                    && alternative.Fat100g == selected.Fat100g)
                {
                    hasEquivalentAlternative = true;
                    break;
                }
            }
        }

        var matchesEveryQueryToken = FoodRelevanceScorer.MatchesEveryQueryToken(top.Candidate, ctx);
        var selectedHasBrand = !string.IsNullOrWhiteSpace(top.Candidate.Dto.Brand);
        var isLegacyExactName = top.Candidate.NameLower == ctx.QueryLower
            || top.Candidate.NameStem == ctx.QueryStem;
        var queryNamesSelectedBrand = FoodRelevanceScorer.IsBrandNamed(top.Candidate, ctx);
        var mayAutoSelectExact = top.Exact && matchesEveryQueryToken
            && (!selectedHasBrand || isLegacyExactName || queryNamesSelectedBrand);
        var mayAutoSelectProbable = !selectedHasBrand && matchesEveryQueryToken && margin >= AmbiguityMargin;
        var status = hasEquivalentAlternative
            ? FoodResolutionStatus.Ambiguous
            : mayAutoSelectExact
                ? FoodResolutionStatus.Exact
                : mayAutoSelectProbable
                    ? FoodResolutionStatus.Probable
                    : FoodResolutionStatus.Ambiguous;

        var alternatives = new List<FoodProductDto>(scored.Count - 1);
        for (var i = 1; i < scored.Count; i++)
        {
            var alternative = scored[i];
            alternatives.Add(alternative.Candidate.Dto with
            {
                MatchConfidence = FoodRelevanceScorer.ComputeConfidence(alternative.Exact, alternative.Coverage)
            });
        }

        return new FoodResolutionDto
        {
            OriginalQuery = query,
            Status = status,
            Selected = top.Candidate.Dto with { MatchConfidence = confidence },
            MatchConfidence = confidence,
            Alternatives = alternatives,
        };
    }

    private static bool ComesBefore(ScoredFoodCandidate left, ScoredFoodCandidate right)
    {
        if (left.Score != right.Score)
            return left.Score > right.Score;

        var nameComparison = StringComparer.Ordinal.Compare(left.Candidate.Dto.Name, right.Candidate.Dto.Name);
        if (nameComparison != 0)
            return nameComparison < 0;

        return StringComparer.Ordinal.Compare(
            left.Candidate.Dto.ExternalId ?? "",
            right.Candidate.Dto.ExternalId ?? "") < 0;
    }

    private readonly record struct ScoredFoodCandidate(
        FoodCandidate Candidate, float Score, float DecisionScore, bool Exact, float Coverage);
}
