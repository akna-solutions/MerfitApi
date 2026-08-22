using MerfitApi.Business.Dtos.Auth;

namespace MerfitApi.Business.Interfaces.Services;

/// <summary>
/// Kimlik dogrulama (kayit, giris vb.) is kurallarini yuruten servis sozlesmesi.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Yeni bir kullanici hesabi ve fitness profili olusturur, ardindan token cifti doner.
    /// </summary>
    /// <exception cref="Domain.Exceptions.ConflictException">E-posta zaten kayitliysa firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Is kurali ihlallerinde firlatilir.</exception>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress);

    /// <summary>
    /// Var olan bir kullaniciyi e-posta/parola ile dogrular, ardindan token cifti doner.
    /// </summary>
    /// <exception cref="Domain.Exceptions.UnauthorizedException">E-posta/parola hatali veya hesap pasifse firlatilir.</exception>
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress);
}