using GutAI.Domain.Enums;

namespace GutAI.Domain.Entities;

public class FoodAdditive
{
    public int Id { get; set; }
    public string? ENumber { get; set; }
    public string Name { get; set; } = default!;
    public string[] AlternateNames { get; set; } = [];
    public string Category { get; set; } = default!;
    public CspiRating CspiRating { get; set; }
    public UsRegulatoryStatus UsRegulatoryStatus { get; set; }
    public EuRegulatoryStatus EuRegulatoryStatus { get; set; }
    public SafetyRating SafetyRating { get; set; }
    public decimal? EfsaAdiMgPerKgBw { get; set; }
    public DateTime? EfsaLastReviewDate { get; set; }
    public string? EpaCancerClass { get; set; }
    public string HealthConcerns { get; set; } = "";
    public string Description { get; set; } = "";
    public int FdaAdverseEventCount { get; set; }
    public int FdaRecallCount { get; set; }
    public string[] BannedInCountries { get; set; } = [];
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public ICollection<FoodProductAdditive> FoodProductAdditives { get; set; } = [];
    public ICollection<UserFoodAlert> UserAlerts { get; set; } = [];
}
