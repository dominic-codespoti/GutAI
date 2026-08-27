using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using GutAI.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
    [Authorize]
    [Description("Get the authenticated user's profile including allergies, gut conditions, dietary preferences, daily nutrition goals, and timezone. Call this at the start of a conversation to personalize recommendations.")]
    public async Task<string> GetUserProfile(
        ClaimsPrincipal? user,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(user!);
            var appUser = await _store.GetUserAsync(userId, ct);
            if (appUser is null)
                throw new McpException("User not found.");

            return JsonSerializer.Serialize(new
            {
                appUser.DisplayName,
                appUser.Allergies,
                appUser.DietaryPreferences,
                appUser.GutConditions,
                appUser.TimezoneId,
                goals = new
                {
                    dailyCalories = appUser.DailyCalorieGoal,
                    dailyProteinG = appUser.DailyProteinGoalG,
                    dailyCarbsG = appUser.DailyCarbGoalG,
                    dailyFatG = appUser.DailyFatGoalG,
                    dailyFiberG = appUser.DailyFiberGoalG
                }
            }, JsonOpts);
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetUserProfile failed");
            throw new McpException("Could not get the user profile. Please try again.");
        }
    }

    private static Guid GetUserId(ClaimsPrincipal? user) =>
        Guid.Parse(user!.FindFirstValue("sub")!);
}
