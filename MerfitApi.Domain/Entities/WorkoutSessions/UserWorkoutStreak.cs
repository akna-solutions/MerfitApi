namespace Merfit.Domain.Entities.WorkoutSessions;

/// <summary>1:1 with User, keyed by UserId. LastWorkoutDate is the user's local calendar date (per UserSettings.Timezone), never a raw UTC timestamp, so day-boundary math is correct for the user.</summary>
public sealed class UserWorkoutStreak
{
    public Guid UserId { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateOnly? LastWorkoutDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
