using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Xunit;

namespace MerfitApi.Business.Tests.Notifications;

public class NotificationCampaignServiceTests
{
    private readonly NotificationAdminTestData _data = new();

    private static AdminUpsertNotificationCampaignRequest Request(NotificationAudienceSegment segment, params long[] userIds) => new()
    {
        Name = "Ekim duyurusu",
        Type = NotificationType.Announcement,
        Segment = segment,
        UserIds = userIds.Length > 0 ? userIds.ToList() : null,
        Title = "Merhaba {firstName}",
        Body = "Yeni antrenman programları yayında.",
        DataJson = "{\"screen\":\"workouts\"}",
    };

    [Fact]
    public async Task Create_StoresDraftWithSelectedUsers_AndRejectsIneligibleOrInvalidContent()
    {
        var a = _data.User();
        var b = _data.User();
        var admin = _data.User(role: UserRole.Admin);

        var campaign = await _data.Campaigns.CreateAsync(Request(NotificationAudienceSegment.SelectedUsers, a.Id, b.Id), 1, null, null);
        Assert.Equal("Draft", campaign.Status);
        Assert.Equal(new[] { a.Id, b.Id }, campaign.UserIds);

        await Assert.ThrowsAsync<AppValidationException>(() => _data.Campaigns.CreateAsync(Request(NotificationAudienceSegment.SelectedUsers, a.Id, admin.Id), 1, null, null));

        var wrongType = Request(NotificationAudienceSegment.AllUsers);
        wrongType.Type = NotificationType.Reward;
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Campaigns.CreateAsync(wrongType, 1, null, null));

