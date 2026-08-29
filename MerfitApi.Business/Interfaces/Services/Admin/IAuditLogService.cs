using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Audit;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Admin tarafindan yapilan kritik islemlerin (madde 32) denetim (audit) kaydini yazan ve
/// bu kayitlari listeleyen servis sozlesmesi.
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Bir admin islemini audit log'a yazar. Cagiran servisler islem basarili SaveChanges/Commit
    /// sonrasinda bu metodu cagirir; hata durumunda audit yazimi ana islemi engellememelidir.
    /// </summary>
    /// <param name="adminUserId">Islemi yapan admin kullanicinin Id'si (JWT "sub" claim'inden).</param>
    /// <param name="action">Islem adi, orn. "USER_STATUS_CHANGED", "WORKOUT_CREATED".</param>
    /// <param name="entity">Islemin ilgili oldugu varlik adi, orn. "ApplicationUser".</param>
    /// <param name="entityId">Islemin ilgili oldugu varlik Id'si.</param>
    /// <param name="ipAddress">Istegin geldigi IP adresi.</param>
    /// <param name="userAgent">Istemci user-agent bilgisi.</param>
    /// <param name="oldValue">Degisiklik oncesi durum (JSON'a serilestirilir); yoksa null.</param>
    /// <param name="newValue">Degisiklik sonrasi durum (JSON'a serilestirilir); yoksa null.</param>
    Task LogAsync(
        long? adminUserId,
        string action,
        string entity,
        long? entityId,
        string? ipAddress,
        string? userAgent,
        object? oldValue = null,
        object? newValue = null,
        CancellationToken cancellationToken = default);

    /// <summary>Filtrelenmis ve sayfalanmis audit log kayitlarini dondurur.</summary>
    Task<PagedResult<AdminAuditLogListItemDto>> GetListAsync(AdminAuditLogListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Tek bir audit log kaydinin detayini (eski/yeni deger dahil) dondurur.</summary>
    Task<AdminAuditLogDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
