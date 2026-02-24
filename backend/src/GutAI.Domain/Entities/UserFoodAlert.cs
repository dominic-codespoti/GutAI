namespace GutAI.Domain.Entities;

public class UserFoodAlert
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int FoodAdditiveId { get; set; }
    public bool AlertEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
