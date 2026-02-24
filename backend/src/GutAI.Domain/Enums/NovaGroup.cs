namespace GutAI.Domain.Enums;

public enum NovaGroup
{
    Unknown = 0,
    Unprocessed = 1,
    ProcessedCulinary = 2,
    Processed = 3,
    UltraProcessed = 4
}

public static class NovaGroupExtensions
{
    public static NovaGroup Unknown => NovaGroup.Unknown;
}
