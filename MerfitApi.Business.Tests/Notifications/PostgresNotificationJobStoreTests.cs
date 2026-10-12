using MerfitApi.Business.Services.Admin.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitApi.Business.Tests.Notifications;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES ortam degiskeni (bildirim migration'i uygulanmis bir test veritabaninin
/// baglanti dizesi) tanimliysa calisir; aksi halde atlanir. Kampanya / aylik odul islerinin kosullu UPDATE
/// tabanli kilitlerini ve donem basina tek is kuralini gercek PostgreSQL'de dogrular.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresNotificationJobStoreTests.ConnectionVariable)))
        {
            Skip = $"{PostgresNotificationJobStoreTests.ConnectionVariable} tanımlı değil.";
        }
    }
}

public class PostgresNotificationJobStoreTests
{
    public const string ConnectionVariable = "MERFIT_TEST_POSTGRES";

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(ConnectionVariable)).Options);

    private static async Task<long> CreateDueCampaignAsync(DateTime now)
    {
        await using var db = CreateContext();
        var campaign = new NotificationCampaign
        {
            Name = "pg-" + Guid.NewGuid().ToString("N"), Type = NotificationType.Announcement, Status = NotificationCampaignStatus.Scheduled,
            Segment = NotificationAudienceSegment.AllUsers, Title = "t", Body = "b", ScheduledAtUtc = now.AddMinutes(-1), CreatedAt = now,
        };
        db.NotificationCampaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    [PostgresFact]
    public async Task ConcurrentCampaignClaims_OnlyOneInstanceWins()
    {
        var now = DateTime.UtcNow;
        var id = await CreateDueCampaignAsync(now);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = CreateContext();
            var store = new NotificationJobStore(new UnitOfWork(db));
            return await store.TryClaimCampaignAsync(id, Guid.NewGuid(), now, now.AddMinutes(10));
        }));

        Assert.Equal(1, results.Count(x => x));
        await using var check = CreateContext();
        var campaign = await check.NotificationCampaigns.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(NotificationCampaignStatus.Processing, campaign.Status);
        Assert.Equal(1, campaign.AttemptCount);
        Assert.NotNull(campaign.StartedAtUtc);
    }

    [PostgresFact]
    public async Task LockOwnership_IsEnforced_AndExpiredLockCanBeReclaimed()
    {
        var now = DateTime.UtcNow;
        var id = await CreateDueCampaignAsync(now);
        var owner = Guid.NewGuid();

        await using var db = CreateContext();
        var store = new NotificationJobStore(new UnitOfWork(db));
        Assert.True(await store.TryClaimCampaignAsync(id, owner, now, now.AddMinutes(10)));

        // Isleyici kampanyayi aldiktan sonra "taslaga al" yarisi kaybeder.
        Assert.False(await store.TryChangeCampaignStatusAsync(id, NotificationCampaignStatus.Scheduled, NotificationCampaignStatus.Draft, now));
        Assert.False(await store.ExtendCampaignLockAsync(id, Guid.NewGuid(), now, now.AddMinutes(20)));
        Assert.False(await store.TryClaimCampaignAsync(id, Guid.NewGuid(), now.AddMinutes(5), now.AddMinutes(15)));

        // Kilit suresi gecince (coken ornek) is baska bir ornege gecer; eski sahip artik tamamlayamaz.
        var newOwner = Guid.NewGuid();
        Assert.True(await store.TryClaimCampaignAsync(id, newOwner, now.AddMinutes(11), now.AddMinutes(21)));
        Assert.False(await store.CompleteCampaignMaterializationAsync(id, owner, 5, now));
        Assert.True(await store.CompleteCampaignMaterializationAsync(id, newOwner, 5, now));

        var campaign = await db.NotificationCampaigns.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(NotificationCampaignStatus.Dispatching, campaign.Status);
        Assert.Equal(5, campaign.RecipientCount);
        Assert.Null(campaign.LockToken);
        Assert.Equal(2, campaign.AttemptCount);
    }

    [PostgresFact]
    public async Task MonthlyRun_IsUniquePerPeriod_AndOnlyDueAfterPeriodEnd()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        var period = new LeaderboardPeriod { Type = LeaderboardPeriodType.Monthly, StartDate = now.AddDays(-20), EndDate = now.AddDays(10), CreatedAt = now };
        db.LeaderboardPeriods.Add(period);
        await db.SaveChangesAsync();
        db.MonthlyRewardNotificationRuns.Add(new MonthlyRewardNotificationRun { LeaderboardPeriodId = period.Id, CreatedAt = now });
        await db.SaveChangesAsync();

        await using (var other = CreateContext())
        {
            other.MonthlyRewardNotificationRuns.Add(new MonthlyRewardNotificationRun { LeaderboardPeriodId = period.Id, CreatedAt = now });
            await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        }

        var store = new NotificationJobStore(new UnitOfWork(db));
        var runId = await db.MonthlyRewardNotificationRuns.Where(r => r.LeaderboardPeriodId == period.Id).Select(r => r.Id).SingleAsync();
        Assert.DoesNotContain(runId, await store.GetDueMonthlyRunIdsAsync(now, 1000));
        Assert.Contains(runId, await store.GetDueMonthlyRunIdsAsync(now.AddDays(11), 1000));
    }
}
