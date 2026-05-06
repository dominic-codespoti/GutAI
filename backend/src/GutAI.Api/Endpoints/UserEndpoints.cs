using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/profile", GetProfile);
        group.MapPut("/profile", UpdateProfile);
        group.MapPut("/goals", UpdateGoals);
        group.MapGet("/alerts", GetFoodAlerts);
        group.MapPost("/alerts", AddFoodAlert);
        group.MapDelete("/alerts/{additiveId:int}", DeleteFoodAlert);
        group.MapDelete("/account", DeleteAccount);
        return group;
    }

    static Guid GetUserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);

    static string DisplayNameOrFallback(User user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email.Split('@')[0] : user.DisplayName;

    static async Task<IResult> GetProfile(ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        if (user is null) return Results.NotFound();
        return Results.Ok(new
        {
            id = user.Id,
            email = user.Email,
            displayName = DisplayNameOrFallback(user),
            dailyCalorieGoal = user.DailyCalorieGoal,
            dailyProteinGoalG = user.DailyProteinGoalG,
            dailyCarbGoalG = user.DailyCarbGoalG,
            dailyFatGoalG = user.DailyFatGoalG,
            dailyFiberGoalG = user.DailyFiberGoalG,
            allergies = user.Allergies,
            dietaryPreferences = user.DietaryPreferences,
            gutConditions = user.GutConditions,
            onboardingCompleted = user.OnboardingCompleted,
            timezoneId = user.TimezoneId,
            createdAt = user.CreatedAt
        });
    }

    static async Task<IResult> UpdateProfile(UpdateProfileRequest request, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        if (user is null) return Results.NotFound();

        if (request.DisplayName is not null && request.DisplayName.Length > 100)
            return Results.BadRequest(new { error = "Display name must not exceed 100 characters" });

        if (request.TimezoneId is not null && request.TimezoneId.Length > 100)
            return Results.BadRequest(new { error = "Timezone ID must not exceed 100 characters" });

        if (request.Allergies.Length > 50)
            return Results.BadRequest(new { error = "Cannot have more than 50 allergies" });

        if (request.Allergies.Any(a => a.Length > 100))
            return Results.BadRequest(new { error = "Each allergy must not exceed 100 characters" });

        if (request.DietaryPreferences.Length > 50)
            return Results.BadRequest(new { error = "Cannot have more than 50 dietary preferences" });

        if (request.DietaryPreferences.Any(d => d.Length > 100))
            return Results.BadRequest(new { error = "Each dietary preference must not exceed 100 characters" });

        if (request.GutConditions.Length > 20)
            return Results.BadRequest(new { error = "Cannot have more than 20 gut conditions" });

        if (request.GutConditions.Any(c => c.Length > 100))
            return Results.BadRequest(new { error = "Each gut condition must not exceed 100 characters" });

        user.DisplayName = request.DisplayName ?? user.DisplayName;
        user.Allergies = request.Allergies ?? user.Allergies;
        user.DietaryPreferences = request.DietaryPreferences ?? user.DietaryPreferences;
        user.GutConditions = request.GutConditions ?? user.GutConditions;
        user.TimezoneId = request.TimezoneId ?? user.TimezoneId;
        if (request.OnboardingCompleted.HasValue)
            user.OnboardingCompleted = request.OnboardingCompleted.Value;
        await store.UpsertUserAsync(user);
        return Results.Ok(new
        {
            id = user.Id,
            email = user.Email,
            displayName = DisplayNameOrFallback(user),
            allergies = user.Allergies,
            dietaryPreferences = user.DietaryPreferences,
            gutConditions = user.GutConditions,
            timezoneId = user.TimezoneId,
            onboardingCompleted = user.OnboardingCompleted,
            dailyCalorieGoal = user.DailyCalorieGoal,
            dailyProteinGoalG = user.DailyProteinGoalG,
            dailyCarbGoalG = user.DailyCarbGoalG,
            dailyFatGoalG = user.DailyFatGoalG,
            dailyFiberGoalG = user.DailyFiberGoalG,
            createdAt = user.CreatedAt
        });
    }

    static async Task<IResult> UpdateGoals(UpdateGoalsRequest request, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        if (user is null) return Results.NotFound();

        if (request.DailyCalorieGoal < 0 || request.DailyCalorieGoal > 20000)
            return Results.BadRequest(new { error = "Daily calorie goal must be between 0 and 20000" });

        if (request.DailyProteinGoalG < 0 || request.DailyProteinGoalG > 2000)
            return Results.BadRequest(new { error = "Daily protein goal must be between 0 and 2000g" });

        if (request.DailyCarbGoalG < 0 || request.DailyCarbGoalG > 2000)
            return Results.BadRequest(new { error = "Daily carb goal must be between 0 and 2000g" });

        if (request.DailyFatGoalG < 0 || request.DailyFatGoalG > 2000)
            return Results.BadRequest(new { error = "Daily fat goal must be between 0 and 2000g" });

        if (request.DailyFiberGoalG < 0 || request.DailyFiberGoalG > 500)
            return Results.BadRequest(new { error = "Daily fiber goal must be between 0 and 500g" });

        user.DailyCalorieGoal = request.DailyCalorieGoal;
        user.DailyProteinGoalG = request.DailyProteinGoalG;
        user.DailyCarbGoalG = request.DailyCarbGoalG;
        user.DailyFatGoalG = request.DailyFatGoalG;
        user.DailyFiberGoalG = request.DailyFiberGoalG;
        await store.UpsertUserAsync(user);
        return Results.Ok(new
        {
            dailyCalorieGoal = user.DailyCalorieGoal,
            dailyProteinGoalG = user.DailyProteinGoalG,
            dailyCarbGoalG = user.DailyCarbGoalG,
            dailyFatGoalG = user.DailyFatGoalG,
            dailyFiberGoalG = user.DailyFiberGoalG
        });
    }

    static async Task<IResult> GetFoodAlerts(ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var alerts = await store.GetUserFoodAlertsAsync(userId);
        var additives = await store.GetAllFoodAdditivesAsync();
        var alertDtos = alerts.Select(a =>
        {
            var additive = additives.FirstOrDefault(x => x.Id == a.FoodAdditiveId);
            return new
            {
                additiveId = a.FoodAdditiveId,
                name = additive?.Name ?? "Unknown",
                cspiRating = additive?.CspiRating.ToString() ?? "Unknown",
                alertEnabled = a.AlertEnabled
            };
        });
        return Results.Ok(alertDtos.OrderBy(a => a.name));
    }

    static async Task<IResult> AddFoodAlert(AddFoodAlertRequest request, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var additive = await store.GetFoodAdditiveAsync(request.AdditiveId);
        if (additive is null)
            return Results.BadRequest(new { error = "Invalid additive" });
        var alert = new UserFoodAlert
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FoodAdditiveId = request.AdditiveId,
            AlertEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        await store.UpsertUserFoodAlertAsync(alert);
        return Results.Created($"/api/user/alerts/{alert.FoodAdditiveId}", new
        {
            additiveId = alert.FoodAdditiveId,
            name = additive.Name,
            cspiRating = additive.CspiRating.ToString(),
            alertEnabled = alert.AlertEnabled
        });
    }

    static async Task<IResult> DeleteFoodAlert(int additiveId, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var alert = await store.GetUserFoodAlertAsync(userId, additiveId);
        if (alert is null) return Results.NotFound();
        await store.DeleteUserFoodAlertAsync(userId, additiveId);
        return Results.NoContent();
    }

    static async Task<IResult> DeleteAccount(ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        await store.DeleteRefreshTokensForUserAsync(userId);
        var alerts = await store.GetUserFoodAlertsAsync(userId);
        foreach (var alert in alerts)
            await store.DeleteUserFoodAlertAsync(userId, alert.FoodAdditiveId);
        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, DateOnly.MinValue, DateOnly.MaxValue);
        foreach (var s in symptoms)
        {
            s.IsDeleted = true;
            await store.UpsertSymptomLogAsync(s);
        }
        var meals = await store.GetMealLogsByDateRangeAsync(userId, DateOnly.MinValue, DateOnly.MaxValue);
        foreach (var m in meals)
        {
            m.IsDeleted = true;
            await store.UpsertMealLogAsync(m);
        }
        await store.DeleteIdentityAsync(userId);
        await store.DeleteUserAsync(userId);
        return Results.NoContent();
    }
}
