namespace Merfit.Domain.Entities.Notifications;

/// <summary>1:1 with User, keyed by UserId.</summary>
public sealed class NotificationPreference
{
    public Guid UserId { get; set; }
    public bool WorkoutReminderEnabled { get; set; } = true;
    public bool StreakReminderEnabled { get; set; } = true;
    public bool NutritionReminderEnabled { get; set; } = true;
    public bool AchievementEnabled { get; set; } = true;
    public bool LeaderboardEnabled { get; set; } = true;
    public bool MarketingEnabled { get; set; }
}
