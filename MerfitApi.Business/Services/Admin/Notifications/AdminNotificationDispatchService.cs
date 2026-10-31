using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Notifications;

/// <summary>
/// Gonderim kayitlari (alici sonuclari) ve cihaz denemeleri. Cihaz tokenlari hicbir yanitta yer almaz.
/// Yeniden deneme yalnizca Failed kayitlari Pending'e alir; gonderimi MerfitCustomerApi'deki gonderim
/// motoru yapar ve onceden kabul edilmis cihazlara tekrar gondermez.
/// </summary>
public class AdminNotificationDispatchService : IAdminNotificationDispatchService
{
    private const int RetryBatchSize = 500;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<NotificationDispatch, object?>>> SortMap =
        new Dictionary<string, Expression<Func<NotificationDispatch, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = x => x.CreatedAt,
            ["processedAt"] = x => x.ProcessedAtUtc,
            ["status"] = x => x.Status,
            ["id"] = x => x.Id,
        };

    public AdminNotificationDispatchService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminNotificationDispatchDto>> GetListAsync(AdminNotificationDispatchListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<NotificationDispatch>().GetQueryable();
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (request.Source.HasValue) query = query.Where(x => x.Source == request.Source.Value);
        if (request.App.HasValue)
        {
            var appTypes = NotificationApps.TypesOf(request.App.Value).ToList();
            query = query.Where(x => appTypes.Contains(x.Type));
        }
        if (request.CampaignId.HasValue) query = query.Where(x => x.NotificationCampaignId == request.CampaignId.Value);
        if (request.RuleId.HasValue) query = query.Where(x => x.NotificationRuleId == request.RuleId.Value);
        if (request.LeaderboardPeriodId.HasValue) query = query.Where(x => x.LeaderboardPeriodId == request.LeaderboardPeriodId.Value);
        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (request.From.HasValue) query = query.Where(x => x.CreatedAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(x => x.CreatedAt <= request.To.Value);

        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => users.Any(u => u.Id == x.UserId && u.Email.ToLower().Contains(search)));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .ApplySort(request.SortBy, !string.IsNullOrEmpty(request.SortDirection) ? request.IsDescending : true, SortMap, defaultKey: "id")
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                Dispatch = x,
                Email = users.Where(u => u.Id == x.UserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminNotificationDispatchDto>.Create(
            rows.Select(r => Fill(new AdminNotificationDispatchDto(), r.Dispatch, r.Email)).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminNotificationDispatchDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var dispatch = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(NotificationDispatch), id);
        var email = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == dispatch.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        var devices = _unitOfWork.Repository<UserDevice>().GetQueryable();
        var attempts = await _unitOfWork.Repository<NotificationDeliveryAttempt>().GetQueryable()
            .Where(a => a.NotificationDispatchId == id)
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                Attempt = a,
                Device = devices.Where(d => d.Id == a.UserDeviceId).Select(d => new { d.Platform, d.DeviceName, d.IsActive }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var dto = Fill(new AdminNotificationDispatchDetailDto(), dispatch, email);
        dto.Body = dispatch.Body;
        dto.DataJson = dispatch.DataJson;
        dto.Attempts = attempts.Select(x => new AdminNotificationDeliveryAttemptDto
        {
            Id = x.Attempt.Id,
            AttemptNumber = x.Attempt.AttemptNumber,
            Platform = x.Device?.Platform.ToString() ?? "-",
            DeviceName = x.Device?.DeviceName,
            DeviceActive = x.Device?.IsActive ?? false,
            Status = x.Attempt.Status.ToString(),
            ErrorCode = x.Attempt.ErrorCode,
            ErrorMessage = x.Attempt.ErrorMessage,
            CreatedAt = x.Attempt.CreatedAt,
            ReceiptCheckedAtUtc = x.Attempt.ReceiptCheckedAtUtc,
        }).ToList();
        return dto;
    }

    public async Task<AdminNotificationRetryResultDto> RetryAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var dispatch = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(NotificationDispatch), id);
        if (dispatch.Status != NotificationDispatchStatus.Failed)
        {
            throw new ConflictException("Yalnizca basarisiz (Failed) gonderimler yeniden denenebilir.");
        }

        var result = await RetryFailedAsync(
            _unitOfWork.Repository<NotificationDispatch>().GetQueryable().Where(x => x.Id == id), DateTime.UtcNow, adminUserId, cancellationToken);
        if (result.RetriedCount == 0)
        {
            throw new ConflictException("Gonderim yeniden denenemez: suresi dolmus veya kaynagi (kural/kampanya) artik aktif degil.");
        }

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_DISPATCH_RETRIED", nameof(NotificationDispatch), id, ipAddress, userAgent, cancellationToken: cancellationToken);
        return result;
    }

    public async Task<AdminNotificationRetryResultDto> RetryFailedAsync(IQueryable<NotificationDispatch> scope, DateTime nowUtc, long? adminUserId, CancellationToken cancellationToken = default)
    {
        var rules = _unitOfWork.Repository<NotificationRule>().GetQueryable();
        var campaigns = _unitOfWork.Repository<NotificationCampaign>().GetQueryable();

        var ids = await scope.Where(x => x.Status == NotificationDispatchStatus.Failed).Select(x => x.Id).OrderBy(x => x).ToListAsync(cancellationToken);
        var retried = 0;
        var skipped = 0;

        foreach (var chunk in ids.Chunk(RetryBatchSize))
        {
            var chunkIds = chunk.ToList();
            var dispatches = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable(asNoTracking: false)
                .Where(x => chunkIds.Contains(x.Id) && x.Status == NotificationDispatchStatus.Failed)
                .Select(x => new
                {
                    Dispatch = x,
                    RuleActive = x.NotificationRuleId == null || rules.Any(r => r.Id == x.NotificationRuleId && r.Status == NotificationRuleStatus.Active),
                    CampaignOpen = x.NotificationCampaignId == null || campaigns.Any(c => c.Id == x.NotificationCampaignId && c.Status != NotificationCampaignStatus.Cancelled),
                })
                .ToListAsync(cancellationToken);

            foreach (var item in dispatches)
            {
                var d = item.Dispatch;
                var expired = d.ExpiresAtUtc.HasValue && d.ExpiresAtUtc.Value <= nowUtc;
                var ruleGone = d.Source == NotificationDispatchSource.Rule && (d.NotificationRuleId == null || !item.RuleActive);
                if (expired || ruleGone || !item.CampaignOpen)
                {
                    skipped++;
                    continue;
                }

                // Admin yeniden denemesi yeni ve sinirli bir deneme dongusu baslatir; daha once kabul edilmis
                // cihazlara gonderim motoru tekrar gondermez.
                d.Status = NotificationDispatchStatus.Pending;
                d.StatusReason = null;
                d.AttemptCount = 0;
                d.NextAttemptAtUtc = nowUtc;
                d.LockToken = null;
                d.LockedUntilUtc = null;
                d.ProcessedAtUtc = null;
                d.LastError = null;
                d.UpdatedAt = nowUtc;
                d.UpdatedUser = adminUserId;
                retried++;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _unitOfWork.ClearTracking();
        }

        return new AdminNotificationRetryResultDto { RetriedCount = retried, SkippedCount = skipped };
    }

    private static T Fill<T>(T dto, NotificationDispatch x, string? email) where T : AdminNotificationDispatchDto
    {
        dto.Id = x.Id;
        dto.UserId = x.UserId;
        dto.UserEmail = email ?? "-";
        dto.Type = x.Type.ToString();
        dto.App = NotificationApps.Of(x.Type).ToString();
        dto.Source = x.Source.ToString();
        dto.NotificationRuleId = x.NotificationRuleId;
        dto.NotificationCampaignId = x.NotificationCampaignId;
        dto.LeaderboardPeriodId = x.LeaderboardPeriodId;
        dto.Title = x.Title;
        dto.Status = x.Status.ToString();
        dto.StatusReason = x.StatusReason;
        dto.AttemptCount = x.AttemptCount;
        dto.LastError = x.LastError;
        dto.ScheduledForUtc = x.ScheduledForUtc;
        dto.ExpiresAtUtc = x.ExpiresAtUtc;
        dto.ProcessedAtUtc = x.ProcessedAtUtc;
        dto.NotificationId = x.NotificationId;
        dto.CreatedAt = x.CreatedAt;
        return dto;
    }
}
