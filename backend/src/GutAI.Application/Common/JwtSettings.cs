namespace GutAI.Application.Common;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "GutAI";
    public string Audience { get; set; } = "GutAI";
    public int ExpiryMinutes { get; set; } = 60;
}
