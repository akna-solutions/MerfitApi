using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminNotificationService'in varsayilan implementasyonu.
///
/// FCM/APNS ENTEGRASYON NOTU (madde 20): Bu servis sadece Notification kayitlarini
/// veritabanina yazar (uygulama-ici bildirim kutusu). Gercek push bildirimi gondermek icin
/// ileride bir "IPushDispatcher" servisi eklenip SendToUserAsync/BroadcastAsync/SendToSegmentAsync
/// icindeki kayit olusturma adimindan hemen sonra cagrilmasi yeterlidir; boylece mevcut admin
/// akisi/DTO'lari degismeden push saglayicisi (FCM/APNS) takilabilir.
/// </summary>
public class AdminNotificationService : IAdminNotificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminNotificationService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminNotificationListItemDto>> GetListAsync(AdminNotificationListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Notification>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (request.IsRead.HasValue) query = query.Where(x => x.IsRead == request.IsRead.Value);
        if (request.From.HasValue) query = query.Where(x => x.CreatedAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(x => x.CreatedAt <= request.To.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.Title,
                x.Body,
                x.ImageUrl,
                x.IsRead,
                x.ReadAt,
                x.ExpiresAt,
                x.CreatedAt,
                UserEmail = users.Where(u => u.Id == x.UserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminNotificationListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = x.UserEmail ?? "-",
            Title = x.Title,
            Body = x.Body,
            ImageUrl = x.ImageUrl,
            IsRead = x.IsRead,
            ReadAt = x.ReadAt,
            ExpiresAt = x.ExpiresAt,
            CreatedAt = x.CreatedAt,
        }).ToList();

        return PagedResult<AdminNotificationListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminNotificationDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == entity.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        return MapToDetail(entity, userEmail ?? "-");
    }

    public async Task<AdminNotificationDetailDto> SendToUserAsync(AdminSendUserNotificationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var userExists = await _unitOfWork.Repository<ApplicationUser>().AnyAsync(u => u.Id == request.UserId && u.DeletedAt == null, cancellationToken);
        if (!userExists)
        {
            throw new AppValidationException(nameof(request.UserId), "Belirtilen kullanici bulunamadi.");
        }

        var entity = new Notification
        {
            UserId = request.UserId,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            ImageUrl = request.ImageUrl,
            DataJson = request.DataJson,
            ExpiresAt = request.ExpiresAt,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<Notification>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_SENT_USER", nameof(Notification), entity.Id, ipAddress, userAgent, newValue: new { request.UserId, request.Title }, cancellationToken: cancellationToken);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == entity.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        return MapToDetail(entity, userEmail ?? "-");
    }

    public async Task<AdminNotificationSendResultDto> BroadcastAsync(AdminBroadcastNotificationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var userIds = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.DeletedAt == null && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var count = await CreateForUsersAsync(userIds, request.Title, request.Body, request.ImageUrl, request.DataJson, request.ExpiresAt, adminUserId, cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_BROADCAST", nameof(Notification), null, ipAddress, userAgent,
            newValue: new { request.Title, RecipientCount = count }, cancellationToken: cancellationToken);

        return new AdminNotificationSendResultDto { RecipientCount = count };
    }

    public async Task<AdminNotificationSendResultDto> SendToSegmentAsync(AdminSegmentNotificationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var userIds = await ResolveSegmentUserIdsAsync(request.Segment, cancellationToken);

        var count = await CreateForUsersAsync(userIds, request.Title, request.Body, request.ImageUrl, request.DataJson, request.ExpiresAt, adminUserId, cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_SEGMENT_SENT", nameof(Notification), null, ipAddress, userAgent,
            newValue: new { Segment = request.Segment.ToString(), request.Title, RecipientCount = count }, cancellationToken: cancellationToken);

        return new AdminNotificationSendResultDto { RecipientCount = count };
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Notification>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), id);

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_DELETED", nameof(Notification), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Verilen kullanici Id listesi icin Notification kayitlarini toplu olarak olusturur.
    /// Buyuk segmentlerde (binlerce kullanici) tek bir SaveChanges ile toplu insert yapar;
    /// EF Core'un AddRangeAsync + tek SaveChangesAsync'i, N adet ayri insert komutundan
    /// cok daha performanslidir (madde 40).
    /// </summary>
    private async Task<int> CreateForUsersAsync(
        List<long> userIds, string title, string body, string? imageUrl, string? dataJson, DateTime? expiresAt,
        long? adminUserId, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var notifications = userIds.Select(userId => new Notification
        {
            UserId = userId,
            Title = title.Trim(),
            Body = body.Trim(),
            ImageUrl = imageUrl,
            DataJson = dataJson,
            ExpiresAt = expiresAt,
            IsRead = false,
            CreatedAt = now,
            CreatedUser = adminUserId,
        }).ToList();

        await _unitOfWork.Repository<Notification>().AddRangeAsync(notifications, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return notifications.Count;
    }

    /// <summary>
    /// Segment adina gore hedef kullanici Id listesini hesaplar. Esik degerler (30/7/14 gun)
    /// urun tarafinda genel kabul goren varsayilan tanimlardir; ileride ihtiyac halinde
    /// yapilandirilabilir hale getirilebilir.
    /// </summary>
    private async Task<List<long>> ResolveSegmentUserIdsAsync(AdminNotificationSegment segment, CancellationToken cancellationToken)
    {
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.DeletedAt == null && u.IsActive);
        var subscriptions = _unitOfWork.Repository<Subscription>().GetQueryable();
        var sessions = _unitOfWork.Repository<WorkoutSession>().GetQueryable();
        var now = DateTime.UtcNow;

        switch (segment)
        {
            case AdminNotificationSegment.AllUsers:
                return await users.Select(u => u.Id).ToListAsync(cancellationToken);

            case AdminNotificationSegment.PlusUsers:
                return await users
                    .Where(u => subscriptions.Any(s => s.UserId == u.Id
                        && s.Status == SubscriptionStatus.Active
                        && (s.ExpiresAt == null || s.ExpiresAt > now)))
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken);

            case AdminNotificationSegment.FreeUsers:
                return await users
                    .Where(u => !subscriptions.Any(s => s.UserId == u.Id
                        && s.Status == SubscriptionStatus.Active
                        && (s.ExpiresAt == null || s.ExpiresAt > now)))
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken);

            case AdminNotificationSegment.InactiveUsers:
                // 30 gundur giris yapmamis (veya hic giris yapmamis) kullanicilar.
                var inactiveSince = now.AddDays(-30);
                return await users
                    .Where(u => u.LastLoginAt == null || u.LastLoginAt < inactiveSince)
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken);

            case AdminNotificationSegment.NewUsers:
                // Son 7 gun icinde kayit olmus kullanicilar.
                var newSince = now.AddDays(-7);
                return await users
                    .Where(u => u.CreatedAt >= newSince)
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken);

            case AdminNotificationSegment.WorkoutInactiveUsers:
                // Son 14 gunde tamamlanmis antrenman oturumu bulunmayan kullanicilar.
                var workoutInactiveSince = now.AddDays(-14);
                return await users
                    .Where(u => !sessions.Any(s => s.UserId == u.Id && s.CompletedAt != null && s.CompletedAt >= workoutInactiveSince))
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken);

            default:
                throw new AppValidationException("segment", "Gecersiz segment.");
        }
    }

    private async Task<Notification> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Notification>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), id);
    }

    private static AdminNotificationDetailDto MapToDetail(Notification entity, string userEmail) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        UserEmail = userEmail,
        Title = entity.Title,
        Body = entity.Body,
        ImageUrl = entity.ImageUrl,
        IsRead = entity.IsRead,
        ReadAt = entity.ReadAt,
        ExpiresAt = entity.ExpiresAt,
        CreatedAt = entity.CreatedAt,
        DataJson = entity.DataJson,
    };
}
