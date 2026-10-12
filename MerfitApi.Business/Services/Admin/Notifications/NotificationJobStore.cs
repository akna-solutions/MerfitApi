using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Notifications;

/// <summary>
/// INotificationJobStore'un EF Core implementasyonu. Her gecis tek bir kosullu UPDATE'tir
/// (ExecuteUpdate); PostgreSQL satir kilidi sayesinde ayni isi iki ornek ayni anda alamaz ve
/// kilit sahibi olmayan (LockToken'i farkli) bir ornek durumu degistiremez.
/// </summary>
public class NotificationJobStore : INotificationJobStore
{
    private readonly IUnitOfWork _unitOfWork;

    public NotificationJobStore(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private IQueryable<NotificationCampaign> Campaigns => _unitOfWork.Repository<NotificationCampaign>().GetQueryable();

    private IQueryable<MonthlyRewardNotificationRun> Runs => _unitOfWork.Repository<MonthlyRewardNotificationRun>().GetQueryable();

    public async Task<IReadOnlyList<long>> GetDueCampaignIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default) =>
        await Campaigns
            .Where(c => (c.Status == NotificationCampaignStatus.Scheduled && c.ScheduledAtUtc <= nowUtc && (c.LockedUntilUtc == null || c.LockedUntilUtc <= nowUtc))
                || (c.Status == NotificationCampaignStatus.Processing && c.LockedUntilUtc < nowUtc))
            .OrderBy(c => c.ScheduledAtUtc).ThenBy(c => c.Id)
            .Select(c => c.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryClaimCampaignAsync(long campaignId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default) =>
        await Campaigns
            .Where(c => c.Id == campaignId)
            .Where(c => (c.Status == NotificationCampaignStatus.Scheduled && c.ScheduledAtUtc <= nowUtc && (c.LockedUntilUtc == null || c.LockedUntilUtc <= nowUtc))
                || (c.Status == NotificationCampaignStatus.Processing && c.LockedUntilUtc < nowUtc))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, NotificationCampaignStatus.Processing)
                .SetProperty(c => c.LockToken, lockToken)
                .SetProperty(c => c.LockedUntilUtc, lockedUntilUtc)
                .SetProperty(c => c.StartedAtUtc, c => c.StartedAtUtc ?? nowUtc)
                .SetProperty(c => c.AttemptCount, c => c.AttemptCount + 1)
                .SetProperty(c => c.UpdatedAt, nowUtc), cancellationToken) == 1;

    public async Task<bool> ExtendCampaignLockAsync(long campaignId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default) =>
        await Campaigns
            .Where(c => c.Id == campaignId && c.Status == NotificationCampaignStatus.Processing && c.LockToken == lockToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LockedUntilUtc, lockedUntilUtc)
                .SetProperty(c => c.UpdatedAt, nowUtc), cancellationToken) == 1;

    public async Task<bool> CompleteCampaignMaterializationAsync(long campaignId, Guid lockToken, int recipientCount, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await Campaigns
            .Where(c => c.Id == campaignId && c.Status == NotificationCampaignStatus.Processing && c.LockToken == lockToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, NotificationCampaignStatus.Dispatching)
                .SetProperty(c => c.RecipientCount, recipientCount)
                .SetProperty(c => c.LockToken, (Guid?)null)
                .SetProperty(c => c.LockedUntilUtc, (DateTime?)null)
                .SetProperty(c => c.LastError, (string?)null)
                .SetProperty(c => c.UpdatedAt, nowUtc), cancellationToken) == 1;

    public async Task<bool> ReleaseCampaignAfterErrorAsync(long campaignId, Guid lockToken, string error, DateTime? retryAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var status = retryAtUtc.HasValue ? NotificationCampaignStatus.Scheduled : NotificationCampaignStatus.Failed;
        return await Campaigns
            .Where(c => c.Id == campaignId && c.Status == NotificationCampaignStatus.Processing && c.LockToken == lockToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.LockToken, (Guid?)null)
                .SetProperty(c => c.LockedUntilUtc, retryAtUtc)
                .SetProperty(c => c.LastError, error)
                .SetProperty(c => c.UpdatedAt, nowUtc), cancellationToken) == 1;
    }

    public async Task<bool> TryChangeCampaignStatusAsync(long campaignId, NotificationCampaignStatus from, NotificationCampaignStatus to, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await Campaigns
            .Where(c => c.Id == campaignId && c.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, to)
                .SetProperty(c => c.UpdatedAt, nowUtc), cancellationToken) == 1;

    public async Task<IReadOnlyList<long>> GetDueMonthlyRunIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default)
    {
        var periods = _unitOfWork.Repository<LeaderboardPeriod>().GetQueryable();
        return await Runs
            .Where(r => (r.Status == MonthlyRewardNotificationRunStatus.Pending && (r.LockedUntilUtc == null || r.LockedUntilUtc <= nowUtc))
                || (r.Status == MonthlyRewardNotificationRunStatus.Processing && r.LockedUntilUtc < nowUtc))
            // Sonuclar donem bittikten sonra kesinlesir; erken gonderim yapilmaz.
            .Where(r => periods.Any(p => p.Id == r.LeaderboardPeriodId && p.EndDate <= nowUtc))
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimMonthlyRunAsync(long runId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default) =>
        await Runs
            .Where(r => r.Id == runId)
            .Where(r => (r.Status == MonthlyRewardNotificationRunStatus.Pending && (r.LockedUntilUtc == null || r.LockedUntilUtc <= nowUtc))
                || (r.Status == MonthlyRewardNotificationRunStatus.Processing && r.LockedUntilUtc < nowUtc))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, MonthlyRewardNotificationRunStatus.Processing)
                .SetProperty(r => r.LockToken, lockToken)
                .SetProperty(r => r.LockedUntilUtc, lockedUntilUtc)
                .SetProperty(r => r.StartedAtUtc, r => r.StartedAtUtc ?? nowUtc)
                .SetProperty(r => r.AttemptCount, r => r.AttemptCount + 1)
                .SetProperty(r => r.UpdatedAt, nowUtc), cancellationToken) == 1;

    public async Task<bool> CompleteMonthlyRunAsync(long runId, Guid lockToken, int recipientCount, int createdCount, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await Runs
            .Where(r => r.Id == runId && r.Status == MonthlyRewardNotificationRunStatus.Processing && r.LockToken == lockToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, MonthlyRewardNotificationRunStatus.Completed)
                .SetProperty(r => r.RecipientCount, recipientCount)
                .SetProperty(r => r.CreatedDispatchCount, createdCount)
                .SetProperty(r => r.CompletedAtUtc, nowUtc)
                .SetProperty(r => r.LockToken, (Guid?)null)
                .SetProperty(r => r.LockedUntilUtc, (DateTime?)null)
                .SetProperty(r => r.LastError, (string?)null)
                .SetProperty(r => r.UpdatedAt, nowUtc), cancellationToken) == 1;

    public async Task<bool> ReleaseMonthlyRunAfterErrorAsync(long runId, Guid lockToken, string error, DateTime? retryAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var status = retryAtUtc.HasValue ? MonthlyRewardNotificationRunStatus.Pending : MonthlyRewardNotificationRunStatus.Failed;
        return await Runs
            .Where(r => r.Id == runId && r.Status == MonthlyRewardNotificationRunStatus.Processing && r.LockToken == lockToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.LockToken, (Guid?)null)
                .SetProperty(r => r.LockedUntilUtc, retryAtUtc)
                .SetProperty(r => r.LastError, error)
                .SetProperty(r => r.UpdatedAt, nowUtc), cancellationToken) == 1;
    }
}
