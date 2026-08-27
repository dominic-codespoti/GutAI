using System.Text.RegularExpressions;
using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Deterministic final gate for agent-proposed Stage-B grounding selections.
/// The agent may propose; this gate decides whether an automatic selection is safe.
/// </summary>
internal static class MealScanAgentDecisionGate
{
    private static readonly string[] StopWords =
        ["with", "and", "the", "for", "fresh", "served", "style"];

    public static string? GetRejection(
        GroundedItem snapshot,
        int candidateIndex,
        decimal confidence,
        IReadOnlyList<string> observedSearchQueries,
        decimal minimumConfidence,
        int inspectionId,
        decimal preReanalysisMatchConfidence,
        decimal minimumReanalysisImprovement)
    {
        if (candidateIndex < 0 || candidateIndex >= snapshot.CandidateProducts.Count)
            return "candidate index is outside the inspected snapshot";

        if (confidence < minimumConfidence)
            return $"confidence {confidence:F2} is below the agent floor {minimumConfidence:F2}";

        var candidate = snapshot.CandidateProducts[candidateIndex];
        if (!HasIdentityOverlap(snapshot.Original, observedSearchQueries, candidate))
            return "candidate identity does not overlap the observed component";

        if (inspectionId > 0 &&
            snapshot.Attempt.MatchConfidence < preReanalysisMatchConfidence + minimumReanalysisImprovement)
        {
            return $"post-reanalysis confidence {snapshot.Attempt.MatchConfidence:F2} did not improve enough over {preReanalysisMatchConfidence:F2}";
        }

        return null;
    }

    private static bool HasIdentityOverlap(
        ScannedComponent observed,
        IReadOnlyList<string> searchQueries,
        FoodProductDto candidate)
    {
        var observedTokens = Tokenize(
        [
            observed.Name,
            observed.PreparationNote,
            .. searchQueries,
        ]);
        var candidateTokens = Tokenize([candidate.Name, candidate.Brand ?? ""]);
        return observedTokens.Overlaps(candidateTokens);
    }

    private static string NormalizeToken(string token)
        => token.Length > 4 && token.EndsWith("es", StringComparison.Ordinal)
            ? token[..^2]
            : token.Length > 3 && token.EndsWith("s", StringComparison.Ordinal)
                ? token[..^1]
                : token;

    private static HashSet<string> Tokenize(IEnumerable<string> values)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            foreach (var raw in Regex.Split(value.ToLowerInvariant(), "[^a-z0-9]+"))
            {
                if (raw.Length < 3 || StopWords.Contains(raw)) continue;
                tokens.Add(NormalizeToken(raw));
            }
        }

        return tokens;
    }
}
