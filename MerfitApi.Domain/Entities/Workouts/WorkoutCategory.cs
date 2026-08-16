using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Workouts;

public sealed class WorkoutCategory : BaseEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
