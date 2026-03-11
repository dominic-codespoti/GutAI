using GutAI.Application.Common.DTOs;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Bidirectional score reconciliation between FODMAP and GutRisk assessments.
/// Prevents contradictory ratings (e.g. "High FODMAP" + "Good Gut Health").
/// Uses continuous capping: each score is capped at the other score + 20 points.
/// </summary>
public static class ScoreReconciler
{
    /// <summary>
    /// Reconciles FODMAP and GutRisk scores bidirectionally.
    /// Returns new assessment DTOs with capped scores and recalculated ratings.
    /// </summary>
    public static (FodmapAssessmentDto fodmap, GutRiskAssessmentDto gutRisk) Reconcile(
        FodmapAssessmentDto fodmap, GutRiskAssessmentDto gutRisk)
    {
        var reconciledFodmap = fodmap;
        var reconciledGutRisk = gutRisk;

        // Direction 1: bad FODMAP → cap gut score (gut can't exceed FODMAP + 20)
        var gutCap = fodmap.FodmapScore + 20;
        if (reconciledGutRisk.GutScore > gutCap)
        {
            var capped = Math.Min(reconciledGutRisk.GutScore, gutCap);
            reconciledGutRisk = reconciledGutRisk with
            {
                GutScore = capped,
                GutRating = RateGut(capped),
            };
        }

        // Direction 2: bad gut risk → cap FODMAP score (FODMAP can't exceed gut + 20)
        var fodmapCap = reconciledGutRisk.GutScore + 20;
        if (reconciledFodmap.FodmapScore > fodmapCap)
        {
            var capped = Math.Min(reconciledFodmap.FodmapScore, fodmapCap);
            reconciledFodmap = reconciledFodmap with
            {
                FodmapScore = capped,
                FodmapRating = RateFodmap(capped),
            };
        }

        return (reconciledFodmap, reconciledGutRisk);
    }

    internal static string RateGut(int score) => score switch
    {
        >= 80 => "Good",
        >= 60 => "Fair",
        >= 40 => "Poor",
        _ => "Bad",
    };

    internal static string RateFodmap(int score) => score switch
    {
        >= 75 => "Low FODMAP",
        >= 60 => "Moderate FODMAP",
        >= 30 => "High FODMAP",
        _ => "Very High FODMAP",
    };
}
