namespace Merfit.Domain.Enums;

public enum Membership
{
    Free,
    Plus
}

public enum SubscriptionProvider
{
    AppStore,
    GooglePlay,
    RevenueCat
}

public enum SubscriptionStatus
{
    Active,
    Trialing,
    GracePeriod,
    Expired,
    Cancelled,
    Paused,
    Unknown
}

public enum SubscriptionEnvironment
{
    Sandbox,
    Production
}

/// <summary>Verbatim match to the frontend's `PlusFeature` union (see docs/frontend-analysis.md §10).</summary>
public enum PlusFeature
{
    AdvancedProgress,
    AiWorkout,
    AiNutrition,
    PersonalInsights,
    AdvancedAnalytics,
    DetailedScore,
    AdvancedLeaderboard
}

public enum WebhookProcessingStatus
{
    Pending,
    Processed,
    Failed,
    Skipped
}
