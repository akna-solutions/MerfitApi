using MerfitApi.Business.Common.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitApi.Business.Services.Admin.Notifications;

/// <summary>
/// Kampanya ve aylik odul islerini ortak NotificationDispatch kuyruguna yazar. Saglayiciya (Expo) cagri
/// yapmaz: gonderim, tercih/sessiz saat/gunluk limit kontrolleri ve yeniden denemeler MerfitCustomerApi'deki
/// NotificationDispatchProcessor tarafindan yapilir. Her alici icin DedupeKey (benzersiz index) ayni
/// kampanya / donem icin ikinci bir kayit olusmasini engeller; bu yuzden yarida kalan is guvenle tekrarlanir.
/// </summary>
public class NotificationJobProcessor : INotificationJobProcessor
{
    public const string RewardTitleFallback = "Tebrikler {firstName}!";
    public const string RewardBodyFallback = "{periodLabel} MB FIT Puanları sıralamasında {rank}. oldun ve {rewardTitle} kazandın.";
    private const int JobsPerTick = 5;
    private static readonly TimeSpan RewardNotificationLifetime = TimeSpan.FromDays(7);

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationJobStore _store;
    private readonly INotificationAudienceResolver _audience;
    private readonly NotificationAdminOptions _options;
    private readonly ILogger<NotificationJobProcessor> _logger;

    public NotificationJobProcessor(
        IUnitOfWork unitOfWork,
        INotificationJobStore store,
        INotificationAudienceResolver audience,
        IOptions<NotificationAdminOptions> options,
        ILogger<NotificationJobProcessor> logger)
    {
        _unitOfWork = unitOfWork;
        _store = store;
        _audience = audience;
        _options = options.Value;
        _logger = logger;
    }

    public static string CampaignDedupeKey(long campaignId, long userId) => $"campaign:{campaignId}:{userId}";

    public static string RewardDedupeKey(long periodId, long userId) => $"reward:{periodId}:{userId}";

    public async Task<int> ProcessDueCampaignsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var processed = 0;
        foreach (var id in await _store.GetDueCampaignIdsAsync(nowUtc, JobsPerTick, cancellationToken))
        {
            var token = Guid.NewGuid();
            if (!await _store.TryClaimCampaignAsync(id, token, nowUtc, nowUtc + _options.JobLockDuration, cancellationToken))
            {
                continue;
            }

            try
            {
                await MaterializeCampaignAsync(id, token, nowUtc, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _unitOfWork.ClearTracking();
                var attempts = await _unitOfWork.Repository<NotificationCampaign>().GetQueryable()
                    .Where(c => c.Id == id).Select(c => c.AttemptCount).FirstOrDefaultAsync(cancellationToken);
                var retryAt = RetryAt(attempts, DateTime.UtcNow);
                _logger.LogError(ex, "Notification campaign {CampaignId} materialization failed (attempt {Attempt}).", id, attempts);
                await _store.ReleaseCampaignAfterErrorAsync(id, token, Truncate(ex.Message, 1000)!, retryAt, DateTime.UtcNow, cancellationToken);
            }
        }

        return processed;
    }

    public async Task<int> ProcessDueMonthlyRunsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var processed = 0;
        foreach (var id in await _store.GetDueMonthlyRunIdsAsync(nowUtc, JobsPerTick, cancellationToken))
        {
            var token = Guid.NewGuid();
            if (!await _store.TryClaimMonthlyRunAsync(id, token, nowUtc, nowUtc + _options.JobLockDuration, cancellationToken))
            {
                continue;
            }

            try
            {
                await ProcessMonthlyRunAsync(id, token, nowUtc, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _unitOfWork.ClearTracking();
                var attempts = await _unitOfWork.Repository<MonthlyRewardNotificationRun>().GetQueryable()
                    .Where(r => r.Id == id).Select(r => r.AttemptCount).FirstOrDefaultAsync(cancellationToken);
                var retryAt = RetryAt(attempts, DateTime.UtcNow);
                _logger.LogError(ex, "Monthly reward notification run {RunId} failed (attempt {Attempt}).", id, attempts);
                await _store.ReleaseMonthlyRunAfterErrorAsync(id, token, Truncate(ex.Message, 1000)!, retryAt, DateTime.UtcNow, cancellationToken);
            }
        }

        return processed;
    }

