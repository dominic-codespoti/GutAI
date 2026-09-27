using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Infrastructure.Data;

namespace GutAI.Infrastructure.Services;

public sealed record GroundingDecision(
    bool AutoSelected,
    FoodProductDto? Selected,
    IReadOnlyList<FoodProductDto> Candidates,
    string Reason);

public static class GroundingPolicy
{
    /// <summary>Version of the auto-select rules; part of golden-harness cache keys and reports.</summary>
    public const string PolicyVersion = "2026-09-24.v1";

    public const decimal MinAutoSelectConfidence = 0.85m;
    public const int MaxCandidates = 3;
    private const float MinCompatibilityMargin = -15f;

    public static class Reasons
    {
        public const string AutoSelected = "auto_selected";
        public const string NoCandidate = "no_candidate";
        public const string StatusNotConfident = "status_not_confident";
        public const string CompatibilityVeto = "compatibility_veto";
        public const string FoodFormVeto = "food_form_veto";
        public const string BelowConfidenceFloor = "below_confidence_floor";
        public const string MissingCalories = "missing_calories";
        public const string ImplausibleNutrition = "implausible_nutrition";
    }

    public static GroundingDecision Decide(FoodResolutionDto resolution, ScannedComponent? observation = null)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var selected = resolution.Selected;
        var isConfidentStatus = resolution.Status is FoodResolutionStatus.Exact or FoodResolutionStatus.Probable;
        string reason;

        if (selected is null)
            reason = Reasons.NoCandidate;
        else if (!isConfidentStatus)
            reason = Reasons.StatusNotConfident;
        else if (observation is not null && IsCompatibilityVetoed(observation, selected))
            reason = Reasons.CompatibilityVeto;
        else if (observation is not null && FoodFormPolicy.Evaluate(observation, selected) is not null)
            reason = Reasons.FoodFormVeto;
        else if (resolution.MatchConfidence < MinAutoSelectConfidence)
            reason = Reasons.BelowConfidenceFloor;
        else if (selected.Calories100g is null)
            reason = Reasons.MissingCalories;
        else
        {
            var basis = NutritionCalculator.BasisFrom(selected)!;
            reason = NutritionSanity.Check(basis, NutritionSanityProfile.Catalog, selected.Name).IsPlausible
                ? Reasons.AutoSelected
                : Reasons.ImplausibleNutrition;
        }

        var autoSelected = reason == Reasons.AutoSelected;
        var allCandidates = new[] { selected }
            .Concat(resolution.Alternatives)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!);
        var ranked = RankCandidates(allCandidates, observation);
        IReadOnlyList<FoodProductDto> candidates = autoSelected
            ? new[] { selected! }
                .Concat(ranked.Where(candidate => !StringComparer.OrdinalIgnoreCase.Equals(
                    FoodCandidateIdentity.Of(candidate),
                    FoodCandidateIdentity.Of(selected!))))
                .Take(MaxCandidates)
                .ToArray()
            : ranked.Take(MaxCandidates).ToArray();

        return new GroundingDecision(autoSelected, autoSelected ? selected : null, candidates, reason);
    }

    public static IReadOnlyList<FoodProductDto> RankCandidates(
        IEnumerable<FoodProductDto> candidates,
        ScannedComponent? observation)
    {
        var distinct = candidates
            .GroupBy(FoodCandidateIdentity.Of, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
        return observation is null
            ? distinct.OrderByDescending(candidate => candidate.MatchConfidence).ToArray()
            : distinct.OrderByDescending(candidate => FoodCandidateCompatibilityScorer.Score(observation, candidate)).ToArray();
    }

    public static IReadOnlyList<string> DataQualityFlags(FoodProductDto candidate)
    {
        var basis = NutritionCalculator.BasisFrom(candidate);
        return basis is null
            ? []
            : NutritionSanity.Check(basis, NutritionSanityProfile.Catalog, candidate.Name).Codes;
    }

    private static bool IsCompatibilityVetoed(ScannedComponent observation, FoodProductDto candidate)
    {
        var compatibility = FoodCandidateCompatibilityScorer.Score(observation, candidate);
        var lexicalBaseline = (float)(candidate.MatchConfidence * 100m);
        return compatibility - lexicalBaseline <= MinCompatibilityMargin;
    }
}
