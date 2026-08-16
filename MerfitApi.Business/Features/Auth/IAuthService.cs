using Merfit.Application.Common.Models;

namespace Merfit.Application.Features.Auth;

public interface IAuthService
{
    Task<Result<AuthTokensResponse>> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<Result<AuthTokensResponse>> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<Result<AuthTokensResponse>> RefreshAsync(string rawRefreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default);

    Task<Result> LogoutAllAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> VerifyEmailAsync(string rawToken, CancellationToken cancellationToken = default);

    Task<Result> ResendVerificationAsync(string email, CancellationToken cancellationToken = default);

    Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default);
}