    private async Task MaterializeCampaignAsync(long campaignId, Guid token, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var campaign = await _unitOfWork.Repository<NotificationCampaign>().GetQueryable()
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
        if (campaign is null || campaign.Status != NotificationCampaignStatus.Processing || campaign.LockToken != token)
        {
            return;
        }

        if (campaign.ExpiresAtUtc.HasValue && campaign.ExpiresAtUtc.Value <= nowUtc)
        {
            await _store.ReleaseCampaignAfterErrorAsync(campaignId, token, "Kampanyanin bitis zamani gonderim baslamadan gecti.", null, nowUtc, cancellationToken);
            return;
        }

        var batchSize = Math.Max(1, _options.MaterializeBatchSize);
        var dispatches = _unitOfWork.Repository<NotificationDispatch>().GetQueryable();
        var profiles = _unitOfWork.Repository<UserProfile>().GetQueryable();
        var lastUserId = 0L;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var after = lastUserId;
            var userIds = await _audience.Query(campaign.Segment, nowUtc, campaignId: campaignId)
                .Where(u => u.Id > after)
                .OrderBy(u => u.Id)
                .Select(u => u.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (userIds.Count == 0)
            {
                break;
            }

            var existing = (await dispatches
                    .Where(d => d.NotificationCampaignId == campaignId && userIds.Contains(d.UserId))
                    .Select(d => d.UserId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            var newUserIds = userIds.Where(id => !existing.Contains(id)).ToList();

            if (newUserIds.Count > 0)
            {
                var names = await FirstNamesAsync(profiles, newUserIds, cancellationToken);
                var rows = newUserIds.Select(userId =>
                {
                    var values = new Dictionary<string, string?> { [NotificationTemplateRenderer.FirstName] = names.GetValueOrDefault(userId) };
                    return NewDispatch(userId, campaign.Type, NotificationDispatchSource.Campaign, CampaignDedupeKey(campaignId, userId),
                        NotificationTemplateRenderer.Render(campaign.Title, values), NotificationTemplateRenderer.Render(campaign.Body, values),
                        campaign.DataJson, nowUtc, campaign.ExpiresAtUtc, d =>
                        {
                            d.NotificationCampaignId = campaignId;
                            d.NotificationTemplateId = campaign.NotificationTemplateId;
                        });
                }).ToList();

                await _unitOfWork.Repository<NotificationDispatch>().AddRangeAsync(rows, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _unitOfWork.ClearTracking();
            }

            lastUserId = userIds[^1];

            // Kampanya iptal edildiyse veya kilit baska bir ornege gectiyse durulur; yazilan kayitlari
            // gonderim motoru kampanya durumuna gore iptal eder.
            var now = DateTime.UtcNow;
            if (!await _store.ExtendCampaignLockAsync(campaignId, token, now, now + _options.JobLockDuration, cancellationToken))
            {
                _logger.LogInformation("Notification campaign {CampaignId} lock lost or campaign cancelled; stopping materialization.", campaignId);
                return;
            }
        }

        var total = await dispatches.CountAsync(d => d.NotificationCampaignId == campaignId, cancellationToken);
        await _store.CompleteCampaignMaterializationAsync(campaignId, token, total, DateTime.UtcNow, cancellationToken);
        _logger.LogInformation("Notification campaign {CampaignId} queued for {Count} recipients.", campaignId, total);
    }

    private async Task ProcessMonthlyRunAsync(long runId, Guid token, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var run = await _unitOfWork.Repository<MonthlyRewardNotificationRun>().GetQueryable().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || run.LockToken != token)
        {
            return;
        }

        var period = await _unitOfWork.Repository<LeaderboardPeriod>().GetQueryable().FirstAsync(p => p.Id == run.LeaderboardPeriodId, cancellationToken);
        var periodId = period.Id;

        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.IsActive && u.DeletedAt == null && u.Role == UserRole.User);
        var rewards = await _unitOfWork.Repository<UserReward>().GetQueryable()
            .Where(x => x.LeaderboardPeriodId == periodId && x.Status != UserRewardStatus.Cancelled)
            .Where(x => users.Any(u => u.Id == x.UserId))
            .Join(_unitOfWork.Repository<Reward>().GetQueryable(), ur => ur.RewardId, r => r.Id, (ur, r) => new { ur.UserId, ur.Rank, r.Title })
            .ToListAsync(cancellationToken);

        var winners = rewards
            .GroupBy(x => x.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Rank = g.Where(x => x.Rank.HasValue).Select(x => x.Rank!.Value).DefaultIfEmpty().Min(),
                Titles = string.Join(" + ", g.Select(x => x.Title).Distinct()),
            })
            .OrderBy(x => x.UserId)
            .ToList();

        var existing = (await _unitOfWork.Repository<NotificationDispatch>().GetQueryable()
                .Where(d => d.Source == NotificationDispatchSource.MonthlyReward && d.LeaderboardPeriodId == periodId)
                .Select(d => d.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var pending = winners.Where(w => !existing.Contains(w.UserId)).ToList();

        var template = await _unitOfWork.Repository<NotificationTemplate>().GetQueryable()
            .Where(t => t.IsActive && t.Type == NotificationType.Reward)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var titleTemplate = template?.TitleTemplate ?? RewardTitleFallback;
        var bodyTemplate = template?.BodyTemplate ?? RewardBodyFallback;
        var dataJson = NotificationNavigation.Normalize(template?.DataJson, out _) ?? NotificationNavigation.Screen("leaderboard");
        var periodLabel = PeriodLabel(period, _options.DefaultTimeZone);

        var created = 0;
        foreach (var chunk in pending.Chunk(Math.Max(1, _options.MaterializeBatchSize)))
        {
            var names = await FirstNamesAsync(_unitOfWork.Repository<UserProfile>().GetQueryable(), chunk.Select(x => x.UserId).ToList(), cancellationToken);
            var rows = chunk.Select(w =>
            {
                var values = new Dictionary<string, string?>
                {
                    [NotificationTemplateRenderer.FirstName] = names.GetValueOrDefault(w.UserId),
                    [NotificationTemplateRenderer.RewardTitle] = w.Titles,
                    [NotificationTemplateRenderer.Rank] = w.Rank > 0 ? w.Rank.ToString() : null,
                    [NotificationTemplateRenderer.PeriodLabel] = periodLabel,
                };
                return NewDispatch(w.UserId, NotificationType.Reward, NotificationDispatchSource.MonthlyReward, RewardDedupeKey(periodId, w.UserId),
                    NotificationTemplateRenderer.Render(titleTemplate, values), NotificationTemplateRenderer.Render(bodyTemplate, values),
                    dataJson, nowUtc, nowUtc + RewardNotificationLifetime, d =>
                    {
                        d.LeaderboardPeriodId = periodId;
                        d.NotificationTemplateId = template?.Id;
                    });
            }).ToList();

            await _unitOfWork.Repository<NotificationDispatch>().AddRangeAsync(rows, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _unitOfWork.ClearTracking();
            created += rows.Count;
        }

        await _store.CompleteMonthlyRunAsync(runId, token, winners.Count, created, DateTime.UtcNow, cancellationToken);
        _logger.LogInformation("Monthly reward notifications for period {PeriodId}: {Winners} winners, {Created} queued.", periodId, winners.Count, created);
    }

    /// <summary>Donemin ortasindaki yerel tarihe gore "Ekim 2026" bicimli etiket.</summary>
    public static string PeriodLabel(LeaderboardPeriod period, string timeZoneId)
    {
        var middle = DateTime.SpecifyKind(period.StartDate + (period.EndDate - period.StartDate) / 2, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(middle, ResolveTimeZone(timeZoneId));
        return NotificationContentRules.MonthLabel(local.Year, local.Month);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // ICU/tzdata olmayan ortamlarda MerfitScoring varsayilani (UTC+3) kullanilir.
            return TimeZoneInfo.CreateCustomTimeZone("Merfit+03", TimeSpan.FromHours(3), "UTC+03", "UTC+03");
        }
    }

    private DateTime? RetryAt(int attempts, DateTime nowUtc)
    {
        if (attempts >= _options.MaxJobAttempts)
        {
            return null;
        }

        var delay = TimeSpan.FromTicks(_options.JobRetryBaseDelay.Ticks * (1L << Math.Clamp(attempts - 1, 0, 10)));
        return nowUtc + delay;
    }

    private static async Task<Dictionary<long, string?>> FirstNamesAsync(IQueryable<UserProfile> profiles, List<long> userIds, CancellationToken cancellationToken)
    {
        var rows = await profiles
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FirstName })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => (string?)g.First().FirstName);
    }

    private static NotificationDispatch NewDispatch(
        long userId, NotificationType type, NotificationDispatchSource source, string dedupeKey, string title, string body,
        string? dataJson, DateTime nowUtc, DateTime? expiresAtUtc, Action<NotificationDispatch> configure)
    {
        var dispatch = new NotificationDispatch
        {
            UserId = userId,
            Type = type,
            Source = source,
            DedupeKey = dedupeKey,
            Title = Truncate(title, 200)!,
            Body = Truncate(body, 500)!,
            DataJson = dataJson,
            ScheduledForUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            Status = NotificationDispatchStatus.Pending,
            NextAttemptAtUtc = nowUtc,
            CreatedAt = nowUtc,
        };
        configure(dispatch);
        return dispatch;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
