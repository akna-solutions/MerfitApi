using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Subscriptions;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Kullanici aboneliklerinin admin tarafindan incelenmesi/durum yonetimi servis sozlesmesi (madde 17).
/// Abonelikler magaza (Apple/Google) webhook'lari uzerinden olustugundan admin sadece durumunu
/// (orn. destek/iade senaryolarinda) degistirebilir; abonelik olusturma/tam guncelleme admin panelinden yapilmaz.
/// </summary>
public interface IAdminSubscriptionService
{
    Task<PagedResult<AdminSubscriptionListItemDto>> GetListAsync(AdminSubscriptionListRequest request, CancellationToken cancellationToken = default);

    Task<AdminSubscriptionDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateSubscriptionStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
