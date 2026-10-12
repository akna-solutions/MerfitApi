using MerfitApi.Business.Services.Admin.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Xunit;

namespace MerfitApi.Business.Tests.Notifications;

public class NotificationJobProcessorTests
{
    private readonly NotificationAdminTestData _data = new();
    private static DateTime Now => NotificationAdminTestData.Now;

    private List<NotificationDispatch> Dispatches => _data.Db.Set<NotificationDispatch>();

    [Fact]
    public async Task DueCampaign_IsMaterializedInBatches_OneDispatchPerEligibleUser()
    {
        var ayse = _data.User("Ayşe");
        var mehmet = _data.User("Mehmet");
        var noProfile = _data.User(firstName: null);
        _data.User(role: UserRole.Admin);
        var campaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Scheduled);
        campaign.ScheduledAtUtc = Now.AddMinutes(-1);
        campaign.ExpiresAtUtc = Now.AddDays(1);

        var processed = await _data.Processor.ProcessDueCampaignsAsync(Now);

        Assert.Equal(1, processed);
        Assert.Equal(NotificationCampaignStatus.Dispatching, campaign.Status);
        Assert.Equal(3, campaign.RecipientCount);
        Assert.Null(campaign.LockToken);
        Assert.Equal(new[] { ayse.Id, mehmet.Id, noProfile.Id }, Dispatches.Select(d => d.UserId).OrderBy(x => x));
        Assert.All(Dispatches, d =>
        {
            Assert.Equal(NotificationDispatchSource.Campaign, d.Source);
            Assert.Equal(NotificationDispatchStatus.Pending, d.Status);
            Assert.Equal($"campaign:{campaign.Id}:{d.UserId}", d.DedupeKey);
            Assert.Equal(campaign.ExpiresAtUtc, d.ExpiresAtUtc);
            Assert.Equal(campaign.DataJson, d.DataJson);
        });
        Assert.Equal("Merhaba Ayşe", Dispatches.Single(d => d.UserId == ayse.Id).Title);
        Assert.Equal("Merhaba", Dispatches.Single(d => d.UserId == noProfile.Id).Title);
    }

    [Fact]
    public async Task CampaignNotYetDue_OrDraft_IsNotProcessed()
    {
        _data.User();
        var future = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Scheduled);
        future.ScheduledAtUtc = Now.AddHours(1);
        _data.Campaign(NotificationAudienceSegment.AllUsers);

        Assert.Equal(0, await _data.Processor.ProcessDueCampaignsAsync(Now));
        Assert.Empty(Dispatches);
    }

    [Fact]
    public async Task RestartedCampaign_DoesNotDuplicateRecipients()
    {
        var a = _data.User();
        var b = _data.User();
        var campaign = _data.Campaign(NotificationAudienceSegment.SelectedUsers, NotificationCampaignStatus.Scheduled, userIds: new[] { a.Id, b.Id });
        campaign.ScheduledAtUtc = Now.AddMinutes(-1);
        // Onceki (coken) bir denemeden kalan kayit.
        Dispatches.Add(new NotificationDispatch { Id = 9000, UserId = a.Id, NotificationCampaignId = campaign.Id, DedupeKey = $"campaign:{campaign.Id}:{a.Id}" });

        await _data.Processor.ProcessDueCampaignsAsync(Now);

        Assert.Equal(2, Dispatches.Count);
        Assert.Single(Dispatches, d => d.UserId == b.Id);
        Assert.Equal(2, campaign.RecipientCount);
    }

    [Fact]
    public async Task CampaignCancelledDuringMaterialization_StopsAndStaysCancelled()
    {
        for (var i = 0; i < 5; i++) _data.User();
        var campaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Scheduled);
        campaign.ScheduledAtUtc = Now.AddMinutes(-1);
        _data.Store.BeforeExtend = c => c.Status = NotificationCampaignStatus.Cancelled;

        await _data.Processor.ProcessDueCampaignsAsync(Now);

        Assert.Equal(NotificationCampaignStatus.Cancelled, campaign.Status);
        Assert.Equal(2, Dispatches.Count); // yalnizca ilk parti (MaterializeBatchSize = 2)
        Assert.Null(campaign.RecipientCount);
    }

    [Fact]
    public async Task ExpiredCampaign_FailsWithoutQueueingAnything()
    {
        _data.User();
        var campaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Scheduled);
        campaign.ScheduledAtUtc = Now.AddHours(-3);
        campaign.ExpiresAtUtc = Now.AddHours(-1);

        await _data.Processor.ProcessDueCampaignsAsync(Now);

        Assert.Equal(NotificationCampaignStatus.Failed, campaign.Status);
        Assert.Empty(Dispatches);
    }

    [Fact]
    public async Task FailingCampaign_IsRetriedWithBackoff_ThenMarkedFailedAfterMaxAttempts()
    {
        _data.User();
        var campaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Scheduled);
        campaign.ScheduledAtUtc = Now.AddMinutes(-1);
        campaign.Segment = (NotificationAudienceSegment)999; // cozulemeyen segment -> hata

        await _data.Processor.ProcessDueCampaignsAsync(Now);
        Assert.Equal(NotificationCampaignStatus.Scheduled, campaign.Status);
        Assert.Equal(1, campaign.AttemptCount);
        Assert.NotNull(campaign.LastError);
        Assert.True(campaign.LockedUntilUtc > Now);
        Assert.Equal(0, await _data.Processor.ProcessDueCampaignsAsync(Now)); // geri cekilme suresi dolmadi

        await _data.Processor.ProcessDueCampaignsAsync(campaign.LockedUntilUtc!.Value);
        await _data.Processor.ProcessDueCampaignsAsync(campaign.LockedUntilUtc!.Value.AddDays(1));

        Assert.Equal(NotificationCampaignStatus.Failed, campaign.Status);
        Assert.Equal(_data.Options.MaxJobAttempts, campaign.AttemptCount);
    }

    private MonthlyRewardNotificationRun Run(LeaderboardPeriod period)
    {
        var run = new MonthlyRewardNotificationRun { Id = 7000 + period.Id, LeaderboardPeriodId = period.Id };
        _data.Db.Set<MonthlyRewardNotificationRun>().Add(run);
        return run;
    }

    [Fact]
    public async Task MonthlyRun_SendsOneRewardNotificationPerWinner_AfterPeriodEnd()
    {
        var period = _data.Period();
        var first = _data.User("Zeynep");
        var second = _data.User("Can");
        var cancelled = _data.User("İptal");
        var inactive = _data.User("Pasif", active: false);
        _data.Reward(period, first.Id, 1, "Merfit Plus 3 Ay");
        _data.Reward(period, first.Id, 1, "Rozet");
        _data.Reward(period, second.Id, 2, "Merfit Plus 1 Ay");
        _data.Reward(period, cancelled.Id, 3, "Hediye", UserRewardStatus.Cancelled);
        _data.Reward(period, inactive.Id, 4, "Hediye");
        var run = Run(period);

        Assert.Equal(1, await _data.Processor.ProcessDueMonthlyRunsAsync(Now));

        Assert.Equal(MonthlyRewardNotificationRunStatus.Completed, run.Status);
        Assert.Equal(2, run.RecipientCount);
        Assert.Equal(2, run.CreatedDispatchCount);
        var zeynep = Dispatches.Single(d => d.UserId == first.Id);
        Assert.Equal(NotificationType.Reward, zeynep.Type);
        Assert.Equal(NotificationDispatchSource.MonthlyReward, zeynep.Source);
        Assert.Equal($"reward:{period.Id}:{first.Id}", zeynep.DedupeKey);
        Assert.Equal(period.Id, zeynep.LeaderboardPeriodId);
        Assert.Equal("Tebrikler Zeynep!", zeynep.Title);
        Assert.Equal("Eylül 2026 MB FIT Puanları sıralamasında 1. oldun ve Merfit Plus 3 Ay + Rozet kazandın.", zeynep.Body);
        Assert.Contains("leaderboard", zeynep.DataJson);
    }

    [Fact]
    public async Task MonthlyRun_WaitsForPeriodEnd()
    {
        var period = _data.Period(ended: false);
        var user = _data.User();
        _data.Reward(period, user.Id, 1, "Hediye");
        var run = Run(period);

        Assert.Equal(0, await _data.Processor.ProcessDueMonthlyRunsAsync(Now));
        Assert.Equal(MonthlyRewardNotificationRunStatus.Pending, run.Status);
        Assert.Empty(Dispatches);
    }

    [Fact]
    public async Task DuplicateMonthlyProcessing_DoesNotSendTwice_ButNotifiesNewWinners()
    {
        var period = _data.Period();
        var first = _data.User();
        _data.Reward(period, first.Id, 1, "Hediye");
        var run = Run(period);
        await _data.Processor.ProcessDueMonthlyRunsAsync(Now);

        // Ikinci kez calistirma (admin tetiklemesi / yeniden dagitim): mevcut kazanana tekrar gonderilmez.
        await _data.Monthly.TriggerAsync(period.Id, 1, null, null);
        Assert.Equal(MonthlyRewardNotificationRunStatus.Pending, run.Status);
        var second = _data.User();
        _data.Reward(period, second.Id, 2, "Hediye 2");
        await _data.Processor.ProcessDueMonthlyRunsAsync(Now);

        Assert.Equal(2, Dispatches.Count);
        Assert.Single(Dispatches, d => d.UserId == first.Id);
        Assert.Equal(1, run.CreatedDispatchCount);
        Assert.Equal(2, run.RecipientCount);
        Assert.Single(_data.Db.Set<MonthlyRewardNotificationRun>());
    }

    [Fact]
    public async Task MonthlyRun_UsesActiveRewardTemplate()
    {
        var template = _data.Template(NotificationType.Reward, "{firstName}, ödülün hazır", "{periodLabel}: {rank}. sıra, {rewardTitle}");
        template.DataJson = "{\"screen\":\"profile\"}";
        var period = _data.Period();
        var user = _data.User("Elif");
        _data.Reward(period, user.Id, 3, "Kupa");
        Run(period);

        await _data.Processor.ProcessDueMonthlyRunsAsync(Now);

        var dispatch = Dispatches.Single();
        Assert.Equal("Elif, ödülün hazır", dispatch.Title);
        Assert.Equal("Eylül 2026: 3. sıra, Kupa", dispatch.Body);
        Assert.Equal(template.Id, dispatch.NotificationTemplateId);
        Assert.Equal("{\"screen\":\"profile\"}", dispatch.DataJson);
    }

    [Fact]
    public async Task EnqueueAfterDistribution_IsIdempotent_AndIgnoresNonMonthlyPeriods()
    {
        var weekly = _data.Period(LeaderboardPeriodType.Weekly);
        await _data.Monthly.EnqueueAfterDistributionAsync(weekly, 1);
        Assert.Empty(_data.Db.Set<MonthlyRewardNotificationRun>());

        var monthly = _data.Period();
        await _data.Monthly.EnqueueAfterDistributionAsync(monthly, 1);
        await _data.Monthly.EnqueueAfterDistributionAsync(monthly, 1);
        var run = Assert.Single(_data.Db.Set<MonthlyRewardNotificationRun>());
        Assert.Equal(MonthlyRewardNotificationRunStatus.Pending, run.Status);

        run.Status = MonthlyRewardNotificationRunStatus.Completed;
        run.AttemptCount = 1;
        await _data.Monthly.EnqueueAfterDistributionAsync(monthly, 1);
        Assert.Equal(MonthlyRewardNotificationRunStatus.Pending, run.Status);
        Assert.Equal(0, run.AttemptCount);
    }

    [Fact]
    public async Task Trigger_RequiresDistributedRewards_AndRejectsRunningJob()
    {
        var period = _data.Period();
        await Assert.ThrowsAsync<ConflictException>(() => _data.Monthly.TriggerAsync(period.Id, 1, null, null));

        var user = _data.User();
        _data.Reward(period, user.Id, 1, "Hediye");
        var run = Run(period);
        run.Status = MonthlyRewardNotificationRunStatus.Processing;
        run.LockedUntilUtc = DateTime.UtcNow.AddMinutes(5);
        await Assert.ThrowsAsync<ConflictException>(() => _data.Monthly.TriggerAsync(period.Id, 1, null, null));

        var weekly = _data.Period(LeaderboardPeriodType.Weekly);
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Monthly.TriggerAsync(weekly.Id, 1, null, null));
    }

    [Fact]
    public void PeriodLabel_UsesLocalMonthOfPeriod()
    {
        var period = new LeaderboardPeriod
        {
            StartDate = new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc),
        };
        Assert.Equal("Ekim 2026", NotificationJobProcessor.PeriodLabel(period, "Europe/Istanbul"));
        Assert.Equal("Ekim 2026", NotificationJobProcessor.PeriodLabel(period, "Invalid/Zone"));
    }
}
