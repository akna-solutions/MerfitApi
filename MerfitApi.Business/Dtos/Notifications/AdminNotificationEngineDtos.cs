using System.ComponentModel.DataAnnotations;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Business.Dtos.Admin.Notifications;

// ---------------------------------------------------------------------------------------------
// Sablonlar
// ---------------------------------------------------------------------------------------------

public class AdminNotificationTemplateDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    /// <summary>"MbFit" | "MbShop".</summary>
    public string App { get; set; } = "MbFit";
    public string TitleTemplate { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Bu sablonu kullanan kural sayisi.</summary>
    public int RuleCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminNotificationTemplateListRequest : PagedRequest
{
    public NotificationType? Type { get; set; }
    public NotificationApp? App { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertNotificationTemplateRequest
{
    [Required(ErrorMessage = "Ad zorunludur.")]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tur zorunludur.")]
    public NotificationType Type { get; set; }

    [Required(ErrorMessage = "Baslik zorunludur.")]
    [StringLength(200, MinimumLength = 1)]
    public string TitleTemplate { get; set; } = string.Empty;

    [Required(ErrorMessage = "Icerik zorunludur.")]
    [StringLength(500, MinimumLength = 1)]
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>Dokunuldugunda acilacak ekran, orn. {"screen":"nutrition"}.</summary>
    [StringLength(2000)]
    public string? DataJson { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Sablon veya serbest metin icin mesaj onizlemesi.</summary>
public class AdminNotificationMessagePreviewRequest
{
    [Required]
    public NotificationType Type { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? DataJson { get; set; }

    /// <summary>Verilirse onizleme bu kullanicinin adiyla yapilir.</summary>
    public long? UserId { get; set; }
}

public class AdminNotificationMessagePreviewDto
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Data { get; set; } = new();
    public IReadOnlyList<string> AllowedPlaceholders { get; set; } = Array.Empty<string>();
}

/// <summary>Sablon ve kampanya formlarinin kullandigi sabit listeler.</summary>
public class AdminNotificationMetadataDto
{
    public IReadOnlyList<string> Types { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> CampaignTypes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RuleKinds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Segments { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Screens { get; set; } = Array.Empty<string>();
    /// <summary>"MbFit", "MbShop".</summary>
    public IReadOnlyList<string> Apps { get; set; } = Array.Empty<string>();
    /// <summary>Her bildirim turunun ait oldugu uygulama.</summary>
    public IReadOnlyDictionary<string, string> AppByType { get; set; } = new Dictionary<string, string>();
    /// <summary>Uygulamaya gore secilebilecek hedef ekranlar.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ScreensByApp { get; set; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PlaceholdersByType { get; set; } = new Dictionary<string, IReadOnlyList<string>>();
    public int LargeAudienceThreshold { get; set; }
    public int MaxSelectedUsers { get; set; }
    public string DefaultTimeZone { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------------------------
// Kurallar
// ---------------------------------------------------------------------------------------------

public class AdminNotificationRuleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    /// <summary>Kullanicinin yerel saatiyle "HH:mm".</summary>
    public string LocalTime { get; set; } = string.Empty;
    public int DaysOfWeekMask { get; set; }
    public string? MealType { get; set; }
    public bool OnlyUsersWithPlan { get; set; }
    public IReadOnlyList<long> TemplateIds { get; set; } = Array.Empty<long>();
    public int ActiveTemplateCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminNotificationRuleListRequest : PagedRequest
{
    public NotificationRuleKind? Kind { get; set; }
    public NotificationRuleStatus? Status { get; set; }
}

public class AdminUpsertNotificationRuleRequest
{
    [Required(ErrorMessage = "Ad zorunludur.")]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public NotificationRuleKind Kind { get; set; }

    /// <summary>Kullanicinin yerel saatiyle "HH:mm".</summary>
    [Required(ErrorMessage = "Saat zorunludur.")]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Saat HH:mm biciminde olmalidir.")]
    public string LocalTime { get; set; } = "09:00";

    /// <summary>Bit 0 = Pazar ... bit 6 = Cumartesi (System.DayOfWeek). 127 = her gun.</summary>
    [Range(1, 127, ErrorMessage = "En az bir gun secilmelidir.")]
    public int DaysOfWeekMask { get; set; } = 127;

    /// <summary>Yalnizca beslenme hatirlaticilari icin zorunludur.</summary>
    public MealType? MealType { get; set; }

    /// <summary>Beslenme: yalnizca aktif beslenme plani olan kullanicilara gonder.</summary>
    public bool OnlyUsersWithPlan { get; set; } = true;

    public List<long> TemplateIds { get; set; } = new();
}

public class AdminChangeNotificationRuleStatusRequest
{
    [Required]
    public NotificationRuleStatus Status { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Kitle
// ---------------------------------------------------------------------------------------------

public class AdminNotificationAudienceRequest
{
    [Required]
    public NotificationAudienceSegment Segment { get; set; }

    /// <summary>SingleUser icin tek, SelectedUsers icin bir veya daha fazla kullanici Id'si.</summary>
    public List<long>? UserIds { get; set; }
}

public class AdminNotificationAudiencePreviewDto
{
    public string Segment { get; set; } = string.Empty;
    /// <summary>Uygulama ici kutuya bildirim dusecek kullanici sayisi.</summary>
    public int RecipientCount { get; set; }
    /// <summary>Bunlardan aktif push cihazi olanlar (tercihler gonderim aninda ayrica uygulanir).</summary>
    public int WithActiveDeviceCount { get; set; }
    public bool RequiresConfirmation { get; set; }
    public int LargeAudienceThreshold { get; set; }
    /// <summary>Ilk birkac alici (yalnizca Id ve e-posta; cihaz/token bilgisi donulmez).</summary>
    public IReadOnlyList<AdminNotificationAudienceUserDto> Sample { get; set; } = Array.Empty<AdminNotificationAudienceUserDto>();
    /// <summary>SelectedUsers/SingleUser icin bulunamayan veya uygun olmayan Id'ler.</summary>
    public IReadOnlyList<long> InvalidUserIds { get; set; } = Array.Empty<long>();
}

public class AdminNotificationAudienceUserDto
{
    public long UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Kampanyalar
// ---------------------------------------------------------------------------------------------

public class AdminNotificationCampaignListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string App { get; set; } = "MbFit";
    public string Status { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime? ScheduledAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int? RecipientCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminNotificationCampaignDetailDto : AdminNotificationCampaignListItemDto
{
    public long? NotificationTemplateId { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public IReadOnlyList<long> UserIds { get; set; } = Array.Empty<long>();
    public long? CreatedUser { get; set; }
}

public class AdminNotificationCampaignListRequest : PagedRequest
{
    public NotificationCampaignStatus? Status { get; set; }
    public NotificationApp? App { get; set; }
}

public class AdminUpsertNotificationCampaignRequest
{
    [Required(ErrorMessage = "Ad zorunludur.")]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public NotificationType Type { get; set; } = NotificationType.Announcement;

    [Required]
    public NotificationAudienceSegment Segment { get; set; }

    public List<long>? UserIds { get; set; }

    /// <summary>Verilirse metin bu sablondan kopyalanir (Title/Body bos birakilabilir).</summary>
    public long? NotificationTemplateId { get; set; }

    [StringLength(200)]
    public string? Title { get; set; }

    [StringLength(500)]
    public string? Body { get; set; }

    [StringLength(2000)]
    public string? DataJson { get; set; }

    /// <summary>Bu zamandan sonra henuz gonderilmemis alicilara push gonderilmez (UTC).</summary>
    public DateTime? ExpiresAtUtc { get; set; }
}

public class AdminScheduleNotificationCampaignRequest
{
    /// <summary>UTC gonderim zamani; bos ise hemen (bir sonraki isleyici turunda) gonderilir.</summary>
    public DateTime? ScheduledAtUtc { get; set; }

    /// <summary>Kitle LargeAudienceThreshold'u asiyorsa true olmalidir.</summary>
    public bool ConfirmLargeAudience { get; set; }
}

public class AdminNotificationCampaignStatsDto
{
    public long CampaignId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? RecipientCount { get; set; }
    public int QueuedCount { get; set; }
    public Dictionary<string, int> DispatchStatusCounts { get; set; } = new();
    public Dictionary<string, int> DeliveryStatusCounts { get; set; } = new();
    public int InboxCount { get; set; }
    public int ReadCount { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Gonderim kayitlari (alici sonuclari)
// ---------------------------------------------------------------------------------------------

public class AdminNotificationDispatchListRequest : PagedRequest
{
    public NotificationDispatchStatus? Status { get; set; }
    public NotificationDispatchSource? Source { get; set; }
    public NotificationApp? App { get; set; }
    public long? CampaignId { get; set; }
    public long? RuleId { get; set; }
    public long? LeaderboardPeriodId { get; set; }
    public long? UserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class AdminNotificationDispatchDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = "-";
    public string Type { get; set; } = string.Empty;
    public string App { get; set; } = "MbFit";
    public string Source { get; set; } = string.Empty;
    public long? NotificationRuleId { get; set; }
    public long? NotificationCampaignId { get; set; }
    public long? LeaderboardPeriodId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime ScheduledForUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public long? NotificationId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Bir gonderimin cihaz bazli denemeleri. Cihaz tokeni hicbir zaman donulmez.</summary>
public class AdminNotificationDeliveryAttemptDto
{
    public long Id { get; set; }
    public int AttemptNumber { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public bool DeviceActive { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReceiptCheckedAtUtc { get; set; }
}

public class AdminNotificationDispatchDetailDto : AdminNotificationDispatchDto
{
    public string Body { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public IReadOnlyList<AdminNotificationDeliveryAttemptDto> Attempts { get; set; } = Array.Empty<AdminNotificationDeliveryAttemptDto>();
}

public class AdminNotificationRetryResultDto
{
    public int RetriedCount { get; set; }
    public int SkippedCount { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Aylik odul bildirimleri
// ---------------------------------------------------------------------------------------------

/// <summary>Aylik bir siralama donemi ve (varsa) bildirim isinin durumu.</summary>
public class AdminMonthlyRewardNotificationRunDto
{
    /// <summary>Is henuz olusturulmadiysa null; Status bu durumda "NotQueued" olur.</summary>
    public long? Id { get; set; }
    public long LeaderboardPeriodId { get; set; }
    public DateTime PeriodStartDate { get; set; }
    public DateTime PeriodEndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>Donem bitmeden calistirilmaz.</summary>
    public bool WaitingForPeriodEnd { get; set; }
    /// <summary>Donemde dagitilmis (iptal edilmemis) odul sayisi.</summary>
    public int RewardCount { get; set; }
    public int AttemptCount { get; set; }
    public int? RecipientCount { get; set; }
    public int? CreatedDispatchCount { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, int> DispatchStatusCounts { get; set; } = new();
    public DateTime? CreatedAt { get; set; }
}

public class AdminMonthlyRewardNotificationRunListRequest : PagedRequest
{
    public MonthlyRewardNotificationRunStatus? Status { get; set; }
}
