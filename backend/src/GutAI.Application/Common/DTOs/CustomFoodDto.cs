namespace GutAI.Application.Common.DTOs;

public class CustomFoodDto
{
    public string Name { get; set; } = default!;
    public string? BrandName { get; set; }
    public decimal ServingSize { get; set; }
    public string ServingSizeUnit { get; set; } = "g";
    
    public decimal Calories { get; set; }
    public decimal ProteinG { get; set; }
    public decimal CarbG { get; set; }
    public decimal FatG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? SugarG { get; set; }
    public decimal? SodiumMg { get; set; }
    
    public string? Ingredients { get; set; }
}
