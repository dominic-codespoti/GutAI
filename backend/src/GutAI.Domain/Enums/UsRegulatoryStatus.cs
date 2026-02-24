namespace GutAI.Domain.Enums;

public enum UsRegulatoryStatus
{
    Unknown = 0,
    Approved,
    Restricted,
    NotAuthorized,
    GRAS,
    Banned,
    Active,
    Inactive,
    Pending,
    Cancelled,
    Expired,
    NotApplicable,
    Unspecified
}

public static class UsRegulatoryStatusExtensions
{
    public static UsRegulatoryStatus Unknown => UsRegulatoryStatus.Unknown;
}
