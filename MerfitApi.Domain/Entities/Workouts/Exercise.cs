using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Workouts;

public sealed class Exercise : AuditableEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string? Instructions { get; set; }
    public MuscleGroup MuscleGroup { get; set; }
    public Equipment Equipment { get; set; } = Equipment.None;
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Beginner;
    public bool IsActive { get; set; } = true;
}
