using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Helpers;

/// <summary>
/// Deterministic safety gate for Stage-B2 candidate choices. The model may only
/// select an existing candidate index and must meet the configured confidence floor.
/// </summary>
public static class MealScanCandidateSelector
{
    public static int? SelectIndex(
        MealScanCandidateChoice? choice,
        int candidateCount,
        decimal minimumConfidence)
    {
        if (choice?.CandidateIndex is not { } index
            || index < 0
            || index >= candidateCount
            || choice.Confidence < minimumConfidence)
            return null;

        return index;
    }
}