        var badPlaceholder = Request(NotificationAudienceSegment.AllUsers);
        badPlaceholder.Body = "{rewardTitle} kazandın";
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Campaigns.CreateAsync(badPlaceholder, 1, null, null));
    }

    [Fact]
    public async Task ShopCampaign_MustTargetShopScreens_AndListsSeparatelyByApp()
    {
        var shop = Request(NotificationAudienceSegment.AllUsers);
        shop.Type = NotificationType.StorePromotion;
        shop.DataJson = "{\"screen\":\"storeProduct\",\"productId\":\"12\"}";
        var created = await _data.Campaigns.CreateAsync(shop, 1, null, null);
        Assert.Equal("MbShop", created.App);

        var shopToFitScreen = Request(NotificationAudienceSegment.AllUsers);
        shopToFitScreen.Type = NotificationType.StorePromotion;
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Campaigns.CreateAsync(shopToFitScreen, 1, null, null));

        var fitToShopScreen = Request(NotificationAudienceSegment.AllUsers);
        fitToShopScreen.DataJson = "{\"screen\":\"store\"}";
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Campaigns.CreateAsync(fitToShopScreen, 1, null, null));

        var fit = await _data.Campaigns.CreateAsync(Request(NotificationAudienceSegment.AllUsers), 1, null, null);
        Assert.Equal("MbFit", fit.App);

        var shopList = await _data.Campaigns.GetListAsync(new AdminNotificationCampaignListRequest { App = NotificationApp.MbShop });
        var fitList = await _data.Campaigns.GetListAsync(new AdminNotificationCampaignListRequest { App = NotificationApp.MbFit });
        Assert.Equal(new[] { created.Id }, shopList.Items.Select(c => c.Id));
        Assert.Equal(new[] { fit.Id }, fitList.Items.Select(c => c.Id));
    }

    [Fact]
    public async Task Create_FromTemplate_CopiesTextAndData()
    {
        var template = _data.Template(NotificationType.Announcement, "Duyuru {firstName}", "Uygulama güncellendi.");
        template.DataJson = "{\"screen\":\"dashboard\"}";
        var request = Request(NotificationAudienceSegment.AllUsers);
        request.NotificationTemplateId = template.Id;
        request.Title = null;
        request.Body = null;
        request.DataJson = null;

        var campaign = await _data.Campaigns.CreateAsync(request, 1, null, null);

        Assert.Equal("Duyuru {firstName}", campaign.Title);
        Assert.Equal("Uygulama güncellendi.", campaign.Body);
        Assert.Equal("{\"screen\":\"dashboard\"}", campaign.DataJson);
    }

    [Fact]
    public async Task Schedule_LargeAudienceRequiresExplicitConfirmation()
    {
        _data.Options.LargeAudienceThreshold = 2;
        for (var i = 0; i < 3; i++) _data.User();
        var campaign = await _data.Campaigns.CreateAsync(Request(NotificationAudienceSegment.AllUsers), 1, null, null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _data.Campaigns.ScheduleAsync(campaign.Id, new AdminScheduleNotificationCampaignRequest(), 1, null, null));
        Assert.Contains("3", ex.Message);
        Assert.Equal(NotificationCampaignStatus.Draft, _data.Db.Set<NotificationCampaign>().Single().Status);

        var scheduled = await _data.Campaigns.ScheduleAsync(campaign.Id, new AdminScheduleNotificationCampaignRequest { ConfirmLargeAudience = true }, 1, null, null);
        Assert.Equal("Scheduled", scheduled.Status);
        Assert.NotNull(scheduled.ScheduledAtUtc);
        // HTTP isteginde alici kaydi yazilmaz; isleyici yazar.
        Assert.Empty(_data.Db.Set<NotificationDispatch>());
    }

    [Fact]
    public async Task Schedule_RejectsEmptyAudiencePastTimeAndNonDraft()
    {
        var campaign = await _data.Campaigns.CreateAsync(Request(NotificationAudienceSegment.AllUsers), 1, null, null);
        await Assert.ThrowsAsync<ConflictException>(() => _data.Campaigns.ScheduleAsync(campaign.Id, new AdminScheduleNotificationCampaignRequest(), 1, null, null));

        _data.User();
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Campaigns.ScheduleAsync(campaign.Id,
            new AdminScheduleNotificationCampaignRequest { ScheduledAtUtc = DateTime.UtcNow.AddHours(-1) }, 1, null, null));

        await _data.Campaigns.ScheduleAsync(campaign.Id, new AdminScheduleNotificationCampaignRequest { ScheduledAtUtc = DateTime.UtcNow.AddDays(1) }, 1, null, null);
        await Assert.ThrowsAsync<ConflictException>(() => _data.Campaigns.ScheduleAsync(campaign.Id, new AdminScheduleNotificationCampaignRequest(), 1, null, null));
        await Assert.ThrowsAsync<ConflictException>(() => _data.Campaigns.UpdateAsync(campaign.Id, Request(NotificationAudienceSegment.AllUsers), 1, null, null));
    }

    [Fact]
    public async Task UnscheduleAndCancel_FollowStatusFlow()
    {
        _data.User();
        var campaign = await _data.Campaigns.CreateAsync(Request(NotificationAudienceSegment.AllUsers), 1, null, null);
        await _data.Campaigns.ScheduleAsync(campaign.Id, new AdminScheduleNotificationCampaignRequest { ScheduledAtUtc = DateTime.UtcNow.AddDays(1) }, 1, null, null);

        Assert.Equal("Draft", (await _data.Campaigns.UnscheduleAsync(campaign.Id, 1, null, null)).Status);
        await Assert.ThrowsAsync<ConflictException>(() => _data.Campaigns.UnscheduleAsync(campaign.Id, 1, null, null));

        var cancelled = await _data.Campaigns.CancelAsync(campaign.Id, 1, null, null);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAtUtc);
        await Assert.ThrowsAsync<ConflictException>(() => _data.Campaigns.CancelAsync(campaign.Id, 1, null, null));
    }

    [Fact]
    public async Task Cancel_CancelsQueuedDispatchesImmediately_AndLeavesProcessedOnesUntouched()
    {
        var campaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Dispatching);
        var pending = AddDispatch(campaign.Id, 1, NotificationDispatchStatus.Pending, null);
        var sent = AddDispatch(campaign.Id, 2, NotificationDispatchStatus.Sent, null);
        var otherCampaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Dispatching);
        var otherPending = AddDispatch(otherCampaign.Id, 3, NotificationDispatchStatus.Pending, null);

        await _data.Campaigns.CancelAsync(campaign.Id, 1, null, null);

        Assert.Equal(NotificationDispatchStatus.Cancelled, pending.Status);
        Assert.Equal("CampaignCancelled", pending.StatusReason);
        Assert.NotNull(pending.ProcessedAtUtc);
        Assert.Equal(NotificationDispatchStatus.Sent, sent.Status);
        Assert.Equal(NotificationDispatchStatus.Pending, otherPending.Status);
        Assert.Equal(0, (await _data.Campaigns.GetStatsAsync(campaign.Id)).QueuedCount);
    }

    [Fact]
    public async Task RetryFailed_RequeuesOnlyRetryableFailures_AndReopensCompletedCampaign()
    {
        var user = _data.User();
        var campaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Completed);
        campaign.CompletedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var failed = AddDispatch(campaign.Id, user.Id, NotificationDispatchStatus.Failed, expires: null);
        var expired = AddDispatch(campaign.Id, user.Id + 1000, NotificationDispatchStatus.Failed, expires: DateTime.UtcNow.AddMinutes(-1));
        var sent = AddDispatch(campaign.Id, user.Id + 2000, NotificationDispatchStatus.Sent, expires: null);

        var result = await _data.Campaigns.RetryFailedAsync(campaign.Id, 1, null, null);

        Assert.Equal(1, result.RetriedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(NotificationDispatchStatus.Pending, failed.Status);
        Assert.Equal(0, failed.AttemptCount);
        Assert.Null(failed.LastError);
        Assert.Equal(NotificationDispatchStatus.Failed, expired.Status);
        Assert.Equal(NotificationDispatchStatus.Sent, sent.Status);
        Assert.Equal(NotificationCampaignStatus.Dispatching, campaign.Status);
        Assert.Null(campaign.CompletedAtUtc);
    }

    [Fact]
    public async Task Stats_GroupsDispatchAndDeliveryStatuses()
    {
        var campaign = _data.Campaign(NotificationAudienceSegment.AllUsers, NotificationCampaignStatus.Dispatching);
        var d1 = AddDispatch(campaign.Id, 1, NotificationDispatchStatus.Sent, null);
        d1.NotificationId = 77;
        AddDispatch(campaign.Id, 2, NotificationDispatchStatus.Pending, null);
        AddDispatch(campaign.Id, 3, NotificationDispatchStatus.Failed, null);
        _data.Db.Set<NotificationDeliveryAttempt>().Add(new NotificationDeliveryAttempt { Id = 5000, NotificationDispatchId = d1.Id, Status = NotificationDeliveryStatus.ReceiptOk });
        _data.Db.Set<Notification>().Add(new Notification { Id = 77, UserId = 1, IsRead = true });

        var stats = await _data.Campaigns.GetStatsAsync(campaign.Id);

        Assert.Equal(1, stats.DispatchStatusCounts["Sent"]);
        Assert.Equal(1, stats.DispatchStatusCounts["Failed"]);
        Assert.Equal(1, stats.QueuedCount);
        Assert.Equal(1, stats.DeliveryStatusCounts["ReceiptOk"]);
        Assert.Equal(1, stats.InboxCount);
        Assert.Equal(1, stats.ReadCount);
    }

    private long _dispatchId = 10_000;

    private NotificationDispatch AddDispatch(long campaignId, long userId, NotificationDispatchStatus status, DateTime? expires)
    {
        var dispatch = new NotificationDispatch
        {
            Id = ++_dispatchId,
            UserId = userId,
            Source = NotificationDispatchSource.Campaign,
            Type = NotificationType.Announcement,
            NotificationCampaignId = campaignId,
            DedupeKey = $"campaign:{campaignId}:{userId}",
            Status = status,
            AttemptCount = status == NotificationDispatchStatus.Failed ? 5 : 1,
            LastError = status == NotificationDispatchStatus.Failed ? "RetriesExhausted" : null,
            ExpiresAtUtc = expires,
        };
        _data.Db.Set<NotificationDispatch>().Add(dispatch);
        return dispatch;
    }
}
