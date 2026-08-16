using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.WorkoutSessions;

public sealed class PersonalRecord : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ExerciseId { get; set; }
    public PersonalRecordType RecordType { get; set; }
    public decimal WeightKg { get; set; }
    public int Reps { get; set; }
    public decimal Estimated1RM { get; set; }
    public DateTimeOffset AchievedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? WorkoutSessionId { get; set; }
}
