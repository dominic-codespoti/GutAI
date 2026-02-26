using System.Security.Claims;
using GutAI.Application.Common;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

public static class AuthEndpoints
{
    private static readonly PasswordHasher<IdentityRecord> Hasher = new();

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapPost("/change-password", ChangePassword).RequireAuthorization();
        return group;
    }

    static async Task<IResult> Register(
        RegisterRequest request,
        IJwtService jwt,
        ITableStore store,
        IOptions<JwtSettings> jwtOptions)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 256 || !request.Email.Contains('@') || !request.Email.Contains('.'))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["InvalidEmail"] = ["A valid email address is required (max 256 characters)."]
            });

        if (string.IsNullOrEmpty(request.Password))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PasswordRequired"] = ["Password is required."]
            });

        if (request.Password.Length > 128)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PasswordTooLong"] = ["Password must not exceed 128 characters."]
            });

        if (request.DisplayName is not null && request.DisplayName.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DisplayNameTooLong"] = ["Display name must not exceed 100 characters."]
            });

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var existing = await store.GetIdentityByEmailAsync(normalizedEmail);
        if (existing is not null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DuplicateEmail"] = ["A user with this email already exists."]
            });

        if (request.Password.Length < 8 || !request.Password.Any(char.IsDigit) || !request.Password.Any(char.IsLower))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PasswordTooWeak"] = ["Password must be at least 8 characters with a digit and lowercase letter."]
            });

        var userId = Guid.NewGuid();
        var identity = new IdentityRecord
        {
            UserId = userId,
            Email = request.Email,
            NormalizedEmail = normalizedEmail,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        identity.PasswordHash = Hasher.HashPassword(identity, request.Password);
        await store.UpsertIdentityAsync(identity);

        var appUser = new User
        {
            Id = userId,
            Email = request.Email,
            DisplayName = request.DisplayName
        };
        await store.UpsertUserAsync(appUser);

        var accessToken = jwt.GenerateAccessToken(appUser.Id, appUser.Email);
        var refreshToken = jwt.GenerateRefreshToken();

        await store.UpsertRefreshTokenAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        var settings = jwtOptions.Value;
        return Results.Created($"/api/user/profile", new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(settings.ExpiryMinutes),
            User = MapProfile(appUser)
        });
    }

    static async Task<IResult> Login(
        LoginRequest request,
        IJwtService jwt,
        ITableStore store,
        IOptions<JwtSettings> jwtOptions)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 256)
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            return Results.Unauthorized();

        var identity = await store.GetIdentityByEmailAsync(request.Email);
        if (identity is null)
            return Results.Unauthorized();

        var result = Hasher.VerifyHashedPassword(identity, identity.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            return Results.Unauthorized();

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            identity.PasswordHash = Hasher.HashPassword(identity, request.Password);
            await store.UpsertIdentityAsync(identity);
        }

        var appUser = await store.GetUserAsync(identity.UserId);
        if (appUser is null)
            return Results.Unauthorized();

        var accessToken = jwt.GenerateAccessToken(appUser.Id, appUser.Email);
        var refreshToken = jwt.GenerateRefreshToken();

        await store.UpsertRefreshTokenAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = appUser.Id,
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        var settings = jwtOptions.Value;
        return Results.Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(settings.ExpiryMinutes),
            User = MapProfile(appUser)
        });
    }

    static async Task<IResult> Refresh(
        RefreshTokenRequest request,
        IJwtService jwt,
        ITableStore store,
        IOptions<JwtSettings> jwtOptions)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 512)
            return Results.Unauthorized();

        var token = await store.GetRefreshTokenByValueAsync(request.RefreshToken);
        if (token is null || !token.IsActive)
            return Results.Unauthorized();

        token.RevokedAt = DateTime.UtcNow;
        var newRefreshToken = jwt.GenerateRefreshToken();
        token.ReplacedByToken = newRefreshToken;
        await store.UpsertRefreshTokenAsync(token);

        await store.UpsertRefreshTokenAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = token.UserId,
            Token = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        var user = await store.GetUserAsync(token.UserId);
        if (user is null)
            return Results.Unauthorized();

        var accessToken = jwt.GenerateAccessToken(token.UserId, user.Email);

        var settings = jwtOptions.Value;
        return Results.Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(settings.ExpiryMinutes),
            User = MapProfile(user)
        });
    }

    static async Task<IResult> Logout(ClaimsPrincipal principal, ITableStore store)
    {
        var userId = Guid.Parse(principal.FindFirstValue("sub")!);
        var tokens = await store.GetActiveRefreshTokensAsync(userId);
        foreach (var t in tokens)
        {
            t.RevokedAt = DateTime.UtcNow;
            await store.UpsertRefreshTokenAsync(t);
        }
        return Results.NoContent();
    }

    static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        ITableStore store)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 128)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["InvalidInput"] = ["Current password is required."]
            });

        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length > 128)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["InvalidInput"] = ["New password is required (max 128 characters)."]
            });

        var userId = Guid.Parse(principal.FindFirstValue("sub")!);
        var identity = await store.GetIdentityByIdAsync(userId);
        if (identity is null)
            return Results.NotFound();

        var verify = Hasher.VerifyHashedPassword(identity, identity.PasswordHash, request.CurrentPassword);
        if (verify == PasswordVerificationResult.Failed)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PasswordMismatch"] = ["Current password is incorrect."]
            });

        if (request.NewPassword.Length < 8 || !request.NewPassword.Any(char.IsDigit) || !request.NewPassword.Any(char.IsLower))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PasswordTooWeak"] = ["Password must be at least 8 characters with a digit and lowercase letter."]
            });

        identity.PasswordHash = Hasher.HashPassword(identity, request.NewPassword);
        identity.SecurityStamp = Guid.NewGuid().ToString();
        await store.UpsertIdentityAsync(identity);

        return Results.NoContent();
    }

    static UserProfileDto MapProfile(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email.Split('@')[0] : u.DisplayName,
        DailyCalorieGoal = u.DailyCalorieGoal,
        DailyProteinGoalG = u.DailyProteinGoalG,
        DailyCarbGoalG = u.DailyCarbGoalG,
        DailyFatGoalG = u.DailyFatGoalG,
        DailyFiberGoalG = u.DailyFiberGoalG,
        OnboardingCompleted = u.OnboardingCompleted,
        Allergies = u.Allergies,
        DietaryPreferences = u.DietaryPreferences,
        GutConditions = u.GutConditions,
        TimezoneId = u.TimezoneId
    };
}
