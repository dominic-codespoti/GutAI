namespace GutAI.Domain.Entities;

public class FoodProductAdditive
{
    public Guid FoodProductId { get; set; }
    public int FoodAdditiveId { get; set; }

    public FoodProduct FoodProduct { get; set; } = default!;
    public FoodAdditive FoodAdditive { get; set; } = default!;
}
