using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Kampanya kitlelerini (segment, tek kullanici, secili kullanicilar) cozer.</summary>
public interface INotificationAudienceResolver
{
    /// <summary>
    /// Kitledeki uygun kullanicilar: aktif, silinmemis ve "User" rolunde. SingleUser/SelectedUsers icin
    /// kullanicilar ya userIds'ten (onizleme) ya da kampanyaya kayitli NotificationCampaignUser
    /// satirlarindan (campaignId) okunur.
    /// </summary>
    IQueryable<ApplicationUser> Query(NotificationAudienceSegment segment, DateTime nowUtc, IReadOnlyCollection<long>? userIds = null, long? campaignId = null);

    /// <summary>Segment / kullanici listesi tutarliligini dogrular; gecersiz Id'leri dondurur.</summary>
    Task<IReadOnlyList<long>> ValidateAsync(NotificationAudienceSegment segment, IReadOnlyCollection<long>? userIds, CancellationToken cancellationToken = default);

    Task<AdminNotificationAudiencePreviewDto> PreviewAsync(AdminNotificationAudienceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Hazir bir kitle sorgusu icin sayim ve ornek alicilar.</summary>
    Task<AdminNotificationAudiencePreviewDto> BuildPreviewAsync(IQueryable<ApplicationUser> audience, NotificationAudienceSegment segment, IReadOnlyList<long> invalidUserIds, CancellationToken cancellationToken = default);
}

public interface IAdminNotificationTemplateService
{
    Task<PagedResult<AdminNotificationTemplateDto>> GetListAsync(AdminNotificationTemplateListRequest request, CancellationToken cancellationToken = default);
    Task<AdminNotificationTemplateDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminNotificationTemplateDto> CreateAsync(AdminUpsertNotificationTemplateRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationTemplateDto> UpdateAsync(long id, AdminUpsertNotificationTemplateRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationMessagePreviewDto> PreviewAsync(AdminNotificationMessagePreviewRequest request, CancellationToken cancellationToken = default);
    AdminNotificationMetadataDto GetMetadata();
}

public interface IAdminNotificationRuleService
{
    Task<PagedResult<AdminNotificationRuleDto>> GetListAsync(AdminNotificationRuleListRequest request, CancellationToken cancellationToken = default);
    Task<AdminNotificationRuleDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminNotificationRuleDto> CreateAsync(AdminUpsertNotificationRuleRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationRuleDto> UpdateAsync(long id, AdminUpsertNotificationRuleRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationRuleDto> ChangeStatusAsync(long id, NotificationRuleStatus status, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

public interface IAdminNotificationCampaignService
{
    Task<PagedResult<AdminNotificationCampaignListItemDto>> GetListAsync(AdminNotificationCampaignListRequest request, CancellationToken cancellationToken = default);
    Task<AdminNotificationCampaignDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminNotificationCampaignDetailDto> CreateAsync(AdminUpsertNotificationCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationCampaignDetailDto> UpdateAsync(long id, AdminUpsertNotificationCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationAudiencePreviewDto> PreviewAudienceAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminNotificationCampaignDetailDto> ScheduleAsync(long id, AdminScheduleNotificationCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationCampaignDetailDto> UnscheduleAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationCampaignDetailDto> CancelAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminNotificationCampaignStatsDto> GetStatsAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminNotificationRetryResultDto> RetryFailedAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

public interface IAdminNotificationDispatchService
{
    Task<PagedResult<AdminNotificationDispatchDto>> GetListAsync(AdminNotificationDispatchListRequest request, CancellationToken cancellationToken = default);
    Task<AdminNotificationDispatchDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminNotificationRetryResultDto> RetryAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filtreye uyan Failed gonderimleri yeniden kuyruga alir (Pending). Suresi gecmis veya kaynagi
    /// (kural/kampanya) artik gecerli olmayanlar atlanir.
    /// </summary>
    Task<AdminNotificationRetryResultDto> RetryFailedAsync(IQueryable<NotificationDispatch> scope, DateTime nowUtc, long? adminUserId, CancellationToken cancellationToken = default);
}

public interface IAdminMonthlyRewardNotificationService
{
    Task<PagedResult<AdminMonthlyRewardNotificationRunDto>> GetListAsync(AdminMonthlyRewardNotificationRunListRequest request, CancellationToken cancellationToken = default);
    Task<AdminMonthlyRewardNotificationRunDto> GetByPeriodAsync(long periodId, CancellationToken cancellationToken = default);

    /// <summary>Admin tetikleme / yeniden deneme. Odul dagitilmamissa veya is calisiyorsa reddedilir.</summary>
    Task<AdminMonthlyRewardNotificationRunDto> TriggerAsync(long periodId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Odul dagitimindan sonra cagrilir: aylik donem icin isi kuyruga alir (yoksa olusturur, bitmis/basarisizsa
    /// yeniden Pending yapar). Gonderim donem bittikten sonra yapilir ve DedupeKey sayesinde ayni kullaniciya
    /// ayni donem icin ikinci kez gonderilmez.
    /// </summary>
    Task EnqueueAfterDistributionAsync(LeaderboardPeriod period, long? adminUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Kampanya ve aylik odul islerinin veritabani tabanli kilitleri. Kosullu UPDATE ile calisir; birden
/// fazla API ornegi ayni isi ayni anda alamaz. Kilidi suresi gecen (coken) is tekrar alinabilir.
/// </summary>
public interface INotificationJobStore
{
    Task<IReadOnlyList<long>> GetDueCampaignIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);
    Task<bool> TryClaimCampaignAsync(long campaignId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default);
    Task<bool> ExtendCampaignLockAsync(long campaignId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default);
    Task<bool> CompleteCampaignMaterializationAsync(long campaignId, Guid lockToken, int recipientCount, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> ReleaseCampaignAfterErrorAsync(long campaignId, Guid lockToken, string error, DateTime? retryAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Scheduled durumundaki kampanyayi kosullu olarak baska duruma alir (isleyiciyle yarisa karsi).</summary>
    Task<bool> TryChangeCampaignStatusAsync(long campaignId, NotificationCampaignStatus from, NotificationCampaignStatus to, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<long>> GetDueMonthlyRunIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);
    Task<bool> TryClaimMonthlyRunAsync(long runId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default);
    Task<bool> CompleteMonthlyRunAsync(long runId, Guid lockToken, int recipientCount, int createdCount, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> ReleaseMonthlyRunAfterErrorAsync(long runId, Guid lockToken, string error, DateTime? retryAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Arka plan isleyicisinin (NotificationJobWorker) cagirdigi is mantigi.</summary>
public interface INotificationJobProcessor
{
    /// <summary>Zamani gelmis kampanyalarin alicilarini partiler halinde kuyruga yazar.</summary>
    Task<int> ProcessDueCampaignsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Donemi bitmis ve kuyruktaki aylik odul bildirim islerini isler.</summary>
    Task<int> ProcessDueMonthlyRunsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
