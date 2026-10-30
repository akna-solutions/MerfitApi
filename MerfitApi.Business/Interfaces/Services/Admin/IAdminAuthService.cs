using MerfitApi.Business.Dtos.Admin.Auth;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Giris yapmis adminin kendi hesabiyla ilgili islemleri (parola degistirme) yuruten servis sozlesmesi.</summary>
public interface IAdminAuthService
{
    /// <summary>
    /// Mevcut parolayi dogrulayip yeni parolayi kaydeder ve adminin tum refresh token'larini iptal eder
    /// (diger cihazlardaki oturumlar yenilenemez). Islem audit log'a yazilir.
    /// </summary>
    Task ChangePasswordAsync(long adminUserId, AdminChangePasswordRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
