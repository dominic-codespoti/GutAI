namespace GutAI.Domain.Enums;

public enum SafetyRating
{
    Unknown = 0,
    Safe = 1,
    Caution = 2,
    Warning = 3,
    Avoid = 4
}

public static class SafetyRatingExtensions
{
    public static SafetyRating Unknown => SafetyRating.Unknown;
}
