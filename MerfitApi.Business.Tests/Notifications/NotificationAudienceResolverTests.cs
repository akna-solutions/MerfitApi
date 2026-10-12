using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Xunit;

namespace MerfitApi.Business.Tests.Notifications;

public class NotificationAudienceResolverTests
{
    private readonly NotificationAdminTestData _data = new();
    private static DateTime Now => NotificationAdminTestData.Now;

    private List<long> Resolve(NotificationAudienceSegment segment, IReadOnlyCollection<long>? ids = null, long? campaignId = null) =>
        _data.Audience.Query(segment, Now, ids, campaignId).Select(u => u.Id).OrderBy(x => x).ToList();

    [Fact]
    public void AllUsers_ExcludesAdminsDeletedAndInactiveUsers()
    {
        var regular = _data.User();
        _data.User(role: UserRole.Admin);
        _data.User(role: UserRole.SuperAdmin);
        _data.User(deleted: true);
        _data.User(active: false);

        Assert.Equal(new[] { regular.Id }, Resolve(NotificationAudienceSegment.AllUsers));
    }

    [Fact]
    public void PlusAndFree_SplitOnActiveUnexpiredSubscription()
    {
        var plus = _data.User();
        var expired = _data.User();
        var free = _data.User();
        _data.Db.Set<Subscription>().Add(new Subscription { Id = 900, UserId = plus.Id, Status = SubscriptionStatus.Active, ExpiresAt = Now.AddDays(10) });
        _data.Db.Set<Subscription>().Add(new Subscription { Id = 901, UserId = expired.Id, Status = SubscriptionStatus.Active, ExpiresAt = Now.AddDays(-1) });

        Assert.Equal(new[] { plus.Id }, Resolve(NotificationAudienceSegment.PlusUsers));
        Assert.Equal(new[] { expired.Id, free.Id }, Resolve(NotificationAudienceSegment.FreeUsers));
    }

    [Fact]
    public void InactiveNewAndWorkoutInactive_UseDocumentedThresholds()
    {
        var neverLoggedIn = _data.User();
        neverLoggedIn.LastLoginAt = null;
        var oldLogin = _data.User(lastLoginAt: Now.AddDays(-31));
        var recentLogin = _data.User(lastLoginAt: Now.AddDays(-2), createdAt: Now.AddDays(-3));
        _data.Db.Set<WorkoutSession>().Add(new WorkoutSession { Id = 950, UserId = recentLogin.Id, CompletedAt = Now.AddDays(-5) });
        _data.Db.Set<WorkoutSession>().Add(new WorkoutSession { Id = 951, UserId = oldLogin.Id, CompletedAt = Now.AddDays(-20) });

        Assert.Equal(new[] { neverLoggedIn.Id, oldLogin.Id }, Resolve(NotificationAudienceSegment.InactiveUsers));
        Assert.Equal(new[] { recentLogin.Id }, Resolve(NotificationAudienceSegment.NewUsers));
        Assert.Equal(new[] { neverLoggedIn.Id, oldLogin.Id }, Resolve(NotificationAudienceSegment.WorkoutInactiveUsers));
    }

    [Fact]
    public void SelectedUsers_FromCampaignRows_OnlyIncludesEligibleUsers()
    {
        var a = _data.User();
        var b = _data.User();
        var admin = _data.User(role: UserRole.Admin);
        _data.User();
        var campaign = _data.Campaign(NotificationAudienceSegment.SelectedUsers, userIds: new[] { a.Id, b.Id, admin.Id });

        Assert.Equal(new[] { a.Id, b.Id }, Resolve(NotificationAudienceSegment.SelectedUsers, campaignId: campaign.Id));
    }

    [Fact]
    public async Task Validate_SingleUserNeedsExactlyOneId_AndReportsIneligibleIds()
    {
        var user = _data.User();
        var deleted = _data.User(deleted: true);

        await Assert.ThrowsAsync<AppValidationException>(() => _data.Audience.ValidateAsync(NotificationAudienceSegment.SingleUser, new long[] { user.Id, deleted.Id }));
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Audience.ValidateAsync(NotificationAudienceSegment.SelectedUsers, Array.Empty<long>()));
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Audience.ValidateAsync(NotificationAudienceSegment.AllUsers, new long[] { user.Id }));

        var invalid = await _data.Audience.ValidateAsync(NotificationAudienceSegment.SelectedUsers, new long[] { user.Id, deleted.Id, 424242 });
        Assert.Equal(new long[] { deleted.Id, 424242 }, invalid.OrderBy(x => x));
    }

    [Fact]
    public async Task Preview_CountsRecipientsAndDevices_FlagsLargeAudience_AndNeverReturnsTokens()
    {
        _data.Options.LargeAudienceThreshold = 2;
        _data.User(withDevice: true);
        _data.User();
        _data.User(withDevice: true);

        var preview = await _data.Audience.PreviewAsync(new AdminNotificationAudienceRequest { Segment = NotificationAudienceSegment.AllUsers });

        Assert.Equal(3, preview.RecipientCount);
        Assert.Equal(2, preview.WithActiveDeviceCount);
        Assert.True(preview.RequiresConfirmation);
        Assert.Equal(3, preview.Sample.Count);
        Assert.DoesNotContain(typeof(AdminNotificationAudienceUserDto).GetProperties(), p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }
}
