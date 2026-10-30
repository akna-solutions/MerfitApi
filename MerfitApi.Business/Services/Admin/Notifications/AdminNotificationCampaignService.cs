using System.Linq.Expressions;
using MerfitApi.Business.Common.Notifications;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitApi.Business.Services.Admin.Notifications;

/// <summary>
/// Admin duyuru kampanyalari. Durum akisi: Draft -> Scheduled -> Processing (alicilar kuyruga yaziliyor)
/// -> Dispatching (gonderim motoru isliyor) -> Completed. Draft/Scheduled/Processing/Dispatching iptal
/// edilebilir; iptal edilen kampanyanin bekleyen gonderimleri gonderim aninda iptal edilir.
/// HTTP istegi icinde saglayiciya (Expo) hicbir cagri yapilmaz: zamanlama yalnizca kampanyayi
/// isaretler, alicilari NotificationJobWorker partiler halinde kuyruga yazar.
/// </summary>
public class AdminNotificationCampaignService : IAdminNotificationCampaignService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationAudienceResolver _audience;
    private readonly INotificationJobStore _jobStore;
    private readonly IAdminNotificationDispatchService _dispatchService;
    private readonly NotificationAdminOptions _options;

    private static readonly IReadOnlyDictionary<string, Expression<Func<NotificationCampaign, object?>>> SortMap =
        new Dictionary<string, Expression<Func<NotificationCampaign, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["scheduledAt"] = x => x.ScheduledAtUtc,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminNotificationCampaignService(
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogService,
        INotificationAudienceResolver audience,
        INotificationJobStore jobStore,
        IAdminNotificationDispatchService dispatchService,
        IOptions<NotificationAdminOptions> options)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _audience = audience;
        _jobStore = jobStore;
        _dispatchService = dispatchService;
        _options = options.Value;
    }

    public async Task<PagedResult<AdminNotificationCampaignListItemDto>> GetListAsync(AdminNotificationCampaignListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<NotificationCampaign>().GetQueryable();
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(search) || x.Title.ToLower().Contains(search));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .ApplySort(request.SortBy, !string.IsNullOrEmpty(request.SortDirection) ? request.IsDescending : true, SortMap, defaultKey: "createdAt")
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<AdminNotificationCampaignListItemDto>.Create(
            items.Select(x => Fill(new AdminNotificationCampaignListItemDto(), x)).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminNotificationCampaignDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<NotificationCampaign>().GetQueryable().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(NotificationCampaign), id);
        var userIds = await _unitOfWork.Repository<NotificationCampaignUser>().GetQueryable()
            .Where(x => x.NotificationCampaignId == id).Select(x => x.UserId).OrderBy(x => x).ToListAsync(cancellationToken);
        return MapToDetail(entity, userIds);
    }

    public async Task<AdminNotificationCampaignDetailDto> CreateAsync(AdminUpsertNotificationCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var content = await ValidateAsync(request, cancellationToken);
        var now = DateTime.UtcNow;

        var entity = new NotificationCampaign
        {
            Name = request.Name.Trim(),
            Type = request.Type,
            Status = NotificationCampaignStatus.Draft,
            Segment = request.Segment,
            NotificationTemplateId = request.NotificationTemplateId,
            Title = content.Title,
            Body = content.Body,
            DataJson = content.DataJson,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedAt = now,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        await _unitOfWork.Repository<NotificationCampaign>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.Repository<NotificationCampaignUser>().AddRangeAsync(content.UserIds.Select(u => new NotificationCampaignUser
        {
            NotificationCampaignId = entity.Id,
            UserId = u,
            CreatedAt = now,
            CreatedUser = adminUserId,
        }), cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_CAMPAIGN_CREATED", nameof(NotificationCampaign), entity.Id, ipAddress, userAgent,
            newValue: new { entity.Name, Type = entity.Type.ToString(), Segment = entity.Segment.ToString(), SelectedUserCount = content.UserIds.Count, entity.Title },
            cancellationToken: cancellationToken);

        return MapToDetail(entity, content.UserIds);
    }

    public async Task<AdminNotificationCampaignDetailDto> UpdateAsync(long id, AdminUpsertNotificationCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetTrackedOrThrowAsync(id, cancellationToken);
        if (entity.Status != NotificationCampaignStatus.Draft)
        {
            throw new ConflictException("Yalnizca taslak kampanyalar duzenlenebilir. Zamanlanmis kampanyayi once taslaga alin.");
        }

        var content = await ValidateAsync(request, cancellationToken);
        var now = DateTime.UtcNow;
        var oldValue = new { entity.Name, Type = entity.Type.ToString(), Segment = entity.Segment.ToString(), entity.Title, entity.Body };

        entity.Name = request.Name.Trim();
        entity.Type = request.Type;
        entity.Segment = request.Segment;
        entity.NotificationTemplateId = request.NotificationTemplateId;
        entity.Title = content.Title;
        entity.Body = content.Body;
        entity.DataJson = content.DataJson;
        entity.ExpiresAtUtc = request.ExpiresAtUtc;
        entity.UpdatedAt = now;
        entity.UpdatedUser = adminUserId;

        var userRepo = _unitOfWork.Repository<NotificationCampaignUser>();
        var existing = await userRepo.GetQueryable(asNoTracking: false).Where(x => x.NotificationCampaignId == id).ToListAsync(cancellationToken);
        var wanted = content.UserIds.ToHashSet();
        userRepo.RemoveRange(existing.Where(x => !wanted.Contains(x.UserId)));
        var existingIds = existing.Select(x => x.UserId).ToHashSet();
        await userRepo.AddRangeAsync(content.UserIds.Where(u => !existingIds.Contains(u)).Select(u => new NotificationCampaignUser
        {
            NotificationCampaignId = id,
            UserId = u,
            CreatedAt = now,
            CreatedUser = adminUserId,
        }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_CAMPAIGN_UPDATED", nameof(NotificationCampaign), id, ipAddress, userAgent,
            oldValue: oldValue, newValue: new { entity.Name, Type = entity.Type.ToString(), Segment = entity.Segment.ToString(), entity.Title, entity.Body },
            cancellationToken: cancellationToken);

        return MapToDetail(entity, content.UserIds);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetTrackedOrThrowAsync(id, cancellationToken);
        if (entity.Status != NotificationCampaignStatus.Draft)
        {
            throw new ConflictException("Yalnizca taslak kampanyalar silinebilir; diger durumlarda iptal edin (gecmis korunur).");
        }

        var userRepo = _unitOfWork.Repository<NotificationCampaignUser>();
        userRepo.RemoveRange(await userRepo.GetQueryable(asNoTracking: false).Where(x => x.NotificationCampaignId == id).ToListAsync(cancellationToken));
        _unitOfWork.Repository<NotificationCampaign>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_CAMPAIGN_DELETED", nameof(NotificationCampaign), id, ipAddress, userAgent,
            oldValue: new { entity.Name, entity.Title }, cancellationToken: cancellationToken);
    }

    public async Task<AdminNotificationAudiencePreviewDto> PreviewAudienceAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<NotificationCampaign>().GetQueryable().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(NotificationCampaign), id);
        return await _audience.BuildPreviewAsync(_audience.Query(entity.Segment, DateTime.UtcNow, campaignId: id), entity.Segment, Array.Empty<long>(), cancellationToken);
    }

    public async Task<AdminNotificationCampaignDetailDto> ScheduleAsync(long id, AdminScheduleNotificationCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetTrackedOrThrowAsync(id, cancellationToken);
        if (entity.Status != NotificationCampaignStatus.Draft)
        {
            throw new ConflictException("Yalnizca taslak kampanyalar zamanlanabilir veya gonderilebilir.");
        }

        var now = DateTime.UtcNow;
        var scheduledAt = request.ScheduledAtUtc.HasValue ? EnsureUtc(request.ScheduledAtUtc.Value) : now;
        if (scheduledAt < now.AddMinutes(-1))
        {
            throw new AppValidationException(nameof(request.ScheduledAtUtc), "Gonderim zamani gecmiste olamaz.");
        }

        if (entity.ExpiresAtUtc.HasValue && entity.ExpiresAtUtc.Value <= scheduledAt)
        {
            throw new AppValidationException(nameof(request.ScheduledAtUtc), "Gonderim zamani kampanyanin bitis zamanindan once olmalidir.");
        }

        var recipientCount = await _audience.Query(entity.Segment, now, campaignId: id).CountAsync(cancellationToken);
        if (recipientCount == 0)
        {
            throw new ConflictException("Bu kitlede bildirim alabilecek kullanici yok.");
        }

        if (recipientCount > _options.LargeAudienceThreshold && !request.ConfirmLargeAudience)
        {
            throw new ConflictException($"Kampanya {recipientCount} kullaniciya gidecek. Buyuk kitleye gonderim icin onay (confirmLargeAudience) gereklidir.");
        }

        entity.Status = NotificationCampaignStatus.Scheduled;
        entity.ScheduledAtUtc = scheduledAt;
        entity.AttemptCount = 0;
        entity.LastError = null;
        entity.LockToken = null;
        entity.LockedUntilUtc = null;
        entity.RecipientCount = null;
        entity.UpdatedAt = now;
        entity.UpdatedUser = adminUserId;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, request.ScheduledAtUtc.HasValue ? "NOTIFICATION_CAMPAIGN_SCHEDULED" : "NOTIFICATION_CAMPAIGN_SEND_NOW",
            nameof(NotificationCampaign), id, ipAddress, userAgent,
            newValue: new { ScheduledAtUtc = scheduledAt, EstimatedRecipientCount = recipientCount, request.ConfirmLargeAudience },
            cancellationToken: cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<AdminNotificationCampaignDetailDto> UnscheduleAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync(id, cancellationToken);
        var changed = await _jobStore.TryChangeCampaignStatusAsync(id, NotificationCampaignStatus.Scheduled, NotificationCampaignStatus.Draft, DateTime.UtcNow, cancellationToken);
        if (!changed)
        {
            throw new ConflictException("Kampanya artik zamanlanmis durumda degil (gonderim baslamis olabilir). Gerekirse iptal edin.");
        }

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_CAMPAIGN_UNSCHEDULED", nameof(NotificationCampaign), id, ipAddress, userAgent, cancellationToken: cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<AdminNotificationCampaignDetailDto> CancelAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetTrackedOrThrowAsync(id, cancellationToken);
        if (entity.Status is NotificationCampaignStatus.Completed or NotificationCampaignStatus.Cancelled)
        {
            throw new ConflictException("Tamamlanmis veya iptal edilmis kampanya iptal edilemez.");
        }

        var oldStatus = entity.Status;
        var now = DateTime.UtcNow;
        entity.Status = NotificationCampaignStatus.Cancelled;
        entity.CancelledAtUtc = now;
        entity.LockToken = null;
        entity.LockedUntilUtc = null;
        entity.UpdatedAt = now;
        entity.UpdatedUser = adminUserId;

        // Kuyrukta bekleyen gonderimler worker'i beklemeden hemen iptal edilir; aksi halde "Kuyrukta" gorunmeye devam ederler.
        var pendingDispatches = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable(asNoTracking: false)
            .Where(x => x.NotificationCampaignId == id && x.Status == NotificationDispatchStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var dispatch in pendingDispatches)
        {
            dispatch.Status = NotificationDispatchStatus.Cancelled;
            dispatch.StatusReason = "CampaignCancelled";
            dispatch.ProcessedAtUtc = now;
            dispatch.LockToken = null;
            dispatch.LockedUntilUtc = null;
            dispatch.UpdatedAt = now;
            dispatch.UpdatedUser = adminUserId;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_CAMPAIGN_CANCELLED", nameof(NotificationCampaign), id, ipAddress, userAgent,
            oldValue: new { Status = oldStatus.ToString() }, newValue: new { Status = entity.Status.ToString(), CancelledDispatchCount = pendingDispatches.Count }, cancellationToken: cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<AdminNotificationCampaignStatsDto> GetStatsAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<NotificationCampaign>().GetQueryable().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(NotificationCampaign), id);

        var dispatches = _unitOfWork.Repository<NotificationDispatch>().GetQueryable().Where(x => x.NotificationCampaignId == id);
        var statusCounts = await dispatches.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var attemptCounts = await _unitOfWork.Repository<NotificationDeliveryAttempt>().GetQueryable()
            .Where(a => dispatches.Any(d => d.Id == a.NotificationDispatchId))
            .GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var notifications = _unitOfWork.Repository<Notification>().GetQueryable()
            .Where(n => dispatches.Any(d => d.NotificationId == n.Id));
        var inboxCount = await notifications.CountAsync(cancellationToken);
        var readCount = await notifications.CountAsync(n => n.IsRead, cancellationToken);

        var byStatus = statusCounts.ToDictionary(x => x.Key.ToString(), x => x.Count);
        return new AdminNotificationCampaignStatsDto
        {
            CampaignId = id,
            Status = entity.Status.ToString(),
            RecipientCount = entity.RecipientCount,
            QueuedCount = byStatus.GetValueOrDefault(nameof(NotificationDispatchStatus.Pending)) + byStatus.GetValueOrDefault(nameof(NotificationDispatchStatus.Processing)),
            DispatchStatusCounts = byStatus,
            DeliveryStatusCounts = attemptCounts.ToDictionary(x => x.Key.ToString(), x => x.Count),
            InboxCount = inboxCount,
            ReadCount = readCount,
        };
    }

    public async Task<AdminNotificationRetryResultDto> RetryFailedAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetTrackedOrThrowAsync(id, cancellationToken);
        var now = DateTime.UtcNow;

        if (entity.ExpiresAtUtc.HasValue && entity.ExpiresAtUtc.Value <= now)
        {
            throw new ConflictException("Kampanyanin bitis zamani gecti; yeniden denenemez.");
        }

        AdminNotificationRetryResultDto result;
        switch (entity.Status)
        {
            case NotificationCampaignStatus.Failed:
                // Alicilar kuyruga yazilamadan basarisiz olduysa is yeniden baslatilir; DedupeKey ayni
                // kullaniciya ikinci kayit yazilmasini engeller.
                entity.Status = NotificationCampaignStatus.Scheduled;
                entity.ScheduledAtUtc ??= now;
                entity.AttemptCount = 0;
                entity.LastError = null;
                entity.LockToken = null;
                entity.LockedUntilUtc = null;
                entity.UpdatedAt = now;
                entity.UpdatedUser = adminUserId;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                result = await _dispatchService.RetryFailedAsync(
                    _unitOfWork.Repository<NotificationDispatch>().GetQueryable().Where(x => x.NotificationCampaignId == id), now, adminUserId, cancellationToken);
                break;

            case NotificationCampaignStatus.Dispatching:
            case NotificationCampaignStatus.Completed:
                result = await _dispatchService.RetryFailedAsync(
                    _unitOfWork.Repository<NotificationDispatch>().GetQueryable().Where(x => x.NotificationCampaignId == id), now, adminUserId, cancellationToken);
                if (result.RetriedCount > 0 && entity.Status == NotificationCampaignStatus.Completed)
                {
                    // Gonderim motoru acik gonderim kalmayinca kampanyayi tekrar Completed yapar.
                    var tracked = await GetTrackedOrThrowAsync(id, cancellationToken);
                    tracked.Status = NotificationCampaignStatus.Dispatching;
                    tracked.CompletedAtUtc = null;
                    tracked.UpdatedAt = now;
                    tracked.UpdatedUser = adminUserId;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                break;

            default:
                throw new ConflictException("Yalnizca gonderimde, tamamlanmis veya basarisiz kampanyalar yeniden denenebilir.");
        }

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_CAMPAIGN_RETRIED", nameof(NotificationCampaign), id, ipAddress, userAgent,
            newValue: new { result.RetriedCount, result.SkippedCount }, cancellationToken: cancellationToken);
        return result;
    }

    private sealed record CampaignContent(string Title, string Body, string? DataJson, List<long> UserIds);

    private async Task<CampaignContent> ValidateAsync(AdminUpsertNotificationCampaignRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AppValidationException(nameof(request.Name), "Ad zorunludur.");
        }

        if (!NotificationContentRules.CampaignTypes.Contains(request.Type))
        {
            throw new AppValidationException(nameof(request.Type),
                $"Kampanya turu su degerlerden biri olmalidir: {string.Join(", ", NotificationContentRules.CampaignTypes)}.");
        }

        var invalidUsers = await _audience.ValidateAsync(request.Segment, request.UserIds, cancellationToken);
        if (invalidUsers.Count > 0)
        {
            throw new AppValidationException(nameof(request.UserIds),
                $"Bildirim gonderilemeyecek kullanicilar (bulunamadi, pasif veya admin): {string.Join(", ", invalidUsers.Take(20))}.");
        }

        var title = request.Title?.Trim();
        var body = request.Body?.Trim();
        var dataJson = request.DataJson;

        if (request.NotificationTemplateId.HasValue)
        {
            var template = await _unitOfWork.Repository<NotificationTemplate>().GetQueryable()
                .FirstOrDefaultAsync(t => t.Id == request.NotificationTemplateId.Value, cancellationToken)
                ?? throw new AppValidationException(nameof(request.NotificationTemplateId), "Sablon bulunamadi.");
            if (!template.IsActive || template.Type != request.Type)
            {
                throw new AppValidationException(nameof(request.NotificationTemplateId), "Sablon aktif ve kampanya ile ayni turde olmalidir.");
            }

            title = string.IsNullOrWhiteSpace(title) ? template.TitleTemplate : title;
            body = string.IsNullOrWhiteSpace(body) ? template.BodyTemplate : body;
            dataJson = string.IsNullOrWhiteSpace(dataJson) ? template.DataJson : dataJson;
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            throw new AppValidationException(nameof(request.Body), "Baslik ve icerik zorunludur (veya bir sablon secin).");
        }

        var disallowed = NotificationContentRules.FindDisallowedPlaceholders(request.Type, title, body);
        if (disallowed.Count > 0)
        {
            throw new AppValidationException(nameof(request.Body),
                $"Kampanyalarda yalnizca {{firstName}} alani kullanilabilir (gecersiz: {string.Join(", ", disallowed.Select(x => "{" + x + "}"))}).");
        }

        var normalizedData = NotificationNavigation.Normalize(dataJson, out var error);
        if (error is not null)
        {
            throw new AppValidationException(nameof(request.DataJson), error);
        }

        if (request.ExpiresAtUtc.HasValue && EnsureUtc(request.ExpiresAtUtc.Value) <= DateTime.UtcNow)
        {
            throw new AppValidationException(nameof(request.ExpiresAtUtc), "Bitis zamani gelecekte olmalidir.");
        }

        if (request.ExpiresAtUtc.HasValue)
        {
            request.ExpiresAtUtc = EnsureUtc(request.ExpiresAtUtc.Value);
        }

        var userIds = request.Segment is NotificationAudienceSegment.SingleUser or NotificationAudienceSegment.SelectedUsers
            ? (request.UserIds ?? new List<long>()).Distinct().OrderBy(x => x).ToList()
            : new List<long>();

        return new CampaignContent(title, body, normalizedData, userIds);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private async Task EnsureExistsAsync(long id, CancellationToken cancellationToken)
    {
        if (!await _unitOfWork.Repository<NotificationCampaign>().AnyAsync(x => x.Id == id, cancellationToken))
        {
            throw new NotFoundException(nameof(NotificationCampaign), id);
        }
    }

    private async Task<NotificationCampaign> GetTrackedOrThrowAsync(long id, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<NotificationCampaign>().GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new NotFoundException(nameof(NotificationCampaign), id);

    private static T Fill<T>(T dto, NotificationCampaign x) where T : AdminNotificationCampaignListItemDto
    {
        dto.Id = x.Id;
        dto.Name = x.Name;
        dto.Type = x.Type.ToString();
        dto.Status = x.Status.ToString();
        dto.Segment = x.Segment.ToString();
        dto.Title = x.Title;
        dto.ScheduledAtUtc = x.ScheduledAtUtc;
        dto.StartedAtUtc = x.StartedAtUtc;
        dto.CompletedAtUtc = x.CompletedAtUtc;
        dto.CancelledAtUtc = x.CancelledAtUtc;
        dto.RecipientCount = x.RecipientCount;
        dto.CreatedAt = x.CreatedAt;
        return dto;
    }

    private static AdminNotificationCampaignDetailDto MapToDetail(NotificationCampaign x, IReadOnlyList<long> userIds)
    {
        var dto = Fill(new AdminNotificationCampaignDetailDto(), x);
        dto.NotificationTemplateId = x.NotificationTemplateId;
        dto.Body = x.Body;
        dto.DataJson = x.DataJson;
        dto.ExpiresAtUtc = x.ExpiresAtUtc;
        dto.AttemptCount = x.AttemptCount;
        dto.LastError = x.LastError;
        dto.UserIds = userIds;
        dto.CreatedUser = x.CreatedUser;
        return dto;
    }
}
