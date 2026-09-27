using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using GutAI.Domain.Entities;

namespace GutAI.Infrastructure.Services;

internal static class GroundingAttempts
{
    internal static GroundingAttemptDto MakeGrounding(
        string query,
        FoodResolutionDto resolution,
        bool autoSelected,
        Guid? productId,
        string? canonicalName,
        IReadOnlyList<GroundingCandidateDto> candidates) => new()
        {
            Query = query,
            Queries = [query],
            ResolutionStatus = resolution.Status.ToString().ToLowerInvariant(),
            AutoSelected = autoSelected,
            SelectedFoodProductId = autoSelected ? productId : null,
            CanonicalName = autoSelected ? canonicalName : null,
            Candidates = candidates,
            MatchConfidence = resolution.MatchConfidence,
            Method = "resolve_async",
        };

    internal static IReadOnlyList<GroundingCandidateDto> ToCandidates(IEnumerable<FoodProductDto> candidates) =>
        candidates.Take(GroundingPolicy.MaxCandidates).Select(candidate =>
        {
            var flags = GroundingPolicy.DataQualityFlags(candidate);
            return new GroundingCandidateDto(
                candidate.Name,
                candidate.Id == Guid.Empty ? null : candidate.Id,
                MapSource(candidate.DataSource),
                candidate.MatchConfidence,
                candidate.Brand,
                candidate.ExternalId,
                candidate.SourceUrl,
                candidate.Calories100g,
                candidate.Protein100g,
                candidate.Carbs100g,
                candidate.Fat100g,
                candidate.Fiber100g,
                candidate.Sugar100g,
                candidate.SodiumMg100g,
                FoodCandidateIdentity.Of(candidate),
                flags.Count == 0 ? null : flags);
        }).ToArray();

    internal static string MapSource(string? dataSource) => dataSource?.Trim().ToLowerInvariant() switch
    {
        "usda" or "usda fdc" or "fdc" => "usda",
        "open food facts" or "off" => "off",
        "au" or "australian" or "afcd" => "au",
        null or "" => "db",
        var other => other,
    };
}
