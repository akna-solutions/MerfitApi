using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.WorkoutSessions;

public sealed class WorkoutSession : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid WorkoutId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public int DurationSeconds { get; set; }
    public WorkoutSessionStatus Status { get; set; } = WorkoutSessionStatus.Started;
    public int CaloriesBurned { get; set; }
    public decimal TotalVolume { get; set; }
    public int ScoreEarned { get; set; }
    public bool IsCompleted { get; set; }

    /// <summary>Index into the workout's ordered exercises, persisted so the client can resume ("Save & Exit").</summary>
    public int CurrentExerciseIndex { get; set; }

    public ICollection<WorkoutExerciseLog> ExerciseLogs { get; set; } = [];
}

public sealed class WorkoutExerciseLog : BaseEntity
{
    public Guid WorkoutSessionId { get; set; }
    public Guid ExerciseId { get; set; }
    public int SortOrder { get; set; }

    public ICollection<SetLog> Sets { get; set; } = [];
}

public sealed class SetLog : BaseEntity
{
    public Guid WorkoutExerciseLogId { get; set; }
    public int SetNumber { get; set; }
    public decimal WeightKg { get; set; }
    public int Reps { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}
