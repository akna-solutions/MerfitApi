using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Nutrition;

/// <summary>One row per user per calendar date; holds that day's calorie/macro/water targets (computed at day-creation time from the user's profile, immutable afterwards unless recomputed explicitly).</summary>
public sealed class DailyNutrition : BaseEntity
{
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public int CalorieTarget { get; set; }
    public int ProteinTarget { get; set; }
    public int CarbTarget { get; set; }
    public int FatTarget { get; set; }
    public decimal WaterTargetLiters { get; set; } = 3.0m;
    public decimal WaterConsumedLiters { get; set; }
}
