using MerfitApi.Business.Common;
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
/// Aylik MB FIT Puan odul bildirimleri. Odul hesaplama/dagitim mantigina dokunmaz: yalnizca
/// AdminLeaderboardService.DistributeRewardsAsync'in olusturdugu UserReward kayitlarini okuyan bir is
/// (MonthlyRewardNotificationRun, donem basina tek satir) kuyruga alir. Is, donem bittikten sonra
/// NotificationJobWorker tarafindan islenir.
/// </summary>
public class AdminMonthlyRewardNotificationService : IAdminMonthlyRewardNotificationService
{
    public const string NotQueuedStatus = "NotQueued";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminMonthlyRewardNotificationService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminMonthlyRewardNotificationRunDto>> GetListAsync(AdminMonthlyRewardNotificationRunListRequest request, CancellationToken cancellationToken = default)
    {
        var runs = _unitOfWork.Repository<MonthlyRewardNotificationRun>().GetQueryable();
        var periods = _unitOfWork.Repository<LeaderboardPeriod>().GetQueryable().Where(p => p.Type == LeaderboardPeriodType.Monthly);
        if (request.Status.HasValue)
        {
            var status = request.Status.Value;
            periods = periods.Where(p => runs.Any(r => r.LeaderboardPeriodId == p.Id && r.Status == status));
        }

        var totalCount = await periods.LongCountAsync(cancellationToken);
        var page = await periods
            .OrderByDescending(p => p.StartDate)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = await BuildDtosAsync(page, cancellationToken);
        return PagedResult<AdminMonthlyRewardNotificationRunDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminMonthlyRewardNotificationRunDto> GetByPeriodAsync(long periodId, CancellationToken cancellationToken = default)
    {
        var period = await GetMonthlyPeriodAsync(periodId, cancellationToken);
        return (await BuildDtosAsync(new List<LeaderboardPeriod> { period }, cancellationToken)).Single();
    }

    public async Task<AdminMonthlyRewardNotificationRunDto> TriggerAsync(long periodId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var period = await GetMonthlyPeriodAsync(periodId, cancellationToken);

        var hasRewards = await _unitOfWork.Repository<UserReward>()
            .AnyAsync(x => x.LeaderboardPeriodId == periodId && x.Status != UserRewardStatus.Cancelled, cancellationToken);
        if (!hasRewards)
        {
            throw new ConflictException("Bu doneme henuz odul dagitilmamis. Once siralamadan odulleri dagitin.");
        }

        var run = await _unitOfWork.Repository<MonthlyRewardNotificationRun>().GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(x => x.LeaderboardPeriodId == periodId, cancellationToken);
        var now = DateTime.UtcNow;
        if (run is { Status: MonthlyRewardNotificationRunStatus.Processing } && run.LockedUntilUtc > now)
        {
            throw new ConflictException("Bu donemin bildirim isi su anda calisiyor.");
        }

        var oldStatus = run?.Status.ToString() ?? NotQueuedStatus;
        await UpsertPendingAsync(period, run, adminUserId, now, cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "MONTHLY_REWARD_NOTIFICATION_TRIGGERED", nameof(MonthlyRewardNotificationRun), periodId, ipAddress, userAgent,
            oldValue: new { Status = oldStatus }, newValue: new { Status = nameof(MonthlyRewardNotificationRunStatus.Pending) }, cancellationToken: cancellationToken);

        return await GetByPeriodAsync(periodId, cancellationToken);
    }

    public async Task EnqueueAfterDistributionAsync(LeaderboardPeriod period, long? adminUserId, CancellationToken cancellationToken = default)
    {
        if (period.Type != LeaderboardPeriodType.Monthly)
        {
            return;
        }

        var run = await _unitOfWork.Repository<MonthlyRewardNotificationRun>().GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(x => x.LeaderboardPeriodId == period.Id, cancellationToken);

        // Calisan veya bekleyen is varsa dokunulmaz; bitmis/basarisiz is yeniden Pending olur ve yalnizca
        // henuz bildirim almamis kazananlara (DedupeKey) gonderir.
        if (run is { Status: MonthlyRewardNotificationRunStatus.Pending or MonthlyRewardNotificationRunStatus.Processing })
        {
            return;
        }

        await UpsertPendingAsync(period, run, adminUserId, DateTime.UtcNow, cancellationToken);
    }

    private async Task UpsertPendingAsync(LeaderboardPeriod period, MonthlyRewardNotificationRun? run, long? adminUserId, DateTime now, CancellationToken cancellationToken)
    {
        if (run is null)
        {
            await _unitOfWork.Repository<MonthlyRewardNotificationRun>().AddAsync(new MonthlyRewardNotificationRun
            {
                LeaderboardPeriodId = period.Id,
                Status = MonthlyRewardNotificationRunStatus.Pending,
                CreatedAt = now,
                CreatedUser = adminUserId,
            }, cancellationToken);
        }
        else
        {
            run.Status = MonthlyRewardNotificationRunStatus.Pending;
            run.AttemptCount = 0;
            run.LastError = null;
            run.LockToken = null;
            run.LockedUntilUtc = null;
            run.CompletedAtUtc = null;
            run.UpdatedAt = now;
            run.UpdatedUser = adminUserId;
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (run is null && DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            // Ayni anda baska bir istek ayni donem icin isi olusturdu; donem basina tek is kurali korunur.
            _unitOfWork.ClearTracking();
        }
    }

    private async Task<List<AdminMonthlyRewardNotificationRunDto>> BuildDtosAsync(List<LeaderboardPeriod> periods, CancellationToken cancellationToken)
    {
        var periodIds = periods.Select(p => p.Id).ToList();
        var runs = await _unitOfWork.Repository<MonthlyRewardNotificationRun>().GetQueryable()
            .Where(r => periodIds.Contains(r.LeaderboardPeriodId))
            .ToDictionaryAsync(r => r.LeaderboardPeriodId, cancellationToken);
        var rewardCounts = (await _unitOfWork.Repository<UserReward>().GetQueryable()
                .Where(x => x.LeaderboardPeriodId != null && periodIds.Contains(x.LeaderboardPeriodId.Value) && x.Status != UserRewardStatus.Cancelled)
                .GroupBy(x => x.LeaderboardPeriodId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Key, x => x.Count);
        var dispatchCounts = (await _unitOfWork.Repository<NotificationDispatch>().GetQueryable()
                .Where(d => d.Source == NotificationDispatchSource.MonthlyReward && d.LeaderboardPeriodId != null && periodIds.Contains(d.LeaderboardPeriodId.Value))
                .GroupBy(d => new { PeriodId = d.LeaderboardPeriodId!.Value, d.Status })
                .Select(g => new { g.Key.PeriodId, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.PeriodId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Status.ToString(), x => x.Count));

        var now = DateTime.UtcNow;
        return periods.Select(p =>
        {
            runs.TryGetValue(p.Id, out var run);
            return new AdminMonthlyRewardNotificationRunDto
            {
                Id = run?.Id,
                LeaderboardPeriodId = p.Id,
                PeriodStartDate = p.StartDate,
                PeriodEndDate = p.EndDate,
                Status = run?.Status.ToString() ?? NotQueuedStatus,
                WaitingForPeriodEnd = p.EndDate > now,
                RewardCount = rewardCounts.GetValueOrDefault(p.Id),
                AttemptCount = run?.AttemptCount ?? 0,
                RecipientCount = run?.RecipientCount,
                CreatedDispatchCount = run?.CreatedDispatchCount,
                StartedAtUtc = run?.StartedAtUtc,
                CompletedAtUtc = run?.CompletedAtUtc,
                LastError = run?.LastError,
                DispatchStatusCounts = dispatchCounts.GetValueOrDefault(p.Id) ?? new Dictionary<string, int>(),
                CreatedAt = run?.CreatedAt,
            };
        }).ToList();
    }

    private async Task<LeaderboardPeriod> GetMonthlyPeriodAsync(long periodId, CancellationToken cancellationToken)
    {
        var period = await _unitOfWork.Repository<LeaderboardPeriod>().GetQueryable().FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaderboardPeriod), periodId);
        if (period.Type != LeaderboardPeriodType.Monthly)
        {
            throw new AppValidationException("periodId", "Odul bildirimleri yalnizca aylik donemler icin gonderilir.");
        }

        return period;
    }
}
