using MerfitApi.Business.Common.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin.Notifications;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MerfitApi.Business.Tests.Notifications;

/// <summary>Admin bildirim servisleri icin ortak bellek ici veri ve sahte bagimliliklar.</summary>
internal sealed class NotificationAdminTestData
{
    public static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    public InMemoryUnitOfWork Db { get; } = new();
    public NotificationAdminOptions Options { get; } = new() { LargeAudienceThreshold = 1000, MaterializeBatchSize = 2, MaxJobAttempts = 3 };
    public Mock<IAuditLogService> Audit { get; } = new();
    public FakeJobStore Store { get; }

    public NotificationAdminTestData()
    {
        Db.Register<ApplicationUser>().Register<UserProfile>().Register<UserDevice>().Register<Subscription>().Register<WorkoutSession>()
            .Register<NotificationTemplate>().Register<NotificationRule>().Register<NotificationRuleTemplate>()
            .Register<NotificationCampaign>().Register<NotificationCampaignUser>().Register<NotificationDispatch>()
            .Register<NotificationDeliveryAttempt>().Register<Notification>().Register<MonthlyRewardNotificationRun>()
            .Register<LeaderboardPeriod>().Register<UserReward>().Register<Reward>();
        Store = new FakeJobStore(this);
    }

    private Microsoft.Extensions.Options.IOptions<NotificationAdminOptions> Opt => Microsoft.Extensions.Options.Options.Create(Options);

    public NotificationAudienceResolver Audience => new(Db.Object, Opt);
    public AdminNotificationTemplateService Templates => new(Db.Object, Audit.Object, Opt);
    public AdminNotificationRuleService Rules => new(Db.Object, Audit.Object);
    public AdminNotificationDispatchService Dispatches => new(Db.Object, Audit.Object);
    public AdminNotificationCampaignService Campaigns => new(Db.Object, Audit.Object, Audience, Store, Dispatches, Opt);
    public AdminMonthlyRewardNotificationService Monthly => new(Db.Object, Audit.Object);
    public NotificationJobProcessor Processor => new(Db.Object, Store, Audience, Opt, NullLogger<NotificationJobProcessor>.Instance);

    private long _nextId = 1;

    public ApplicationUser User(string? firstName = "Ayşe", UserRole role = UserRole.User, bool active = true, bool deleted = false,
        DateTime? createdAt = null, DateTime? lastLoginAt = null, bool withDevice = false)
    {
        var user = new ApplicationUser
        {
            Id = _nextId++,
            Email = $"user{_nextId}@test.local",
            Role = role,
            IsActive = active,
            DeletedAt = deleted ? Now.AddDays(-1) : null,
            CreatedAt = createdAt ?? Now.AddDays(-60),
            LastLoginAt = lastLoginAt ?? Now.AddDays(-1),
        };
        Db.Set<ApplicationUser>().Add(user);
        if (firstName is not null)
        {
            Db.Set<UserProfile>().Add(new UserProfile { Id = _nextId++, UserId = user.Id, FirstName = firstName, LastName = "Test" });
        }

        if (withDevice)
        {
            Db.Set<UserDevice>().Add(new UserDevice { Id = _nextId++, UserId = user.Id, DeviceToken = $"ExponentPushToken[device{user.Id}xxxxxx]", IsActive = true });
        }

        return user;
    }

    public NotificationTemplate Template(NotificationType type, string title = "Merhaba {firstName}", string body = "Bugün harika bir gün.", bool active = true)
    {
        var template = new NotificationTemplate { Id = _nextId++, Name = $"T{_nextId}", Type = type, TitleTemplate = title, BodyTemplate = body, IsActive = active, CreatedAt = Now };
        Db.Set<NotificationTemplate>().Add(template);
        return template;
    }

