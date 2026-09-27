using Microsoft.Extensions.Configuration;

namespace GutAI.Infrastructure.Services;

/// <summary>
/// Keyed <c>IChatClient</c> workloads (AGENTS.md #9). Each key resolves its deployment and
/// default reasoning effort from <c>AzureOpenAI:Workloads:{key}:{Deployment,ReasoningEffort}</c>
/// and falls back to <c>AzureOpenAI:DeploymentName</c> when no workload deployment is set.
/// </summary>
public static class AiWorkloads
{
    /// <summary>Stage-A meal photo decomposition and agent reanalysis.</summary>
    public const string Vision = "vision";

    /// <summary>Batched Stage-B2 candidate choice and the bounded agent grounding review.</summary>
    public const string Selection = "selection";

    /// <summary>Web-page nutrition extraction and nutrition-label fallback extraction.</summary>
    public const string Extraction = "extraction";

    /// <summary>Conversational coach tool loop.</summary>
    public const string Coach = "coach";

    /// <summary>Describe-food decomposition.</summary>
    public const string Describe = "describe";

    /// <summary>Grounded meal suggestions.</summary>
    public const string Suggestion = "suggestion";

    public static readonly IReadOnlyList<string> All =
        [Vision, Selection, Extraction, Coach, Describe, Suggestion];

    /// <summary>Deployment used when neither the workload nor <c>AzureOpenAI:DeploymentName</c> is configured.</summary>
    public const string DefaultDeployment = "gpt-5-nano";

    /// <summary>
    /// Deployment actually used for <paramref name="workload"/>:
    /// <c>AzureOpenAI:Workloads:{workload}:Deployment</c> → <c>AzureOpenAI:DeploymentName</c> →
    /// <see cref="DefaultDeployment"/>. Drafts and telemetry record this value.
    /// </summary>
    public static string ResolveDeployment(IConfiguration config, string workload)
    {
        var workloadDeployment = config[$"AzureOpenAI:Workloads:{workload}:Deployment"];
        if (!string.IsNullOrWhiteSpace(workloadDeployment)) return workloadDeployment;
        var shared = config["AzureOpenAI:DeploymentName"];
        return string.IsNullOrWhiteSpace(shared) ? DefaultDeployment : shared;
    }

    /// <summary>
    /// Configured reasoning effort (<c>none|low|medium|high|xhigh|max</c>) for
    /// <paramref name="workload"/>, or null to leave the model default.
    /// </summary>
    public static string? ResolveReasoningEffort(IConfiguration config, string workload)
    {
        var effort = config[$"AzureOpenAI:Workloads:{workload}:ReasoningEffort"];
        return string.IsNullOrWhiteSpace(effort) ? null : effort.Trim();
    }
}
