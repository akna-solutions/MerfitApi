using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Profile;

/// <summary>1:1 with User, keyed by UserId. Notification toggles live separately on NotificationPreference (Merfit.Domain.Entities.Notifications) to match the distinct /profile/settings vs /notifications/preferences endpoints.</summary>
public sealed class UserSettings
{
    public Guid UserId { get; set; }
    public string Language { get; set; } = "en";
    public AppTheme Theme { get; set; } = AppTheme.System;
    public UnitSystem UnitSystem { get; set; } = UnitSystem.Metric;

    /// <summary>IANA timezone id, e.g. "Europe/Istanbul". Used to compute streak day boundaries in the user's local day, never blindly in UTC.</summary>
    public string Timezone { get; set; } = "UTC";
    public bool LeaderboardVisible { get; set; } = true;
}
