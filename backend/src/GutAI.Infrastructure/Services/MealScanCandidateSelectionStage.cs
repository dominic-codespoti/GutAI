using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GutAI.Infrastructure.Services;

/// <summary>Runs one Stage-B2 model choice over all ambiguous components in a scan.</summary>
internal sealed class MealScanCandidateSelectionStage(
    IChatClient selectionClient,
    MealScanAgentReviewService agentReview,
    IConfiguration config,
    ILogger logger)
{
    private static readonly ChatRole DeveloperRole = new("developer");
    private const string CandidateChoiceDeveloperInstructions = """
        You are selecting food catalog candidates for visible components in a meal photo.
        For each supplied component, choose ONLY one candidate index from that component's
        supplied list, or abstain with candidate_index=null. Never invent a candidate,
        ingredient, brand, species, or nutrition value. Prefer generic foods when the image
        does not prove a brand or species. Abstain when candidates are visually
        indistinguishable, when candidates are packaged snacks but fresh food is observed,
        or when the image cannot establish the requested specificity. Component indices must
        refer to the supplied component list. Return one choice per component when possible.
        """;

    public async Task<IReadOnlyList<GroundedItem>> SelectAsync(
        IReadOnlyList<GroundedItem> items,
        byte[] imageBytes,
        string contentType,
        AiUsageMeter meter,
        CancellationToken ct)
    {
        var eligible = config.GetValue("MealScan:EnableCandidateDisambiguation", true)
            ? items.Select((item, index) => (item, index))
                .Where(x => !x.item.Attempt.AutoSelected
                    && x.item.ResolvedProduct is null
                    && x.item.Attempt.ResolutionStatus == "ambiguous"
                    && x.item.CandidateProducts.Count >= 2)
                .ToArray()
            : [];

        if (eligible.Length == 0)
            return items;

        var prompt = string.Join(Environment.NewLine + Environment.NewLine,
            eligible.Select((entry, componentIndex) =>
            {
                var lines = entry.item.CandidateProducts.Select((candidate, index) =>
                {
                    var basis = NutritionCalculator.BasisFrom(candidate);
                    var nutrition = basis is null
                        ? "n/a"
                        : $"{basis.CaloriesKcal} {basis.ProteinG} {basis.CarbsG} {basis.FatG}";
                    return $"{index}: {candidate.Name} | {candidate.DataSource} | {candidate.Brand ?? "generic"} | {nutrition}";
                });
                return $"Component {componentIndex}: {entry.item.Original.Name}\n" +
                    $"Preparation note: {entry.item.Original.PreparationNote ?? "none"}\n" +
                    "Candidates (kcal/100g P C F):\n" + string.Join(Environment.NewLine, lines);
            }));

        try
        {
            var messages = new List<ChatMessage>
            {
                new(DeveloperRole, CandidateChoiceDeveloperInstructions),
                new(ChatRole.User,
                [
                    new DataContent(imageBytes, contentType == "image/png" ? "image/png" : "image/jpeg"),
                    new TextContent(prompt),
                ]),
            };
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await selectionClient.GetResponseAsync<MealScanBatchCandidateChoice>(
                messages,
                options: MealScanReasoningOptions.Create(
                    AiWorkloads.ResolveReasoningEffort(config, AiWorkloads.Selection) ?? "low"),
                useJsonSchemaResponseFormat: true,
                cancellationToken: ct);
            stopwatch.Stop();
            meter.Record(
                "selection",
                AiWorkloads.ResolveDeployment(config, AiWorkloads.Selection),
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount,
                stopwatch.Elapsed);

            var minConfidence = config.GetValue("MealScan:MinCandidateSelectionConfidence", 0.85m);
            var requireCompatibilityAgreement = config.GetValue("MealScan:RequireCompatibilityAgreement", false);
            var selectedByComponent = new Dictionary<int, MealScanBatchChoiceItem>();
            foreach (var choice in response.Result?.Choices ?? [])
            {
                if (choice.ComponentIndex < 0 || choice.ComponentIndex >= eligible.Length
                    || selectedByComponent.ContainsKey(choice.ComponentIndex))
                    continue;
                selectedByComponent.Add(choice.ComponentIndex, choice);
            }

            var result = items.ToArray();
            for (var componentIndex = 0; componentIndex < eligible.Length; componentIndex++)
            {
                if (!selectedByComponent.TryGetValue(componentIndex, out var choice))
                    continue;

                var item = eligible[componentIndex].item;
                var selectedIndex = MealScanCandidateSelector.SelectIndex(
                    choice, item.CandidateProducts.Count, minConfidence);
                if (selectedIndex is not { } index
                    || (requireCompatibilityAgreement && index != 0))
                    continue;

                var selected = item.CandidateProducts[index];
                if (selected.Calories100g is null || GroundingPolicy.DataQualityFlags(selected).Count != 0)
                    continue;

                var attempt = item.Attempt with
                {
                    ResolutionStatus = "vision_selected",
                    AutoSelected = true,
                    SelectedFoodProductId = selected.Id,
                    CanonicalName = selected.Name,
                    MatchConfidence = selected.MatchConfidence,
                    Method = "vision_candidate_selection",
                };
                result[eligible[componentIndex].index] = item with { ResolvedProduct = selected, Attempt = attempt };
                logger.LogInformation(
                    "Batched B2 selected candidate {Candidate} for '{Component}' with confidence {Confidence:F2}.",
                    selected.Name, item.Original.Name, choice.Confidence);
            }

            if (config.GetValue("MealScan:EnableAgentGroundingReview", false))
            {
                foreach (var entry in eligible)
                {
                    var item = result[entry.index];
                    if (item.ResolvedProduct is null)
                        result[entry.index] = await agentReview.ReviewAsync(item, imageBytes, contentType, meter, ct);
                }
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batched B2 candidate selection failed; retaining grounded items.");
            return items;
        }
    }
}