    public NotificationCampaign Campaign(NotificationAudienceSegment segment, NotificationCampaignStatus status = NotificationCampaignStatus.Draft,
        string title = "Merhaba {firstName}", params long[] userIds)
    {
        var campaign = new NotificationCampaign
        {
            Id = _nextId++,
            Name = "Kampanya",
            Type = NotificationType.Announcement,
            Segment = segment,
            Status = status,
            Title = title,
            Body = "Yeni özellikler yayında.",
            DataJson = NotificationNavigation.Screen("dashboard"),
            CreatedAt = Now,
        };
        Db.Set<NotificationCampaign>().Add(campaign);
        foreach (var userId in userIds)
        {
            Db.Set<NotificationCampaignUser>().Add(new NotificationCampaignUser { Id = _nextId++, NotificationCampaignId = campaign.Id, UserId = userId });
        }

        return campaign;
    }

    public LeaderboardPeriod Period(LeaderboardPeriodType type = LeaderboardPeriodType.Monthly, bool ended = true)
    {
        var period = new LeaderboardPeriod
        {
            Id = _nextId++,
            Type = type,
            StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = ended ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) : Now.AddDays(10),
        };
        Db.Set<LeaderboardPeriod>().Add(period);
        return period;
    }

    public UserReward Reward(LeaderboardPeriod period, long userId, int rank, string title, UserRewardStatus status = UserRewardStatus.Pending)
    {
        var reward = new Reward { Id = _nextId++, Title = title, IsActive = true };
        Db.Set<Reward>().Add(reward);
        var userReward = new UserReward { Id = _nextId++, UserId = userId, RewardId = reward.Id, LeaderboardPeriodId = period.Id, Rank = rank, Status = status };
        Db.Set<UserReward>().Add(userReward);
        return userReward;
    }
}

/// <summary>
/// INotificationJobStore'un bellek ici esdegeri: gercek implementasyonun kosullu UPDATE semantigini
/// (durum + kilit sahipligi) birebir uygular. Gercek SQL davranisi PostgresNotificationJobStoreTests'te dogrulanir.
/// </summary>
internal sealed class FakeJobStore : INotificationJobStore
{
    private readonly NotificationAdminTestData _data;

    public FakeJobStore(NotificationAdminTestData data) => _data = data;

    /// <summary>Kampanya kilidi uzatilirken bir kez cagrilir (orn. araya giren iptali taklit etmek icin).</summary>
    public Action<NotificationCampaign>? BeforeExtend { get; set; }

    private List<NotificationCampaign> Campaigns => _data.Db.Set<NotificationCampaign>();
    private List<MonthlyRewardNotificationRun> Runs => _data.Db.Set<MonthlyRewardNotificationRun>();

    private static bool CampaignClaimable(NotificationCampaign c, DateTime now) =>
        (c.Status == NotificationCampaignStatus.Scheduled && c.ScheduledAtUtc <= now && (c.LockedUntilUtc == null || c.LockedUntilUtc <= now))
        || (c.Status == NotificationCampaignStatus.Processing && c.LockedUntilUtc < now);

    private static bool RunClaimable(MonthlyRewardNotificationRun r, DateTime now) =>
        (r.Status == MonthlyRewardNotificationRunStatus.Pending && (r.LockedUntilUtc == null || r.LockedUntilUtc <= now))
        || (r.Status == MonthlyRewardNotificationRunStatus.Processing && r.LockedUntilUtc < now);

    public Task<IReadOnlyList<long>> GetDueCampaignIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<long>>(Campaigns.Where(c => CampaignClaimable(c, nowUtc)).Select(c => c.Id).Take(take).ToList());

    public Task<bool> TryClaimCampaignAsync(long campaignId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default)
    {
        var c = Campaigns.Single(x => x.Id == campaignId);
        if (!CampaignClaimable(c, nowUtc)) return Task.FromResult(false);
        c.Status = NotificationCampaignStatus.Processing;
        c.LockToken = lockToken;
        c.LockedUntilUtc = lockedUntilUtc;
        c.StartedAtUtc ??= nowUtc;
        c.AttemptCount++;
        return Task.FromResult(true);
    }

