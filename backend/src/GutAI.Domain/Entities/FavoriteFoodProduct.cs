namespace GutAI.Domain.Entities;

public class FavoriteFoodProduct
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FoodProductId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
