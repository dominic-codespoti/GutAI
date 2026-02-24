namespace GutAI.Domain.Enums;

public enum CspiRating
{
    Unknown = 0,
    Safe = 1,
    CutBack = 2,
    Caution = 3,
    CertainPeopleShouldAvoid = 4,
    Avoid = 5
}

public static class CspiRatingExtensions
{
    public static CspiRating Unknown => CspiRating.Unknown;
}
