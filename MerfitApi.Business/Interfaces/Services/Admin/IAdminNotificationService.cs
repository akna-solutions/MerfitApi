using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Uygulama ici bildirim kutusu gecmisi (Notification kayitlari): listeleme, detay ve silme.</summary>
public interface IAdminNotificationService
{
    Task<PagedResult<AdminNotificationListItemDto>> GetListAsync(AdminNotificationListRequest request, CancellationToken cancellationToken = default);

    Task<AdminNotificationDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
