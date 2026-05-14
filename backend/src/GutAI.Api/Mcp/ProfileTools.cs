using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.Interfaces;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GutAI.Api.Mcp;

[McpServerToolType]
public class ProfileTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ITableStore _store;
    private readonly ILogger<ProfileTools> _logger;

    public ProfileTools(ITableStore store, ILogger<ProfileTools> logger)
    {
        _store = store;
        _logger = logger;
    }

    [McpServerTool(Name = "gutai_get_user_profile", ReadOnly = true)]
    [Description("Get the authenticated user's profile including allergies, gut conditions, dietary preferences, daily nutrition goals, and timezone. Call this at the start of a conversation to personalize recommendations.")]
    public async Task<string> GetUserProfile(
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(httpContext);
            var user = await _store.GetUserAsync(userId, ct);
            if (user is null)
                throw new McpException("User not found.");

            return JsonSerializer.Serialize(new
            {
                user.DisplayName,
                user.Allergies,
                user.DietaryPreferences,
                user.GutConditions,
                user.TimezoneId,
                goals = new
                {
                    dailyCalories = user.DailyCalorieGoal,
                    dailyProteinG = user.DailyProteinGoalG,
                    dailyCarbsG = user.DailyCarbGoalG,
                    dailyFatG = user.DailyFatGoalG,
                    dailyFiberG = user.DailyFiberGoalG
                }
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetUserProfile failed");
            throw new McpException($"Error getting user profile: {ex.Message}");
        }
    }

    private static Guid GetUserId(HttpContext httpContext) =>
        Guid.Parse(httpContext.User.FindFirstValue("sub")!);
}
