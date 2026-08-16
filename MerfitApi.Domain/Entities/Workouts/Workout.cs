using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Workouts;

public sealed class Workout : AuditableEntity
{
    public required string Title { get; set; }
    public string? Tagline { get; set; }
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public DifficultyLevel Difficulty { get; set; }
    public Guid? CategoryId { get; set; }
    public MuscleGroup MuscleGroup { get; set; }
    public bool Featured { get; set; }
    public bool IsPremium { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public WorkoutCategory? Category { get; set; }
    public ICollection<WorkoutEquipment> Equipment { get; set; } = [];
    public ICollection<WorkoutExercise> Exercises { get; set; } = [];
}

public sealed class WorkoutEquipment
{
    public Guid WorkoutId { get; set; }
    public Equipment Equipment { get; set; }
}

public sealed class WorkoutExercise : BaseEntity
{
    public Guid WorkoutId { get; set; }
    public Guid ExerciseId { get; set; }
    public int SortOrder { get; set; }
    public int Sets { get; set; }
    public int Reps { get; set; }
    public int RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Notes { get; set; }

    public Exercise? Exercise { get; set; }
}
