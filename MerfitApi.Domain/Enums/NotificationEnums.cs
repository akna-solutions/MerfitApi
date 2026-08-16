namespace Merfit.Domain.Enums;

public enum NotificationType
{
    WorkoutReminder,
    StreakReminder,
    NutritionReminder,
    AchievementEarned,
    LeaderboardChange,
    WeeklySummary,
    SubscriptionExpiring,
    SubscriptionRenewed,
    Marketing
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
    Read
}

public enum DevicePlatform
{
    Ios,
    Android,
    Web
}
