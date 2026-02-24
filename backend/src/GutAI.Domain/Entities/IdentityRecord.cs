namespace GutAI.Domain.Entities;

public class IdentityRecord
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? SecurityStamp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
