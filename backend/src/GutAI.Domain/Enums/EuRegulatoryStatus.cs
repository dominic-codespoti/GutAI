namespace GutAI.Domain.Enums;

public enum EuRegulatoryStatus
{
    Unknown = 0,
    Approved,
    Restricted,
    NotAuthorized,
    Banned,
    NotApplicable,
    NotAvailable,
    NotCompliant,
    Compliant
}

public static class EuRegulatoryStatusExtensions
{
    public static EuRegulatoryStatus Unknown => EuRegulatoryStatus.Unknown;
}