    public Task<bool> ExtendCampaignLockAsync(long campaignId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default)
    {
        var c = Campaigns.Single(x => x.Id == campaignId);
        BeforeExtend?.Invoke(c);
        BeforeExtend = null;
        if (c.Status != NotificationCampaignStatus.Processing || c.LockToken != lockToken) return Task.FromResult(false);
        c.LockedUntilUtc = lockedUntilUtc;
        return Task.FromResult(true);
    }

    public Task<bool> CompleteCampaignMaterializationAsync(long campaignId, Guid lockToken, int recipientCount, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var c = Campaigns.Single(x => x.Id == campaignId);
        if (c.Status != NotificationCampaignStatus.Processing || c.LockToken != lockToken) return Task.FromResult(false);
        c.Status = NotificationCampaignStatus.Dispatching;
        c.RecipientCount = recipientCount;
        c.LockToken = null;
        c.LockedUntilUtc = null;
        c.LastError = null;
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseCampaignAfterErrorAsync(long campaignId, Guid lockToken, string error, DateTime? retryAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var c = Campaigns.Single(x => x.Id == campaignId);
        if (c.Status != NotificationCampaignStatus.Processing || c.LockToken != lockToken) return Task.FromResult(false);
        c.Status = retryAtUtc.HasValue ? NotificationCampaignStatus.Scheduled : NotificationCampaignStatus.Failed;
        c.LockToken = null;
        c.LockedUntilUtc = retryAtUtc;
        c.LastError = error;
        return Task.FromResult(true);
    }

    public Task<bool> TryChangeCampaignStatusAsync(long campaignId, NotificationCampaignStatus from, NotificationCampaignStatus to, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var c = Campaigns.Single(x => x.Id == campaignId);
        if (c.Status != from) return Task.FromResult(false);
        c.Status = to;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<long>> GetDueMonthlyRunIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default)
    {
        var periods = _data.Db.Set<LeaderboardPeriod>();
        return Task.FromResult<IReadOnlyList<long>>(Runs
            .Where(r => RunClaimable(r, nowUtc) && periods.Any(p => p.Id == r.LeaderboardPeriodId && p.EndDate <= nowUtc))
            .Select(r => r.Id).Take(take).ToList());
    }

    public Task<bool> TryClaimMonthlyRunAsync(long runId, Guid lockToken, DateTime nowUtc, DateTime lockedUntilUtc, CancellationToken cancellationToken = default)
    {
        var r = Runs.Single(x => x.Id == runId);
        if (!RunClaimable(r, nowUtc)) return Task.FromResult(false);
        r.Status = MonthlyRewardNotificationRunStatus.Processing;
        r.LockToken = lockToken;
        r.LockedUntilUtc = lockedUntilUtc;
        r.StartedAtUtc ??= nowUtc;
        r.AttemptCount++;
        return Task.FromResult(true);
    }

    public Task<bool> CompleteMonthlyRunAsync(long runId, Guid lockToken, int recipientCount, int createdCount, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var r = Runs.Single(x => x.Id == runId);
        if (r.Status != MonthlyRewardNotificationRunStatus.Processing || r.LockToken != lockToken) return Task.FromResult(false);
        r.Status = MonthlyRewardNotificationRunStatus.Completed;
        r.RecipientCount = recipientCount;
        r.CreatedDispatchCount = createdCount;
        r.CompletedAtUtc = nowUtc;
        r.LockToken = null;
        r.LockedUntilUtc = null;
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseMonthlyRunAfterErrorAsync(long runId, Guid lockToken, string error, DateTime? retryAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var r = Runs.Single(x => x.Id == runId);
        if (r.Status != MonthlyRewardNotificationRunStatus.Processing || r.LockToken != lockToken) return Task.FromResult(false);
        r.Status = retryAtUtc.HasValue ? MonthlyRewardNotificationRunStatus.Pending : MonthlyRewardNotificationRunStatus.Failed;
        r.LockToken = null;
        r.LockedUntilUtc = retryAtUtc;
        r.LastError = error;
        return Task.FromResult(true);
    }
}
