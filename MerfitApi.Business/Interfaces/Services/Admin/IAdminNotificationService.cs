using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Uygulama-ici bildirim (Notification) yonetimi servis sozlesmesi (madde 20).
/// Bildirim kayitlarini veritabanina yazar; gercek push (FCM/APNS) gonderimi bu servisin
/// disinda, ileride eklenecek ayri bir dispatcher tarafindan (Notification kaydi tetikleyicisi
/// olarak) yapilacak sekilde tasarlanmistir - bkz. AdminNotificationService yorumlari.
/// </summary>
public interface IAdminNotificationService
{
    Task<PagedResult<AdminNotificationListItemDto>> GetListAsync(AdminNotificationListRequest request, CancellationToken cancellationToken = default);

    Task<AdminNotificationDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminNotificationDetailDto> SendToUserAsync(AdminSendUserNotificationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminNotificationSendResultDto> BroadcastAsync(AdminBroadcastNotificationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminNotificationSendResultDto> SendToSegmentAsync(AdminSegmentNotificationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
