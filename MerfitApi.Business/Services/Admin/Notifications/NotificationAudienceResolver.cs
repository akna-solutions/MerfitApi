using MerfitApi.Business.Common.Notifications;
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
/// Kampanya kitlelerini cozer. Segment esikleri onceki AdminNotificationService ile aynidir:
/// Plus = aktif ve suresi dolmamis aboneligi olan; Inactive = 30 gundur giris yapmamis;
/// New = son 7 gunde kayit olmus; WorkoutInactive = son 14 gunde tamamlanmis antrenmani olmayan.
/// Tum kitleler yalnizca aktif, silinmemis ve "User" rolundeki kullanicilari icerir (admin hesaplari haric).
/// </summary>
public class NotificationAudienceResolver : INotificationAudienceResolver
{
    public const int InactiveDays = 30;
    public const int NewUserDays = 7;
    public const int WorkoutInactiveDays = 14;
    private const int SampleSize = 10;

    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationAdminOptions _options;

    public NotificationAudienceResolver(IUnitOfWork unitOfWork, IOptions<NotificationAdminOptions> options)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public IQueryable<ApplicationUser> Query(NotificationAudienceSegment segment, DateTime nowUtc, IReadOnlyCollection<long>? userIds = null, long? campaignId = null)
    {
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.DeletedAt == null && u.IsActive && u.Role == UserRole.User);
        var subscriptions = _unitOfWork.Repository<Subscription>().GetQueryable();
        var sessions = _unitOfWork.Repository<WorkoutSession>().GetQueryable();

        switch (segment)
        {
            case NotificationAudienceSegment.AllUsers:
                return users;

            case NotificationAudienceSegment.PlusUsers:
                return users.Where(u => subscriptions.Any(s => s.UserId == u.Id
                    && s.Status == SubscriptionStatus.Active
                    && (s.ExpiresAt == null || s.ExpiresAt > nowUtc)));

            case NotificationAudienceSegment.FreeUsers:
                return users.Where(u => !subscriptions.Any(s => s.UserId == u.Id
                    && s.Status == SubscriptionStatus.Active
                    && (s.ExpiresAt == null || s.ExpiresAt > nowUtc)));

            case NotificationAudienceSegment.InactiveUsers:
                var inactiveSince = nowUtc.AddDays(-InactiveDays);
                return users.Where(u => u.LastLoginAt == null || u.LastLoginAt < inactiveSince);

            case NotificationAudienceSegment.NewUsers:
                var newSince = nowUtc.AddDays(-NewUserDays);
                return users.Where(u => u.CreatedAt >= newSince);

            case NotificationAudienceSegment.WorkoutInactiveUsers:
                var workoutSince = nowUtc.AddDays(-WorkoutInactiveDays);
                return users.Where(u => !sessions.Any(s => s.UserId == u.Id && s.CompletedAt != null && s.CompletedAt >= workoutSince));

            case NotificationAudienceSegment.SingleUser:
            case NotificationAudienceSegment.SelectedUsers:
                if (campaignId.HasValue)
                {
                    var id = campaignId.Value;
                    var selected = _unitOfWork.Repository<NotificationCampaignUser>().GetQueryable()
                        .Where(x => x.NotificationCampaignId == id);
                    return users.Where(u => selected.Any(x => x.UserId == u.Id));
                }

                var ids = (userIds ?? Array.Empty<long>()).Distinct().ToList();
                return users.Where(u => ids.Contains(u.Id));

            default:
                throw new AppValidationException("segment", "Gecersiz segment.");
        }
    }

    public async Task<IReadOnlyList<long>> ValidateAsync(NotificationAudienceSegment segment, IReadOnlyCollection<long>? userIds, CancellationToken cancellationToken = default)
    {
        var ids = (userIds ?? Array.Empty<long>()).Distinct().ToList();
        switch (segment)
        {
            case NotificationAudienceSegment.SingleUser when ids.Count != 1:
                throw new AppValidationException("userIds", "Tek kullanici kitlesi icin tam olarak bir kullanici secilmelidir.");
            case NotificationAudienceSegment.SelectedUsers when ids.Count == 0:
                throw new AppValidationException("userIds", "En az bir kullanici secilmelidir.");
            case NotificationAudienceSegment.SelectedUsers when ids.Count > _options.MaxSelectedUsers:
                throw new AppValidationException("userIds", $"En fazla {_options.MaxSelectedUsers} kullanici secilebilir.");
            case NotificationAudienceSegment.SingleUser:
            case NotificationAudienceSegment.SelectedUsers:
                break;
            default:
                if (ids.Count > 0)
                {
                    throw new AppValidationException("userIds", "Kullanici listesi yalnizca tek/secili kullanici kitlelerinde kullanilir.");
                }
                return Array.Empty<long>();
        }

        var valid = await Query(segment, DateTime.UtcNow, ids).Select(u => u.Id).ToListAsync(cancellationToken);
        return ids.Except(valid).ToList();
    }

    public async Task<AdminNotificationAudiencePreviewDto> PreviewAsync(AdminNotificationAudienceRequest request, CancellationToken cancellationToken = default)
    {
        var invalid = await ValidateAsync(request.Segment, request.UserIds, cancellationToken);
        return await BuildPreviewAsync(Query(request.Segment, DateTime.UtcNow, request.UserIds), request.Segment, invalid, cancellationToken);
    }

    /// <summary>Kampanyaya kayitli kitle icin onizleme (kampanya servisi kullanir).</summary>
    public async Task<AdminNotificationAudiencePreviewDto> BuildPreviewAsync(
        IQueryable<ApplicationUser> audience, NotificationAudienceSegment segment, IReadOnlyList<long> invalidUserIds, CancellationToken cancellationToken = default)
    {
        var devices = _unitOfWork.Repository<UserDevice>().GetQueryable().Where(d => d.IsActive);
        var profiles = _unitOfWork.Repository<UserProfile>().GetQueryable();

        var count = await audience.CountAsync(cancellationToken);
        var withDevice = await audience.CountAsync(u => devices.Any(d => d.UserId == u.Id), cancellationToken);
        var sample = await audience
            .OrderBy(u => u.Id)
            .Take(SampleSize)
            .Select(u => new AdminNotificationAudienceUserDto
            {
                UserId = u.Id,
                Email = u.Email,
                FullName = profiles.Where(p => p.UserId == u.Id).Select(p => p.FirstName + " " + p.LastName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new AdminNotificationAudiencePreviewDto
        {
            Segment = segment.ToString(),
            RecipientCount = count,
            WithActiveDeviceCount = withDevice,
            RequiresConfirmation = count > _options.LargeAudienceThreshold,
            LargeAudienceThreshold = _options.LargeAudienceThreshold,
            Sample = sample,
            InvalidUserIds = invalidUserIds,
        };
    }
}
