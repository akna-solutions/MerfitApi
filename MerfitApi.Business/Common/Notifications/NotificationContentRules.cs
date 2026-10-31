using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Business.Common.Notifications;

/// <summary>
/// Bildirim turune gore sablonda kullanilabilecek alanlar ve kural turu - bildirim turu eslemesi.
/// Gonderim motoru (MerfitCustomerApi NotificationRulePlanner.TypeFor) ayni eslemeyi kullanir.
/// </summary>
public static class NotificationContentRules
{
    /// <summary>Kampanyalarin secebilecegi bildirim turleri (kural ve odul turleri otomatik gonderimlere aittir).</summary>
    public static readonly IReadOnlySet<NotificationType> CampaignTypes = new HashSet<NotificationType>
    {
        NotificationType.Announcement, NotificationType.Marketing, NotificationType.System,
        // MB Shop kampanyalari: mobilde MB Shop gelen kutusuna ve ayri Android kanalina duser.
        NotificationType.StorePromotion,
    };

    public static NotificationType TypeFor(NotificationRuleKind kind) => kind switch
    {
        NotificationRuleKind.NutritionReminder => NotificationType.NutritionReminder,
        NotificationRuleKind.WorkoutReminder => NotificationType.WorkoutReminder,
        _ => NotificationType.DailyMotivation,
    };

    public static IReadOnlySet<string> AllowedPlaceholders(NotificationType type) => type switch
    {
        NotificationType.NutritionReminder => Set(NotificationTemplateRenderer.FirstName, NotificationTemplateRenderer.MealName),
        NotificationType.WorkoutReminder => Set(NotificationTemplateRenderer.FirstName, NotificationTemplateRenderer.WorkoutName),
        NotificationType.Reward => Set(NotificationTemplateRenderer.FirstName, NotificationTemplateRenderer.RewardTitle,
            NotificationTemplateRenderer.Rank, NotificationTemplateRenderer.PeriodLabel),
        _ => Set(NotificationTemplateRenderer.FirstName),
    };

    /// <summary>Metinde bu tur icin izin verilmeyen alanlar.</summary>
    public static IReadOnlyList<string> FindDisallowedPlaceholders(NotificationType type, params string?[] texts)
    {
        var allowed = AllowedPlaceholders(type);
        return texts
            .Where(t => !string.IsNullOrEmpty(t))
            .SelectMany(t => NotificationTemplateRenderer.FindPlaceholders(t!))
            .Where(p => !allowed.Contains(p))
            .Distinct()
            .ToList();
    }

    /// <summary>Onizleme icin ornek degerler.</summary>
    public static IReadOnlyDictionary<string, string?> SampleValues(string? firstName = null) => new Dictionary<string, string?>
    {
        [NotificationTemplateRenderer.FirstName] = string.IsNullOrWhiteSpace(firstName) ? "Ayşe" : firstName,
        [NotificationTemplateRenderer.MealName] = "öğle yemeği",
        [NotificationTemplateRenderer.WorkoutName] = "Tüm Vücut Kuvvet",
        [NotificationTemplateRenderer.RewardTitle] = "Merfit Plus 1 Ay",
        [NotificationTemplateRenderer.Rank] = "1",
        [NotificationTemplateRenderer.PeriodLabel] = "Ekim 2026",
    };

    private static readonly string[] MonthNames =
    {
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
    };

    public static string MonthLabel(int year, int month) => $"{MonthNames[month - 1]} {year}";

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);
}
