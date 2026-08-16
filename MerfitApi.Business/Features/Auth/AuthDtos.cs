namespace Merfit.Application.Features.Auth;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string Username,
    string? FirstName,
    string? LastName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record VerifyEmailRequest(string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed record AuthTokensResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string Username,
    string? FirstName,
    string? LastName,
    string? AvatarUrl,
    bool IsEmailVerified,
    string Membership,
    IReadOnlyCollection<string> Roles,
    bool OnboardingCompleted);
