namespace GutAI.Application.Common.DTOs;

public record AuthResponse
{
    public string AccessToken { get; init; } = default!;
    public string RefreshToken { get; init; } = default!;
    public DateTime ExpiresAt { get; init; }
    public UserProfileDto User { get; init; } = default!;
}

public record LoginRequest
{
    public string Email { get; init; } = default!;
    public string Password { get; init; } = default!;
}

public record RegisterRequest
{
    public string Email { get; init; } = default!;
    public string Password { get; init; } = default!;
    public string? DisplayName { get; init; }
}

public record RefreshTokenRequest
{
    public string RefreshToken { get; init; } = default!;
}

public record UserProfileDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = default!;
    public string? DisplayName { get; init; }
    public int DailyCalorieGoal { get; init; }
    public int DailyProteinGoalG { get; init; }
    public int DailyCarbGoalG { get; init; }
    public int DailyFatGoalG { get; init; }
    public int DailyFiberGoalG { get; init; }
    public string[] Allergies { get; init; } = [];
    public string[] DietaryPreferences { get; init; } = [];
    public string[] GutConditions { get; init; } = [];
    public bool OnboardingCompleted { get; init; }
    public string? TimezoneId { get; init; }
}

public record UpdateProfileRequest
{
    public string? DisplayName { get; init; }
    public string[] Allergies { get; init; } = [];
    public string[] DietaryPreferences { get; init; } = [];
    public string[] GutConditions { get; init; } = [];
    public string? TimezoneId { get; init; }
    public bool? OnboardingCompleted { get; init; }
}

public record UpdateGoalsRequest
{
    public int DailyCalorieGoal { get; init; }
    public int DailyProteinGoalG { get; init; }
    public int DailyCarbGoalG { get; init; }
    public int DailyFatGoalG { get; init; }
    public int DailyFiberGoalG { get; init; }
}

public record ChangePasswordRequest
{
    public string CurrentPassword { get; init; } = default!;
    public string NewPassword { get; init; } = default!;
}
